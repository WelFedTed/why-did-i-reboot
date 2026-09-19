using System.Globalization;

namespace WhyDidIReboot.Core;

/// <summary>Names and plain-English hints for common STOP (bugcheck) codes.</summary>
public static class BugcheckCatalog
{
    private static readonly Dictionary<uint, (string Name, string Hint)> Codes = new()
    {
        [0x0000000A] = ("IRQL_NOT_LESS_OR_EQUAL", "A driver touched memory it should not have. Usually a faulty driver, sometimes bad RAM."),
        [0x00000019] = ("BAD_POOL_HEADER", "A driver corrupted a kernel memory pool."),
        [0x0000001A] = ("MEMORY_MANAGEMENT", "Windows found corrupt memory structures. Often bad RAM or a driver bug. Run the Windows Memory Diagnostic."),
        [0x0000001E] = ("KMODE_EXCEPTION_NOT_HANDLED", "A kernel-mode program raised an exception nothing handled. Typically a driver."),
        [0x00000024] = ("NTFS_FILE_SYSTEM", "The NTFS driver hit a problem. Check the disk (chkdsk) and the drive's health."),
        [0x0000002E] = ("DATA_BUS_ERROR", "A hardware parity error, usually faulty RAM or cache."),
        [0x0000003B] = ("SYSTEM_SERVICE_EXCEPTION", "An exception happened inside a system service. Commonly graphics or anti-virus drivers."),
        [0x0000003D] = ("INTERRUPT_EXCEPTION_NOT_HANDLED", "An interrupt handler failed. Usually a driver."),
        [0x00000044] = ("MULTIPLE_IRP_COMPLETE_REQUESTS", "A driver completed the same request twice. Driver bug."),
        [0x0000004E] = ("PFN_LIST_CORRUPT", "Memory page list corruption. Often bad RAM."),
        [0x00000050] = ("PAGE_FAULT_IN_NONPAGED_AREA", "Invalid memory was referenced. Faulty driver, bad RAM or a failing disk."),
        [0x00000051] = ("REGISTRY_ERROR", "The registry could not be read or written. Possible disk problem."),
        [0x0000007A] = ("KERNEL_DATA_INPAGE_ERROR", "Windows could not read paged kernel data from disk. Check the drive and cables."),
        [0x0000007B] = ("INACCESSIBLE_BOOT_DEVICE", "Windows lost access to the boot drive. Storage driver or controller mode change."),
        [0x0000007E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "A system thread raised an unhandled exception. The faulting driver is usually named in the dump."),
        [0x0000007F] = ("UNEXPECTED_KERNEL_MODE_TRAP", "A CPU trap the kernel did not expect. Overclocking, bad RAM or a driver."),
        [0x0000008E] = ("KERNEL_MODE_EXCEPTION_NOT_HANDLED", "Unhandled kernel exception. Usually a driver."),
        [0x0000009F] = ("DRIVER_POWER_STATE_FAILURE", "A driver did not respond to a power change (sleep, wake or shutdown) in time. Often USB, network, audio or graphics drivers."),
        [0x000000A0] = ("INTERNAL_POWER_ERROR", "The power manager hit a fatal error, often while hibernating."),
        [0x000000BE] = ("ATTEMPTED_WRITE_TO_READONLY_MEMORY", "A driver wrote to read-only memory."),
        [0x000000C2] = ("BAD_POOL_CALLER", "A driver made a bad memory pool request."),
        [0x000000C4] = ("DRIVER_VERIFIER_DETECTED_VIOLATION", "Driver Verifier caught a misbehaving driver."),
        [0x000000C5] = ("DRIVER_CORRUPTED_EXPOOL", "A driver corrupted kernel pool memory."),
        [0x000000D1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "A driver accessed pageable memory at the wrong time. Driver bug, often network or storage."),
        [0x000000D5] = ("DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL", "A driver used memory after freeing it."),
        [0x000000DE] = ("POOL_CORRUPTION_IN_FILE_AREA", "A driver corrupted file-system pool memory."),
        [0x000000E2] = ("MANUALLY_INITIATED_CRASH", "The crash was triggered on purpose (keyboard crash shortcut or NMI)."),
        [0x000000EA] = ("THREAD_STUCK_IN_DEVICE_DRIVER", "The display driver hung in an infinite loop."),
        [0x000000EF] = ("CRITICAL_PROCESS_DIED", "A critical Windows process (csrss, wininit, svchost...) ended. Corrupt system files, disk or malware."),
        [0x000000F4] = ("CRITICAL_OBJECT_TERMINATION", "A critical process or thread ended. Often disk or storage related."),
        [0x000000F5] = ("FLTMGR_FILE_SYSTEM", "A file-system filter driver (anti-virus, backup) failed."),
        [0x000000F7] = ("DRIVER_OVERRAN_STACK_BUFFER", "A driver overran a stack buffer."),
        [0x000000FC] = ("ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", "Code ran from non-executable memory. Driver bug or bad RAM."),
        [0x00000101] = ("CLOCK_WATCHDOG_TIMEOUT", "A CPU core stopped responding. Overclock, BIOS, CPU or power delivery problems."),
        [0x00000109] = ("CRITICAL_STRUCTURE_CORRUPTION", "Kernel code or data was modified. Driver bug, bad RAM or a rootkit."),
        [0x0000010D] = ("WDF_VIOLATION", "A framework-based driver broke a rule. Often USB or camera drivers."),
        [0x0000010E] = ("VIDEO_MEMORY_MANAGEMENT_INTERNAL", "The graphics memory manager hit an internal error."),
        [0x00000116] = ("VIDEO_TDR_FAILURE", "The graphics driver stopped responding and could not recover. GPU driver, overheating or a failing card."),
        [0x00000117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "The graphics driver timed out and was reset."),
        [0x00000119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "The graphics scheduler hit a fatal error. GPU driver."),
        [0x00000122] = ("WHEA_INTERNAL_ERROR", "Hardware error reporting itself failed."),
        [0x00000124] = ("WHEA_UNCORRECTABLE_ERROR", "The CPU or hardware reported a fatal machine-check error. Overclock, heat, power or failing hardware."),
        [0x00000133] = ("DPC_WATCHDOG_VIOLATION", "A driver ran too long at high priority. Often storage (SSD firmware/driver) or network drivers."),
        [0x00000135] = ("REGISTRY_FILTER_DRIVER_EXCEPTION", "A registry filter driver (usually security software) failed."),
        [0x00000139] = ("KERNEL_SECURITY_CHECK_FAILURE", "The kernel detected corrupted data. Driver bug or bad RAM."),
        [0x0000013A] = ("KERNEL_MODE_HEAP_CORRUPTION", "A driver corrupted kernel heap memory."),
        [0x00000141] = ("VIDEO_ENGINE_TIMEOUT_DETECTED", "A graphics engine stopped responding (GPU hang)."),
        [0x00000144] = ("BUGCODE_USB3_DRIVER", "The USB 3 driver hit a fatal error."),
        [0x00000154] = ("UNEXPECTED_STORE_EXCEPTION", "The memory compression store hit an error. Disk or RAM."),
        [0x0000015E] = ("BUGCODE_NDIS_DRIVER_LIVE_DUMP", "A network driver problem was captured."),
        [0x00000161] = ("LIVE_SYSTEM_DUMP", "A live system dump was requested."),
        [0x00000162] = ("KERNEL_AUTO_BOOST_INVALID_LOCK_RELEASE", "A driver released a lock it did not own."),
        [0x00000193] = ("VIDEO_DXGKRNL_LIVEDUMP", "The DirectX graphics kernel captured a problem."),
        [0x000001A8] = ("VIDEO_DXGKRNL_BLACK_SCREEN_LIVEDUMP", "A black screen event was captured by the graphics kernel."),
        [0x000001C7] = ("STORE_DATA_STRUCTURE_CORRUPTION", "The memory store's data structures were corrupted."),
        [0x000001CA] = ("SYNTHETIC_WATCHDOG_TIMEOUT", "The system stopped responding (synthetic watchdog)."),
        [0x000001CC] = ("EXRESOURCE_TIMEOUT_LIVEDUMP", "A kernel lock was held too long (resource timeout). Often storage or a driver hang."),
        [0x000001D3] = ("WFP_INVALID_OPERATION", "The Windows Filtering Platform (firewall) hit an invalid operation."),
        [0x000001E4] = ("VIDEO_DXGKRNL_SYSMM_FATAL_ERROR", "The graphics kernel's system memory manager failed."),
        [0x000001E7] = ("SMB_SERVER_LIVEDUMP", "The SMB file server captured a problem."),
        [0xC000021A] = ("STATUS_SYSTEM_PROCESS_TERMINATED", "A critical system process (winlogon or csrss) crashed. Corrupt system files or a bad driver."),
        [0xC0000221] = ("STATUS_IMAGE_CHECKSUM_MISMATCH", "A system file or driver is corrupt on disk."),
    };

    public static string Name(uint code) =>
        Codes.TryGetValue(code, out var e) ? e.Name : "Unknown bugcheck";

    public static string? Hint(uint code) =>
        Codes.TryGetValue(code, out var e) ? e.Hint : null;

    public static string Format(uint code) => $"0x{code:X8} {Name(code)}";

    /// <summary>Parses a decimal value ("159") as written by Kernel-Power 41.</summary>
    public static uint? ParseDecimal(string? text) =>
        uint.TryParse(text?.Trim(), out var v) ? v : null;

    /// <summary>Parses a hex value ("0x0000009f", "9f") as written by the bugcheck and WER events.</summary>
    public static uint? ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
