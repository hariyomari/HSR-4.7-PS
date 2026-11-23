using System.Globalization;
using March7thHoney.Internationalization;

namespace March7thHoney.Util;

public static class UtilTools
{
    public static string GetCurrentLanguage()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        return LanguageCodes.FromCulture(uiCulture.Name);
    }
}