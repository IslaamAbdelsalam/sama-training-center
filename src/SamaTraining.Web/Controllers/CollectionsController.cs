using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>US-18: record registration fee payments, always in the workshop's currency.</summary>
[HasPermission(Permissions.CollectionsManage)]
public class CollectionsController : AppController
{
    private readonly ApplicationDbContext _db;

    public CollectionsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Create(int workshopId, int? registrationId)
    {
        var w = await _db.Workshops.FindAsync(workshopId);
        if (w == null) return NotFound();
        if (Blocked(w) is { } blocked) return blocked;

        var model = new CollectionFormModel
        {
            WorkshopId = w.Id,
            RegistrationId = registrationId,
            CollectedOn = DateTime.Today,
            Method = PaymentMethod.Cash
        };

        // Suggest the participant's remaining balance.
        if (registrationId.HasValue)
        {
            // Summed in memory: SQLite can't sum decimals in the database.
            var paid = (await _db.FeeCollections.Where(c => c.RegistrationId == registrationId).Select(c => c.Amount).ToListAsync()).Sum();
            var left = (w.Fee ?? 0) - paid;
            if (left > 0) model.Amount = left;
        }
        else
        {
            model.Amount = w.Fee;
        }

        await FillAsync(model, w);
        return View("Form", model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var c = await _db.FeeCollections.Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        if (Blocked(c.Workshop!) is { } blocked) return blocked;

        var model = new CollectionFormModel
        {
            Id = c.Id,
            WorkshopId = c.WorkshopId,
            RegistrationId = c.RegistrationId,
            Amount = c.Amount,
            CollectedOn = c.CollectedOn,
            Method = c.Method,
            Reference = c.Reference,
            Notes = c.Notes
        };
        await FillAsync(model, c.Workshop!);
        return View("Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Save(CollectionFormModel model, bool addAnother = false)
    {
        var w = await _db.Workshops.FindAsync(model.WorkshopId);
        if (w == null) return NotFound();
        if (Blocked(w) is { } blocked) return blocked;

        if (model.RegistrationId.HasValue
            && !await _db.Registrations.AnyAsync(r => r.Id == model.RegistrationId && r.WorkshopId == w.Id && !r.IsCancelled))
        {
            ModelState.AddModelError(nameof(model.RegistrationId), "Choose an active participant registered for this workshop (cancelled registrations can't pay).");
        }

        if (!ModelState.IsValid)
        {
            await FillAsync(model, w);
            return View("Form", model);
        }

        FeeCollection? c;
        var isNew = model.Id == 0;
        if (isNew)
        {
            c = new FeeCollection { WorkshopId = w.Id, CreatedBy = CurrentUserName, CreatedAt = DateTime.UtcNow };
            _db.FeeCollections.Add(c);
        }
        else
        {
            c = await _db.FeeCollections.FirstOrDefaultAsync(x => x.Id == model.Id && x.WorkshopId == w.Id);
            if (c == null) return NotFound();
            c.UpdatedBy = CurrentUserName;
            c.UpdatedAt = DateTime.UtcNow;
        }

        c.RegistrationId = model.RegistrationId!.Value;
        c.Amount = Math.Round(model.Amount!.Value, 2);
        c.CurrencyCode = w.CurrencyCode;
        c.CollectedOn = model.CollectedOn!.Value.Date;
        c.Method = model.Method!.Value;
        c.Reference = model.Reference?.Trim();
        c.Notes = model.Notes?.Trim();
        await _db.SaveChangesAsync();

        Success(isNew ? "Payment recorded" : "Payment updated", $"{MoneyFormat.Format(c.Amount, w.CurrencyCode)} for \"{w.Name}\".");
        Highlight("c" + c.Id);
        if (isNew && addAnother) return RedirectToAction(nameof(Create), new { workshopId = w.Id });
        return RedirectToAction("Details", "Workshops", new { id = w.Id, tab = "payments" });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.FeeCollections.Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        if (Blocked(c.Workshop!) is { } blocked) return blocked;

        _db.FeeCollections.Remove(c);
        await _db.SaveChangesAsync();
        Success("Payment deleted", $"{MoneyFormat.Format(c.Amount, c.CurrencyCode)} was removed from the collected total.");
        return RedirectToAction("Details", "Workshops", new { id = c.WorkshopId, tab = "payments" });
    }

    /// <summary>US-09: no payments on completed workshops; none needed on free ones.</summary>
    private IActionResult? Blocked(Workshop w)
    {
        if (w.Status == WorkshopStatus.Completed)
        {
            Error("Workshop is completed", "Collections can't be added or changed after a workshop is completed. Existing payments remain visible.");
            return RedirectToAction("Details", "Workshops", new { id = w.Id, tab = "payments" });
        }

        if (w.IsFree)
        {
            Info("Free workshop", "This workshop has no registration fee, so there is nothing to collect.");
            return RedirectToAction("Details", "Workshops", new { id = w.Id });
        }

        return null;
    }

    private async Task FillAsync(CollectionFormModel model, Workshop w)
    {
        model.WorkshopName = w.Name;
        model.CurrencyCode = w.CurrencyCode;
        model.Fee = w.Fee ?? 0;

        var paid = (await _db.FeeCollections.Where(c => c.WorkshopId == w.Id && c.Id != model.Id)
                .Select(c => new { c.RegistrationId, c.Amount }).ToListAsync())
            .GroupBy(c => c.RegistrationId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        model.Participants = (await _db.Registrations.Where(r => r.WorkshopId == w.Id && !r.IsCancelled).OrderBy(r => r.FullName)
                .Select(r => new { r.Id, r.FullName, r.Phone }).ToListAsync())
            .Select(r =>
            {
                var p = paid.GetValueOrDefault(r.Id);
                var status = DisplayFormat.Label(FinanceService.StatusOf(w.Fee, p));
                return new SelectListItem($"{r.FullName} — {status} ({p:N2} / {w.Fee:N2})", r.Id.ToString());
            })
            .ToList();
    }
}
