using Microsoft.AspNetCore.Mvc;

namespace SamaTraining.Web.Controllers;

/// <summary>Shared helpers for staff-facing controllers.</summary>
public abstract class AppController : Controller
{
    protected string? CurrentUserName => User.Identity?.Name;

    protected void Success(string message) => TempData["FlashSuccess"] = message;

    protected void Error(string message) => TempData["FlashError"] = message;
}
