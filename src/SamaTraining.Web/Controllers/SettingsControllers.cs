using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;

namespace SamaTraining.Web.Controllers;

/// <summary>US-01: expense categories (lookup values).</summary>
[HasPermission(Permissions.SettingsManage)]
public class ExpenseCategoriesController : AppController
{
    private readonly ApplicationDbContext _db;

    public ExpenseCategoriesController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var items = await _db.ExpenseCategories
            .OrderBy(c => c.Name)
            .Select(c => new { Category = c, Uses = c.Expenses.Count })
            .ToListAsync();

        ViewBag.Uses = items.ToDictionary(i => i.Category.Id, i => i.Uses);
        return View(items.Select(i => i.Category).ToList());
    }

    public IActionResult Create() => View("Form", new ExpenseCategoryFormModel());

    public async Task<IActionResult> Edit(int id)
    {
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c == null) return NotFound();
        return View("Form", new ExpenseCategoryFormModel { Id = c.Id, Name = c.Name, Description = c.Description, IsActive = c.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Save(ExpenseCategoryFormModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (await _db.ExpenseCategories.AnyAsync(c => c.Name == model.Name && c.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
        }

        if (!ModelState.IsValid) return View("Form", model);

        ExpenseCategory? entity;
        if (model.Id == 0)
        {
            entity = new ExpenseCategory();
            _db.ExpenseCategories.Add(entity);
        }
        else
        {
            entity = await _db.ExpenseCategories.FindAsync(model.Id);
            if (entity == null) return NotFound();
        }

        entity.Name = model.Name;
        entity.Description = model.Description;
        entity.IsActive = model.IsActive;
        await _db.SaveChangesAsync();

        Success($"Expense category \"{entity.Name}\" saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c == null) return NotFound();

        if (await _db.WorkshopExpenses.AnyAsync(e => e.ExpenseCategoryId == id))
        {
            Error($"\"{c.Name}\" is used by existing expenses, so it cannot be deleted. Deactivate it instead.");
            return RedirectToAction(nameof(Index));
        }

        _db.ExpenseCategories.Remove(c);
        await _db.SaveChangesAsync();
        Success($"Expense category \"{c.Name}\" deleted.");
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>US-02: workshop configuration — halls and locations.</summary>
[HasPermission(Permissions.SettingsManage)]
public class HallsController : AppController
{
    private readonly ApplicationDbContext _db;

    public HallsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var items = await _db.Halls
            .OrderBy(h => h.Name)
            .Select(h => new { Hall = h, Uses = h.Workshops.Count })
            .ToListAsync();

        ViewBag.Uses = items.ToDictionary(i => i.Hall.Id, i => i.Uses);
        return View(items.Select(i => i.Hall).ToList());
    }

    public IActionResult Create() => View("Form", new HallFormModel());

    public async Task<IActionResult> Edit(int id)
    {
        var h = await _db.Halls.FindAsync(id);
        if (h == null) return NotFound();
        return View("Form", new HallFormModel { Id = h.Id, Name = h.Name, Location = h.Location, Capacity = h.Capacity, IsActive = h.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Save(HallFormModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (await _db.Halls.AnyAsync(h => h.Name == model.Name && h.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), "A hall with this name already exists.");
        }

        if (!ModelState.IsValid) return View("Form", model);

        Hall? entity;
        if (model.Id == 0)
        {
            entity = new Hall();
            _db.Halls.Add(entity);
        }
        else
        {
            entity = await _db.Halls.FindAsync(model.Id);
            if (entity == null) return NotFound();
        }

        entity.Name = model.Name;
        entity.Location = model.Location;
        entity.Capacity = model.Capacity;
        entity.IsActive = model.IsActive;
        await _db.SaveChangesAsync();

        Success($"Hall \"{entity.Name}\" saved.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var h = await _db.Halls.FindAsync(id);
        if (h == null) return NotFound();

        if (await _db.Workshops.AnyAsync(w => w.HallId == id))
        {
            Error($"\"{h.Name}\" is used by existing workshops, so it cannot be deleted. Deactivate it instead.");
            return RedirectToAction(nameof(Index));
        }

        _db.Halls.Remove(h);
        await _db.SaveChangesAsync();
        Success($"Hall \"{h.Name}\" deleted.");
        return RedirectToAction(nameof(Index));
    }
}
