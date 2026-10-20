using System.IO;
using System.Text;

namespace WhyDidIReboot.Core;

/// <summary>A driver or kernel module loaded when the dump was written.</summary>
public sealed record DumpModule(string Name, ulong Base, uint Size)
{
    public bool Contains(ulong address) => address >= Base && address - Base < Size;
}

/// <summary>What a kernel minidump says about the crash, and which module it points to.</summary>
public sealed record KernelDumpInfo(uint BugcheckCode, IReadOnlyList<ulong> Parameters, IReadOnlyList<DumpModule> Modules, string? CausedByDriver, string? CausedByAddress)
{
    /// <summary>True when the module blamed is Windows' own kernel or HAL, which usually means the real culprit was not pinned down.</summary>
    public bool BlamesCore => CausedByDriver is not null && KernelDump.IsCore(CausedByDriver);
}

public enum DumpReadStatus { Ok, AccessDenied, NotAMinidump, Unreadable }

/// <summary>
/// Reads the small kernel memory dumps Windows writes to C:\Windows\Minidump (64-bit "PAGEDU64" triage
/// dumps): the bugcheck, the list of loaded drivers and the crashing thread's stack. Like BlueScreenView,
/// it names the "caused by" driver as the first non-Windows-core module that a code address in the bugcheck
/// parameters or on the stack falls in. No debugger or symbols needed; WinDbg's !analyze goes much deeper.
/// </summary>
public static class KernelDump
{
    private const uint PageSignature = 0x45474150;   // "PAGE"
    private const uint Dump64Signature = 0x34365544; // "DU64"
    private const int HeaderSize = 0x2000;
    private const int BugcheckCodeOffset = 0x38;
    private const int BugcheckParametersOffset = 0x40;
    private const int ContextOffset = 0x348;
    private const int ContextRipOffset = 0xF8;
    private const int DumpTypeOffset = 0xF98;
    private const uint TriageDump = 4;
    private const int TriageSize = 0x50;
    private const ulong KernelSpace = 0xFFFF_8000_0000_0000;

