namespace SamaTraining.Web.Services;

public class AppSettings
{
    public const string Section = "App";

    public string OrganizationName { get; set; } = "SAMA Organization";
    public string SystemName { get; set; } = "SAMA Training Center";

    /// <summary>Currency code shown next to expense amounts.</summary>
    public string CurrencyCode { get; set; } = "USD";

    public int SessionTimeoutMinutes { get; set; } = 30;
}

public record OperationResult(bool Success, string? Error = null)
{
    public static OperationResult Ok() => new(true);
    public static OperationResult Fail(string error) => new(false, error);
}
