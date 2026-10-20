using System.IO;
using System.Text;
using WhyDidIReboot.Core;
using Xunit;
using static WhyDidIReboot.Tests.Events;

namespace WhyDidIReboot.Tests;

public class KernelDumpTests
{
    private const ulong NtBase = 0xFFFFF807_40000000, HalBase = 0xFFFFF807_3F000000, MyFaultBase = 0xFFFFF807_44C90000, PciBase = 0xFFFFF807_41200000;

    private static readonly (string Name, ulong Base, uint Size)[] Loaded =
    {
        ("ntoskrnl.exe", NtBase, 0x1047000), ("hal.dll", HalBase, 0x6000), ("pci.sys", PciBase, 0x30000), ("myfault.sys", MyFaultBase, 0x9000),
    };

    /// <summary>
    /// Writes a 64-bit kernel triage dump the way Windows lays it out: the 0x2000-byte DUMP_HEADER64, the
    /// TRIAGE_DUMP block after it, a DUMP_DRIVER_ENTRY64 table, a string pool of DUMP_STRINGs and the raw stack.
    /// </summary>
    private static byte[] TriageDump(uint code, ulong[] parameters, ulong[] stack, int stride = 0x90, uint dumpType = 4, ulong rip = NtBase + 0x3F5A10,
        (string Name, ulong Base, uint Size)[]? modules = null, ulong topOfStack = 0xFFFFD40C_F206F000)
    {
        modules ??= Loaded;
        var file = new byte[0x8000];
        void U32(int at, uint v) => BitConverter.GetBytes(v).CopyTo(file, at);
        void U64(int at, ulong v) => BitConverter.GetBytes(v).CopyTo(file, at);

        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(file, 0);
        U32(0x38, code);
        for (var i = 0; i < 4; i++) U64(0x40 + 8 * i, parameters[i]);
        U64(0x348 + 0xF8, rip);
        U32(0xF98, dumpType);

        const int driverList = 0x3000, pool = 0x5000, stackAt = 0x6000;
        var poolAt = pool;
        for (var i = 0; i < modules.Length; i++)
        {
            var entry = driverList + i * stride;
            U32(entry, (uint)poolAt);
            U64(entry + 0x38, modules[i].Base);
            U32(entry + 0x48, modules[i].Size);
            U32(poolAt, (uint)modules[i].Name.Length);
            Encoding.Unicode.GetBytes(modules[i].Name + "\0").CopyTo(file, poolAt + 4);
            poolAt += 4 + (modules[i].Name.Length + 1) * 2;
        }
        for (var i = 0; i < stack.Length; i++) U64(stackAt + 8 * i, stack[i]);

        const int t = 0x2000;
        U32(t + 0x28, stackAt); U32(t + 0x2C, (uint)(stack.Length * 8));
        U32(t + 0x30, driverList); U32(t + 0x34, (uint)modules.Length);
        U32(t + 0x38, pool); U32(t + 0x3C, (uint)(poolAt - pool));
        U64(t + 0x48, topOfStack);
        return file;
    }

    private static KernelDumpInfo? Parse(byte[] dump) => KernelDump.Parse(new MemoryStream(dump));

    [Fact]
    public void A_driver_irql_crash_is_blamed_on_the_driver_at_the_faulting_address()
    {
        // 0xD1: parameter 4 is the instruction that touched the memory, here inside myfault.sys.
        var dump = TriageDump(0xD1, new[] { 0xFFFF9206DE6CF760, 2UL, 0UL, MyFaultBase + 0x1E35 },
            stack: new[] { NtBase + 0x420B29, 0x10UL, NtBase + 0x41C4A0, PciBase + 0x123 });

        var info = Parse(dump)!;
        Assert.Equal(0xD1u, info.BugcheckCode);
        Assert.Equal(4, info.Modules.Count);
        Assert.Equal("myfault.sys", info.CausedByDriver);
        Assert.Equal("myfault.sys+1e35", info.CausedByAddress);
        Assert.False(info.BlamesCore);
    }

    [Fact]
    public void Without_a_code_address_in_the_parameters_the_stack_names_the_first_driver_past_the_kernel()
    {
        var dump = TriageDump(0x9F, new[] { 4UL, 0x12CUL, 0xFFFFC30DF3DF2040, 0xFFFFD40CF206F5D0 },
            stack: new[] { NtBase + 0x420B29, 0x4UL, HalBase + 0x10, NtBase + 0x2C1A, PciBase + 0x8A44, MyFaultBase + 0x10 });

        var info = Parse(dump)!;
        Assert.Equal("pci.sys", info.CausedByDriver);
        Assert.Equal("pci.sys+8a44", info.CausedByAddress);

        // The same bytes with a stack address that is not a kernel one are not trusted as a stack.
        var suspect = TriageDump(0x9F, new[] { 4UL, 0x12CUL, 0xFFFFC30DF3DF2040, 0xFFFFD40CF206F5D0 },
            stack: new[] { PciBase + 0x8A44 }, topOfStack: 0x12345678);
        Assert.Equal("ntoskrnl.exe", Parse(suspect)!.CausedByDriver);   // only the context's instruction pointer is left
    }

    [Fact]
    public void When_only_windows_itself_is_involved_the_kernel_is_named_and_flagged()
    {
        var dump = TriageDump(0x1A, new[] { 0x41792UL, 0xFFFFFE3FFE1F0BE0, 0x100000UL, 0UL }, stack: new[] { NtBase + 0x420B29, 0x20UL });
        var info = Parse(dump)!;
        Assert.Equal("ntoskrnl.exe", info.CausedByDriver);
        Assert.True(info.BlamesCore);
    }

