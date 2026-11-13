using System.Globalization;
using SysWatch.Config;

namespace SysWatch;

// Strings are translated where they are used, L.T("Türkçe", "English"), so they follow the language in effect
// when a window, message or reply is built. Don't cache them in static fields.
public static class L
{
    public static bool Turkish { get; private set; } = SystemIsTurkish();

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Turkish ? "tr-TR" : "en-US");

    // Automatic follows the Windows display language: Turkish there, English everywhere else.
    public static void Apply(UiLanguage language) => Turkish = language switch
    {
        UiLanguage.Turkish => true,
        UiLanguage.English => false,
        _ => SystemIsTurkish(),
    };

    public static bool SystemIsTurkish() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

    public static string T(string turkish, string english) => Turkish ? turkish : english;

    // Channel names double as internal keys ("E-posta"); only the shown name is translated.
    public static string Channel(string key) => key == "E-posta" ? T("E-posta", "E-mail") : key;

    public static string Percent(double value, string format = "0") =>
        Turkish ? "%" + value.ToString(format, Culture) : value.ToString(format, Culture) + "%";
}
