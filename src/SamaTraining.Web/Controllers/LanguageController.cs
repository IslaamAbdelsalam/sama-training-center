using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>Switches the staff screens between English and Arabic (remembered for a year in a cookie).</summary>
[AllowAnonymous]
public class LanguageController : Controller
{
    [HttpGet]
    public IActionResult Set(string lang, string? returnUrl)
    {
        Response.Cookies.Append(UiText.CookieName, lang == "ar" ? "ar" : "en", new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
