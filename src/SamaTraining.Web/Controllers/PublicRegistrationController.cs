using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>US-11 / US-12: the public, bilingual registration page reached at /register/{token}.</summary>
[AllowAnonymous]
public class PublicRegistrationController : Controller
{
    private readonly WorkshopService _service;
    private readonly PublicText _text;
    private readonly PhoneNumberService _phones;

    public PublicRegistrationController(WorkshopService service, PublicText text, PhoneNumberService phones)
    {
        _service = service;
        _text = text;
        _phones = phones;
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
        var phone = _phones.Check(form.Phone);
        if (phone.Status != PhoneCheck.Valid)
            ModelState.AddModelError(nameof(form.Phone), PhoneError(phone, l));
        else
            form.Phone = phone.Normalized; // shown back without spaces, after the fixed 00
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
            case WorkshopService.RegisterOutcome.DuplicatePhone:
                model.Error = _text.Get("DuplicatePhone", l);
                ModelState.AddModelError(nameof(form.Phone), _text.Get("DuplicatePhone", l));
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

        // US-14: capacity is checked internally; the number of seats left is never shown.
        if (workshop.Capacity.HasValue && await _service.CountRegistrationsAsync(workshop.Id) >= workshop.Capacity.Value)
        {
            model.State = "full";
        }

        return model;
    }

    /// <summary>A specific, friendly message for each kind of phone mistake.</summary>
    private string PhoneError(PhoneResult r, string l)
    {
        var name = r.Country == null ? "" : (l == "ar" ? r.Country.NameAr : r.Country.Name);
        return r.Status switch
        {
            PhoneCheck.Empty => _text.Get("PhoneRequired", l),
            PhoneCheck.NotDigits => _text.Get("PhoneDigitsOnly", l),
            PhoneCheck.UnknownCountry => _text.Get("PhoneUnknownCountry", l),
            PhoneCheck.LeadingZero => string.Format(_text.Get("PhoneLeadingZero", l), r.Country!.DialCode, r.Country.Example),
            PhoneCheck.WrongLength => string.Format(_text.Get("PhoneWrongLength", l), name, r.Country!.DialCode, r.Country.Digits, r.Country.Example),
            _ => _text.Get("PhoneDigitsOnly", l)
        };
    }
}
