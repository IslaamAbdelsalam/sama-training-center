namespace SamaTraining.Web.Services;

/// <summary>
/// Arabic and English text for the public registration page (US-11).
/// The staff-facing system is English only, as agreed in the proposal.
/// </summary>
public class PublicText
{
    private static readonly Dictionary<string, (string En, string Ar)> Text = new()
    {
        ["Title"] = ("Workshop Registration", "التسجيل في الورشة"),
        ["Subtitle"] = ("Fill in your details to register for this workshop.", "يرجى تعبئة بياناتك للتسجيل في هذه الورشة."),
        ["Date"] = ("Date", "التاريخ"),
        ["Time"] = ("Time", "الوقت"),
        ["Type"] = ("Type", "النوع"),
        ["Location"] = ("Location", "المكان"),
        ["Online"] = ("Online", "عن بُعد"),
        ["OnSite"] = ("On-site", "حضوري"),
        ["About"] = ("About this workshop", "عن الورشة"),
        ["FullName"] = ("Full name", "الاسم الكامل"),
        ["Email"] = ("Email", "البريد الإلكتروني"),
        ["Phone"] = ("Phone number", "رقم الهاتف"),
        ["Organization"] = ("Organization / University (optional)", "الجهة / الجامعة (اختياري)"),
        ["Notes"] = ("Notes (optional)", "ملاحظات (اختياري)"),
        ["Submit"] = ("Register", "تسجيل"),
        ["Required"] = ("This field is required.", "هذا الحقل مطلوب."),
        ["InvalidEmail"] = ("Please enter a valid email address.", "يرجى إدخال بريد إلكتروني صحيح."),
        ["InvalidPhone"] = ("Please enter a valid phone number.", "يرجى إدخال رقم هاتف صحيح."),
        ["SuccessTitle"] = ("Registration received", "تم استلام التسجيل"),
        ["SuccessBody"] = ("Thank you. You are registered for this workshop. We will contact you with any updates.", "شكراً لك. تم تسجيلك في هذه الورشة، وسنتواصل معك بأي مستجدات."),
        ["ClosedTitle"] = ("Registration closed", "التسجيل مغلق"),
        ["ClosedBody"] = ("Registration for this workshop is closed.", "التسجيل في هذه الورشة مغلق حالياً."),
        ["FullTitle"] = ("Workshop full", "اكتمل العدد"),
        ["FullBody"] = ("This workshop has reached its maximum number of participants.", "وصلت هذه الورشة إلى الحد الأقصى من المشاركين."),
        ["Duplicate"] = ("This email is already registered for this workshop.", "هذا البريد الإلكتروني مسجل مسبقاً في هذه الورشة."),
        ["NotFoundTitle"] = ("Link not found", "الرابط غير موجود"),
        ["NotFoundBody"] = ("This registration link is not valid. Please check the link you received.", "رابط التسجيل غير صالح. يرجى التحقق من الرابط الذي استلمته."),
        ["SeatsLeft"] = ("Seats left", "المقاعد المتبقية"),
        ["Language"] = ("العربية", "English"),
    };

    public static string Normalize(string? lang) => lang == "ar" ? "ar" : "en";

    public string Get(string key, string lang) =>
        Text.TryGetValue(key, out var t) ? (lang == "ar" ? t.Ar : t.En) : key;
}
