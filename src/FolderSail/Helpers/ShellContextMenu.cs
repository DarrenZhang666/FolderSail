using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FolderSail.Helpers;

/// <summary>
/// Hosts the real Windows Shell context menu for one filesystem item. This is
/// intentionally native rather than a reimplementation: installed Shell
/// extensions (7-Zip, Git, OneDrive, antivirus tools, and so on) participate
/// exactly as they do in Explorer.
/// </summary>
public static class ShellContextMenu
{
    private const uint FirstShellCommand = 1;
    private const uint LastShellCommand = 0x6FFF;
    private const uint FirstOwnCommand = 0x7001;

    private const uint CmfNormal = 0x00000000;
    private const uint CmfExplore = 0x00000004;
    private const uint CmfCanRename = 0x00000010;
    private const uint CmfExtendedVerbs = 0x00000100;

    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;

    private const uint MfByPosition = 0x00000400;
    private const uint MfSeparator = 0x00000800;
    private const uint MfString = 0x00000000;

    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const int SwShowNormal = 1;

    private const int WmInitMenuPopup = 0x0117;
    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;
    private const int WmMenuChar = 0x0120;

    public static string? LastError { get; private set; }

    /// <summary>A FolderSail-specific entry prepended above the Shell's own verbs.</summary>
    public sealed record OwnCommand(string Label, Action Invoke);

    public static bool Show(
        Window owner,
        string path,
        IReadOnlyList<OwnCommand> ownCommands,
        bool showExtendedVerbs)
    {
        LastError = null;

        if (owner == null || string.IsNullOrWhiteSpace(path))
        {
            LastError = "无效的窗口或路径";
            return false;
        }

        var ownerHandle = new WindowInteropHelper(owner).Handle;
        if (ownerHandle == IntPtr.Zero)
        {
            LastError = "无法取得窗口句柄";
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IntPtr contextMenuPointer = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;
        HwndSourceHook? hook = null;
        IntPtr menu = IntPtr.Zero;

        try
        {
            ThrowIfFailed(SHParseDisplayName(path, IntPtr.Zero, out absolutePidl, 0, out _));

            var shellFolderId = typeof(IShellFolder).GUID;
            ThrowIfFailed(SHBindToParent(
                absolutePidl,
                ref shellFolderId,
                out parentFolder,
                out var childPidl));

            var contextMenuId = typeof(IContextMenu).GUID;
            ThrowIfFailed(parentFolder.GetUIObjectOf(
                ownerHandle,
                1,
                [childPidl],
                ref contextMenuId,
                IntPtr.Zero,
                out contextMenuPointer));

            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPointer);
            Marshal.Release(contextMenuPointer);
            contextMenuPointer = IntPtr.Zero;

            menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                LastError = "CreatePopupMenu 失败";
                return false;
            }

            var queryFlags = CmfNormal | CmfExplore | CmfCanRename;
            if (showExtendedVerbs)
            {
                queryFlags |= CmfExtendedVerbs;
            }

            ThrowIfFailed(contextMenu.QueryContextMenu(
                menu,
                0,
                FirstShellCommand,
                LastShellCommand,
                queryFlags));

            for (var i = 0; i < ownCommands.Count; i++)
            {
                InsertMenu(
                    menu,
                    (uint)i,
                    MfByPosition | MfString,
                    FirstOwnCommand + (uint)i,
                    ownCommands[i].Label);
            }

            if (ownCommands.Count > 0)
            {
                InsertMenu(menu, (uint)ownCommands.Count, MfByPosition | MfSeparator, 0, null);
            }

            contextMenu3 = TryGetContextMenu3(contextMenu);
            contextMenu2 = contextMenu3 == null ? TryGetContextMenu2(contextMenu) : null;

            if ((contextMenu3 != null || contextMenu2 != null) &&
                HwndSource.FromHwnd(ownerHandle) is { } source)
            {
                hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar))
                    {
                        return IntPtr.Zero;
                    }

                    try
                    {
                        if (contextMenu3 != null)
                        {
                            var result = contextMenu3.HandleMenuMsg2(
                                (uint)message,
                                wParam,
                                lParam,
                                out var menuResult);

                            if (result == 0)
                            {
                                handled = message == WmMenuChar;
                                return menuResult;
                            }
                        }
                        else if (contextMenu2 != null && message != WmMenuChar)
                        {
                            contextMenu2.HandleMenuMsg((uint)message, wParam, lParam);
                        }
                    }
                    catch (COMException)
                    {
                        // A third-party extension may reject a message. Let the
                        // window continue processing it rather than taking down the app.
                    }

