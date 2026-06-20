using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AuraToolsExp.Dll.Infrastructure;

public enum OptionalFileDialogStatus
{
    Selected,
    Cancelled,
    Unavailable,
    Error
}

public sealed class OptionalFileDialogResult
{
    public OptionalFileDialogStatus Status { get; set; }

    public string Path { get; set; } = "";

    public string Message { get; set; } = "";

    public bool Selected => Status == OptionalFileDialogStatus.Selected && !string.IsNullOrWhiteSpace(Path);
}

public sealed class OptionalFileDialogFilter
{
    public OptionalFileDialogFilter(string name, string spec)
    {
        Name = name;
        Spec = spec;
    }

    public string Name { get; }

    public string Spec { get; }
}

public static class OptionalFileDialog
{
    private const int HResultCancelled = unchecked((int)0x800704C7);
    private const string DispatcherName = "AuraTools.FileDialogDispatcher";
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();
    private static readonly object DispatcherLock = new();
    private static FileDialogDispatcher? dispatcher;
    private static int requestSequence;

    public static string PickAudioFile()
    {
        AuraToolsLog.Warn("Synchronous file picker is disabled; use PickAudioFileAsync.");
        return "";
    }

    public static OptionalFileDialogResult PickAudioFileDetailed(string initialDirectory = "")
    {
        AuraToolsLog.Warn("Synchronous file picker is disabled; use PickAudioFileAsync.");
        return Unavailable("Synchronous file picker is disabled.");
    }

    public static void PickAudioFileAsync(string initialDirectory, Action<OptionalFileDialogResult> completed)
    {
        PickFileAsync(
            "选择音频文件",
            new[]
            {
                new OptionalFileDialogFilter("音频文件", "*.mp3;*.wav;*.ogg"),
                new OptionalFileDialogFilter("所有文件", "*.*")
            },
            "mp3",
            initialDirectory,
            completed);
    }

    public static void PickImageFileAsync(string initialDirectory, Action<OptionalFileDialogResult> completed)
    {
        PickFileAsync(
            "选择图片文件",
            new[]
            {
                new OptionalFileDialogFilter("图片文件", "*.png;*.jpg;*.jpeg"),
                new OptionalFileDialogFilter("所有文件", "*.*")
            },
            "png",
            initialDirectory,
            completed);
    }

    public static void PickFileAsync(
        string title,
        OptionalFileDialogFilter[] filters,
        string defaultExtension,
        string initialDirectory,
        Action<OptionalFileDialogResult> completed)
    {
        if (completed == null)
        {
            return;
        }

        EnsureDispatcher();
        var requestId = Interlocked.Increment(ref requestSequence);
        AuraToolsLog.Info("[FileDialog] opening request " + requestId + ": " + title);

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            EnqueueOnMainThread(() => completed(Unavailable("File picker is only implemented on Windows.")));
            return;
        }