    /// <summary>The Windows kernel and HAL: they are on every crash stack, so they are blamed only when nothing else is.</summary>
    private static readonly HashSet<string> Core = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntoskrnl.exe", "ntkrnlmp.exe", "ntkrnlpa.exe", "ntkrpamp.exe", "nt.exe", "hal.dll", "halmacpi.dll", "halacpi.dll",
    };

    public static bool IsCore(string module) => Core.Contains(module);

    /// <summary>Reads the dump at <paramref name="path"/>; never throws.</summary>
    public static (KernelDumpInfo? Info, DumpReadStatus Status) TryRead(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var info = Parse(stream);
            return (info, info is null ? DumpReadStatus.NotAMinidump : DumpReadStatus.Ok);
        }
        catch (UnauthorizedAccessException)
        {
            return (null, DumpReadStatus.AccessDenied);
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException or NotSupportedException)
        {
            return (null, DumpReadStatus.Unreadable);
        }
    }

    /// <summary>Parses a 64-bit kernel triage dump; null for anything else (full or kernel memory dumps, 32-bit, damaged).</summary>
    public static KernelDumpInfo? Parse(Stream stream)
    {
        var length = stream.Length;
        if (length < HeaderSize + TriageSize) return null;
        var header = Read(stream, 0, HeaderSize);
        if (U32(header, 0) != PageSignature || U32(header, 4) != Dump64Signature) return null;
        if (U32(header, DumpTypeOffset) != TriageDump) return null;

        var code = U32(header, BugcheckCodeOffset);
        var parameters = Enumerable.Range(0, 4).Select(i => U64(header, BugcheckParametersOffset + 8 * i)).ToArray();
        var rip = U64(header, ContextOffset + ContextRipOffset);

        // TRIAGE_DUMP follows the header; its offsets are from the start of the file.
        var triage = Read(stream, HeaderSize, TriageSize);
        var callStackOffset = U32(triage, 0x28);
        var callStackSize = U32(triage, 0x2C);
        var driverListOffset = U32(triage, 0x30);
        var driverCount = U32(triage, 0x34);
        var stringPoolOffset = U32(triage, 0x38);
        var stringPoolSize = U32(triage, 0x3C);

        var modules = ReadModules(stream, driverListOffset, driverCount, stringPoolOffset, stringPoolSize);

        // Only trust the stack when its recorded address is a kernel one and its size fits a kernel stack;
        // otherwise random bytes could be read as return addresses and blame the wrong driver.
        var topOfStack = U64(triage, 0x48);
        var stack = new List<ulong>();
        if (callStackOffset >= HeaderSize + TriageSize && callStackSize is > 0 and <= 0x40000 && callStackOffset + (long)callStackSize <= length &&
            topOfStack >= KernelSpace)
        {
            var bytes = Read(stream, callStackOffset, (int)callStackSize);
            for (var i = 0; i + 8 <= bytes.Length; i += 8) stack.Add(U64(bytes, i));
        }

        var (driver, address) = Blame(modules, parameters, rip, stack);
        return new KernelDumpInfo(code, parameters, modules, driver, address);
    }

    /// <summary>
    /// The first module outside the Windows core that a bugcheck parameter or a stack value points into
    /// (parameters first: for many bugchecks one of them is the faulting instruction). Falls back to the core
    /// module the crash was in when no other driver is involved.
    /// </summary>
    public static (string? Driver, string? Address) Blame(IReadOnlyList<DumpModule> modules, IReadOnlyList<ulong> parameters, ulong rip, IReadOnlyList<ulong> stack)
    {
        if (modules.Count == 0) return (null, null);
        (string, string)? Hit(ulong address, bool core)
        {
            if (address < KernelSpace) return null;
            var m = modules.FirstOrDefault(x => x.Contains(address));
            if (m is null || IsCore(m.Name) != core) return null;
            return (m.Name, $"{m.Name}+{address - m.Base:x}");
        }

        foreach (var a in parameters.Concat(stack))
            if (Hit(a, core: false) is { } driver) return driver;
        foreach (var a in parameters.Append(rip).Concat(stack))
            if (Hit(a, core: true) is { } core) return core;
        return (null, null);
    }

    /// <summary>
    /// DUMP_DRIVER_ENTRY64: a ULONG offset of the driver's name, then its KLDR_DATA_TABLE_ENTRY64. The
    /// layout is checked rather than assumed: each candidate is tried and the one where (nearly) every
    /// entry has a kernel-space base, a sane size and a driver-like name wins; if none does, no modules.
    /// </summary>
    private static List<DumpModule> ReadModules(Stream stream, uint offset, uint count, uint poolOffset, uint poolSize)
    {
        var length = stream.Length;
        if (offset == 0 || count == 0 || count > 4096) return new();

        // (entry size, DllBase offset, SizeOfImage offset) within an entry.
        (int Stride, int Base, int Size)[] layouts = { (0x90, 0x38, 0x48), (0xA0, 0x38, 0x48), (0x98, 0x38, 0x48), (0x88, 0x30, 0x40), (0x90, 0x30, 0x40) };
        List<DumpModule>? best = null;
        foreach (var (stride, baseAt, sizeAt) in layouts)
        {
            if (offset + (long)stride * count > length) continue;
            var table = Read(stream, offset, stride * (int)count);
            var found = new List<DumpModule>();
            for (var i = 0; i < count; i++)
            {
                var entry = i * stride;
                var nameOffset = U32(table, entry);
                var dllBase = U64(table, entry + baseAt);
                var size = U32(table, entry + sizeAt);
                if (dllBase < KernelSpace || size == 0 || size > 0x4000_0000) continue;
                if (poolSize > 0 && (nameOffset < poolOffset || nameOffset >= poolOffset + (long)poolSize)) continue;
                var name = ReadName(stream, nameOffset);
                if (name is null) continue;
                found.Add(new DumpModule(name, dllBase, size));
            }
            if (best is null || found.Count > best.Count) best = found;
        }
        return best is not null && best.Count >= Math.Max(1, count * 8 / 10) ? best : new();
    }

    /// <summary>DUMP_STRING: a ULONG length in characters, then UTF-16 text. Returns the file name, or null if it does not look like one.</summary>
    private static string? ReadName(Stream stream, uint offset)
    {
        if (offset == 0 || offset + 4L > stream.Length) return null;
        var chars = U32(Read(stream, offset, 4), 0);
        if (chars == 0 || chars > 512 || offset + 4L + chars * 2 > stream.Length) return null;
        var text = Encoding.Unicode.GetString(Read(stream, offset + 4, (int)chars * 2)).TrimEnd('\0');
        var name = text.Replace('/', '\\');
        name = name[(name.LastIndexOf('\\') + 1)..];
        var ext = Path.GetExtension(name);
        if (name.Length < 5 || name.Any(c => c < 0x20 || c > 0x7E) ||
            !(ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            return null;
        return name;
    }

    private static byte[] Read(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static uint U32(byte[] b, int at) => BitConverter.ToUInt32(b, at);
    private static ulong U64(byte[] b, int at) => BitConverter.ToUInt64(b, at);
}
