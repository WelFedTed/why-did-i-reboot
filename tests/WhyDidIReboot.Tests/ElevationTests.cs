using System.ComponentModel;
using System.Diagnostics;
using WhyDidIReboot.Core;
using Xunit;

namespace WhyDidIReboot.Tests;

public class ElevationTests
{
    private const string Exe = @"C:\Tools\Why Did I Reboot\WhyDidIReboot.exe";

    [Fact]
    public void Relaunch_goes_through_the_shell_with_the_runas_verb()
    {
        var psi = Elevation.RelaunchStartInfo(Exe);
        Assert.Equal(Exe, psi.FileName);
        Assert.Equal("runas", psi.Verb);
        Assert.True(psi.UseShellExecute);
        Assert.Equal(@"C:\Tools\Why Did I Reboot", psi.WorkingDirectory);
    }

    [Fact]
    public void Relaunch_reports_success_a_declined_prompt_and_real_failures_differently()
    {
        ProcessStartInfo? seen = null;
        Assert.True(Elevation.Relaunch(Exe, psi => { seen = psi; return null; }));
        Assert.Equal("runas", seen!.Verb);

        Assert.False(Elevation.Relaunch(Exe, _ => throw new Win32Exception(Elevation.UacCancelled)));

        const int fileNotFound = 2;
        Assert.Throws<Win32Exception>(() => Elevation.Relaunch(Exe, _ => throw new Win32Exception(fileNotFound)));
    }
}
