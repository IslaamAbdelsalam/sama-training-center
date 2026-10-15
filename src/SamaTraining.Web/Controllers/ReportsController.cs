using System.Text;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

[HasPermission(Permissions.ReportsView)]
public class ReportsController : AppController
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports) => _reports = reports;

    public async Task<IActionResult> Index(ReportFilter filter)
    {
        var model = await _reports.BuildAsync(filter);
        if (!User.Can(Permissions.ExpensesView))
        {
            // Users without expense access still see registrations, not money.
            model.CategoryBreakdowns.Clear();
            foreach (var s in model.Summaries) s.Expenses = 0;
            foreach (var r in model.Rows) r.Expenses = 0;
        }

        ViewBag.ShowMoney = User.Can(Permissions.ExpensesView);
        return View(model);
    }

    public async Task<IActionResult> Export(ReportFilter filter)
    {
        var model = await _reports.BuildAsync(filter);
        var showMoney = User.Can(Permissions.ExpensesView);

        var sb = new StringBuilder();
        sb.Append("Workshop,Date,Country,Type,Status,Registrations,Capacity,Fill %");
        sb.AppendLine(showMoney ? ",Currency,Expenses,Cost per student" : "");
        foreach (var r in model.Rows)
        {
            sb.Append(string.Join(",",
                Csv(r.Name), r.StartDateTime.ToString("yyyy-MM-dd"), Csv(r.CountryName), r.Type, r.Status,
                r.Registrations, r.Capacity?.ToString() ?? "", r.FillPercent?.ToString() ?? ""));
            if (showMoney)
            {
                sb.Append($",{r.Currency},{r.Expenses:0.00},{r.CostPerStudent?.ToString("0.00") ?? ""}");
            }
            sb.AppendLine();
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var f = model.Filter;
        return File(bytes, "text/csv", $"workshops-report-{f.From:yyyyMMdd}-{f.To:yyyyMMdd}.csv");
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if ("=+-@".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
