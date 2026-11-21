using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;
using Microsoft.Extensions.Options;

namespace SamaTraining.Web.Controllers;

public class DashboardController : AppController
{
    private readonly ApplicationDbContext _db;
    private readonly AppSettings _settings;
    private readonly FinanceService _finance;

    public DashboardController(ApplicationDbContext db, IOptions<AppSettings> settings, FinanceService finance)
    {
        _db = db;
        _settings = settings.Value;
        _finance = finance;
    }

    public async Task<IActionResult> Index()
    {
        if (!User.Can(Permissions.DashboardView))
        {
            // Users without dashboard access land on the first module they can use.
            if (User.Can(Permissions.WorkshopsView)) return RedirectToAction("Index", "Workshops");
            if (User.Can(Permissions.UsersManage)) return RedirectToAction("Index", "Users");
            return RedirectToAction("AccessDenied", "Account");
        }

        var counts = await _db.Workshops
            .GroupBy(w => w.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var today = DateTime.Today;
        var model = new DashboardViewModel
        {
            Pending = counts.FirstOrDefault(c => c.Status == WorkshopStatus.Pending)?.Count ?? 0,
            Ongoing = counts.FirstOrDefault(c => c.Status == WorkshopStatus.Ongoing)?.Count ?? 0,
            Completed = counts.FirstOrDefault(c => c.Status == WorkshopStatus.Completed)?.Count ?? 0,
            OpenRegistrations = await _db.Workshops.CountAsync(w => w.RegistrationOpen),
            TotalRegistrations = await _db.Registrations.CountAsync(),
            ShowExpenses = User.Can(Permissions.ExpensesView),
            ShowIncome = User.Can(Permissions.CollectionsView),
            Upcoming = await _db.Workshops
                .Include(w => w.Hall)
                .Where(w => w.Status != WorkshopStatus.Completed && w.EndDateTime >= today)
                .OrderBy(w => w.StartDateTime)
                .Take(6)
                .ToListAsync()
        };

        // ---- Everything grouped by currency (SDG, EGP), exactly as entered: no conversion. ----
        // Small data, so it is loaded and summed in memory (works on every database provider).
        var workshops = await _db.Workshops
            .Select(w => new { w.Id, w.CurrencyCode, w.Status, Students = w.Registrations.Count })
            .ToListAsync();

        // Users without expense permission get no amounts (the query returns nothing).
        var showExpenses = model.ShowExpenses;
        var expenses = await _db.WorkshopExpenses
            .Where(e => showExpenses)
            .Select(e => new { e.WorkshopId, e.Workshop!.CurrencyCode, e.Amount, e.ExpenseDate, Category = e.ExpenseCategory!.Name })
            .ToListAsync();

        var monthStart = new DateTime(today.Year, today.Month, 1);
        var financeByCurrency = FinanceService.ByCurrency((await _finance.ForWorkshopsAsync()).Values).ToDictionary(f => f.Currency);

        // Every configured currency gets a block (even with no data yet), plus any other currency found in the data.
        var currencies = _settings.GetCountries()
            .Select(c => (c.Currency, Country: c.Name))
            .Concat(workshops.Select(w => (Currency: w.CurrencyCode, Country: w.CurrencyCode)))
            .GroupBy(c => c.Currency)
            .Select(g => g.First());

        foreach (var (currency, country) in currencies)
        {
            var ws = workshops.Where(w => w.CurrencyCode == currency).ToList();
            var ex = expenses.Where(e => e.CurrencyCode == currency).ToList();
            var top = ex.GroupBy(e => e.Category)
                .Select(g => new { Category = g.Key, Amount = g.Sum(e => e.Amount) })
                .OrderByDescending(g => g.Amount)
                .FirstOrDefault();

            model.Currencies.Add(new DashboardCurrencySummary
            {
                Currency = currency,
                CountryName = string.Join(", ", _settings.GetCountries().Where(c => c.Currency == currency).Select(c => c.Name).DefaultIfEmpty(country)),
                Workshops = ws.Count,
                Pending = ws.Count(w => w.Status == WorkshopStatus.Pending),
                Ongoing = ws.Count(w => w.Status == WorkshopStatus.Ongoing),
                Completed = ws.Count(w => w.Status == WorkshopStatus.Completed),
                Students = ws.Sum(w => w.Students),
                TotalExpenses = ex.Sum(e => e.Amount),
                ExpensesThisMonth = ex.Where(e => e.ExpenseDate >= monthStart).Sum(e => e.Amount),
                TopCategory = top?.Category,
                TopCategoryAmount = top?.Amount ?? 0,
                Finance = financeByCurrency.GetValueOrDefault(currency) ?? new FinancialSummary { Currency = currency }
            });
        }

        if (model.ShowExpenses)
        {
            var upcomingIds = model.Upcoming.Select(w => w.Id).ToHashSet();
            model.UpcomingExpenses = expenses
                .Where(e => upcomingIds.Contains(e.WorkshopId))
                .GroupBy(e => e.WorkshopId)
                .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        }

        return View(model);
    }
}

[AllowAnonymous]
public class HomeController : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
