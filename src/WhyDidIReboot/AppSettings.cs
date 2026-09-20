using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WhyDidIReboot;

/// <summary>User preferences persisted to %LocalAppData%\WhyDidIReboot\settings.json.</summary>
public sealed class AppSettings
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhyDidIReboot", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Query GitHub for a newer release once, shortly after the app starts.</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>The last folder chosen for another Windows installation's logs, to pre-select next time.</summary>
    public string? LastOfflineFolder { get; set; }

    /// <summary>Which chat service "Ask AI" opens: ChatGPT, Microsoft Copilot, Perplexity or Claude.</summary>
    public string AiChat { get; set; } = "ChatGPT";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or unreadable file: fall back to defaults and overwrite on next save.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Not fatal; settings still apply for this session.
        }
    }
}
