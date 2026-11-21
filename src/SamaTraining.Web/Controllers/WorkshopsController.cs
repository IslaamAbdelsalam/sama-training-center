using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using Microsoft.Extensions.Options;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

public class WorkshopsController : AppController
{
    private readonly ApplicationDbContext _db;
    private readonly WorkshopService _service;
    private readonly AppSettings _settings;
    private readonly FinanceService _finance;

    public WorkshopsController(ApplicationDbContext db, WorkshopService service, IOptions<AppSettings> settings, FinanceService finance)
    {
        _db = db;
        _service = service;
        _settings = settings.Value;
        _finance = finance;
    }

    // US-06: search and filter
    [HasPermission(Permissions.WorkshopsView)]
    public async Task<IActionResult> Index(WorkshopFilterModel filter)
    {
        var query = _db.Workshops.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(w => w.Name.Contains(s) || (w.NameAr != null && w.NameAr.Contains(s)));
        }

        if (filter.Status.HasValue) query = query.Where(w => w.Status == filter.Status);
        if (filter.Type.HasValue) query = query.Where(w => w.Type == filter.Type);
        if (!string.IsNullOrEmpty(filter.Country)) query = query.Where(w => w.Country == filter.Country);
        if (filter.From.HasValue) query = query.Where(w => w.StartDateTime >= filter.From.Value.Date);
        if (filter.To.HasValue)
        {
            var end = filter.To.Value.Date.AddDays(1);
            query = query.Where(w => w.StartDateTime < end);
        }

        var items = await query
            .OrderByDescending(w => w.StartDateTime)
            .Select(w => new WorkshopListItem
            {
                Id = w.Id,
                Name = w.Name,
                StartDateTime = w.StartDateTime,
                EndDateTime = w.EndDateTime,
                Type = w.Type,
                Country = w.Country,
                HallName = w.Hall != null ? w.Hall.Name : null,
                Status = w.Status,
                RegistrationOpen = w.RegistrationOpen,
                Registrations = w.Registrations.Count
            })
            .ToListAsync();

