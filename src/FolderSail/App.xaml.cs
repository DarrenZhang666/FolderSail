using System.IO;
using FolderSail.Core.Services;
using FolderSail.Helpers;
using FolderSail.Views;

namespace FolderSail;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var settings = new SettingsStore().Load();
        ThemeManager.Apply(settings.IsDarkTheme);
        LanguageManager.Apply(settings.Language);
        base.OnStartup(e);

        // StartupUri removed so we can open a workspace from the command line:
        // FolderSail.exe "D:\Work\FolderSail_GUI.foldersail"
        var workspacePath = ResolveWorkspaceArgument(e.Args);
        var window = string.IsNullOrWhiteSpace(workspacePath)
            ? new MainWindow()
            : new MainWindow(workspacePath);
        MainWindow = window;
        window.Show();
    }

    private static string? ResolveWorkspaceArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim().Trim('"');
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-w", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    var path = args[i + 1].Trim().Trim('"');
                    return File.Exists(path) ? Path.GetFullPath(path) : null;
                }

                continue;
            }

            if (arg.EndsWith(".foldersail", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            {
                return Path.GetFullPath(arg);
            }
        }

        return null;
    }
}
