using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;

namespace SamaTraining.Web.Controllers;

public class DashboardController : AppController
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

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
            Upcoming = await _db.Workshops
                .Include(w => w.Hall)
                .Where(w => w.Status != WorkshopStatus.Completed && w.EndDateTime >= today)
                .OrderBy(w => w.StartDateTime)
                .Take(6)
                .ToListAsync()
        };

        if (model.ShowExpenses)
        {
            // Summed in memory so it works on every database provider.
            var amounts = await _db.WorkshopExpenses.Select(e => e.Amount).ToListAsync();
            model.TotalExpenses = amounts.Sum();
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
