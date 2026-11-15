using System.Reflection;
using UnityEngine;
using ILogger = Core.Logging.ILogger;

namespace LinuxFixes;

/// <summary>
/// Wrapper class that prevents LinuxFixes from loading its types on non-linux operating systems.
/// </summary>
public class LinuxFreeWrapper : IMod {
    private IDisposable? mod;

    static LinuxFreeWrapper() {
        Console.WriteLine("LinuxFreeWrapper");
    }
    
    public LinuxFreeWrapper(ILogger logger) {
        var os = SystemInfo.operatingSystemFamily;
        if (os != OperatingSystemFamily.Linux) { 
            logger.Error?.Log("LinuxFixes was not loaded on linux, what are you doing!?! This mod only fixes things on Linux.");
            return;
        }

        mod = CreateMod(logger);
    }

    private static IDisposable CreateMod(ILogger logger) {
        return new LinuxFixes(logger);
    }
    
    public void Dispose() {
        if (mod != null)
            mod.Dispose();
    }
}