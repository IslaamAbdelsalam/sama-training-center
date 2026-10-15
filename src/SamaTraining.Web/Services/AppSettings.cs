namespace SamaTraining.Web.Services;

public class AppSettings
{
    public const string Section = "App";

    public string OrganizationName { get; set; } = "SAMA Organization";
    public string SystemName { get; set; } = "SAMA Training Center";

    public int SessionTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Countries where workshops run. Each workshop's money (expenses, reports) is in its country's currency.
    /// Configure in appsettings.json under App:Countries.
    /// </summary>
    public List<CountryOption> Countries { get; set; } = new();

    private static readonly List<CountryOption> DefaultCountries = new()
    {
        new CountryOption { Code = "SD", Name = "Sudan", NameAr = "السودان", Currency = "SDG" },
        new CountryOption { Code = "EG", Name = "Egypt", NameAr = "مصر", Currency = "EGP" },
    };

    public IReadOnlyList<CountryOption> GetCountries() => Countries.Count > 0 ? Countries : DefaultCountries;

    public CountryOption? FindCountry(string? code) =>
        GetCountries().FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public string CountryName(string? code) => FindCountry(code)?.Name ?? code ?? "—";
}

public class CountryOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
}

public record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}

public static class MoneyFormat
{
    /// <summary>"1,250.00 SDG"</summary>
    public static string Format(decimal amount, string? currency) =>
        $"{amount:N2} {currency}".Trim();
}
