using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace SamaTraining.Web.Services;

/// <summary>
/// Staff-screen translations. Keys are the English text; Resources/ar.json holds the Arabic.
/// Loaded once into memory (embedded in the app), so a lookup is a dictionary read: no files, no database.
/// Only the UI language changes; number and date formats stay the same so amounts and forms behave identically.
/// </summary>
public class UiText
{
    public const string CookieName = "sama.lang";

    private static readonly Dictionary<string, string> Arabic = Load();

    private static Dictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SamaTraining.Web.Resources.ar.json");
        if (stream == null) return new Dictionary<string, string>();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
    }

    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    public static string Lang => IsArabic ? "ar" : "en";

    /// <summary>Arabic when the UI is Arabic and a translation exists; otherwise the English key.</summary>
    public static string Get(string key) =>
        IsArabic && key != null && Arabic.TryGetValue(key, out var ar) ? ar : key ?? string.Empty;

    public string this[string key] => Get(key);

    /// <summary>Translated format string for code (controllers, services).</summary>
    public static string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Translated format string, e.g. L["{0} participants", 4].</summary>
    public string this[string key, params object?[] args] => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>The Arabic name when the screen is in Arabic and one exists, otherwise the English one.</summary>
    public static string Pick(string? english, string? arabic) =>
        IsArabic && !string.IsNullOrWhiteSpace(arabic) ? arabic! : english ?? string.Empty;

    /// <summary>Workshop status in the current language.</summary>
    public static string Status(SamaTraining.Web.Data.WorkshopStatus s) => Get(s.ToString());

    internal static bool Has(string key) => Arabic.ContainsKey(key);
}

/// <summary>Localizer used for validation messages and [Display] names (same dictionary).</summary>
public class UiStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, UiText.Get(name), !UiText.Has(name));

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(CultureInfo.CurrentCulture, UiText.Get(name), arguments), !UiText.Has(name));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
}

public class UiStringLocalizerFactory : IStringLocalizerFactory
{
    private static readonly UiStringLocalizer Shared = new();
    public IStringLocalizer Create(Type resourceSource) => Shared;
    public IStringLocalizer Create(string baseName, string location) => Shared;
}

/// <summary>Sets the UI language from the "sama.lang" cookie (en by default). Cheap: one cookie read per request.</summary>
public class UiLanguageMiddleware
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en");
    private readonly RequestDelegate _next;

    public UiLanguageMiddleware(RequestDelegate next) => _next = next;

    public Task Invoke(HttpContext context)
    {
        CultureInfo.CurrentUICulture = context.Request.Cookies[UiText.CookieName] == "ar" ? Ar : En;
        return _next(context);
    }
}
