using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;

namespace SamaTraining.Web.Services;

/// <summary>Builds the Reports page. Totals are grouped by currency because SDG and EGP can't be added together.</summary>
public class ReportService
{
    private readonly ApplicationDbContext _db;
    private readonly AppSettings _settings;

    public ReportService(ApplicationDbContext db, IOptions<AppSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public static ReportFilter WithDefaults(ReportFilter filter)
    {
        // Default: the current calendar year.
        filter.From ??= new DateTime(DateTime.Today.Year, 1, 1);
        filter.To ??= new DateTime(DateTime.Today.Year, 12, 31);
        if (filter.To < filter.From) (filter.From, filter.To) = (filter.To, filter.From);
        return filter;
    }

    public async Task<ReportViewModel> BuildAsync(ReportFilter filter)
    {
        filter = WithDefaults(filter);
        var from = filter.From!.Value.Date;
        var toExclusive = filter.To!.Value.Date.AddDays(1);

        var query = _db.Workshops.AsNoTracking()
            .Where(w => w.StartDateTime >= from && w.StartDateTime < toExclusive);
        if (!string.IsNullOrEmpty(filter.Country)) query = query.Where(w => w.Country == filter.Country);
        if (filter.Status.HasValue) query = query.Where(w => w.Status == filter.Status);

        var workshops = await query
            .Select(w => new
            {
                w.Id, w.Name, w.StartDateTime, w.Country, w.CurrencyCode, w.Type, w.Status, w.Capacity,
                Registrations = w.Registrations.Count
            })
            .ToListAsync();

        var ids = workshops.Select(w => w.Id).ToList();

        // Loaded and summed in memory: small data, and works on every database provider.
        var expenses = await _db.WorkshopExpenses.AsNoTracking()
            .Where(e => ids.Contains(e.WorkshopId))
            .Select(e => new { e.WorkshopId, e.Amount, Category = e.ExpenseCategory!.Name })
            .ToListAsync();

        var registrationDates = await _db.Registrations.AsNoTracking()
            .Where(r => ids.Contains(r.WorkshopId))
            .Select(r => r.RegisteredAt)
            .ToListAsync();

        var expenseByWorkshop = expenses.GroupBy(e => e.WorkshopId).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));

        var model = new ReportViewModel { Filter = filter };

        model.Rows = workshops
            .OrderByDescending(w => w.StartDateTime)
            .Select(w => new WorkshopReportRow
            {
                Id = w.Id,
                Name = w.Name,
                StartDateTime = w.StartDateTime,
                Country = w.Country,
                CountryName = _settings.CountryName(w.Country),
                Currency = w.CurrencyCode,
                Type = w.Type,
                Status = w.Status,
                Capacity = w.Capacity,
                Registrations = w.Registrations,
                Expenses = expenseByWorkshop.GetValueOrDefault(w.Id)
            })
            .ToList();

        model.Summaries = model.Rows
            .GroupBy(r => r.Currency)
            .OrderBy(g => g.Key)
            .Select(g => new CurrencySummary
            {
                Currency = g.Key,
                CountryName = string.Join(", ", g.Select(r => r.CountryName).Distinct()),
                Workshops = g.Count(),
                Completed = g.Count(r => r.Status == WorkshopStatus.Completed),
                Registrations = g.Sum(r => r.Registrations),
                Expenses = g.Sum(r => r.Expenses)
            })
            .ToList();

        var currencyByWorkshop = workshops.ToDictionary(w => w.Id, w => w.CurrencyCode);
        model.CategoryBreakdowns = expenses
            .GroupBy(e => currencyByWorkshop[e.WorkshopId])
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = g.Sum(e => e.Amount);
                return new CurrencyCategoryBreakdown
                {
                    Currency = g.Key,
                    Total = total,
                    Categories = g.GroupBy(e => e.Category)
                        .Select(c => new CategoryAmount
                        {
                            Category = c.Key,
                            Amount = c.Sum(e => e.Amount),
                            Percent = total > 0 ? (int)Math.Round(100m * c.Sum(e => e.Amount) / total) : 0
                        })
                        .OrderByDescending(c => c.Amount)
                        .ToList()
                };
            })
            .ToList();

        // One bar per month in the range (max 24 months shown).
        var months = new List<MonthCount>();
        var cursor = new DateTime(from.Year, from.Month, 1);
        var last = new DateTime(filter.To.Value.Year, filter.To.Value.Month, 1);
        while (cursor <= last && months.Count < 24)
        {
            var next = cursor.AddMonths(1);
            months.Add(new MonthCount
            {
                Month = cursor,
                Count = registrationDates.Count(d => d.ToLocalTime() >= cursor && d.ToLocalTime() < next)
            });
            cursor = next;
        }
        model.RegistrationsByMonth = months;

        return model;
    }
}
