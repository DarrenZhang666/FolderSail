using System.Runtime.InteropServices;

namespace FolderSail.Helpers;

/// <summary>
/// Creates Windows .lnk shortcuts that launch FolderSail with a workspace file.
/// </summary>
internal static class ShortcutHelper
{
    public static void CreateWorkspaceShortcut(
        string shortcutPath,
        string targetExe,
        string workspaceFile,
        string description)
    {
        // WScript.Shell COM is the most reliable way to write .lnk files from managed code
        // without shipping an extra NuGet package.
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建快捷方式（缺少 WScript.Shell）");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("无法创建快捷方式");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            if (shortcut is null)
            {
                throw new InvalidOperationException("无法创建快捷方式");
            }

            var shortcutType = shortcut.GetType();
            Set(shortcutType, shortcut, "TargetPath", targetExe);
            Set(shortcutType, shortcut, "Arguments", Quote(workspaceFile));
            Set(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(targetExe) ?? string.Empty);
            Set(shortcutType, shortcut, "Description", description);
            Set(shortcutType, shortcut, "IconLocation", targetExe + ",0");
            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void Set(Type type, object target, string property, object value) =>
        type.InvokeMember(
            property,
            System.Reflection.BindingFlags.SetProperty,
            binder: null,
            target: target,
            args: [value]);

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
