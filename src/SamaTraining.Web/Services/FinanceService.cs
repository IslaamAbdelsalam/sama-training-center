using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;

namespace SamaTraining.Web.Services;

/// <summary>
/// US-19: one set of money figures used everywhere (dashboard, workshop page, reports), always per currency:
/// <list type="bullet">
/// <item>Expected income = workshop fee × registered participants</item>
/// <item>Collected = sum of recorded fee collections</item>
/// <item>Outstanding = expected − collected</item>
/// <item>Total expenses = sum of recorded expenses</item>
/// <item>Net profit = collected − total expenses</item>
/// <item>Target income = fee × capacity (all seats filled); known from the moment a workshop is created</item>
/// <item>Projected profit = target income − total expenses</item>
/// </list>
/// </summary>
public class FinancialSummary
{
    public string Currency { get; set; } = string.Empty;
    public decimal ExpectedIncome { get; set; }
    public decimal Collected { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal Outstanding => ExpectedIncome - Collected;
    public decimal NetProfit => Collected - TotalExpenses;

    /// <summary>Income if every seat is taken (fee × capacity). Shown even before anyone registers.</summary>
    public decimal TargetIncome { get; set; }
    public decimal ProjectedProfit => TargetIncome - TotalExpenses;

    /// <summary>Seats offered and seats taken.</summary>
    public int Seats { get; set; }
    public int Participants { get; set; }
    public int FillPercent => Seats > 0 ? (int)Math.Min(100, Math.Round(100m * Participants / Seats)) : 0;

    public int CollectedPercent => ExpectedIncome > 0 ? (int)Math.Min(100, Math.Round(100m * Collected / ExpectedIncome)) : 0;

    public void Add(FinancialSummary other)
    {
        ExpectedIncome += other.ExpectedIncome;
        Collected += other.Collected;
        TotalExpenses += other.TotalExpenses;
        TargetIncome += other.TargetIncome;
        Seats += other.Seats;
        Participants += other.Participants;
    }
}

public class FinanceService
{
    private readonly ApplicationDbContext _db;

    public FinanceService(ApplicationDbContext db) => _db = db;

    /// <summary>Money figures for each workshop (in that workshop's own currency). Null ids = all workshops.</summary>
    public async Task<Dictionary<int, FinancialSummary>> ForWorkshopsAsync(ICollection<int>? workshopIds = null)
    {
        var workshops = _db.Workshops.AsNoTracking();
        var expenses = _db.WorkshopExpenses.AsNoTracking();
        var collections = _db.FeeCollections.AsNoTracking();
        if (workshopIds != null)
        {
            var ids = workshopIds.ToList();
            workshops = workshops.Where(w => ids.Contains(w.Id));
            expenses = expenses.Where(e => ids.Contains(e.WorkshopId));
            collections = collections.Where(c => ids.Contains(c.WorkshopId));
        }

        // Small data: loaded and summed in memory, which works the same on every database provider.
        var ws = await workshops.Select(w => new { w.Id, w.CurrencyCode, w.Fee, w.Capacity, Students = w.Registrations.Count }).ToListAsync();
        var ex = (await expenses.Select(e => new { e.WorkshopId, e.Amount }).ToListAsync())
            .GroupBy(e => e.WorkshopId).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        var co = (await collections.Select(c => new { c.WorkshopId, c.Amount }).ToListAsync())
            .GroupBy(c => c.WorkshopId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        return ws.ToDictionary(w => w.Id, w => new FinancialSummary
        {
            Currency = w.CurrencyCode,
            ExpectedIncome = (w.Fee ?? 0) * w.Students,
            // Older workshops saved without a capacity count their registrations as their seats.
            Seats = w.Capacity ?? w.Students,
            Participants = w.Students,
            TargetIncome = (w.Fee ?? 0) * (w.Capacity ?? w.Students),
            Collected = co.GetValueOrDefault(w.Id),
            TotalExpenses = ex.GetValueOrDefault(w.Id)
        });
    }

    /// <summary>Totals per currency. Different currencies are never added together.</summary>
    public static List<FinancialSummary> ByCurrency(IEnumerable<FinancialSummary> perWorkshop) =>
        perWorkshop.GroupBy(f => f.Currency)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = new FinancialSummary { Currency = g.Key };
                foreach (var f in g) total.Add(f);
                return total;
            })
            .ToList();

    public static PaymentStatus StatusOf(decimal? fee, decimal paid)
    {
        if (fee is null or <= 0) return PaymentStatus.Free;
        if (paid >= fee) return PaymentStatus.Paid;
        return paid > 0 ? PaymentStatus.PartiallyPaid : PaymentStatus.Unpaid;
    }
}
