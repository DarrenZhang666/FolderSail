using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FolderSail.Helpers;

/// <summary>
/// Shows the real Windows Shell context menu. A dedicated Win32 popup host is
/// used because FolderSail's chrome-less WPF window cannot reliably own
/// TrackPopupMenu.
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

    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmReturnCommand = 0x0100;

    private const uint MfByPosition = 0x00000400;
    private const uint MfSeparator = 0x00000800;
    private const uint MfString = 0x00000000;

    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const int SwShowNormal = 1;
    private const int SwShowNoActivate = 4;

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsExToolWindow = 0x00000080;

    private const int WmInitMenuPopup = 0x0117;
    private const int WmDrawItem = 0x002B;
    private const int WmMeasureItem = 0x002C;
    private const int WmMenuChar = 0x0120;

    public static string? LastError { get; private set; }

    public sealed record OwnCommand(string Label, Action Invoke);

    public static bool Show(
        Window owner,
        string path,
        IReadOnlyList<OwnCommand> ownCommands,
        bool showExtendedVerbs,
        bool folderBackground = false)
    {
        LastError = null;

        if (owner == null || string.IsNullOrWhiteSpace(path))
        {
            LastError = "无效的窗口或路径";
            return false;
        }

        IntPtr absolutePidl = IntPtr.Zero;
        IntPtr pidlArray = IntPtr.Zero;
        IntPtr parentPointer = IntPtr.Zero;
        IntPtr folderPointer = IntPtr.Zero;
        IntPtr contextMenuPointer = IntPtr.Zero;
        IShellFolder? parentFolder = null;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        IContextMenu2? contextMenu2 = null;
        IContextMenu3? contextMenu3 = null;
        HwndSource? host = null;
        IntPtr menu = IntPtr.Zero;

        try
        {
            ThrowIfFailed(SHParseDisplayName(path, IntPtr.Zero, out absolutePidl, 0, out _));
            if (absolutePidl == IntPtr.Zero)
            {
                LastError = "无法解析路径";
                return false;
            }

            var shellFolderId = typeof(IShellFolder).GUID;
            ThrowIfFailed(SHBindToParent(absolutePidl, ref shellFolderId, out parentPointer, out var childPidl));
            parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentPointer);

            var contextMenuId = typeof(IContextMenu).GUID;
            if (folderBackground)
            {
                ThrowIfFailed(parentFolder.BindToObject(childPidl, IntPtr.Zero, ref shellFolderId, out folderPointer));
                folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPointer);
                ThrowIfFailed(folder.CreateViewObject(IntPtr.Zero, ref contextMenuId, out contextMenuPointer));
            }
            else
            {
                pidlArray = Marshal.AllocCoTaskMem(IntPtr.Size);
                Marshal.WriteIntPtr(pidlArray, childPidl);
                ThrowIfFailed(parentFolder.GetUIObjectOf(
                    IntPtr.Zero,
                    1,
                    pidlArray,
                    ref contextMenuId,
                    IntPtr.Zero,
                    out contextMenuPointer));
            }

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

            if (GetMenuItemCount(menu) <= 0 && ownCommands.Count == 0)
            {
                LastError = "系统菜单为空";
                return false;
            }

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

            contextMenu3 = TryGetInterface<IContextMenu3>(contextMenu);
            contextMenu2 = contextMenu3 == null ? TryGetInterface<IContextMenu2>(contextMenu) : null;

            GetCursorPos(out var cursor);
            host = CreateHost(cursor, contextMenu2, contextMenu3);
            ShowWindow(host.Handle, SwShowNoActivate);
            BringToFront(host.Handle);

            var command = TrackPopupMenuEx(
                menu,
                TpmLeftAlign | TpmReturnCommand,
                cursor.X,
                cursor.Y,
                host.Handle,
                IntPtr.Zero);

            PostMessage(host.Handle, 0x0000, IntPtr.Zero, IntPtr.Zero);

            if (command == 0)
            {
                return true;
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

            Invoke(contextMenu, new WindowInteropHelper(owner).EnsureHandle(), command - FirstShellCommand, cursor);
            return true;
        }
        catch (Exception exception)
        {
            LastError = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
        finally
        {
            if (menu != IntPtr.Zero)
            {
                DestroyMenu(menu);
            }

            host?.Dispose();

            SafeRelease(contextMenu3);
            SafeRelease(contextMenu2);
            SafeRelease(contextMenu);
            SafeRelease(folder);
            SafeRelease(parentFolder);

            if (contextMenuPointer != IntPtr.Zero)
            {
                Marshal.Release(contextMenuPointer);
            }

            if (folderPointer != IntPtr.Zero)
            {
                Marshal.Release(folderPointer);
            }

            if (parentPointer != IntPtr.Zero)
            {
                Marshal.Release(parentPointer);
            }

            if (pidlArray != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pidlArray);
            }

            if (absolutePidl != IntPtr.Zero)
            {
                CoTaskMemFree(absolutePidl);
            }
        }
    }

    private static HwndSourceHook? _activeHook;

    private static HwndSource CreateHost(NativePoint cursor, IContextMenu2? menu2, IContextMenu3? menu3)
    {
        _activeHook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message is not (WmInitMenuPopup or WmDrawItem or WmMeasureItem or WmMenuChar))
            {
                return IntPtr.Zero;
            }

            try
            {
                if (menu3 != null)
                {
                    var result = menu3.HandleMenuMsg2((uint)message, wParam, lParam, out var menuResult);
                    if (result == 0)
                    {
                        handled = message == WmMenuChar;
                        return menuResult;
                    }
                }
                else if (menu2 != null && message != WmMenuChar)
                {
                    menu2.HandleMenuMsg((uint)message, wParam, lParam);
                }
            }
            catch (COMException)
            {
            }

            return IntPtr.Zero;
        };

        var parameters = new HwndSourceParameters("FolderSailShellMenu")
        {
            Width = 1,
            Height = 1,
            PositionX = cursor.X,
            PositionY = cursor.Y,
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow
        };

        var source = new HwndSource(parameters);
        source.AddHook(_activeHook);
        return source;
    }

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

    private static void BringToFront(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            AttachThreadInput(foregroundThread, currentThread, true);
            SetForegroundWindow(window);
            AttachThreadInput(foregroundThread, currentThread, false);
        }
        else
        {
            SetForegroundWindow(window);
        }
    }

    private static void ThrowIfFailed(int hResult)
    {
        if (hResult < 0)
        {
            Marshal.ThrowExceptionForHR(hResult);
        }
    }

    private static void SafeRelease(object? comObject)
    {
        if (comObject == null)
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        catch (InvalidComObjectException)
        {
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
        int GetAttributesOf(uint itemCount, IntPtr itemIdLists, ref uint attributes);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint itemCount,
            IntPtr itemIdLists,
            ref Guid interfaceId,
            IntPtr reserved,
            out IntPtr result);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr itemIdList, uint flags, IntPtr name);

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
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        void QueryContextMenuSlot();
        void InvokeCommandSlot();
        void GetCommandStringSlot();

        [PreserveSig]
        int HandleMenuMsg(uint message, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        void QueryContextMenuSlot();
        void InvokeCommandSlot();
        void GetCommandStringSlot();
        void HandleMenuMsgSlot();

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
        out IntPtr parentFolder,
        out IntPtr childItemIdList);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool connect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr memory);
}
