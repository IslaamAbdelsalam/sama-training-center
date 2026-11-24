using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

/// <summary>US-01: expense categories (lookup values).</summary>
[HasPermission(Permissions.SettingsManage)]
public class ExpenseCategoriesController : AppController
{
    private readonly ApplicationDbContext _db;

    public ExpenseCategoriesController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? cursor)
    {
        var query = _db.ExpenseCategories.AsQueryable();
        var total = await query.CountAsync();
        var cur = Paging.Decode(cursor, 2);
        if (cur != null && Paging.TryInt(cur[1], out var afterId))
        {
            var afterName = cur[0];
            query = query.Where(c => string.Compare(c.Name, afterName) > 0 || (c.Name == afterName && c.Id > afterId));
        }
        else cur = null;
        var (items, more) = Paging.Trim(await query
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Take(Paging.DetailSize + 1)
            .Select(c => new { Category = c, Uses = c.Expenses.Count })
            .ToListAsync(), Paging.DetailSize);
        ViewBag.Pager = new PageLinks
        {
            IsPaged = cur != null, Shown = items.Count, Total = total,
            Next = more ? Paging.Encode(items[^1].Category.Name, items[^1].Category.Id) : null
        };

        ViewBag.Uses = items.ToDictionary(i => i.Category.Id, i => i.Uses);
        return View(items.Select(i => i.Category).ToList());
    }

    public IActionResult Create() => View("Form", new ExpenseCategoryFormModel());

    public async Task<IActionResult> Edit(int id)
    {
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c == null) return NotFound();
        return View("Form", new ExpenseCategoryFormModel
        {
            Id = c.Id, Name = c.Name, NameAr = c.NameAr, Description = c.Description, IsActive = c.IsActive,
            UsedBy = await _db.WorkshopExpenses.CountAsync(e => e.ExpenseCategoryId == id)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(ExpenseCategoryFormModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (await _db.ExpenseCategories.AnyAsync(c => c.Name == model.Name && c.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), UiText.Get("A category with this name already exists."));
        }

        if (!ModelState.IsValid)
        {
            model.UsedBy = model.Id == 0 ? 0 : await _db.WorkshopExpenses.CountAsync(e => e.ExpenseCategoryId == model.Id);
            return View("Form", model);
        }

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
        entity.NameAr = model.NameAr?.Trim();
        entity.Description = model.Description;
        entity.IsActive = model.IsActive;
        await _db.SaveChangesAsync();

        Success(model.Id == 0 ? "Category added" : "Category updated", UiText.Format("\"{0}\" is saved.", entity.Name));
        Highlight(entity.Id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, bool fromEdit = false)
    {
        var c = await _db.ExpenseCategories.FindAsync(id);
        if (c == null) return NotFound();

        // US-01: a category used by any expense can't be deleted.
        if (await _db.WorkshopExpenses.AnyAsync(e => e.ExpenseCategoryId == id))
        {
            Error("Category can't be deleted", UiText.Format("\"{0}\" is used by existing expenses. Switch it to inactive instead.", c.Name));
            return fromEdit ? RedirectToAction(nameof(Edit), new { id }) : RedirectToAction(nameof(Index));
        }

        _db.ExpenseCategories.Remove(c);
        await _db.SaveChangesAsync();
        Success("Category deleted", UiText.Format("\"{0}\" was removed.", c.Name));
        return RedirectToAction(nameof(Index));
    }
}

/// <summary>US-02: workshop configuration — halls and locations.</summary>
[HasPermission(Permissions.SettingsManage)]
public class HallsController : AppController
{
    private readonly ApplicationDbContext _db;

    public HallsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? cursor)
    {
        var query = _db.Halls.AsQueryable();
        var total = await query.CountAsync();
        var cur = Paging.Decode(cursor, 2);
        if (cur != null && Paging.TryInt(cur[1], out var afterId))
        {
            var afterName = cur[0];
            query = query.Where(h => string.Compare(h.Name, afterName) > 0 || (h.Name == afterName && h.Id > afterId));
        }
        else cur = null;
        var (items, more) = Paging.Trim(await query
            .OrderBy(h => h.Name).ThenBy(h => h.Id)
            .Take(Paging.DetailSize + 1)
            .Select(h => new { Hall = h, Uses = h.Workshops.Count })
            .ToListAsync(), Paging.DetailSize);
        ViewBag.Pager = new PageLinks
        {
            IsPaged = cur != null, Shown = items.Count, Total = total,
            Next = more ? Paging.Encode(items[^1].Hall.Name, items[^1].Hall.Id) : null
        };

        ViewBag.Uses = items.ToDictionary(i => i.Hall.Id, i => i.Uses);
        return View(items.Select(i => i.Hall).ToList());
    }

    public IActionResult Create() => View("Form", new HallFormModel());

    public async Task<IActionResult> Edit(int id)
    {
        var h = await _db.Halls.FindAsync(id);
        if (h == null) return NotFound();
        return View("Form", new HallFormModel
        {
            Id = h.Id, Name = h.Name, NameAr = h.NameAr, Location = h.Location, LocationAr = h.LocationAr,
            Capacity = h.Capacity, IsActive = h.IsActive,
            UsedBy = await _db.Workshops.CountAsync(w => w.HallId == id)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(HallFormModel model)
    {
        model.Name = model.Name?.Trim() ?? string.Empty;
        if (await _db.Halls.AnyAsync(h => h.Name == model.Name && h.Id != model.Id))
        {
            ModelState.AddModelError(nameof(model.Name), UiText.Get("A hall with this name already exists."));
        }

        if (!ModelState.IsValid)
        {
            model.UsedBy = model.Id == 0 ? 0 : await _db.Workshops.CountAsync(w => w.HallId == model.Id);
            return View("Form", model);
        }

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
        entity.NameAr = model.NameAr?.Trim();
        entity.Location = model.Location;
        entity.LocationAr = model.LocationAr?.Trim();
        entity.Capacity = model.Capacity;
        entity.IsActive = model.IsActive;
        await _db.SaveChangesAsync();

        Success(model.Id == 0 ? "Hall added" : "Hall updated", UiText.Format("\"{0}\" is saved.", entity.Name));
        Highlight(entity.Id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, bool fromEdit = false)
    {
        var h = await _db.Halls.FindAsync(id);
        if (h == null) return NotFound();

        // US-01: a hall or location used by any workshop can't be deleted.
        if (await _db.Workshops.AnyAsync(w => w.HallId == id))
        {
            Error("Hall can't be deleted", UiText.Format("\"{0}\" is used by existing workshops. Switch it to inactive instead.", h.Name));
            return fromEdit ? RedirectToAction(nameof(Edit), new { id }) : RedirectToAction(nameof(Index));
        }

        _db.Halls.Remove(h);
        await _db.SaveChangesAsync();
        Success("Hall deleted", UiText.Format("\"{0}\" was removed.", h.Name));
        return RedirectToAction(nameof(Index));
    }
}
