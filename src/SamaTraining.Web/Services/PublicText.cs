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
        ["Phone"] = ("Phone number (WhatsApp preferred)", "رقم الهاتف (يفضّل واتساب)"),
        ["Organization"] = ("Organization or university", "الجهة أو الجامعة"),
        ["Notes"] = ("Anything we should know?", "هل هناك ما تود إخبارنا به؟"),
        ["Optional"] = ("(optional)", "(اختياري)"),
        ["Submit"] = ("Register now", "سجّل الآن"),
        ["Sending"] = ("Sending…", "جارٍ الإرسال…"),
        ["Privacy"] = ("Your details are only used for this workshop.", "تُستخدم بياناتك لهذه الورشة فقط."),
        ["Required"] = ("Please enter your full name.", "يرجى إدخال اسمك الكامل."),
        ["InvalidEmail"] = ("Please enter a valid email address.", "يرجى إدخال بريد إلكتروني صحيح."),
        ["PhoneHint"] = ("Type the country code, then the number without the first 0. Example: 249912345678", "اكتب مفتاح الدولة ثم الرقم بدون الصفر الأول. مثال: 249912345678"),
        ["PhoneRequired"] = ("Please enter your phone number.", "يرجى إدخال رقم الهاتف."),
        ["PhoneDigitsOnly"] = ("Use digits only: the country code, then the number.", "استخدم الأرقام فقط: مفتاح الدولة ثم الرقم."),
        ["PhoneUnknownCountry"] = ("Start with your country code, e.g. 249 for Sudan or 20 for Egypt.", "ابدأ بمفتاح الدولة، مثل 249 للسودان أو 20 لمصر."),
        ["PhoneLeadingZero"] = ("Remove the 0 after the country code. Example: {0}{1}", "احذف الصفر بعد مفتاح الدولة. مثال: {0}{1}"),
        ["PhoneWrongLength"] = ("{0} numbers are {1} followed by {2} digits. Example: {1}{3}", "رقم {0} يتكون من {1} ثم {2} أرقام. مثال: {1}{3}"),
        ["DuplicatePhone"] = ("This phone number is already registered for this workshop.", "رقم الهاتف هذا مسجل مسبقاً في هذه الورشة."),
        ["SuccessTitle"] = ("You're registered!", "تم تسجيلك بنجاح!"),
        ["SuccessBody"] = ("Thank you. We've saved your seat and will contact you with any updates before the workshop.", "شكراً لك. تم حجز مقعدك وسنتواصل معك بأي مستجدات قبل موعد الورشة."),
        ["ClosedTitle"] = ("Registration is closed", "التسجيل مغلق"),
        ["ClosedBody"] = ("This workshop is no longer accepting registrations. Follow SAMA for upcoming workshops.", "لم يعد التسجيل متاحاً لهذه الورشة. تابعوا سما لمعرفة الورش القادمة."),
        ["FullTitle"] = ("Sorry, this workshop is full", "نعتذر، اكتمل العدد في هذه الورشة"),
        ["FullBody"] = ("We apologise — all seats were taken before your registration could be completed, so you have not been registered. Please follow SAMA for upcoming workshops.", "نعتذر منك — اكتملت جميع المقاعد قبل إتمام تسجيلك، لذلك لم يتم تسجيلك. يرجى متابعة سما لمعرفة الورش القادمة."),
        ["Fee"] = ("Fee", "الرسوم"),
        ["FreeNote"] = ("Free registration", "التسجيل مجاني"),
        ["Dates"] = ("Date", "التاريخ"),
        ["Time"] = ("Time", "الوقت"),
        ["Daily"] = ("daily", "يومياً"),
        ["Location"] = ("Location", "المكان"),
        ["Type"] = ("Workshop type", "نوع الورشة"),
        ["CancelledBefore"] = ("Your registration for this workshop was cancelled, so you can't register for it again. Please contact SAMA if you think this is a mistake.", "تم إلغاء تسجيلك في هذه الورشة، لذلك لا يمكنك التسجيل فيها مرة أخرى. يرجى التواصل مع سما إذا كان هناك خطأ."),
        ["Duplicate"] = ("You're already registered for this workshop with this email.", "أنت مسجل مسبقاً في هذه الورشة بهذا البريد الإلكتروني."),
        ["NotFoundTitle"] = ("Link not found", "الرابط غير موجود"),
        ["NotFoundBody"] = ("This registration link isn't valid. Please check the link you received.", "رابط التسجيل غير صالح. يرجى التحقق من الرابط الذي استلمته."),
        ["Language"] = ("العربية", "English"),
        ["TryAgain"] = ("The form was open for too long. Please check your details and submit again.", "انتهت مهلة النموذج. يرجى مراجعة بياناتك والإرسال مرة أخرى."),
    };

    public static string Normalize(string? lang) => lang == "ar" ? "ar" : "en";

    public string Get(string key, string lang) =>
        Text.TryGetValue(key, out var t) ? (lang == "ar" ? t.Ar : t.En) : key;
}
