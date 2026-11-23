using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace March7thHoney.WebServer.Services;

/// <summary>
///     Host diagnostics for the MUIP server-info endpoint and the admin overview. Static facts (CPU model and OS
///     version) are read once per process; CPU usage is sampled at most once per <see cref="CpuSampleInterval" />
///     so a request never waits for a measurement window.
/// </summary>
public static class HostMetrics
{
    /// <summary>Minimum spacing between two CPU usage measurements; requests in between reuse the last value.</summary>
    private static readonly TimeSpan CpuSampleInterval = TimeSpan.FromSeconds(1);

    private static readonly Lazy<(string ModelName, int Cores, float Frequency)> CpuDetails =
        new(ReadCpuDetails, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<string> SystemVersion =
        new(ReadSystemVersion, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly object CpuLock = new();
    private static long _lastCpuSampleTicks;
    private static float _lastCpuUsage;
    private static bool _cpuSampled;
    private static PerformanceCounter? _windowsCpuCounter;
    private static bool _windowsCpuCounterFailed;

    /// <summary>Total and available physical memory in MB for the current OS (0 when unavailable).</summary>
    public static (float TotalMb, float AvailableMb) GetMachineMemory()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return GetMemoryWindows();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return (GetTotalMemoryLinux(), GetAvailableMemoryLinux());
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return (GetTotalMemoryMacOS(), GetAvailableMemoryMacOS());
        }
        catch
        {
            // Diagnostics only.
        }

        return (0, 0);
    }

