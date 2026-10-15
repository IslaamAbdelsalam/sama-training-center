using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>US-11 / US-12: the public, bilingual registration page reached at /register/{token}.</summary>
[AllowAnonymous]
public partial class PublicRegistrationController : Controller
{
    private readonly WorkshopService _service;
    private readonly PublicText _text;

    public PublicRegistrationController(WorkshopService service, PublicText text)
    {
        _service = service;
        _text = text;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string token, string? lang)
    {
        var model = await BuildAsync(token, lang);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Index(string token, string? lang, PublicRegistrationForm form)
    {
        var model = await BuildAsync(token, lang);
        model.Form = form;
        if (model.State != "form")
        {
            return View(model);
        }

        // Validation messages in the visitor's language.
        ModelState.Clear();
        var l = model.Lang;
        if (string.IsNullOrWhiteSpace(form.FullName) || form.FullName.Trim().Length > 150)
            ModelState.AddModelError(nameof(form.FullName), _text.Get("Required", l));
        if (string.IsNullOrWhiteSpace(form.Email) || form.Email.Length > 256 || !new EmailAddressAttribute().IsValid(form.Email.Trim()))
            ModelState.AddModelError(nameof(form.Email), _text.Get("InvalidEmail", l));
        if (string.IsNullOrWhiteSpace(form.Phone) || !PhoneRegex().IsMatch(form.Phone.Trim()))
            ModelState.AddModelError(nameof(form.Phone), _text.Get("InvalidPhone", l));
        if (form.Organization?.Length > 150) form.Organization = form.Organization[..150];
        if (form.Notes?.Length > 500) form.Notes = form.Notes[..500];

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var outcome = await _service.RegisterAsync(token, new Registration
        {
            FullName = form.FullName!,
            Email = form.Email!,
            Phone = form.Phone!,
            Organization = form.Organization,
            Notes = form.Notes,
            Language = l
        });

        switch (outcome)
        {
            case WorkshopService.RegisterOutcome.Success:
                // Post/Redirect/Get so a refresh does not resubmit.
                return RedirectToAction(nameof(Done), new { token, lang = l });
            case WorkshopService.RegisterOutcome.Duplicate:
                model.Error = _text.Get("Duplicate", l);
                return View(model);
            case WorkshopService.RegisterOutcome.Full:
                model.State = "full";
                return View(model);
            case WorkshopService.RegisterOutcome.Closed:
                model.State = "closed";
                return View(model);
            default:
                model.State = "notfound";
                return View(model);
        }
    }

    [HttpGet("register/{token}/done")]
    public async Task<IActionResult> Done(string token, string? lang)
    {
        var model = await BuildAsync(token, lang);
        if (model.Workshop != null) model.State = "success";
        return View(nameof(Index), model);
    }

    private async Task<PublicRegistrationViewModel> BuildAsync(string token, string? lang)
    {
        var model = new PublicRegistrationViewModel { Token = token, Lang = PublicText.Normalize(lang) };
        var workshop = string.IsNullOrWhiteSpace(token) ? null : await _service.FindByTokenAsync(token);

        if (workshop == null)
        {
            model.State = "notfound";
            return model;
        }

        model.Workshop = workshop;
        if (!workshop.RegistrationOpen || workshop.Status == WorkshopStatus.Completed)
        {
            model.State = "closed";
            return model;
        }

        if (workshop.Capacity.HasValue)
        {
            var count = await _service.CountRegistrationsAsync(workshop.Id);
            model.SeatsLeft = Math.Max(0, workshop.Capacity.Value - count);
            if (model.SeatsLeft == 0) model.State = "full";
        }

        return model;
    }

    [GeneratedRegex(@"^\+?[0-9\s\-()]{7,20}$")]
    private static partial Regex PhoneRegex();
}
