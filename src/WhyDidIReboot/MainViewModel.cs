using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WhyDidIReboot.Core;

namespace WhyDidIReboot;

/// <summary>A period in the drop-down: a rolling "last N days", an explicit window, or the "Custom range…" picker entry.</summary>
public sealed record RangeOption(string Label, int? Days, TimeRange? Window = null, bool IsPicker = false)
{
    public TimeRange Range => Window ?? TimeRange.LastDays(Days);
}
public sealed record ThemeOption(string Label, ThemeMode Mode);

/// <summary>Glyph per category plus theme-aware brushes, shared by chips and cards.</summary>
public static class CategoryStyle
{
    public sealed record Info(string Label, string Glyph, bool DefaultOn);

    public static readonly IReadOnlyDictionary<RebootCategory, Info> All = new Dictionary<RebootCategory, Info>
    {
        [RebootCategory.WindowsUpdate] = new("Windows Update", "", true),
        [RebootCategory.UserInitiated] = new("User", "", true),
        [RebootCategory.Application] = new("App or service", "", true),
        [RebootCategory.BlueScreen] = new("Blue screen", "", true),
        [RebootCategory.Unexpected] = new("Power loss / freeze", "", true),
        [RebootCategory.CleanNoReason] = new("Clean, no reason", "", true),
        [RebootCategory.Unknown] = new("Unknown", "", true),
        [RebootCategory.LiveKernelEvent] = new("Kernel error (no reboot)", "", true),
        [RebootCategory.Sleep] = new("Sleep / wake", "", false),
    };

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    public static SolidColorBrush Brush(RebootCategory c) => Brush(CategoryPalette.Hex(c, ThemeManager.IsDark));
    public static SolidColorBrush Tint(RebootCategory c) => Brush(CategoryPalette.Hex(c, ThemeManager.IsDark), ThemeManager.IsDark ? 0.18 : 0.12);

    public static SolidColorBrush Brush(string hex, double opacity = 1)
    {
        var key = hex + opacity.ToString(CultureInfo.InvariantCulture);
        if (BrushCache.TryGetValue(key, out var b)) return b;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        b = new SolidColorBrush(color) { Opacity = opacity };
        b.Freeze();
        BrushCache[key] = b;
        return b;
    }
}

public sealed class CategoryFilter : INotifyPropertyChanged
{
    private bool _isChecked;
    private int _count;

    public CategoryFilter(RebootCategory category)
    {
        Category = category;
        var info = CategoryStyle.All[category];
        Label = info.Label;
        Glyph = info.Glyph;
        _isChecked = info.DefaultOn;
    }

    public RebootCategory Category { get; }
    public string Label { get; }
    public string Glyph { get; }
    public Brush Brush => CategoryStyle.Brush(Category);
    public Brush Tint => CategoryStyle.Tint(Category);

