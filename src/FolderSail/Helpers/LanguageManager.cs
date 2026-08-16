using System.Windows;

namespace FolderSail.Helpers;

public static class Loc
{
    public static string Get(string key)
    {
        var app = Application.Current;
        if (app?.TryFindResource(key) is string text)
        {
            return text;
        }

        return key;
    }

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}

public static class LanguageManager
{
    public const string Chinese = "zh-CN";
    public const string English = "en";

    private static readonly Uri ChineseUri = new("Loc/Strings.zh-CN.xaml", UriKind.Relative);
    private static readonly Uri EnglishUri = new("Loc/Strings.en.xaml", UriKind.Relative);

    public static string Current { get; private set; } = Chinese;

    public static bool IsEnglish => Current == English;

    public static event EventHandler? Changed;

    public static void Apply(string? language)
    {
        var code = string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : Chinese;
        var app = Application.Current;
        if (app is null)
        {
            Current = code;
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        if (dictionaries.Count < 2)
        {
            return;
        }

        dictionaries[1] = new ResourceDictionary { Source = code == English ? EnglishUri : ChineseUri };
        Current = code;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
