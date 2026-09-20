using System.Text;
using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>A chat service that accepts a prefilled prompt in its URL.</summary>
public sealed record AiChatService(string Name, string UrlTemplate, bool NeedsSignIn)
{
    public string Url(string prompt) => string.Format(UrlTemplate, Uri.EscapeDataString(prompt));
}

/// <summary>Turns WinDbg's "!analyze -v" output plus a card into a prompt for a chat assistant.</summary>
public static partial class CrashAnalysis
{
    public static readonly IReadOnlyList<AiChatService> Services = new[]
    {
        new AiChatService("ChatGPT", "https://chatgpt.com/?q={0}", false),
        new AiChatService("Microsoft Copilot", "https://copilot.microsoft.com/?q={0}", false),
        new AiChatService("Perplexity", "https://www.perplexity.ai/search?q={0}", false),
        new AiChatService("Claude", "https://claude.ai/new?q={0}", true),
    };

    public static AiChatService ServiceByName(string? name) =>
        Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Services[0];

    /// <summary>Browsers and the services start dropping or refusing query strings somewhere above this.</summary>
    public const int MaxUrlLength = 7500;

    private static readonly string[] KeyFields =
    {
        "BUGCHECK_CODE", "BUGCHECK_P1", "BUGCHECK_P2", "BUGCHECK_P3", "BUGCHECK_P4",
        "FILE_IN_CAB", "DUMP_FILE_ATTRIBUTES", "OSBUILD", "BUILD_VERSION_STRING", "OSPLATFORM_TYPE",
        "PROCESS_NAME", "MODULE_NAME", "IMAGE_NAME", "IMAGE_VERSION", "FAULTING_MODULE",
        "DEVICE_OBJECT", "DRIVER_OBJECT", "DRIVER_VERIFIER_IO_VIOLATION_TYPE",
        "EXCEPTION_CODE", "EXCEPTION_CODE_STR", "ERROR_CODE", "EXCEPTION_PARAMETER1", "EXCEPTION_PARAMETER2",
        "SYMBOL_NAME", "STACK_COMMAND", "FAILURE_BUCKET_ID", "BUCKET_ID", "FAILURE_ID_HASH_STRING", "FAILURE_ID_HASH",
        "DEFAULT_BUCKET_ID", "IRP_ADDRESS", "DEVICE_NAME", "DPC_TIMEOUT_TYPE", "CUSTOMER_CRASH_COUNT",
    };

    [GeneratedRegex(@"^([A-Z_0-9]+):\s*(.*)$")]
    private static partial Regex FieldLine();

    /// <summary>
    /// Keeps the parts of "!analyze -v" a person (or a model) actually reasons from: the summary line,
    /// the key fields, and the first lines of STACK_TEXT, then trims to <paramref name="maxChars"/>.
    /// </summary>
    public static string Summarize(string log, int maxChars = 4000, int stackLines = 25)
    {
        if (string.IsNullOrWhiteSpace(log)) return "";
        var lines = log.Replace("\r", "").Split('\n');
        var sb = new StringBuilder();
        var fields = new List<string>();
        string? caused = null;
        var stack = new List<string>();
        var inStack = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (inStack)
            {
                if (line.Length == 0 || FieldLine().IsMatch(line)) { inStack = false; }
                else { if (stack.Count < stackLines) stack.Add(line.Trim()); continue; }
            }
            if (line.StartsWith("Probably caused by", StringComparison.OrdinalIgnoreCase)) { caused = line.Trim(); continue; }
            if (line.StartsWith("STACK_TEXT:", StringComparison.Ordinal)) { inStack = true; continue; }
            var m = FieldLine().Match(line);
            if (m.Success && KeyFields.Contains(m.Groups[1].Value) && m.Groups[2].Value.Length > 0)
            {
                // Multi-line values (FAULTING_MODULE etc.) continue until the next blank line; keep only the first line.
                fields.Add($"{m.Groups[1].Value}: {m.Groups[2].Value.Trim()}");
            }
        }

        if (caused is not null) sb.AppendLine(caused);
        foreach (var f in fields.Distinct()) sb.AppendLine(f);
        if (stack.Count > 0)
        {
            sb.AppendLine("STACK_TEXT:");
            foreach (var s in stack) sb.AppendLine("  " + s);
        }

        var text = sb.ToString().TrimEnd();
        if (text.Length == 0)
        {
            // Not a recognisable !analyze output; keep the tail, which is where the conclusion tends to be.
            var whole = log.Trim();
            return whole.Length <= maxChars ? whole : "…" + whole[^(maxChars - 1)..].TrimStart();
        }
        return text.Length <= maxChars ? text : text[..maxChars].TrimEnd() + "\n…(trimmed)";
    }

    /// <summary>The prompt sent to the chat service.</summary>
    public static string BuildPrompt(string entryText, string analysis)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are helping a Windows user understand why their PC crashed and what to do about it.");
        sb.AppendLine("Below is a plain-English summary of the crash from the Why Did I Reboot app, followed by the key parts of WinDbg's \"!analyze -v\" output for the minidump.");
        sb.AppendLine("Please: (1) say in one or two sentences what most likely caused the crash, naming the driver or component if the analysis points at one; (2) list the most likely fixes in order, with concrete steps (driver to update or roll back, hardware to test, Windows tools to run); (3) say what evidence would confirm or rule out each; (4) flag anything in the analysis that is ambiguous.");
        sb.AppendLine();
        sb.AppendLine("=== Crash summary (Why Did I Reboot) ===");
        sb.AppendLine(entryText.Trim());
        sb.AppendLine();
        sb.AppendLine("=== WinDbg !analyze -v (key sections) ===");
        sb.AppendLine(analysis.Trim());
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the URL, shrinking the analysis part until the whole thing fits <see cref="MaxUrlLength"/>.
    /// Returns the URL and the prompt that was actually encoded (the caller puts the full prompt on the clipboard).
    /// </summary>
    public static (string Url, string Prompt, bool Trimmed) BuildUrl(AiChatService service, string entryText, string analysis)
    {
        var budget = analysis.Length;
        var trimmed = false;
        while (true)
        {
            var part = budget >= analysis.Length ? analysis : analysis[..budget].TrimEnd() + "\n…(trimmed; the full text is on the clipboard)";
            var prompt = BuildPrompt(entryText, part);
            var url = service.Url(prompt);
            if (url.Length <= MaxUrlLength || budget <= 200) return (url, prompt, trimmed);
            budget = Math.Max(200, budget * 3 / 4);
            trimmed = true;
        }
    }
}