    public bool IsChecked { get => _isChecked; set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } } }
    public int Count { get => _count; set { if (_count != value) { _count = value; OnPropertyChanged(); } } }

    /// <summary>Called after a theme swap so the chip picks up the new palette.</summary>
    public void RefreshBrushes() { OnPropertyChanged(nameof(Brush)); OnPropertyChanged(nameof(Tint)); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null) { _run = run; _can = can; }
    public bool CanExecute(object? p) => _can?.Invoke(p) ?? true;
    public void Execute(object? p) => _run(p);
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<RebootEntry> _entries = new();
    private string _searchText = "";
    private RangeOption _range;
    private ThemeOption _theme;
    private bool _isBusy;
    private string _statusText = "";
    private string _currentSessionText = "";
    private string _warningText = "";
    private int _shownCount;
    private AnalysisResult? _last;

    public MainViewModel()
    {
        View = CollectionViewSource.GetDefaultView(_entries);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RebootEntry.DateGroup)));
        View.Filter = Filter;

        Categories = new ObservableCollection<CategoryFilter>(
            CategoryStyle.All.Keys.Select(c => new CategoryFilter(c)));
        foreach (var c in Categories) c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CategoryFilter.IsChecked)) View.Refresh(); UpdateShown(); };

        Ranges = new ObservableCollection<RangeOption>
        {
            new("Last 7 days", 7),
            new("Last 30 days", 30),
            new("Last 90 days", 90),
            new("Last year", 365),
            new("Everything in the log", null),
            new("Custom range…", null, IsPicker: true),
        };
        _range = Ranges[3];

        Themes = new[]
        {
            new ThemeOption("System theme", ThemeMode.System),
            new ThemeOption("Light", ThemeMode.Light),
            new ThemeOption("Dark", ThemeMode.Dark),
        };
        _theme = Themes.First(t => t.Mode == ThemeManager.Mode);
        ThemeManager.Changed += OnThemeChanged;

        RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsBusy);
        AllOnCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = true; });
        RebootsOnlyCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = c.Category is not (RebootCategory.Sleep or RebootCategory.LiveKernelEvent); });
        ProblemsOnlyCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = c.Category is RebootCategory.BlueScreen or RebootCategory.Unexpected or RebootCategory.LiveKernelEvent or RebootCategory.Unknown; });
        NoneCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = false; });
        DefaultCommand = new RelayCommand(_ => { foreach (var c in Categories) c.IsChecked = CategoryStyle.All[c.Category].DefaultOn; });
        CopyEntryCommand = new RelayCommand(p =>
        {
            if (p is RebootEntry e)
                try { System.Windows.Clipboard.SetText(TextExporter.EntryToText(e)); } catch { /* clipboard busy */ }
        });
        OpenDumpCommand = new RelayCommand(p =>
        {
            if (p is not RebootEntry { DumpPath: { Length: > 0 } path }) return;
            try
            {
                var psi = System.IO.File.Exists(path)
                    ? new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    : new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{System.IO.Path.GetDirectoryName(path)}\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch { /* folder may be inaccessible without elevation */ }
        });
    }

    // ---------------------------------------------------------------- version and updates

    private static readonly Lazy<System.Net.Http.HttpClient> Http = new(() =>
    {
        var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"WhyDidIReboot/{VersionString}");
        return client;
    });

    /// <summary>The version stamped from the csproj, e.g. "0.7.0".</summary>
    public static string VersionString => AppInfo.VersionString;

    public static Version CurrentVersion => UpdateChecker.ParseTag(VersionString) ?? new Version(0, 0, 0);

    private string _updateStatus = "Click to see whether a newer release is available.";
    private string? _updateUrl;
    private bool _hasUpdate;
    private bool _isCheckingUpdates;

    public string VersionText => $"Why Did I Reboot v{VersionString}";
    public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; OnPropertyChanged(); } }
    public string? UpdateUrl { get => _updateUrl; private set { _updateUrl = value; OnPropertyChanged(); } }
    public bool HasUpdate { get => _hasUpdate; private set { _hasUpdate = value; OnPropertyChanged(); OnPropertyChanged(nameof(UpdateButtonText)); } }
    public bool IsCheckingUpdates { get => _isCheckingUpdates; private set { _isCheckingUpdates = value; OnPropertyChanged(); } }

    public ICommand CheckForUpdatesCommand => _checkForUpdates ??= new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => !IsCheckingUpdates);
    private RelayCommand? _checkForUpdates;

    public ICommand OpenUrlCommand => _openUrl ??= new RelayCommand(p =>
    {
        if (p is not string url || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser association */ }
    });
    private RelayCommand? _openUrl;

    private bool _showUpdateBanner;

    /// <summary>Shown in the main window after a startup check finds a newer release; dismissible.</summary>
    public bool ShowUpdateBanner { get => _showUpdateBanner; set { _showUpdateBanner = value; OnPropertyChanged(); } }

    public bool CheckUpdatesOnStartup
    {
        get => AppSettings.Current.CheckUpdatesOnStartup;
        set
        {
            if (AppSettings.Current.CheckUpdatesOnStartup == value) return;
            AppSettings.Current.CheckUpdatesOnStartup = value;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    public ICommand DismissUpdateCommand => _dismissUpdate ??= new RelayCommand(_ => ShowUpdateBanner = false);
    private RelayCommand? _dismissUpdate;

    private UpdateInfo? _latest;
    private bool _isUpdating;

    /// <summary>True while a release is being downloaded and swapped in.</summary>
    public bool IsUpdating { get => _isUpdating; private set { _isUpdating = value; OnPropertyChanged(); OnPropertyChanged(nameof(UpdateButtonText)); } }

    /// <summary>The Settings button reads "Check for updates" until one is found, then "Update now".</summary>
    public string UpdateButtonText => IsUpdating ? "Updating…" : HasUpdate ? "Update now" : "Check for updates";

    /// <summary>Checks when nothing is known yet; installs once a newer release has been found.</summary>
    public ICommand UpdateActionCommand => _updateAction ??= new RelayCommand(
        async _ => { if (HasUpdate) await UpdateNowAsync(); else await CheckForUpdatesAsync(); },
        _ => !IsCheckingUpdates && !IsUpdating);
    private RelayCommand? _updateAction;

    /// <summary>Downloads the matching asset, verifies it, swaps it in beside the running exe, relaunches and exits.</summary>
    public async Task UpdateNowAsync()
    {
        if (_latest is null || IsUpdating) return;
        IsUpdating = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the running executable.");
            var appDir = System.IO.Path.GetDirectoryName(exe)!;
            var frameworkDependent = UpdateInstaller.IsFrameworkDependent(exe);
            var asset = UpdateInstaller.ChooseAsset(_latest, frameworkDependent)
                ?? throw new InvalidOperationException($"Release {_latest.Tag} has no {(frameworkDependent ? UpdateInstaller.FrameworkDependentAsset : UpdateInstaller.SelfContainedAsset)} asset.");

            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WhyDidIReboot-update");
            System.IO.Directory.CreateDirectory(temp);
            var download = System.IO.Path.Combine(temp, asset.Name);

            var mb = asset.Size / 1048576.0;
            var progress = new Progress<double>(p => UpdateStatus = $"Downloading v{_latest.Latest} ({mb:N0} MB)… {p:P0}");
            await UpdateInstaller.DownloadAsync(Http.Value, asset, download, progress, CancellationToken.None);

            UpdateStatus = "Verifying…";
            Dictionary<string, string> staged;
            if (frameworkDependent)
            {
                staged = await Task.Run(() => UpdateInstaller.StageZip(download, System.IO.Path.Combine(temp, "staged")));
                UpdateInstaller.VerifyExecutable(staged[UpdateInstaller.ExeName], _latest.Latest);
            }
            else
            {
                UpdateInstaller.VerifyExecutable(download, _latest.Latest);
                staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [UpdateInstaller.ExeName] = download };
            }

            UpdateStatus = "Installing…";
            UpdateInstaller.Swap(appDir, staged);

            UpdateStatus = $"Restarting as v{_latest.Latest}…";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(appDir, UpdateInstaller.ExeName))
            {
                WorkingDirectory = appDir,
                UseShellExecute = true,
            });
            System.Windows.Application.Current.Shutdown();
        }
        catch (UnauthorizedAccessException)
        {
            UpdateStatus = "Windows would not let the app replace its own files here. Run it as administrator, or download the new version from the release page.";
        }
        catch (Exception ex)
        {
            UpdateStatus = "Update failed: " + (ex.InnerException?.Message ?? ex.Message) + " You can download it from the release page instead.";
        }
        finally
        {
            IsUpdating = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <param name="silent">Startup check: only surface a result when a newer release exists.</param>
    public async Task CheckForUpdatesAsync(bool silent = false)
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        HasUpdate = false;
        UpdateUrl = null;
        if (!silent) UpdateStatus = "Checking GitHub for the latest release…";
        try
        {
            var info = await UpdateChecker.CheckAsync(Http.Value, CurrentVersion);
            _latest = info;
            if (info.IsNewer)
            {
                UpdateUrl = info.Url;
                HasUpdate = true;
                UpdateStatus = $"v{info.Latest} is available. You have v{VersionString}.";
                if (silent) ShowUpdateBanner = true;
            }
            else
            {
                UpdateStatus = $"You have the latest version (v{VersionString}).";
            }
        }
        catch (Exception ex)
        {
            if (!silent) UpdateStatus = "Could not check for updates: " + (ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            IsCheckingUpdates = false;
            CommandManager.InvalidateRequerySuggested();   // re-enable the button without waiting for input
        }
    }

    // ---------------------------------------------------------------- log source

    private LogLocation _location = LogLocation.Local;

    public LogLocation Location { get => _location; private set { _location = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOffline)); OnPropertyChanged(nameof(WindowTitle)); } }
    public bool IsOffline => _location.IsOffline;
    public string WindowTitle => IsOffline ? $"Why Did I Reboot — {_location.Display}" : "Why Did I Reboot";

    /// <summary>Switches to another Windows installation's logs and re-reads. Returns false if none were found there.</summary>
    public async Task<bool> LoadOfflineAsync(string folder)
    {
        var resolved = LogLocation.Resolve(folder);
        if (resolved is null) return false;
        AppSettings.Current.LastOfflineFolder = folder;
        AppSettings.Current.Save();
        Location = resolved;
        await RefreshAsync();
        return true;
    }

    public ICommand BackToThisPcCommand => _backToThisPc ??= new RelayCommand(async _ => { Location = LogLocation.Local; await RefreshAsync(); }, _ => IsOffline);
    private RelayCommand? _backToThisPc;

    /// <summary>Opens a CSV report this app exported. Returns false if the file is not one of ours.</summary>
    public async Task<bool> LoadCsvAsync(string path)
    {
        var resolved = LogLocation.Resolve(path);
        if (resolved is null || !resolved.IsCsv) return false;
        Location = resolved;
        await RefreshAsync();
        return true;
    }

    // ---------------------------------------------------------------- WinDbg

    /// <summary>Set by the window: asks "install WinDbg with winget?" and returns the answer.</summary>
    public Func<string, bool>? Confirm { get; set; }

    /// <summary>Set by the window: shows a message the user must dismiss.</summary>
    public Action<string>? Notify { get; set; }

    private bool _isInstallingWinDbg;

    /// <summary>Opens the entry's dump in WinDbg with "!analyze -v" queued, installing WinDbg through winget first if needed.</summary>
    public ICommand OpenInWinDbgCommand => _openInWinDbg ??= new RelayCommand(async p => { if (p is RebootEntry e) await OpenInWinDbgAsync(e); }, _ => !_isInstallingWinDbg);
    private RelayCommand? _openInWinDbg;

    public async Task OpenInWinDbgAsync(RebootEntry entry)
    {
        var dump = DumpFor(entry);
        if (dump is null) return;
        var windbg = await EnsureWinDbgAsync("the dump will open when it finishes");
        if (windbg is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(windbg, WinDbgLocator.Arguments(dump)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Could not start WinDbg: " + ex.Message);
        }
    }

    /// <summary>The entry's dump path mapped onto the current source, or null (after telling the user) if it is gone.</summary>
    private string? DumpFor(RebootEntry entry)
    {
        if (entry.DumpPath is null) return null;
        var dump = _location.MapPath(entry.DumpPath);
        if (System.IO.File.Exists(dump)) return dump;
        Notify?.Invoke($"The dump file is no longer there:\n{dump}");
        return null;
    }

    /// <summary>Finds WinDbg, offering to install it with winget when missing. Null when unavailable or declined.</summary>
    private async Task<string?> EnsureWinDbgAsync(string thenWhat)
    {
        var windbg = WinDbgLocator.Find();
        if (windbg is not null) return windbg;

        var winget = WinDbgLocator.FindWinget();
        if (winget is null)
        {
            Notify?.Invoke("WinDbg is not installed and winget is not available to install it. Opening the Microsoft Store page instead.");
            OpenExternal(WinDbgLocator.StoreUri, WinDbgLocator.LearnUrl);
            return null;
        }
        if (Confirm?.Invoke($"WinDbg is not installed.\n\nInstall it now with winget (Microsoft.WinDbg)? A console window will show the download progress, and {thenWhat}.") != true)
            return null;

        _isInstallingWinDbg = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(winget, WinDbgLocator.WingetInstallArguments) { UseShellExecute = true });
            if (proc is not null) await proc.WaitForExitAsync();
            windbg = WinDbgLocator.Find();
            if (windbg is null)
            {
                Notify?.Invoke("winget finished but WinDbg was not found afterwards" + (proc is { ExitCode: not 0 } ? $" (winget exit code {proc.ExitCode})" : "") + ". Opening the Microsoft Store page so you can install it there.");
                OpenExternal(WinDbgLocator.StoreUri, WinDbgLocator.LearnUrl);
            }
            return windbg;
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Could not run winget: " + ex.Message);
            return null;
        }
        finally
        {
            _isInstallingWinDbg = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ---------------------------------------------------------------- Ask AI

    public IReadOnlyList<AiChatService> AiChats => CrashAnalysis.Services;

    public AiChatService AiChat
    {
        get => CrashAnalysis.ServiceByName(AppSettings.Current.AiChat);
        set
        {
            if (value is null || value.Name == AppSettings.Current.AiChat) return;
            AppSettings.Current.AiChat = value.Name;
            AppSettings.Current.Save();
            OnPropertyChanged();
        }
    }

    private bool _isAskingAi;
    public bool IsAskingAi { get => _isAskingAi; private set { _isAskingAi = value; OnPropertyChanged(); } }

    /// <summary>Runs "!analyze -v" on the dump through WinDbg, then opens the chosen chat service with a prompt built from the output and the card.</summary>
    public ICommand AskAiCommand => _askAi ??= new RelayCommand(async p => { if (p is RebootEntry e) await AskAiAsync(e); }, _ => !_isInstallingWinDbg && !IsAskingAi);
    private RelayCommand? _askAi;

    public async Task AskAiAsync(RebootEntry entry)
    {
        var dump = DumpFor(entry);
        if (dump is null) return;
        var windbg = await EnsureWinDbgAsync("the analysis will run when it finishes");
        if (windbg is null) return;

        IsAskingAi = true;
        CommandManager.InvalidateRequerySuggested();
        var workDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WhyDidIReboot-analysis");
        var log = System.IO.Path.Combine(workDir, System.IO.Path.GetFileNameWithoutExtension(dump) + ".analyze.txt");
        try
        {
            System.IO.Directory.CreateDirectory(workDir);
            if (System.IO.File.Exists(log)) System.IO.File.Delete(log);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(windbg, WinDbgLocator.AnalyzeToLogArguments(dump, log)) { UseShellExecute = true });

            var text = await WinDbgLocator.WaitForLogAsync(log, TimeSpan.FromMinutes(4));
            if (text is null)
            {
                Notify?.Invoke("WinDbg did not finish writing the analysis within 4 minutes (symbol downloads can be slow the first time). Leave WinDbg open and try Ask AI again once it has finished.");
                return;
            }

            var summary = CrashAnalysis.Summarize(text);
            var entryText = TextExporter.EntryToText(entry);
            var service = AiChat;
            var (url, _, trimmed) = CrashAnalysis.BuildUrl(service, entryText, summary);
            var full = CrashAnalysis.BuildPrompt(entryText, summary);
            try { System.Windows.Clipboard.SetText(full); } catch { /* clipboard busy */ }

            OpenExternal(url, service.UrlTemplate.Replace("{0}", ""));
            Notify?.Invoke($"Opening {service.Name} with the crash analysis." +
                           (trimmed ? " The web address had to shorten the analysis, so the full prompt has been copied to the clipboard: paste it if the chat looks incomplete." : " The prompt has also been copied to the clipboard.") +
                           (service.NeedsSignIn ? $"\n\n{service.Name} needs you to be signed in." : "") +
                           $"\n\nFull WinDbg output: {log}");
        }
        catch (Exception ex)
        {
            Notify?.Invoke("Ask AI failed: " + ex.Message);
        }
        finally
        {
            IsAskingAi = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private static void OpenExternal(string primary, string fallback)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(primary) { UseShellExecute = true }); }
        catch { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fallback) { UseShellExecute = true }); } catch { } }
    }

    // ---------------------------------------------------------------- bindings

    public ObservableCollection<CategoryFilter> Categories { get; }
    public ObservableCollection<RangeOption> Ranges { get; }

    /// <summary>Set by the window: shows the from/to picker and returns the chosen window, or null if cancelled.</summary>
    public Func<TimeRange?, TimeRange?>? RequestCustomRange { get; set; }
    public IReadOnlyList<ThemeOption> Themes { get; }
    public ICollectionView View { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AllOnCommand { get; }
    public ICommand RebootsOnlyCommand { get; }
    public ICommand ProblemsOnlyCommand { get; }
    public ICommand NoneCommand { get; }
    public ICommand DefaultCommand { get; }
    public ICommand CopyEntryCommand { get; }
    public ICommand OpenDumpCommand { get; }

    public AnalysisResult? LastResult => _last;
    public IEnumerable<RebootEntry> VisibleEntries => View.Cast<RebootEntry>();
    public string RangeLabel => _range.Label;

    public RangeOption Range
    {
        get => _range;
        set
        {
            if (value is null || ReferenceEquals(_range, value)) return;
            if (value.IsPicker)
            {
                // Open the picker after the binding has finished, not inside the setter: WPF swallows
                // exceptions thrown here and a modal dialog is not allowed while a binding is updating.
                var previous = _range;
                _range = value;
                OnPropertyChanged();
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var chosen = RequestCustomRange?.Invoke(previous.Window);
                    if (chosen is null)
                    {
                        _range = previous;                     // cancelled: back to what was selected before
                        OnPropertyChanged(nameof(Range));
                        return;
                    }
                    var custom = new RangeOption(chosen.Label, null, chosen);
                    var existing = Ranges.FirstOrDefault(r => r.Window is not null && !r.IsPicker);
                    if (existing is not null) Ranges[Ranges.IndexOf(existing)] = custom;
                    else Ranges.Insert(Ranges.Count - 1, custom);   // just above "Custom range…"
                    _range = custom;
                    OnPropertyChanged(nameof(Range));
                    _ = RefreshAsync();
                });
                return;
            }
            _range = value;
            OnPropertyChanged();
            _ = RefreshAsync();
        }
    }

    public ThemeOption Theme
    {
        get => _theme;
        set { if (!ReferenceEquals(_theme, value) && value is not null) { _theme = value; OnPropertyChanged(); ThemeManager.SetMode(value.Mode); } }
    }

    private string[] _searchTerms = Array.Empty<string>();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? "";
            _searchTerms = SearchIndex.Terms(_searchText);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SearchTerms));
            OnPropertyChanged(nameof(HasSearch));
            View.Refresh();
            UpdateShown();
        }
    }

    /// <summary>The words being searched for; cards highlight them and open sections that contain them.</summary>
    public string[] SearchTerms => _searchTerms;
    public bool HasSearch => _searchTerms.Length > 0;

    public ICommand ClearSearchCommand => _clearSearch ??= new RelayCommand(_ => SearchText = "");
    private RelayCommand? _clearSearch;

    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public string CurrentSessionText { get => _currentSessionText; private set { _currentSessionText = value; OnPropertyChanged(); } }
    public string WarningText { get => _warningText; private set { _warningText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWarning)); } }
    public bool HasWarning => _warningText.Length > 0;
    public int ShownCount { get => _shownCount; private set { _shownCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); OnPropertyChanged(nameof(ShownText)); } }
    public bool ShowEmpty => !IsBusy && ShownCount == 0;

    /// <summary>"1 of 24 shown": cards passing the filters out of all cards loaded for the range.</summary>
    public string ShownText => $"{ShownCount} of {_entries.Count} shown";

    /// <summary>"v0.7.0", for the status bar.</summary>
    public string VersionTag => "v" + VersionString;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Reading the Windows event logs…";
        var range = _range.Range;
        var location = _location;
        try
        {
            var result = await Task.Run(() => RebootAnalyzer.Analyze(range, location));
            _last = result;
            _entries.Clear();
            foreach (var e in result.Entries) _entries.Add(e);
            foreach (var c in Categories) c.Count = result.Entries.Count(e => e.Category == c.Category);

            if (location.IsCsv)
            {
                CurrentSessionText = $"Showing {result.RecordsRead} entries imported from {location.Display}." +
                                     (result.CurrentBootTime == DateTime.MinValue ? "" : $" Last recorded boot: {Format.When(result.CurrentBootTime)}.");
            }
            else if (location.IsOffline)
            {
                CurrentSessionText = result.CurrentBootTime == DateTime.MinValue
                    ? $"Showing logs from {location.Display} (another Windows installation). No boots recorded in this range."
                    : $"Showing logs from {location.Display} (another Windows installation). Last recorded boot: {Format.When(result.CurrentBootTime)}.";
            }
            else
            {
                var uptime = DateTime.Now - result.CurrentBootTime;
                CurrentSessionText = $"This PC has been running since {Format.When(result.CurrentBootTime)} ({Format.Duration(uptime)} ago).";
            }
            WarningText = string.Join("  ", result.Warnings);
            var reboots = result.Entries.Count(e => e.IsReboot);
            StatusText = $"{reboots} boots found · {result.RecordsRead:N0} log records read in {result.Elapsed.TotalMilliseconds:N0} ms";
        }
        catch (Exception ex)
        {
            WarningText = "Could not read the event log: " + ex.Message;
            StatusText = "Failed";
        }
        finally
        {
            IsBusy = false;
            View.Refresh();
            UpdateShown();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnThemeChanged()
    {
        var match = Themes.FirstOrDefault(t => t.Mode == ThemeManager.Mode);
        if (match is not null && !ReferenceEquals(match, _theme)) { _theme = match; OnPropertyChanged(nameof(Theme)); }
        foreach (var c in Categories) c.RefreshBrushes();
        // Cards bind their colours through a converter; a refresh regenerates them with the new palette.
        View.Refresh();
        UpdateShown();
    }

    private bool Filter(object o)
    {
        if (o is not RebootEntry e) return false;
        var cat = Categories.FirstOrDefault(c => c.Category == e.Category);
        if (cat is { IsChecked: false }) return false;
        return SearchIndex.Matches(e, _searchTerms);
    }

    private void UpdateShown() => ShownCount = View.Cast<object>().Count();

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new(n));
}