    /// <summary>
    ///     Total and available physical memory in MB as the MUIP server-info endpoint reports it: -1 on an
    ///     unsupported OS, and read errors are not swallowed.
    /// </summary>
    public static (float TotalMb, float AvailableMb) GetMachineMemoryStrict()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetMemoryWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return (GetTotalMemoryLinux(), GetAvailableMemoryLinux());
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return (GetTotalMemoryMacOS(), GetAvailableMemoryMacOS());
        return (-1, -1);
    }

    /// <summary>
    ///     CPU usage in percent. The value is refreshed when the previous sample is older than
    ///     <see cref="CpuSampleInterval" />. On Windows the performance counter reports the average since its previous
    ///     read, so the first call after start-up returns 0 instead of blocking for a measurement window.
    /// </summary>
    public static float GetCpuUsage()
    {
        lock (CpuLock)
        {
            var now = Environment.TickCount64;
            if (_cpuSampled && now - _lastCpuSampleTicks < (long)CpuSampleInterval.TotalMilliseconds)
                return _lastCpuUsage;

            _lastCpuUsage = SampleCpuUsage();
            _lastCpuSampleTicks = now;
            _cpuSampled = true;
            return _lastCpuUsage;
        }
    }

    /// <summary>CPU model, physical core count and maximum frequency in GHz, read once per process.</summary>
    public static (string ModelName, int Cores, float Frequency) GetCpuDetails()
    {
        return CpuDetails.Value;
    }

    /// <summary>Operating system description, read once per process.</summary>
    public static string GetSystemVersion()
    {
        return SystemVersion.Value;
    }

    private static float SampleCpuUsage()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetCpuUsageLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GetCpuUsageWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return GetCpuUsageMacOS();
        return 0;
    }

    private static (string ModelName, int Cores, float Frequency) ReadCpuDetails()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetCpuDetailsLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return GetCpuDetailsWindows();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return GetCpuDetailsMacOS();
        return ("Unknown", Environment.ProcessorCount, 0f);
    }

    private static string ReadSystemVersion()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.OSVersion.VersionString;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetLinuxVersion();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return GetMacOSVersion();
        return RuntimeInformation.OSDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static (float TotalMb, float AvailableMb) GetMemoryWindows()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0);

        // A single native snapshot avoids WMI/PerformanceCounter initialization on the HTTP request path
        // and works under NativeAOT without COM. Read both values together so they describe one sample.
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? (status.TotalPhysical / (1024f * 1024f), status.AvailablePhysical / (1024f * 1024f))
            : (0, 0);
    }

    private static float GetTotalMemoryLinux()
    {
        var lines = File.ReadAllLines("/proc/meminfo");
        foreach (var line in lines)
            if (line.StartsWith("MemTotal"))
                return float.Parse(line.Split(':')[1].Trim().Split(' ')[0]) / 1024;
        return 0;
    }

    private static float GetAvailableMemoryLinux()
    {
        var lines = File.ReadAllLines("/proc/meminfo");
        foreach (var line in lines)
            if (line.StartsWith("MemAvailable"))
                return float.Parse(line.Split(':')[1].Trim().Split(' ')[0]) / 1024;
        return 0;
    }

    private static float GetCpuUsageLinux()
    {
        var lines = File.ReadAllLines("/proc/stat");
        var cpuInfo = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var idleTime = float.Parse(cpuInfo[4]);
        float totalTime = 0;

        for (var i = 1; i < cpuInfo.Length; i++) totalTime += float.Parse(cpuInfo[i]);

        return 100 * (1 - idleTime / totalTime);
    }

    private static float GetCpuUsageWindows()
    {
        if (!OperatingSystem.IsWindows() || _windowsCpuCounterFailed) return 0;

        try
        {
            if (_windowsCpuCounter == null)
            {
                // The first read only establishes the baseline that later reads average against.
                _windowsCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _windowsCpuCounter.NextValue();
                return 0;
            }

            return _windowsCpuCounter.NextValue();
        }
        catch
        {
            // Counters unavailable (disabled performance data, restricted account): stop retrying.
            _windowsCpuCounterFailed = true;
            _windowsCpuCounter?.Dispose();
            _windowsCpuCounter = null;
            return 0;
        }
    }

    private static (string ModelName, int Cores, float Frequency) GetCpuDetailsLinux()
    {
        var lines = File.ReadAllLines("/proc/cpuinfo");
        var modelName = "";
        var cores = 0;
        float frequency = 0;

        foreach (var line in lines)
        {
            if (line.StartsWith("model name")) modelName = line.Split(':')[1].Trim();
            if (line.StartsWith("cpu cores")) cores = int.Parse(line.Split(':')[1].Trim());
            if (line.StartsWith("cpu MHz")) frequency = float.Parse(line.Split(':')[1].Trim());
        }

        return (modelName, cores, frequency);
    }

    private static (string ModelName, int Cores, float Frequency) GetCpuDetailsWindows()
    {
        var modelName = "Unknown";
        var cores = Environment.ProcessorCount;
        float frequency = 0;
        if (!OperatingSystem.IsWindows()) return (modelName, cores, frequency);

        try
        {
            // WMI (System.Management) is COM-based and throws under NativeAOT — fall back to process info.
            using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
            foreach (var item in searcher.Get())
            {
                modelName = item["Name"]?.ToString() ?? "Unknown";
                cores = int.Parse(item["NumberOfCores"]?.ToString() ?? "0");
                frequency = float.Parse(item["MaxClockSpeed"]?.ToString() ?? "0") / 1000; // MHz to GHz
            }
        }
        catch
        {
            // WMI unavailable (e.g. NativeAOT) — keep the process-derived fallback above.
        }

        return (modelName, cores, frequency);
    }

    private static string GetLinuxVersion()
    {
        var version = string.Empty;
        if (File.Exists("/etc/os-release"))
        {
            var lines = File.ReadAllLines("/etc/os-release");
            foreach (var line in lines)
                if (line.StartsWith("PRETTY_NAME"))
                {
                    version = line.Split('=')[1].Trim('"');
                    break;
                }
        }
        else if (File.Exists("/proc/version"))
        {
            version = File.ReadAllText("/proc/version");
        }

        return version;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static float GetTotalMemoryMacOS()
    {
        return ulong.TryParse(RunCommand("/usr/sbin/sysctl", "-n hw.memsize"), out var bytes)
            ? bytes / 1024f / 1024f
            : 0;
    }

    private static float GetAvailableMemoryMacOS()
    {
        var pageSize = long.TryParse(RunCommand("/usr/sbin/sysctl", "-n hw.pagesize"), out var ps) ? ps : 4096;
        long freePages = 0;
        foreach (var line in RunCommand("/usr/bin/vm_stat", string.Empty).Split('\n'))
        {
            if (!line.StartsWith("Pages free:") && !line.StartsWith("Pages inactive:") &&
                !line.StartsWith("Pages speculative:")) continue;

            var digits = string.Empty;
            foreach (var ch in line)
                if (char.IsDigit(ch))
                    digits += ch;
            if (long.TryParse(digits, out var pages)) freePages += pages;
        }

        return freePages * pageSize / 1024f / 1024f;
    }

    private static float GetCpuUsageMacOS()
    {
        var output = RunCommand("/bin/sh", "-c \"top -l 2 -n 0 | grep 'CPU usage' | tail -1\"");
        var idleIndex = output.IndexOf("% idle", StringComparison.OrdinalIgnoreCase);
        if (idleIndex <= 0) return 0;

        var segment = output[..idleIndex];
        var lastSpace = segment.LastIndexOf(' ');
        return lastSpace >= 0 && float.TryParse(segment[(lastSpace + 1)..], out var idle) ? 100 - idle : 0;
    }

    private static (string ModelName, int Cores, float Frequency) GetCpuDetailsMacOS()
    {
        var modelName = RunCommand("/usr/sbin/sysctl", "-n machdep.cpu.brand_string");
        if (string.IsNullOrEmpty(modelName)) modelName = "Apple Silicon";
        var cores = int.TryParse(RunCommand("/usr/sbin/sysctl", "-n hw.physicalcpu"), out var c)
            ? c
            : Environment.ProcessorCount;
        // hw.cpufrequency is unavailable on Apple Silicon and reports 0 there
        var frequency = long.TryParse(RunCommand("/usr/sbin/sysctl", "-n hw.cpufrequency"), out var f)
            ? f / 1_000_000_000f
            : 0f;
        return (modelName, cores, frequency);
    }

    private static string GetMacOSVersion()
    {
        var name = RunCommand("/usr/bin/sw_vers", "-productName");
        var version = RunCommand("/usr/bin/sw_vers", "-productVersion");
        var build = RunCommand("/usr/bin/sw_vers", "-buildVersion");
        var combined = $"{name} {version} ({build})".Trim();
        return string.IsNullOrWhiteSpace(name) ? RuntimeInformation.OSDescription : combined;
    }
}
