using System.ComponentModel.DataAnnotations;
using SamaTraining.Web.Data;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Models;

public class ReportFilter
{
    [DataType(DataType.Date)]
    public DateTime? From { get; set; }

    [DataType(DataType.Date)]
    public DateTime? To { get; set; }

    [StringLength(2)]
    public string? Country { get; set; }

    public WorkshopStatus? Status { get; set; }

    /// <summary>Opaque position of the next page of the table (cursor pagination).</summary>
    [StringLength(200)]
    public string? Cursor { get; set; }
}

/// <summary>One currency's totals. Money in different currencies is never added together.</summary>
public class CurrencySummary
{
    public string Currency { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public int Workshops { get; set; }
    public int Completed { get; set; }
    public int Registrations { get; set; }
    public decimal Expenses { get; set; }
    public decimal? CostPerStudent => Registrations > 0 ? Math.Round(Expenses / Registrations, 2) : null;

    /// <summary>US-19 figures for this currency.</summary>
    public decimal ExpectedIncome { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding => ExpectedIncome - Collected;
    public decimal NetProfit => Collected - Expenses;
    public decimal TargetIncome { get; set; }
    public decimal ProjectedProfit => TargetIncome - Expenses;
}

public class WorkshopReportRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public DateTime StartDateTime { get; set; }
    public string Country { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public WorkshopType Type { get; set; }
    public WorkshopStatus Status { get; set; }
    public int Registrations { get; set; }
    public int? Capacity { get; set; }
    public decimal Expenses { get; set; }
    public decimal? Fee { get; set; }
    public decimal ExpectedIncome { get; set; }
    public decimal Collected { get; set; }
    public decimal Outstanding => ExpectedIncome - Collected;
    public decimal NetProfit => Collected - Expenses;
    public decimal TargetIncome { get; set; }
    public decimal ProjectedProfit => TargetIncome - Expenses;
    public DateTime EndDateTime { get; set; }

    public int? FillPercent => Capacity is > 0 ? (int)Math.Round(100.0 * Registrations / Capacity.Value) : null;
    public decimal? CostPerStudent => Registrations > 0 ? Math.Round(Expenses / Registrations, 2) : null;
}

public class CategoryAmount
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Percent { get; set; }
}

public class CurrencyCategoryBreakdown
{
    public string Currency { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<CategoryAmount> Categories { get; set; } = new();
}

public class MonthCount
{
    public DateTime Month { get; set; }
    public int Count { get; set; }
}

public class ReportViewModel
{
    public ReportFilter Filter { get; set; } = new();
    public List<CurrencySummary> Summaries { get; set; } = new();
    public List<WorkshopReportRow> Rows { get; set; } = new();

    /// <summary>The rows shown in the table: one page at a time (cursor); totals still cover the whole period.</summary>
    public List<WorkshopReportRow> PageRows { get; set; } = new();
    public PageLinks Pager { get; set; } = new();
    public List<CurrencyCategoryBreakdown> CategoryBreakdowns { get; set; } = new();
    public List<MonthCount> RegistrationsByMonth { get; set; } = new();
    public int TotalRegistrations => Rows.Sum(r => r.Registrations);
    public int? AverageFill
    {
        get
        {
            var withCapacity = Rows.Where(r => r.FillPercent.HasValue).ToList();
            return withCapacity.Count == 0 ? null : (int)Math.Round(withCapacity.Average(r => r.FillPercent!.Value));
        }
    }
}