                    return IntPtr.Zero;
                };

                source.AddHook(hook);
            }

            GetCursorPos(out var cursor);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCommand,
                cursor.X,
                cursor.Y,
                ownerHandle,
                IntPtr.Zero);

            if (command == 0)
            {
                return false;
            }

            if (command >= FirstOwnCommand && command < FirstOwnCommand + ownCommands.Count)
            {
                ownCommands[(int)(command - FirstOwnCommand)].Invoke();
                return true;
            }

            if (command < FirstShellCommand || command > LastShellCommand)
            {
                return false;
            }

            Invoke(contextMenu, ownerHandle, command - FirstShellCommand, cursor);
            return true;
        }
        catch (Exception exception) when (
            exception is COMException or InvalidCastException or SEHException)
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        finally
        {
            if (hook != null && HwndSource.FromHwnd(ownerHandle) is { } source)
            {
                source.RemoveHook(hook);
            }

            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            if (contextMenu3 != null)
            {
                SafeRelease(contextMenu3);
            }

            if (contextMenu2 != null)
            {
                SafeRelease(contextMenu2);
            }

            if (contextMenu != null)
            {
                SafeRelease(contextMenu);
            }

            if (contextMenuPointer != IntPtr.Zero)
            {
                Marshal.Release(contextMenuPointer);
            }

            if (parentFolder != null)
            {
                SafeRelease(parentFolder);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                CoTaskMemFree(absolutePidl);
            }
        }
    }

    private static IContextMenu3? TryGetContextMenu3(IContextMenu contextMenu)
        => TryGetInterface<IContextMenu3>(contextMenu);

    private static IContextMenu2? TryGetContextMenu2(IContextMenu contextMenu)
        => TryGetInterface<IContextMenu2>(contextMenu);

    private static T? TryGetInterface<T>(IContextMenu contextMenu)
        where T : class
    {
        var unknown = Marshal.GetIUnknownForObject(contextMenu);
        var interfaceId = typeof(T).GUID;
        IntPtr interfacePointer = IntPtr.Zero;

        try
        {
            if (Marshal.QueryInterface(unknown, ref interfaceId, out interfacePointer) != 0 ||
                interfacePointer == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.GetObjectForIUnknown(interfacePointer) as T;
        }
        finally
        {
            if (interfacePointer != IntPtr.Zero)
            {
                Marshal.Release(interfacePointer);
            }

            Marshal.Release(unknown);
        }
    }

    private static void Invoke(
        IContextMenu contextMenu,
        IntPtr ownerHandle,
        uint commandOffset,
        NativePoint cursor)
    {
        var commandInfo = new CommandInfoEx
        {
            Size = Marshal.SizeOf<CommandInfoEx>(),
            Mask = CmicMaskUnicode | CmicMaskPtInvoke,
            Owner = ownerHandle,
            Verb = (IntPtr)commandOffset,
            VerbW = (IntPtr)commandOffset,
            Show = SwShowNormal,
            InvokePoint = cursor
        };

        ThrowIfFailed(contextMenu.InvokeCommand(ref commandInfo));
    }

    private static void ThrowIfFailed(int hResult)
    {
        if (hResult < 0)
        {
            Marshal.ThrowExceptionForHR(hResult);
        }
    }

    private static void SafeRelease(object comObject)
    {
        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (InvalidComObjectException)
        {
            // The same RCW can surface through both IContextMenu and IContextMenu3.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CommandInfoEx
    {
        public int Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public int HotKey;
        public IntPtr Icon;
        public IntPtr Title;
        public IntPtr VerbW;
        public IntPtr ParametersW;
        public IntPtr DirectoryW;
        public IntPtr TitleW;
        public NativePoint InvokePoint;
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr menu,
            uint indexMenu,
            uint firstCommand,
            uint lastCommand,
            uint flags);

        [PreserveSig]
        int InvokeCommand(ref CommandInfoEx commandInfo);

        [PreserveSig]
        int GetCommandString(
            UIntPtr commandOffset,
            uint flags,
            IntPtr reserved,
            IntPtr name,
            uint characterCount);

        [PreserveSig]
        int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr bindContext,
            [MarshalAs(UnmanagedType.LPWStr)] string displayName,
            ref uint eaten,
            out IntPtr itemIdList,
            ref uint attributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);

        [PreserveSig]
        int BindToObject(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);

        [PreserveSig]
        int BindToStorage(IntPtr itemIdList, IntPtr bindContext, ref Guid interfaceId, out IntPtr result);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr first, IntPtr second);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid interfaceId, out IntPtr result);

        [PreserveSig]
        int GetAttributesOf(uint itemCount, IntPtr[] itemIdLists, ref uint attributes);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint itemCount,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists,
            ref Guid interfaceId,
            IntPtr reserved,
            out IntPtr result);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr itemIdList, uint flags, out IntPtr name);

        [PreserveSig]
        int SetNameOf(
            IntPtr hwnd,
            IntPtr itemIdList,
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            uint flags,
            out IntPtr newItemIdList);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr menu,
            uint indexMenu,
            uint firstCommand,
            uint lastCommand,
            uint flags);

        [PreserveSig]
        int InvokeCommand(ref CommandInfoEx commandInfo);

        [PreserveSig]
        int GetCommandString(
            UIntPtr commandOffset,
            uint flags,
            IntPtr reserved,
            IntPtr name,
            uint characterCount);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(
            IntPtr menu,
            uint indexMenu,
            uint firstCommand,
            uint lastCommand,
            uint flags);

        [PreserveSig]
        int InvokeCommand(ref CommandInfoEx commandInfo);

        [PreserveSig]
        int GetCommandString(
            UIntPtr commandOffset,
            uint flags,
            IntPtr reserved,
            IntPtr name,
            uint characterCount);

        [PreserveSig]
        int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        IntPtr bindContext,
        out IntPtr itemIdList,
        uint attributesIn,
        out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr itemIdList,
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder parentFolder,
        out IntPtr childItemIdList);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InsertMenu(
        IntPtr menu,
        uint position,
        uint flags,
        uint newItemId,
        string? newItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr owner,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr memory);
}
