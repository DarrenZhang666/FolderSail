using System.Windows;

namespace FolderSail.Helpers;

public static class ThemeManager
{
    private static readonly Uri LightUri = new("Themes/Tokens.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Tokens.Dark.xaml", UriKind.Relative);

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            return;
        }

        dictionaries[0] = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        IsDark = dark;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