        var thread = new Thread(() =>
        {
            OptionalFileDialogResult result;
            try
            {
                result = PickFileOnSta(title, filters, defaultExtension, initialDirectory);
            }
            catch (Exception ex)
            {
                result = Error(ex.Message);
            }

            EnqueueOnMainThread(() =>
            {
                AuraToolsLog.Info("[FileDialog] finished request " + requestId + ": " + result.Status);
                completed(result);
            });
        })
        {
            IsBackground = true,
            Name = "AuraTools.FileDialog." + requestId
        };

        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[FileDialog] failed to start picker thread: " + ex.Message);
            EnqueueOnMainThread(() => completed(Error(ex.Message)));
        }
    }

    private static OptionalFileDialogResult PickFileOnSta(
        string title,
        OptionalFileDialogFilter[] filters,
        string defaultExtension,
        string initialDirectory)
    {
        IFileDialog? dialog = null;
        IShellItem? folder = null;
        IShellItem? item = null;
        IntPtr fileNamePtr = IntPtr.Zero;

        try
        {
            dialog = (IFileDialog)new FileOpenDialog();
            dialog.SetOptions(FileOpenOptions.ForceFileSystem
                              | FileOpenOptions.FileMustExist
                              | FileOpenOptions.PathMustExist
                              | FileOpenOptions.NoChangeDir);
            dialog.SetTitle(title);
            dialog.SetFileTypes((uint)filters.Length, ToFilterSpecs(filters));
            dialog.SetFileTypeIndex(1);
            dialog.SetDefaultExtension(defaultExtension);

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                TrySetInitialDirectory(dialog, initialDirectory, out folder);
            }

            var hresult = dialog.Show(GetForegroundWindow());
            if (hresult == HResultCancelled)
            {
                return new OptionalFileDialogResult
                {
                    Status = OptionalFileDialogStatus.Cancelled,
                    Message = "cancelled"
                };
            }

            if (hresult < 0)
            {
                Marshal.ThrowExceptionForHR(hresult);
            }

            dialog.GetResult(out item);
            item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out fileNamePtr);
            var path = Marshal.PtrToStringUni(fileNamePtr) ?? "";
            return string.IsNullOrWhiteSpace(path)
                ? Error("Selected file path is empty.")
                : new OptionalFileDialogResult
                {
                    Status = OptionalFileDialogStatus.Selected,
                    Path = path,
                    Message = "selected"
                };
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[FileDialog] unavailable: " + ex.Message);
            return Error(ex.Message);
        }
        finally
        {
            if (fileNamePtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileNamePtr);
            }

            ReleaseCom(item);
            ReleaseCom(folder);
            ReleaseCom(dialog);
        }
    }

    private static FilterSpec[] ToFilterSpecs(OptionalFileDialogFilter[] filters)
    {
        if (filters.Length == 0)
        {
            return new[] { new FilterSpec("所有文件", "*.*") };
        }

        var specs = new FilterSpec[filters.Length];
        for (var i = 0; i < filters.Length; i++)
        {
            specs[i] = new FilterSpec(filters[i].Name, filters[i].Spec);
        }

        return specs;
    }

    private static void TrySetInitialDirectory(IFileDialog dialog, string initialDirectory, out IShellItem? folder)
    {
        folder = null;
        try
        {
            var shellItemId = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref shellItemId, out folder);
            if (folder != null)
            {
                dialog.SetFolder(folder);
            }
        }
        catch (Exception ex)
        {
            AuraToolsLog.Warn("[FileDialog] initial directory failed: " + ex.Message);
        }
    }

    private static void EnsureDispatcher()
    {
        if (dispatcher != null)
        {
            return;
        }

        lock (DispatcherLock)
        {
            if (dispatcher != null)
            {
                return;
            }

            var existing = GameObject.Find(DispatcherName);
            if (existing != null)
            {
                dispatcher = existing.GetComponent<FileDialogDispatcher>();
                if (dispatcher != null)
                {
                    return;
                }
            }

            var go = new GameObject(DispatcherName);
            dispatcher = go.AddComponent<FileDialogDispatcher>();
            Object.DontDestroyOnLoad(go);
        }
    }

    private static void EnqueueOnMainThread(Action action)
    {
        MainThreadActions.Enqueue(action);
    }

    private static OptionalFileDialogResult Unavailable(string message)
    {
        AuraToolsLog.Warn("[FileDialog] unavailable: " + message);
        return new OptionalFileDialogResult
        {
            Status = OptionalFileDialogStatus.Unavailable,
            Message = message
        };
    }

    private static OptionalFileDialogResult Error(string message)
    {
        return new OptionalFileDialogResult
        {
            Status = OptionalFileDialogStatus.Error,
            Message = message
        };
    }

    private static void ReleaseCom(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private sealed class FileDialogDispatcher : MonoBehaviour
    {
        private void Update()
        {
            while (MainThreadActions.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    AuraToolsLog.Error("[FileDialog] callback failed", ex);
                }
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr bindContext,
        ref Guid interfaceId,
        out IShellItem shellItem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [ComImport]
    [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialog
    {
    }

    [ComImport]
    [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filterSpecs);

        void SetFileTypeIndex(uint fileTypeIndex);

        void GetFileTypeIndex(out uint fileTypeIndex);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(FileOpenOptions options);

        void GetOptions(out FileOpenOptions options);

        void SetDefaultFolder(IShellItem shellItem);

        void SetFolder(IShellItem shellItem);

        void GetFolder(out IShellItem shellItem);

        void GetCurrentSelection(out IShellItem shellItem);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string fileName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem shellItem);

        void AddPlace(IShellItem shellItem, FileDialogAddPlace addPlace);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);

        void Close(int hresult);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(
            IntPtr bindContext,
            ref Guid handlerId,
            ref Guid interfaceId,
            out IntPtr result);

        void GetParent(out IShellItem shellItem);

        void GetDisplayName(ShellItemDisplayName displayName, out IntPtr name);

        void GetAttributes(uint attributeMask, out uint attributes);

        void Compare(IShellItem shellItem, uint hint, out int order);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;

        public FilterSpec(string name, string spec)
        {
            Name = name;
            Spec = spec;
        }
    }

    [Flags]
    private enum FileOpenOptions : uint
    {
        NoChangeDir = 0x00000008,
        ForceFileSystem = 0x00000040,
        PathMustExist = 0x00000800,
        FileMustExist = 0x00001000
    }

    private enum FileDialogAddPlace
    {
        Bottom = 0,
        Top = 1
    }

    private enum ShellItemDisplayName : uint
    {
        FileSystemPath = 0x80058000
    }
}
