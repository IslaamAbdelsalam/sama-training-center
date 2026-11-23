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

    /// <summary>The one date format used everywhere: dd/MM/yyyy (e.g. 05/01/2026).</summary>
    public static string D(DateTime d) => d.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);
    public static string D(DateTime? d) => d.HasValue ? D(d.Value) : "";

    /// <summary>Date and time: "05/01/2026 14:30".</summary>
    public static string DT(DateTime d) => d.ToString("dd'/'MM'/'yyyy HH:mm", CultureInfo.InvariantCulture);
    public static string DT(DateTime? d) => d.HasValue ? DT(d.Value) : "";

    /// <summary>Day name and date: "Monday 05/01/2026" / "الاثنين 05/01/2026".</summary>
    public static string DayAndDate(DateTime d, string lang) => $"{d.ToString("dddd", Culture(lang))} {D(d)}";

    /// <summary>Single day: "Monday 05/01/2026". Date range: "05/01/2026 – 09/01/2026 (5 days)".</summary>
    public static string Dates(Workshop w, string lang)
    {
        if (!w.IsMultiDay) return DayAndDate(w.StartDateTime, lang);
        var days = lang == "ar" ? $"{w.Days} أيام" : $"{w.Days} days";
        if (lang == "ar" && w.Days > 10) days = $"{w.Days} يوماً";
        return $"{D(w.StartDateTime)} – {D(w.EndDateTime)} ({days})";
    }

    /// <summary>Daily hours, e.g. "09:00 – 13:00".</summary>
    public static string Hours(Workshop w) => $"{w.StartDateTime:HH:mm} – {w.EndDateTime:HH:mm}";

    /// <summary>Staff-facing date: "05/01/2026" or "05/01/2026 – 09/01/2026".</summary>
    public static string ShortDates(DateTime start, DateTime end) =>
        end.Date <= start.Date ? D(start) : $"{D(start)} – {D(end)}";

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
