using System.Globalization;
using SamaTraining.Web.Data;

namespace SamaTraining.Web.Services;

/// <summary>US-05 / US-06 / US-12 / US-13: dates, fees and currencies shown in English or Arabic.</summary>
public static class DisplayFormat
{
    private static readonly Dictionary<string, (string En, string Ar)> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SDG"] = ("SDG", "جنيه سوداني"),
        ["EGP"] = ("EGP", "جنيه مصري"),
        ["USD"] = ("USD", "دولار أمريكي"),
        ["SAR"] = ("SAR", "ريال سعودي"),
        ["QAR"] = ("QAR", "ريال قطري"),
        ["AED"] = ("AED", "درهم إماراتي"),
    };

    public static CultureInfo Culture(string lang) => CultureInfo.GetCultureInfo(lang == "ar" ? "ar-EG" : "en-GB");

    public static string CurrencyName(string? code, string lang) =>
        code != null && CurrencyNames.TryGetValue(code, out var n) ? (lang == "ar" ? n.Ar : n.En) : code ?? "";

    /// <summary>"Free" / "مجاني", or "1,500.00 SDG" / "1,500.00 جنيه سوداني".</summary>
    public static string Fee(Workshop w, string lang) =>
        w.IsFree
            ? (lang == "ar" ? "مجاني" : "Free")
            : $"{w.Fee!.Value.ToString("N2", CultureInfo.InvariantCulture)} {CurrencyName(w.CurrencyCode, lang)}";

    /// <summary>Single day: "Monday 5 January 2026". Date range: "5 – 9 January 2026 (5 days)".</summary>
    public static string Dates(Workshop w, string lang)
    {
        var c = Culture(lang);
        var s = w.StartDateTime;
        var e = w.EndDateTime;
        if (!w.IsMultiDay) return s.ToString("dddd d MMMM yyyy", c);

        string range;
        if (s.Year == e.Year && s.Month == e.Month) range = $"{s.ToString("%d", c)} – {e.ToString("d MMMM yyyy", c)}";
        else if (s.Year == e.Year) range = $"{s.ToString("d MMMM", c)} – {e.ToString("d MMMM yyyy", c)}";
        else range = $"{s.ToString("d MMMM yyyy", c)} – {e.ToString("d MMMM yyyy", c)}";

        var days = lang == "ar" ? $"{w.Days} أيام" : $"{w.Days} days";
        if (lang == "ar" && w.Days > 10) days = $"{w.Days} يوماً";
        return $"{range} ({days})";
    }

    /// <summary>Daily hours, e.g. "09:00 – 13:00".</summary>
    public static string Hours(Workshop w) => $"{w.StartDateTime:HH:mm} – {w.EndDateTime:HH:mm}";

    /// <summary>Short staff-facing date: "05 Jan 2026" or "05 – 09 Jan 2026".</summary>
    public static string ShortDates(DateTime start, DateTime end)
    {
        if (end.Date <= start.Date) return start.ToString("dd MMM yyyy");
        if (start.Year == end.Year && start.Month == end.Month) return $"{start:dd} – {end:dd MMM yyyy}";
        if (start.Year == end.Year) return $"{start:dd MMM} – {end:dd MMM yyyy}";
        return $"{start:dd MMM yyyy} – {end:dd MMM yyyy}";
    }

    public static string Label(PaymentStatus s) => s switch
    {
        PaymentStatus.Free => UiText.Get("Free"),
        PaymentStatus.Paid => UiText.Get("Paid"),
        PaymentStatus.PartiallyPaid => UiText.Get("Partially paid"),
        _ => UiText.Get("Unpaid")
    };

    public static string BadgeCss(PaymentStatus s) => s switch
    {
        PaymentStatus.Paid => "bg-label-success",
        PaymentStatus.PartiallyPaid => "bg-label-warning",
        PaymentStatus.Unpaid => "bg-label-danger",
        _ => "bg-label-secondary"
    };

    public static string Label(PaymentMethod m) => m switch
    {
        PaymentMethod.BankTransfer => UiText.Get("Bank transfer"),
        PaymentMethod.MobileWallet => UiText.Get("Mobile wallet"),
        PaymentMethod.Other => UiText.Get("Other"),
        _ => UiText.Get("Cash")
    };

    public static string Label(WorkshopHistoryAction a) => a switch
    {
        WorkshopHistoryAction.RegistrationOpened => UiText.Get("Registration opened"),
        WorkshopHistoryAction.RegistrationClosed => UiText.Get("Registration closed"),
        WorkshopHistoryAction.LinkRegenerated => UiText.Get("New registration link created"),
        WorkshopHistoryAction.ParticipantCancelled => UiText.Get("Registration cancelled"),
        WorkshopHistoryAction.ParticipantRestored => UiText.Get("Cancellation undone"),
        _ => UiText.Get("Status changed")
    };
}
