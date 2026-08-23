using Assimp.Unmanaged;
using System.Linq.Expressions;
using System.Reflection;
using Core.Localization;
using Crosstales.FB.Wrapper.Linux;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using ShapezShifter.SharpDetour;
using System.Runtime.InteropServices;
using Unity.Baselib;
using UnityEngine;
using ILogger = Core.Logging.ILogger;

namespace LinuxFixes;

public class LinuxFixes : IMod {
    private readonly ILogger _logger;
    private readonly IDisposable[]? _hooks;
    private readonly IntPtr[]? _natives;

    public LinuxFixes(ILogger logger) {
        _logger = logger;
        var os = SystemInfo.operatingSystemFamily;
        if (os != OperatingSystemFamily.Linux) { 
            logger.Error?.Log("LinuxFixes was not loaded on linux, what are you doing!?! This mod only fixes things on Linux.");
            return;
        }
        logger.Info?.Log("LinuxFixes detected Linux, loading.");
        
        var directoryName = Path.GetDirectoryName(typeof (LinuxFixes).Assembly.Location);
        if (directoryName == null) {
            logger.Error?.Log("Mod path is null");
            return;
        }

        // load the libcorsstales reimplementation as we need this to replace calls later on.
        var lctfb = dlopen($"{directoryName}/libcrosstales_filebrowser_reimpl.so", /*RTLD_NOW*/0x002);
        var err = Marshal.PtrToStringAnsi(dlerror());
        if (!string.IsNullOrEmpty(err))
            logger.Error?.Log(err);
        logger.Info?.Log($"Loaded libcrosstales_filebrowser_reimpl at {lctfb}");
        // preload minizip as a global library so that when assimp tries to load it, it can find it
        var minizip = dlopen($"{directoryName}/libminizip.so", /*RTLD_NOW*/0x002 | /*RTLD_GLOBAL*/0x100);
        var err2 = Marshal.PtrToStringAnsi(dlerror());
        if (!string.IsNullOrEmpty(err2))
            logger.Error?.Log(err2);
        logger.Info?.Log($"Loaded libminizip at {minizip}");
        _natives = [lctfb, minizip];

        _hooks = [
            // fix crosstales file browser not working on wayland by using a patched version
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFilePanel), Marshal.GetDelegateForFunctionPointer<DialogOpenFilePanelDelegate>(dlsym(lctfb, "DialogOpenFilePanel"))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFolderPanel), Marshal.GetDelegateForFunctionPointer<DialogOpenFolderPanelDelegate>(dlsym(lctfb, "DialogOpenFolderPanel"))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogSaveFilePanel), Marshal.GetDelegateForFunctionPointer<DialogSaveFilePanelDelegate>(dlsym(lctfb, "DialogSaveFilePanel"))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFilePanelAsync), Marshal.GetDelegateForFunctionPointer<DialogOpenFilePanelAsyncDelegate>(dlsym(lctfb, "DialogOpenFilePanelAsync"))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFolderPanelAsync), Marshal.GetDelegateForFunctionPointer<DialogOpenFolderPanelAsyncDelegate>(dlsym(lctfb, "DialogOpenFolderPanelAsync"))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogSaveFilePanelAsync), Marshal.GetDelegateForFunctionPointer<DialogSaveFilePanelAsyncDelegate>(dlsym(lctfb, "DialogSaveFilePanelAsync"))),
            
            // fix assimp using an old libdl version
            new ILHook(typeof(UnmanagedLibrary.UnmanagedLinuxLibraryImplementation).GetMethod("NativeLoadLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!, ReplaceDlMethods),
            new ILHook(typeof(UnmanagedLibrary.UnmanagedLinuxLibraryImplementation).GetMethod("NativeGetProcAddress", BindingFlags.Instance | BindingFlags.NonPublic)!, ReplaceDlMethods),
            new ILHook(typeof(UnmanagedLibrary.UnmanagedLinuxLibraryImplementation).GetMethod("NativeFreeLibrary", BindingFlags.Instance | BindingFlags.NonPublic)!, ReplaceDlMethods),
        ];
    }

    private static void ReplaceDlMethods(ILContext il)
    {
        foreach (var (key, value) in new Dictionary<MethodInfo, MethodInfo> {
                     { DetourHelper.GetRuntimeMethod((string s, int i) => UnmanagedLibrary.UnmanagedLinuxLibraryImplementation.dlopen(s, i)), DetourHelper.GetRuntimeMethod((string s, int i) => dlopen(s, i)) },
                     { DetourHelper.GetRuntimeMethod((IntPtr i, string s) => UnmanagedLibrary.UnmanagedLinuxLibraryImplementation.dlsym(i, s)), DetourHelper.GetRuntimeMethod((IntPtr i, string s) => dlsym(i, s)) },
                     { DetourHelper.GetRuntimeMethod((IntPtr i) => UnmanagedLibrary.UnmanagedLinuxLibraryImplementation.dlclose(i)), DetourHelper.GetRuntimeMethod((IntPtr i) => dlclose(i)) },
                     { DetourHelper.GetRuntimeMethod(() => UnmanagedLibrary.UnmanagedLinuxLibraryImplementation.dlerror()), DetourHelper.GetRuntimeMethod(() => dlerror()) },
                 }) {
            var cursor = new ILCursor(il).Goto(0);
            while (cursor.TryGotoNext(i => i.MatchCall(key))) {
                cursor.Remove();
                cursor.EmitCall(value);
            }
        }
    }

    public void Dispose() {
        if (_hooks != null)
            foreach (var hook in _hooks)
                hook.Dispose();

        if (_natives != null)
            foreach (var native in _natives)
                dlclose(native);
    }
    
    // ReSharper disable InconsistentNaming
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlopen(string fileName, int flags);

    [DllImport("libdl.so.2")]
    private static extern IntPtr dlsym(IntPtr handle, string functionName);

    [DllImport("libdl.so.2")]
    private static extern int dlclose(IntPtr handle);

    [DllImport("libdl.so.2")]
    private static extern IntPtr dlerror();
    
    
    private delegate nint DialogOpenFilePanelDelegate(string title, string directory, string filters, bool multiselect);
    private delegate nint DialogOpenFolderPanelDelegate(string title, string directory, bool multiselect);
    private delegate nint DialogSaveFilePanelDelegate(string title, string directory, string default_name, string filters);
    private delegate void DialogOpenFilePanelAsyncDelegate(string title, string directory, string filters, bool multiselect, NativeMethods.AsyncCallback cb);
    private delegate void DialogOpenFolderPanelAsyncDelegate(string title, string directory, bool multiselect, NativeMethods.AsyncCallback cb);
    private delegate void DialogSaveFilePanelAsyncDelegate(string title, string directory, string default_name, string filters, NativeMethods.AsyncCallback cb);
    
    // ReSharper restore InconsistentNaming
}
public static class LoggerExtensions {
    extension(ILogger thiz) {
        public void Debug(string message) {
            thiz.Debug?.Log(message);
        }

        public void Info(string message) {
            thiz.Info?.Log(message);
        }

        public void Warn(string message) {
            thiz.Warning?.Log(message);
        }

        public void Error(string message) {
            thiz.Error?.Log(message);
        }
    }
}