// ------------------------------------------------------------------ converters

/// <summary>Category → Brush / Tint / Glyph / Label depending on the parameter, using the active theme's palette.</summary>
public sealed class CategoryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RebootCategory c || !CategoryStyle.All.TryGetValue(c, out var info)) return Binding.DoNothing;
        return (parameter as string) switch
        {
            "Tint" => CategoryStyle.Tint(c),
            "Glyph" => info.Glyph,
            "Label" => info.Label,
            _ => CategoryStyle.Brush(c),
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Builds the grey line under a card title: when, downtime, previous uptime.</summary>
public sealed class SubtitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RebootEntry e) return "";
        var parts = new List<string> { Format.When(e.Timestamp) };
        if (e.IsReboot)
        {
            if (e.ShutdownTime is null && e.BootTime is not null) parts[0] = "Back up at " + Format.When(e.BootTime);
            if (e.Downtime is TimeSpan d) parts.Add("down for " + Format.Duration(d));
            if (e.PreviousUptime is TimeSpan u) parts.Add("previous session ran " + Format.Duration(u));
        }
        return string.Join("  ·  ", parts);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// [entry, search terms] → true when a term appears in the section named by the parameter
/// ("updates" or "details"), so that expander opens automatically.
/// </summary>
public sealed class SectionMatchConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[0] is RebootEntry e && values[1] is string[] terms && parameter is string section
        && SearchIndex.SectionMatches(e, terms, section);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Appends an external-link arrow to a label, for link buttons whose Content is a plain string.</summary>
public sealed class ExternalLinkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 ? s + " ↗" : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime t ? t.ToString("d MMM yyyy HH:mm:ss") : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
