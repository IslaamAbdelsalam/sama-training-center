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
        ["FormTitle"] = ("Your details", "بياناتك"),
        ["Subtitle"] = ("It only takes a minute. We'll use these details to confirm your seat.", "لن يستغرق الأمر سوى دقيقة. سنستخدم هذه البيانات لتأكيد مقعدك."),
        ["Online"] = ("Online", "عن بُعد"),
        ["OnSite"] = ("On-site", "حضوري"),
        ["About"] = ("What you'll learn", "محاور الورشة"),
        ["FullName"] = ("Full name", "الاسم الكامل"),
        ["FullNameHint"] = ("As you want it on your certificate", "كما تريده أن يظهر في الشهادة"),
        ["Email"] = ("Email", "البريد الإلكتروني"),
        ["Phone"] = ("Phone (WhatsApp preferred)", "رقم الهاتف (يفضّل واتساب)"),
        ["Organization"] = ("Organization or university", "الجهة أو الجامعة"),
        ["Notes"] = ("Anything we should know?", "هل هناك ما تود إخبارنا به؟"),
        ["Optional"] = ("(optional)", "(اختياري)"),
        ["Submit"] = ("Register now", "سجّل الآن"),
        ["Sending"] = ("Sending…", "جارٍ الإرسال…"),
        ["Privacy"] = ("Your details are only used for this workshop.", "تُستخدم بياناتك لهذه الورشة فقط."),
        ["Required"] = ("Please enter your full name.", "يرجى إدخال اسمك الكامل."),
        ["InvalidEmail"] = ("Please enter a valid email address.", "يرجى إدخال بريد إلكتروني صحيح."),
        ["InvalidPhone"] = ("Please enter a valid phone number, e.g. +249 91 000 0000.", "يرجى إدخال رقم هاتف صحيح، مثل ‎+249 91 000 0000."),
        ["SuccessTitle"] = ("You're registered!", "تم تسجيلك بنجاح!"),
        ["SuccessBody"] = ("Thank you. We've saved your seat and will contact you with any updates before the workshop.", "شكراً لك. تم حجز مقعدك وسنتواصل معك بأي مستجدات قبل موعد الورشة."),
        ["ClosedTitle"] = ("Registration is closed", "التسجيل مغلق"),
        ["ClosedBody"] = ("This workshop is no longer accepting registrations. Follow SAMA for upcoming workshops.", "لم يعد التسجيل متاحاً لهذه الورشة. تابعوا سما لمعرفة الورش القادمة."),
        ["FullTitle"] = ("All seats are taken", "اكتمل العدد"),
        ["FullBody"] = ("This workshop has reached its maximum number of participants.", "وصلت هذه الورشة إلى الحد الأقصى من المشاركين."),
        ["Duplicate"] = ("You're already registered for this workshop with this email.", "أنت مسجل مسبقاً في هذه الورشة بهذا البريد الإلكتروني."),
        ["NotFoundTitle"] = ("Link not found", "الرابط غير موجود"),
        ["NotFoundBody"] = ("This registration link isn't valid. Please check the link you received.", "رابط التسجيل غير صالح. يرجى التحقق من الرابط الذي استلمته."),
        ["SeatsLeft"] = ("Seats left", "المقاعد المتبقية"),
        ["Language"] = ("العربية", "English"),
    };

    public static string Normalize(string? lang) => lang == "ar" ? "ar" : "en";

    public string Get(string key, string lang) =>
        Text.TryGetValue(key, out var t) ? (lang == "ar" ? t.Ar : t.En) : key;
}
