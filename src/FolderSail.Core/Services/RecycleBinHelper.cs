using System.Runtime.InteropServices;

namespace FolderSail.Core.Services;

internal static class RecycleBinHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public FileOperationFunc wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public FileOperationFlags fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [Flags]
    private enum FileOperationFlags : ushort
    {
        Silent = 0x0004,
        NoConfirmation = 0x0010,
        AllowUndo = 0x0040,
        NoErrorUI = 0x0400
    }

    private enum FileOperationFunc : uint
    {
        Delete = 0x0003
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static void SendToRecycleBin(IEnumerable<string> paths)
    {
        var joined = string.Join('\0', paths.Select(Path.GetFullPath)) + '\0' + '\0';
        var fileOp = new SHFILEOPSTRUCT
        {
            wFunc = FileOperationFunc.Delete,
            pFrom = joined,
            fFlags = FileOperationFlags.AllowUndo | FileOperationFlags.NoConfirmation | FileOperationFlags.NoErrorUI
        };

        var result = SHFileOperation(ref fileOp);
        if (result != 0)
        {
            throw new IOException($"删除到回收站失败，错误码: {result}");
        }
    }
}
