using System.Text.RegularExpressions;

namespace WhyDidIReboot.Core;

/// <summary>Plain-English labels for driver and system module file names that show up in crash data and log messages.</summary>
public static partial class DriverCatalog
{
    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows kernel and core
        ["ntoskrnl.exe"] = "Windows kernel",
        ["hal.dll"] = "Windows hardware abstraction layer",
        ["ntfs.sys"] = "NTFS file system driver",
        ["fltmgr.sys"] = "file system filter manager",
        ["win32k.sys"] = "Windows GUI subsystem",
        ["win32kbase.sys"] = "Windows GUI subsystem",
        ["win32kfull.sys"] = "Windows GUI subsystem",
        ["dxgkrnl.sys"] = "DirectX graphics kernel",
        ["dxgmms2.sys"] = "DirectX graphics memory manager",
        ["watchdog.sys"] = "Windows watchdog timer driver",
        ["wdf01000.sys"] = "Windows Driver Framework runtime",
        ["ACPI.sys"] = "ACPI firmware interface driver",
        ["pci.sys"] = "PCI bus driver",
        ["intelppm.sys"] = "Intel processor power management driver",
        ["amdppm.sys"] = "AMD processor power management driver",
        ["storport.sys"] = "Storport storage driver",
        ["stornvme.sys"] = "Microsoft NVMe storage driver",
        ["storahci.sys"] = "Microsoft AHCI SATA driver",
        ["iaStorAC.sys"] = "Intel Rapid Storage (RST) driver",
        ["iaStorVD.sys"] = "Intel Rapid Storage (RST) driver",
        ["iaStorV.sys"] = "Intel Rapid Storage (RST) driver",
        ["disk.sys"] = "disk class driver",
        ["classpnp.sys"] = "storage class driver",
        ["volmgr.sys"] = "volume manager",
        ["volsnap.sys"] = "volume shadow copy driver",
        ["partmgr.sys"] = "partition manager",
        ["ndis.sys"] = "Windows network driver interface (NDIS)",
        ["tcpip.sys"] = "Windows TCP/IP stack",
        ["netio.sys"] = "Windows network I/O subsystem",
        ["afd.sys"] = "Windows sockets (Winsock) driver",
        ["fwpkclnt.sys"] = "Windows Filtering Platform (firewall)",
        ["srv2.sys"] = "SMB file server driver",
        ["mrxsmb.sys"] = "SMB network client driver",
        ["usbxhci.sys"] = "USB 3 host controller driver",
        ["usbhub3.sys"] = "USB 3 hub driver",
        ["usbport.sys"] = "USB host controller driver",
        ["usbccgp.sys"] = "USB composite device driver",
        ["hidclass.sys"] = "HID (input device) class driver",
        ["hidusb.sys"] = "USB HID driver",
        ["kbdclass.sys"] = "keyboard class driver",
        ["mouclass.sys"] = "mouse class driver",
        ["HDAudBus.sys"] = "High Definition Audio bus driver",
        ["portcls.sys"] = "Windows audio port class driver",
        ["ks.sys"] = "Windows kernel streaming (audio/video)",
        ["bthport.sys"] = "Windows Bluetooth driver",
        ["bthusb.sys"] = "Windows Bluetooth USB driver",
        ["vmbus.sys"] = "Hyper-V virtual machine bus",
        ["rdyboost.sys"] = "ReadyBoost driver",
        ["cng.sys"] = "Windows cryptography driver",
        ["ksecdd.sys"] = "Windows kernel security driver",
        ["WdFilter.sys"] = "Microsoft Defender file system filter",
        ["WdBoot.sys"] = "Microsoft Defender early-launch driver",
        ["WdNisDrv.sys"] = "Microsoft Defender network inspection driver",
        ["MpKsl.sys"] = "Microsoft Defender kernel driver",

        // Graphics
        ["nvlddmkm.sys"] = "NVIDIA kernel-mode display driver",
        ["nvhda64v.sys"] = "NVIDIA HDMI audio driver",
        ["nvvad64v.sys"] = "NVIDIA virtual audio driver",
        ["amdkmdag.sys"] = "AMD Radeon display driver",
        ["amdkmdap.sys"] = "AMD Radeon display driver",
        ["atikmdag.sys"] = "AMD/ATI Radeon display driver",
        ["atikmpag.sys"] = "AMD/ATI Radeon display driver",
        ["amdfendr.sys"] = "AMD display driver helper",
        ["igdkmd64.sys"] = "Intel graphics driver",
        ["igdkmdn64.sys"] = "Intel graphics driver",
        ["igdkmdnd64.sys"] = "Intel Arc graphics driver",

