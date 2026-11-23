using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.ViewComponents;

/// <summary>
/// Navbar bell: warns administrators and coordinators (anyone who can cancel/restore registrations or manage
/// workshops) about workshops that have more active participants than seats, e.g. after a cancellation was undone.
/// </summary>
public class CapacityAlertsViewComponent : ViewComponent
{
    private readonly WorkshopService _service;
    private readonly IMemoryCache _cache;

    public CapacityAlertsViewComponent(WorkshopService service, IMemoryCache cache)
    {
        _service = service;
        _cache = cache;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!(UserClaimsPrincipal.Can(Permissions.RegistrationsCancel) || UserClaimsPrincipal.Can(Permissions.WorkshopsManage)))
        {
            return Content(string.Empty);
        }

        // Shown on every page: one small query at most every 30 seconds for the whole site.
        var alerts = await _cache.GetOrCreateAsync("capacity-alerts", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return _service.OverCapacityAsync();
        });
        return View(alerts ?? new());
    }
}
