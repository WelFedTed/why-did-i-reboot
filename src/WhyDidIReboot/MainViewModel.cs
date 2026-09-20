using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WhyDidIReboot.Core;

namespace WhyDidIReboot;

public sealed record RangeOption(string Label, int? Days);
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

        Ranges = new[]
        {
            new RangeOption("Last 7 days", 7),
            new RangeOption("Last 30 days", 30),
            new RangeOption("Last 90 days", 90),
            new RangeOption("Last year", 365),
            new RangeOption("Everything in the log", null),
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

    /// <summary>The version stamped from the csproj, e.g. "0.3.0" (any "+commit" suffix stripped).</summary>
    public static string VersionString
    {
        get
        {
            var info = typeof(MainViewModel).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }
            var v = typeof(MainViewModel).Assembly.GetName().Version;
            return v is null ? "0.0.0" : UpdateChecker.Normalise(v).ToString();
        }
    }

    public static Version CurrentVersion => UpdateChecker.ParseTag(VersionString) ?? new Version(0, 0, 0);

    private string _updateStatus = "Click to see whether a newer release is available.";
    private string? _updateUrl;
    private bool _hasUpdate;
    private bool _isCheckingUpdates;

    public string VersionText => $"Why Did I Reboot v{VersionString}";
    public string UpdateStatus { get => _updateStatus; private set { _updateStatus = value; OnPropertyChanged(); } }
    public string? UpdateUrl { get => _updateUrl; private set { _updateUrl = value; OnPropertyChanged(); } }
    public bool HasUpdate { get => _hasUpdate; private set { _hasUpdate = value; OnPropertyChanged(); } }
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

    // ---------------------------------------------------------------- bindings

    public ObservableCollection<CategoryFilter> Categories { get; }
    public IReadOnlyList<RangeOption> Ranges { get; }
    public IReadOnlyList<ThemeOption> Themes { get; }
    public ICollectionView View { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AllOnCommand { get; }
    public ICommand RebootsOnlyCommand { get; }
    public ICommand ProblemsOnlyCommand { get; }
    public ICommand NoneCommand { get; }
    public ICommand CopyEntryCommand { get; }
    public ICommand OpenDumpCommand { get; }

    public AnalysisResult? LastResult => _last;
    public IEnumerable<RebootEntry> VisibleEntries => View.Cast<RebootEntry>();
    public string RangeLabel => _range.Label;

    public RangeOption Range
    {
        get => _range;
        set { if (!ReferenceEquals(_range, value) && value is not null) { _range = value; OnPropertyChanged(); _ = RefreshAsync(); } }
    }

    public ThemeOption Theme
    {
        get => _theme;
        set { if (!ReferenceEquals(_theme, value) && value is not null) { _theme = value; OnPropertyChanged(); ThemeManager.SetMode(value.Mode); } }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText != value) { _searchText = value; OnPropertyChanged(); View.Refresh(); UpdateShown(); } }
    }

    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); } }
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public string CurrentSessionText { get => _currentSessionText; private set { _currentSessionText = value; OnPropertyChanged(); } }
    public string WarningText { get => _warningText; private set { _warningText = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWarning)); } }
    public bool HasWarning => _warningText.Length > 0;
    public int ShownCount { get => _shownCount; private set { _shownCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmpty)); } }
    public bool ShowEmpty => !IsBusy && ShownCount == 0;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Reading the Windows event logs…";
        var days = _range.Days;
        var location = _location;
        try
        {
            var result = await Task.Run(() => RebootAnalyzer.Analyze(days, location));
            _last = result;
            _entries.Clear();
            foreach (var e in result.Entries) _entries.Add(e);
            foreach (var c in Categories) c.Count = result.Entries.Count(e => e.Category == c.Category);

            if (location.IsOffline)
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
        if (string.IsNullOrWhiteSpace(_searchText)) return true;
        var q = _searchText.Trim();
        return e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Summary.Contains(q, StringComparison.OrdinalIgnoreCase)
            || e.Details.Any(d => d.Value.Contains(q, StringComparison.OrdinalIgnoreCase))
            || e.Evidence.Any(v => v.Message.Contains(q, StringComparison.OrdinalIgnoreCase));
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
