using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Aether_App;

/// <summary>
/// User-facing text from <c>Strings/en-us/Resources.resw</c>. Keys follow <c>Area_Name</c>, which rules out
/// <c>x:Uid</c> property keys (<c>Name.Text</c>), so the shell sets its labels from code. UI thread.
/// </summary>
internal static class AppStrings
{
    private static readonly ResourceLoader Loader = new();

    public static string Get(string key) => Loader.GetString(key);

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
