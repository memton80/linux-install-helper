using System.Runtime.InteropServices;

namespace LinuxInstallHelper.App.Services;

public interface IFilePickerService
{
    /// <summary>Lets the user pick an ISO file. Returns null when cancelled.</summary>
    string? PickIsoFile(string title, string filterName);

    /// <summary>Lets the user pick a folder. Returns null when cancelled.</summary>
    string? PickFolder(string title, string? initialFolder);
}

/// <summary>
/// Win32 common item dialog. The WinRT pickers do not work in an elevated (administrator) process,
/// which this application always is.
/// </summary>
public sealed class FilePickerService : IFilePickerService
{
    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosPathMustExist = 0x00000800;
    private const uint FosFileMustExist = 0x00001000;
    private const uint SigdnFileSysPath = 0x80058000;
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    public string? PickIsoFile(string title, string filterName)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
        try
        {
            dialog.SetOptions(FosForceFileSystem | FosPathMustExist | FosFileMustExist);
            dialog.SetTitle(title);
            dialog.SetFileTypes(1, [new FilterSpec { Name = filterName, Spec = "*.iso" }]);
            return Show(dialog);
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    public string? PickFolder(string title, string? initialFolder)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
        try
        {
            dialog.SetOptions(FosPickFolders | FosForceFileSystem | FosPathMustExist);
            dialog.SetTitle(title);
            if (!string.IsNullOrEmpty(initialFolder) && Directory.Exists(initialFolder)
                && SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
            {
                dialog.SetFolder(folder);
            }

            return Show(dialog);
        }
        finally
        {
            Marshal.FinalReleaseComObject(dialog);
        }
    }

    private static string? Show(IFileOpenDialog dialog)
    {
        var owner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var hr = dialog.Show(owner);
        if (hr == ErrorCancelled)
        {
            return null;
        }

        Marshal.ThrowExceptionForHR(hr);
        dialog.GetResult(out var item);
        item.GetDisplayName(SigdnFileSysPath, out var path);
        return path;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw
    {
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;
    }

    [ComImport]
    [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr parent);

        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem folder);

        void SetFolder(IShellItem folder);

        void GetFolder(out IShellItem folder);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, int placement);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);

        void GetResults(out IntPtr items);

        void GetSelectedItems(out IntPtr items);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, [MarshalAs(UnmanagedType.LPWStr)] out string name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }
}
