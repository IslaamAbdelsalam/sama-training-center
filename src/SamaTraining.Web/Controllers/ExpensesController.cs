using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>US-08 / US-09 / US-17: workshop expenses, always in the workshop's currency.
/// They can be managed from creation (before the workshop starts) until it is completed.</summary>
[HasPermission(Permissions.ExpensesManage)]
public class ExpensesController : AppController
{
    private readonly ApplicationDbContext _db;

    public ExpensesController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Create(int workshopId)
    {
        var w = await _db.Workshops.FindAsync(workshopId);
        if (w == null) return NotFound();
        if (!WorkshopService.CanAddExpense(w)) return Finalised(w.Id);

        var model = new ExpenseFormModel { WorkshopId = w.Id, WorkshopName = w.Name, CurrencyCode = w.CurrencyCode, ExpenseDate = DateTime.Today };
        await LoadCategoriesAsync(model, null);
        return View("Form", model);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var e = await _db.WorkshopExpenses.Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (!WorkshopService.CanAddExpense(e.Workshop!)) return Finalised(e.WorkshopId);

        var model = new ExpenseFormModel
        {
            Id = e.Id,
            WorkshopId = e.WorkshopId,
            WorkshopName = e.Workshop?.Name,
            CurrencyCode = e.Workshop?.CurrencyCode,
            ExpenseCategoryId = e.ExpenseCategoryId,
            Amount = e.Amount,
            ExpenseDate = e.ExpenseDate,
            Description = e.Description
        };
        await LoadCategoriesAsync(model, e.ExpenseCategoryId);
        return View("Form", model);
    }

    /// <param name="addAnother">When true, returns to a blank form after saving ("Save &amp; add another").</param>
    [HttpPost]
    public async Task<IActionResult> Save(ExpenseFormModel model, bool addAnother = false)
    {
        var w = await _db.Workshops.FindAsync(model.WorkshopId);
        if (w == null) return NotFound();
        model.WorkshopName = w.Name;
        model.CurrencyCode = w.CurrencyCode;

        if (!WorkshopService.CanAddExpense(w)) return Finalised(w.Id);

        if (model.ExpenseCategoryId.HasValue)
        {
            var category = await _db.ExpenseCategories.FindAsync(model.ExpenseCategoryId.Value);
            WorkshopExpense? existing = model.Id == 0 ? null : await _db.WorkshopExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.Id);
            var keepingInactive = existing != null && existing.ExpenseCategoryId == model.ExpenseCategoryId;
            if (category == null || (!category.IsActive && !keepingInactive))
            {
                ModelState.AddModelError(nameof(model.ExpenseCategoryId), "Choose an active expense type.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadCategoriesAsync(model, model.ExpenseCategoryId);
            return View("Form", model);
        }

        WorkshopExpense? e;
        var isNew = model.Id == 0;
        if (isNew)
        {
            e = new WorkshopExpense { WorkshopId = w.Id, CreatedBy = CurrentUserName, CreatedAt = DateTime.UtcNow };
            _db.WorkshopExpenses.Add(e);
        }
        else
        {
            e = await _db.WorkshopExpenses.FirstOrDefaultAsync(x => x.Id == model.Id && x.WorkshopId == w.Id);
            if (e == null) return NotFound();
            e.UpdatedBy = CurrentUserName;
            e.UpdatedAt = DateTime.UtcNow;
        }

        e.ExpenseCategoryId = model.ExpenseCategoryId!.Value;
        e.Amount = Math.Round(model.Amount!.Value, 2);
        e.ExpenseDate = model.ExpenseDate!.Value.Date;
        e.Description = model.Description;
        await _db.SaveChangesAsync();

        var amount = MoneyFormat.Format(e.Amount, w.CurrencyCode);
        Success(isNew ? "Expense added" : "Expense updated", $"{amount} recorded for \"{w.Name}\".");
        Highlight(e.Id);

        if (isNew && addAnother)
        {
            return RedirectToAction(nameof(Create), new { workshopId = w.Id });
        }

        return RedirectToAction("Details", "Workshops", new { id = w.Id, tab = "expenses" });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.WorkshopExpenses.Include(x => x.Workshop).FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (!WorkshopService.CanAddExpense(e.Workshop!)) return Finalised(e.WorkshopId);

        var workshopId = e.WorkshopId;
        var amount = MoneyFormat.Format(e.Amount, e.Workshop?.CurrencyCode);
        _db.WorkshopExpenses.Remove(e);
        await _db.SaveChangesAsync();
        Success("Expense deleted", $"{amount} was removed from the workshop total.");
        return RedirectToAction("Details", "Workshops", new { id = workshopId, tab = "expenses" });
    }

    /// <summary>US-09: a completed workshop's expenses are final; they stay visible but can't change.</summary>
    private IActionResult Finalised(int workshopId)
    {
        Error("Workshop is completed", "Expenses can't be added or changed after a workshop is completed. Existing expenses remain visible.");
        return RedirectToAction("Details", "Workshops", new { id = workshopId, tab = "expenses" });
    }

    private async Task LoadCategoriesAsync(ExpenseFormModel model, int? currentId)
    {
        model.Categories = await _db.ExpenseCategories
            .Where(c => c.IsActive || c.Id == currentId)
            .OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.NameAr != null && c.NameAr != "" ? c.Name + " — " + c.NameAr : c.Name, c.Id.ToString()))
            .ToListAsync();
    }
}
