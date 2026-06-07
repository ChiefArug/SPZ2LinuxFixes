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
    private readonly IntPtr? _native;

    public LinuxFixes(ILogger logger) {
        _logger = logger;
        var os = SystemInfo.operatingSystemFamily;
        if (os != OperatingSystemFamily.Linux) { 
            logger.Error?.Log("CtfbFix was not loaded on linux, what are you doing!?! This mod only fixes things on Linux.");
            return;
        }
        logger.Info?.Log("CtfbFix detected Linux, loading.");
        
        var directoryName = Path.GetDirectoryName(typeof (LinuxFixes).Assembly.Location);
        if (directoryName == null) {
            logger.Error?.Log("Mod path is null");
            return;
        }

        var native = dlopen($"{directoryName}/libcrosstales_filebrowser_reimpl.so", 2);
        _native = native;

        _hooks = [
            // fix crosstales file browser not working on wayland by using a patched version
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFilePanel), Marshal.GetDelegateForFunctionPointer<DialogOpenFilePanelDelegate>(dlsym(native, nameof(DialogOpenFilePanel)))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFolderPanel), Marshal.GetDelegateForFunctionPointer<DialogOpenFolderPanelDelegate>(dlsym(native, nameof(DialogOpenFolderPanel)))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogSaveFilePanel), Marshal.GetDelegateForFunctionPointer<DialogSaveFilePanelDelegate>(dlsym(native, nameof(DialogSaveFilePanel)))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFilePanelAsync), Marshal.GetDelegateForFunctionPointer<DialogOpenFilePanelAsyncDelegate>(dlsym(native, nameof(DialogOpenFilePanelAsync)))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogOpenFolderPanelAsync), Marshal.GetDelegateForFunctionPointer<DialogOpenFolderPanelAsyncDelegate>(dlsym(native, nameof(DialogOpenFolderPanelAsync)))),
            new NativeHook(Marshal.GetFunctionPointerForDelegate(NativeMethods.DialogSaveFilePanelAsync), Marshal.GetDelegateForFunctionPointer<DialogSaveFilePanelAsyncDelegate>(dlsym(native, nameof(DialogSaveFilePanelAsync)))),
            
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
        if (_hooks != null) {
            foreach (var hook in _hooks) {
                hook.Dispose();
            }
        }

        if (_native != null)
            dlclose(_native.Value);
    }
    
    // ReSharper disable InconsistentNaming
    [DllImport("libdl.so.2")]
    private static extern IntPtr dlopen(string fileName, int flags);

    [DllImport("libdl.so.2")]
    private static extern IntPtr dlsym(IntPtr handle, string functionName);

    [DllImport("libdl.so.2")]
    private static extern int dlclose(IntPtr handle);

    [DllImport("libdl.so.2")]
    public static extern IntPtr dlerror();
    
    
    /// open a synchronous file dialog
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern nint DialogOpenFilePanel(string title, string directory, string filters, bool multiselect);
    private delegate nint DialogOpenFilePanelDelegate(string title, string directory, string filters, bool multiselect);

    /// open a synchronous folder dialog
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern nint DialogOpenFolderPanel(string title, string directory, bool multiselect);
    private delegate nint DialogOpenFolderPanelDelegate(string title, string directory, bool multiselect);

    /// open a synchronous save file dialog
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern nint DialogSaveFilePanel(string title, string directory, string default_name, string filters);
    private delegate nint DialogSaveFilePanelDelegate(string title, string directory, string default_name, string filters);


    /// open an asynchronous file dialog, calling the callback later from another thread 
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern void DialogOpenFilePanelAsync(string title, string directory, string filters, bool multiselect, NativeMethods.AsyncCallback cb);
    private delegate void DialogOpenFilePanelAsyncDelegate(string title, string directory, string filters, bool multiselect, NativeMethods.AsyncCallback cb);
    
    /// open an asynchronous folder dialog, calling the callback later from another thread 
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern void DialogOpenFolderPanelAsync(string title, string directory, bool multiselect, NativeMethods.AsyncCallback cb);
    private delegate void DialogOpenFolderPanelAsyncDelegate(string title, string directory, bool multiselect, NativeMethods.AsyncCallback cb);
    
    /// open an asynchronous save file dialog, calling the callback later from another thread 
    [DllImport("libcrosstales_filebrowser_reimpl.so")]
    private static extern void DialogSaveFilePanelAsync(string title, string directory, string default_name, string filters, NativeMethods.AsyncCallback cb);
    private delegate void DialogSaveFilePanelAsyncDelegate(string title, string directory, string default_name, string filters, NativeMethods.AsyncCallback cb);
    
    // ReSharper restore InconsistentNaming
}