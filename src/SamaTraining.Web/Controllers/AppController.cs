using Microsoft.AspNetCore.Mvc;

namespace SamaTraining.Web.Controllers;

/// <summary>Shared helpers for staff-facing controllers.</summary>
public abstract class AppController : Controller
{
    protected string? CurrentUserName => User.Identity?.Name;

    /// <summary>Green toast after a successful add, update or delete. Shown on the page we redirect to.</summary>
    protected void Success(string title, string? message = null) => Flash("success", title, message);

    /// <summary>Red toast when an action could not be done.</summary>
    protected void Error(string title, string? message = null) => Flash("danger", title, message);

    /// <summary>Neutral toast for information.</summary>
    protected void Info(string title, string? message = null) => Flash("info", title, message);

    /// <summary>Briefly highlights the row with this id on the next list page.</summary>
    protected void Highlight(object id) => TempData["Highlight"] = id.ToString();

    private void Flash(string kind, string title, string? message)
    {
        TempData["FlashKind"] = kind;
        TempData["FlashTitle"] = title;
        TempData["FlashMessage"] = message;
    }
}