        return View(new WorkshopIndexViewModel { Filter = filter, Items = items });
    }

    [HasPermission(Permissions.WorkshopsView)]
    public async Task<IActionResult> Details(int id, string tab = "overview")
    {
        var workshop = await _db.Workshops.Include(w => w.Hall).FirstOrDefaultAsync(w => w.Id == id);
        if (workshop == null) return NotFound();

        var model = new WorkshopDetailsViewModel
        {
            Workshop = workshop,
            Tab = tab,
            CanEdit = WorkshopService.CanEdit(workshop),
            CanDelete = await _service.CanDeleteAsync(workshop),
            CanAddExpense = WorkshopService.CanAddExpense(workshop),
            CanCollect = WorkshopService.CanCollect(workshop),
            NextStatus = WorkshopService.NextStatus(workshop.Status),
            PreviousStatus = WorkshopService.PreviousStatus(workshop.Status),
            RegistrationCount = await _service.CountRegistrationsAsync(workshop.Id),
            History = await _db.WorkshopStatusHistory.Where(h => h.WorkshopId == id).OrderBy(h => h.ChangedAt).ToListAsync()
        };

        if (workshop.RegistrationToken != null)
        {
            model.RegistrationUrl = Url.Action("Index", "PublicRegistration", new { token = workshop.RegistrationToken }, Request.Scheme);
        }

        if (User.Can(Permissions.RegistrationsView))
        {
            model.Registrations = await _db.Registrations.Where(r => r.WorkshopId == id).OrderByDescending(r => r.RegisteredAt).ToListAsync();

            // Returning students: same phone number registered for other workshops.
            var phones = model.Registrations.Select(r => r.Phone).Distinct().ToList();
            model.EarlierWorkshopsByPhone = (await _db.Registrations
                    .Where(r => r.WorkshopId != id && phones.Contains(r.Phone))
                    .Select(r => new { r.Phone, r.WorkshopId })
                    .ToListAsync())
                .GroupBy(r => r.Phone)
                .ToDictionary(g => g.Key, g => g.Select(x => x.WorkshopId).Distinct().Count());
        }

        if (User.Can(Permissions.ExpensesView))
        {
            model.Expenses = await _db.WorkshopExpenses.Include(e => e.ExpenseCategory)
                .Where(e => e.WorkshopId == id).OrderByDescending(e => e.ExpenseDate).ToListAsync();
            model.TotalExpenses = model.Expenses.Sum(e => e.Amount);
        }

        if (User.Can(Permissions.CollectionsView))
        {
            model.Collections = await _db.FeeCollections.Include(c => c.Registration)
                .Where(c => c.WorkshopId == id).OrderByDescending(c => c.CollectedOn).ThenByDescending(c => c.Id).ToListAsync();
            model.PaidByRegistration = model.Collections.GroupBy(c => c.RegistrationId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        }

        // US-19: the same figures as the dashboard and reports.
        model.Finance = (await _finance.ForWorkshopsAsync(new[] { id })).GetValueOrDefault(id) ?? new FinancialSummary { Currency = workshop.CurrencyCode };

        return View(model);
    }

    // US-03 / US-04
    [HasPermission(Permissions.WorkshopsManage)]
    public async Task<IActionResult> Create()
    {
        var model = new WorkshopFormModel
        {
            Date = DateTime.Today.AddDays(7),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(13, 0, 0),
            Type = WorkshopType.OnSite,
            Country = _settings.GetCountries().FirstOrDefault()?.Code
        };
        model.EndDate = model.Date;
        await LoadHallsAsync(model, null);
        return View("Form", model);
    }

    // US-05
    [HasPermission(Permissions.WorkshopsManage)]
    public async Task<IActionResult> Edit(int id)
    {
        var w = await _db.Workshops.FindAsync(id);
        if (w == null) return NotFound();
        if (!WorkshopService.CanEdit(w))
        {
            Info("This workshop is read-only", "Completed workshops can't be edited.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new WorkshopFormModel
        {
            Id = w.Id,
            Name = w.Name,
            Date = w.StartDateTime.Date,
            IsMultiDay = w.IsMultiDay,
            EndDate = w.EndDateTime.Date,
            Fee = w.Fee,
            StartTime = w.StartDateTime.TimeOfDay,
            EndTime = w.EndDateTime.TimeOfDay,
            Type = w.Type,
            Country = w.Country,
            CountryLocked = await _db.WorkshopExpenses.AnyAsync(e => e.WorkshopId == w.Id),
            HallId = w.HallId,
            MeetingLink = w.MeetingLink,
            Capacity = w.Capacity,
            NameAr = w.NameAr,
            Description = w.Description,
            DescriptionAr = w.DescriptionAr,
            Content = RichText.ForEditor(w.Content),
            ContentAr = RichText.ForEditor(w.ContentAr)
        };
        await LoadHallsAsync(model, w.HallId);
        return View("Form", model);
    }

    [HttpPost, HasPermission(Permissions.WorkshopsManage)]
    public async Task<IActionResult> Save(WorkshopFormModel model)
    {
        // Cross-field rules, checked here so they show together with any missing-field errors.
        // US-05: single day, or a date range whose end date is not before the start date.
        if (model.IsMultiDay)
        {
            if (model.EndDate == null)
            {
                ModelState.AddModelError(nameof(model.EndDate), "Choose the end date of the workshop.");
            }
            else if (model.Date.HasValue && model.EndDate.Value.Date < model.Date.Value.Date)
            {
                ModelState.AddModelError(nameof(model.EndDate), "The end date can't be earlier than the start date.");
            }
        }

        if (model.StartTime.HasValue && model.EndTime.HasValue && model.EndTime <= model.StartTime)
        {
            ModelState.AddModelError(nameof(model.EndTime), "End time must be after the start time.");
        }

        if (model.Type == WorkshopType.OnSite && model.HallId == null)
        {
            ModelState.AddModelError(nameof(model.HallId), "Choose a hall or location for an on-site workshop.");
        }

        var country = _settings.FindCountry(model.Country);
        if (model.Country != null && country == null)
        {
            ModelState.AddModelError(nameof(model.Country), "Choose a country from the list.");
        }

        if (model.Id != 0)
        {
            var existing = await _db.Workshops.AsNoTracking().FirstOrDefaultAsync(x => x.Id == model.Id);
            if (existing != null && existing.Country != model.Country
                && await _db.WorkshopExpenses.AnyAsync(e => e.WorkshopId == model.Id))
            {
                ModelState.AddModelError(nameof(model.Country),
                    $"This workshop already has expenses in {existing.CurrencyCode}, so its country can't change.");
                model.CountryLocked = true;
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadHallsAsync(model, model.HallId);
            return View("Form", model);
        }

        Workshop? w;
        if (model.Id == 0)
        {
            w = new Workshop
            {
                Status = WorkshopStatus.Pending,
                RegistrationOpen = false,
                CreatedBy = CurrentUserName,
                CreatedAt = DateTime.UtcNow
            };
            _db.Workshops.Add(w);
        }
        else
        {
            w = await _db.Workshops.FindAsync(model.Id);
            if (w == null) return NotFound();
            if (!WorkshopService.CanEdit(w))
            {
                Info("This workshop is read-only", "Completed workshops can't be edited.");
                return RedirectToAction(nameof(Details), new { id = w.Id });
            }

            w.UpdatedBy = CurrentUserName;
            w.UpdatedAt = DateTime.UtcNow;
        }

        var date = model.Date!.Value.Date;
        var endDate = model.IsMultiDay && model.EndDate.HasValue ? model.EndDate.Value.Date : date;
        w.Name = model.Name.Trim();
        w.NameAr = model.NameAr?.Trim();
        w.StartDateTime = date + model.StartTime!.Value;
        w.EndDateTime = endDate + model.EndTime!.Value;
        w.Fee = model.Fee is > 0 ? Math.Round(model.Fee.Value, 2) : null;
        w.Type = model.Type!.Value;
        w.Country = country!.Code;
        w.CurrencyCode = country.Currency;
        w.HallId = model.Type == WorkshopType.OnSite ? model.HallId : null;
        w.MeetingLink = model.Type == WorkshopType.Online ? model.MeetingLink : null;
        w.Capacity = model.Capacity;
        w.Description = model.Description;
        w.DescriptionAr = model.DescriptionAr;
        // US-04: keep the agenda's formatting, without anything unsafe.
        w.Content = RichText.Clean(model.Content);
        w.ContentAr = RichText.Clean(model.ContentAr);

        var isNew = model.Id == 0;
        await _db.SaveChangesAsync();
        if (isNew) Success("Workshop created", $"\"{w.Name}\" is pending. Open registration when you're ready to share the link.");
        else Success("Changes saved", $"\"{w.Name}\" is up to date.");
        return RedirectToAction(nameof(Details), new { id = w.Id });
    }

    // US-07
    [HttpPost, HasPermission(Permissions.WorkshopsDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        var w = await _db.Workshops.FindAsync(id);
        if (w == null) return NotFound();

        if (!await _service.CanDeleteAsync(w))
        {
            Error("Workshop can't be deleted", "Only pending workshops with no registrations or expenses can be deleted.");
            return RedirectToAction(nameof(Details), new { id });
        }

        _db.Workshops.Remove(w);
        await _db.SaveChangesAsync();
        Success("Workshop deleted", $"\"{w.Name}\" was removed.");
        return RedirectToAction(nameof(Index));
    }

    // Status forward (Pending → Ongoing → Completed)
    [HttpPost, HasPermission(Permissions.WorkshopsChangeStatus)]
    public async Task<IActionResult> AdvanceStatus(int id, WorkshopStatus target)
    {
        var result = await _service.ChangeStatusAsync(id, target, CurrentUserName, allowReverse: false);
        if (result.Success)
        {
            if (target == WorkshopStatus.Ongoing) Success("Workshop started", "Status is now Ongoing.");
            else Success("Workshop completed", "Registration is closed. Expenses and collections are now finalised.");
        }
        else Error("Status not changed", result.Error);
        return RedirectToAction(nameof(Details), new { id, tab = "history" });
    }

    // US-10: reverse a mistaken status change (Ongoing → Pending, Completed → Ongoing)
    [HttpPost, HasPermission(Permissions.WorkshopsReverseStatus)]
    public async Task<IActionResult> ReverseStatus(int id, WorkshopStatus target)
    {
        var result = await _service.ChangeStatusAsync(id, target, CurrentUserName, allowReverse: true);
        if (result.Success) Success("Status reversed", $"The workshop is back to {target}. The change is recorded in History.");
        else Error("Status not changed", result.Error);
        return RedirectToAction(nameof(Details), new { id, tab = "history" });
    }

    // US-09 / US-10
    [HttpPost, HasPermission(Permissions.RegistrationsManage)]
    public async Task<IActionResult> OpenRegistration(int id)
    {
        var result = await _service.OpenRegistrationAsync(id, CurrentUserName);
        if (result.Success) Success("Registration is open", "Copy the link from the Registration panel and share it with students.");
        else Error("Registration not opened", result.Error);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, HasPermission(Permissions.RegistrationsManage)]
    public async Task<IActionResult> CloseRegistration(int id)
    {
        var result = await _service.CloseRegistrationAsync(id, CurrentUserName);
        if (result.Success) Success("Registration is closed", "The link now shows students that registration is closed.");
        else Error("Registration not closed", result.Error);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, HasPermission(Permissions.RegistrationsManage)]
    public async Task<IActionResult> RegenerateLink(int id)
    {
        var result = await _service.RegenerateLinkAsync(id, CurrentUserName);
        if (result.Success) Success("New link created", "The old link no longer works. Share the new one.");
        else Error("Link not changed", result.Error);
        return RedirectToAction(nameof(Details), new { id });
    }

    // US-13: export registered students
    [HasPermission(Permissions.RegistrationsView)]
    public async Task<IActionResult> ExportRegistrations(int id)
    {
        var w = await _db.Workshops.FindAsync(id);
        if (w == null) return NotFound();

        var rows = await _db.Registrations.Where(r => r.WorkshopId == id).OrderBy(r => r.RegisteredAt).ToListAsync();
        var sb = new StringBuilder();
        var showMoney = User.Can(Permissions.CollectionsView) && !w.IsFree;
        var paid = showMoney
            ? (await _db.FeeCollections.Where(c => c.WorkshopId == id).Select(c => new { c.RegistrationId, c.Amount }).ToListAsync())
                .GroupBy(c => c.RegistrationId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount))
            : new Dictionary<int, decimal>();
        sb.Append("Full name,Email,Phone,Organization,Notes,Language,Registered at (UTC)");
        sb.AppendLine(showMoney ? $",Fee ({w.CurrencyCode}),Paid ({w.CurrencyCode}),Outstanding ({w.CurrencyCode}),Payment status" : "");
        var phoneFormat = HttpContext.RequestServices.GetRequiredService<PhoneNumberService>();
        foreach (var r in rows)
        {
            sb.Append(string.Join(",", Csv(r.FullName), Csv(r.Email), Csv(phoneFormat.Format(r.Phone)), Csv(r.Organization),
                Csv(r.Notes), r.Language, r.RegisteredAt.ToString("yyyy-MM-dd HH:mm")));
            if (showMoney)
            {
                var p = paid.GetValueOrDefault(r.Id);
                sb.Append($",{w.Fee:0.00},{p:0.00},{(w.Fee ?? 0) - p:0.00},{DisplayFormat.Label(FinanceService.StatusOf(w.Fee, p))}");
            }
            sb.AppendLine();
        }

        // UTF-8 with BOM so Excel shows Arabic names correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var safeName = string.Concat(w.Name.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim().Replace(' ', '-');
        return File(bytes, "text/csv", $"registrations-{safeName}-{w.Id}.csv");
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Neutralise spreadsheet formulas and escape quotes.
        if ("=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private async Task LoadHallsAsync(WorkshopFormModel model, int? currentHallId)
    {
        model.Halls = await _db.Halls
            .Where(h => h.IsActive || h.Id == currentHallId)
            .OrderBy(h => h.Name)
            .Select(h => new SelectListItem(h.Location != null ? h.Name + " — " + h.Location : h.Name, h.Id.ToString()))
            .ToListAsync();
    }
}