    [Theory]
    [InlineData(0x90)]
    [InlineData(0xA0)]
    [InlineData(0x98)]
    public void The_driver_table_layout_is_detected_not_assumed(int stride)
    {
        var dump = TriageDump(0xD1, new[] { 0UL, 2UL, 0UL, MyFaultBase + 0x1E35 }, stack: Array.Empty<ulong>(), stride: stride);
        Assert.Equal("myfault.sys", Parse(dump)!.CausedByDriver);
    }

    [Fact]
    public void Other_files_and_other_dump_kinds_are_not_read_as_triage_dumps()
    {
        Assert.Null(Parse(new byte[0x3000]));                                                                 // zeros
        Assert.Null(Parse(Encoding.ASCII.GetBytes("MDMP").Concat(new byte[0x3000]).ToArray()));                // a user-mode app dump
        Assert.Null(Parse(TriageDump(0xD1, new ulong[4], Array.Empty<ulong>(), dumpType: 1)));                 // a full memory dump
        Assert.Null(Parse(new byte[100]));                                                                    // too short
        var garbage = TriageDump(0xD1, new[] { 0UL, 0UL, 0UL, MyFaultBase }, Array.Empty<ulong>());
        BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(garbage, 0x2000 + 0x30);                                     // driver list points past the end
        var info = Parse(garbage)!;
        Assert.Empty(info.Modules);
        Assert.Null(info.CausedByDriver);
    }

    [Fact]
    public void TryRead_reports_a_dump_only_administrators_can_read()
    {
        var path = Path.Combine(Path.GetTempPath(), "wdir-dump-" + Guid.NewGuid().ToString("N") + ".dmp");
        File.WriteAllBytes(path, TriageDump(0xD1, new[] { 0UL, 2UL, 0UL, MyFaultBase + 0x1E35 }, Array.Empty<ulong>()));
        var info = new FileInfo(path);
        var security = info.GetAccessControl();
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
            System.Security.AccessControl.FileSystemRights.ReadData, System.Security.AccessControl.AccessControlType.Deny);
        try
        {
            Assert.Equal(("myfault.sys", DumpReadStatus.Ok), (KernelDump.TryRead(path).Info?.CausedByDriver, KernelDump.TryRead(path).Status));
            security.AddAccessRule(deny);
            info.SetAccessControl(security);
            Assert.Equal(DumpReadStatus.AccessDenied, KernelDump.TryRead(path).Status);
            Assert.Equal(DumpReadStatus.Unreadable, KernelDump.TryRead(path + ".missing").Status);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            info.SetAccessControl(security);
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------ on the card

    private static RebootEntry CrashCard(Func<string, (KernelDumpInfo?, DumpReadStatus)> readDump, out string dumpPath)
    {
        dumpPath = Path.Combine(Path.GetTempPath(), "wdir-card-" + Guid.NewGuid().ToString("N") + ".dmp");
        File.WriteAllBytes(dumpPath, new byte[] { 1 });   // only has to exist; the reader is injected
        var boot = T0.AddHours(1);
        var events = new List<RawEvent>
        {
            Boot(T0), Boot(boot),
            KernelPower41(boot.AddSeconds(2), 209),
            Bugcheck1001(boot.AddSeconds(7), "0x000000d1", dumpPath, "35f4830d"),
        };
        try { return RebootAnalyzer.Build(events, new AnalysisOptions { ReadDump = readDump }).Single(e => e.Category == RebootCategory.BlueScreen); }
        finally { File.Delete(dumpPath); }
    }

    [Fact]
    public void The_card_shows_the_driver_right_after_the_stop_code_and_searches_for_it()
    {
        var info = Parse(TriageDump(0xD1, new[] { 0UL, 2UL, 0UL, MyFaultBase + 0x1E35 }, Array.Empty<ulong>()));
        var e = CrashCard(_ => (info, DumpReadStatus.Ok), out _);

        Assert.Equal(new[] { "STOP code", RebootAnalyzer.CausedByDetail }, e.Details.Take(2).Select(d => d.Key));
        Assert.StartsWith("myfault.sys (Sysinternals NotMyFault", e.Details[1].Value);
        Assert.Contains("The crash dump points to myfault.sys", e.Summary);
        Assert.Equal("DRIVER_IRQL_NOT_LESS_OR_EQUAL 0xD1 myfault.sys", e.SearchQuery);
    }

    [Fact]
    public void A_dump_only_administrators_can_read_says_so_and_a_kernel_only_dump_does_not_blame_windows()
    {
        var denied = CrashCard(_ => (null, DumpReadStatus.AccessDenied), out _);
        Assert.Contains("administrators", denied.Details.Single(d => d.Key == RebootAnalyzer.CausedByDetail).Value);
        Assert.Equal("DRIVER_IRQL_NOT_LESS_OR_EQUAL 0xD1", denied.SearchQuery);

        var kernelOnly = Parse(TriageDump(0xD1, new ulong[4], new[] { NtBase + 0x100 }));
        var core = CrashCard(_ => (kernelOnly, DumpReadStatus.Ok), out _);
        Assert.StartsWith("ntoskrnl.exe (Windows kernel); no other driver", core.Details.Single(d => d.Key == RebootAnalyzer.CausedByDetail).Value);
        Assert.DoesNotContain("points to", core.Summary);
        Assert.Equal("DRIVER_IRQL_NOT_LESS_OR_EQUAL 0xD1", core.SearchQuery);

        var unreadable = CrashCard(_ => (null, DumpReadStatus.NotAMinidump), out _);
        Assert.DoesNotContain(unreadable.Details, d => d.Key == RebootAnalyzer.CausedByDetail);
    }
}