        // Network
        ["e1dexpress.sys"] = "Intel Ethernet driver",
        ["e1rexpress.sys"] = "Intel Ethernet driver",
        ["e2fexpress.sys"] = "Intel Ethernet driver",
        ["rt640x64.sys"] = "Realtek Ethernet driver",
        ["rtwlane.sys"] = "Realtek wireless adapter driver",
        ["rtwlane01.sys"] = "Realtek wireless adapter driver",
        ["rtwlanu.sys"] = "Realtek USB wireless adapter driver",
        ["mtkwl6ex.sys"] = "MediaTek wireless adapter driver",
        ["mtkwlan.sys"] = "MediaTek wireless adapter driver",
        ["athw8x.sys"] = "Qualcomm Atheros wireless adapter driver",
        ["athwbx.sys"] = "Qualcomm Atheros wireless adapter driver",
        ["bcmwl63a.sys"] = "Broadcom wireless adapter driver",
        ["kwl.sys"] = "Killer wireless adapter driver",
        ["ibtusb.sys"] = "Intel Bluetooth driver",

        // Audio and peripherals
        ["RTKVHD64.sys"] = "Realtek HD audio driver",
        ["rzudd.sys"] = "Razer device driver",
        ["rzdev.sys"] = "Razer device driver",
        ["LGBusEnum.sys"] = "Logitech gaming device driver",
        ["lgcoretemp.sys"] = "Logitech G HUB CPU temperature driver",
        ["CorsairVBusDriver.sys"] = "Corsair iCUE virtual bus driver",
        ["SteelSeries.sys"] = "SteelSeries device driver",
        ["FTDIBUS.sys"] = "FTDI USB serial driver",

        // Anti-cheat, security and virtualisation
        ["vgk.sys"] = "Riot Vanguard anti-cheat driver",
        ["EasyAntiCheat.sys"] = "Easy Anti-Cheat driver",
        ["EasyAntiCheat_EOS.sys"] = "Easy Anti-Cheat driver",
        ["BEDaisy.sys"] = "BattlEye anti-cheat driver",
        ["faceit.sys"] = "FACEIT anti-cheat driver",
        ["klif.sys"] = "Kaspersky filter driver",
        ["aswSP.sys"] = "Avast self-protection driver",
        ["aswbidsdriver.sys"] = "Avast behaviour shield driver",
        ["mfewfpk.sys"] = "McAfee firewall driver",
        ["SRTSP64.SYS"] = "Norton/Symantec real-time protection driver",
        ["csagent.sys"] = "CrowdStrike Falcon sensor",
        ["VBoxDrv.sys"] = "VirtualBox support driver",
        ["vmci.sys"] = "VMware VMCI bus driver",
        ["vmx86.sys"] = "VMware virtual machine monitor",
        ["hvsocket.sys"] = "Hyper-V socket driver",
        ["WinRing0x64.sys"] = "WinRing0 hardware access driver (used by monitoring tools)",
        ["cpuz.sys"] = "CPU-Z hardware access driver",
        ["HWiNFO64A.sys"] = "HWiNFO hardware access driver",
        ["AsIO3.sys"] = "ASUS hardware access driver",
        ["AsUpIO.sys"] = "ASUS hardware access driver",
        ["MSIO64.sys"] = "MSI hardware access driver",
        ["NTIOLib_X64.sys"] = "MSI hardware access driver",
        ["GPU-Z.sys"] = "GPU-Z hardware access driver",
    };

    // Families whose file names carry a number, e.g. Netwtw04.sys, Netwtw06.sys, Netwtw10.sys, MpKsl1a2b.sys
    private static readonly (Regex Pattern, string Label)[] Families =
    {
        (new Regex(@"^Netwtw\d+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel wireless adapter driver"),
        (new Regex(@"^Netwsw\d+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel wireless adapter driver"),
        (new Regex(@"^Netwbw\d+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel wireless adapter driver"),
        (new Regex(@"^Netwlv\d+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel wireless adapter driver"),
        (new Regex(@"^rtwlan[eu]\d*\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Realtek wireless adapter driver"),
        (new Regex(@"^MpKsl[0-9a-f]*\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Microsoft Defender kernel driver"),
        (new Regex(@"^e1[a-z]\d*express\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel Ethernet driver"),
        (new Regex(@"^igdkmd\w*\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "Intel graphics driver"),
        (new Regex(@"^nv\w+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "NVIDIA driver"),
        (new Regex(@"^amd\w+\.sys$", RegexOptions.IgnoreCase | RegexOptions.Compiled), "AMD driver"),
    };

    [GeneratedRegex(@"\b[A-Za-z0-9_\-]+\.(?:sys|SYS|exe|dll)\b")]
    private static partial Regex ModuleName();

    /// <summary>"nvlddmkm.sys" → "NVIDIA kernel-mode display driver", or null when unknown.</summary>
    public static string? Label(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var name = fileName.Trim();
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0) name = name[(slash + 1)..];
        if (Exact.TryGetValue(name, out var label)) return label;
        foreach (var (pattern, family) in Families)
            if (pattern.IsMatch(name)) return family;
        return null;
    }

    /// <summary>Appends " (label)" after every known driver name in the text, once per occurrence, e.g. "nvlddmkm.sys (NVIDIA kernel-mode display driver)".</summary>
    public static string Annotate(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('.')) return text ?? "";
        return ModuleName().Replace(text, m =>
        {
            var label = Label(m.Value);
            if (label is null) return m.Value;
            // Skip if the text already carries a label right after the name.
            var after = text.AsSpan(m.Index + m.Length).TrimStart();
            return after.StartsWith("(") ? m.Value : $"{m.Value} ({label})";
        });
    }
}
