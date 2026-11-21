using Microsoft.AspNetCore.Mvc;
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

    public CapacityAlertsViewComponent(WorkshopService service) => _service = service;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!(UserClaimsPrincipal.Can(Permissions.RegistrationsCancel) || UserClaimsPrincipal.Can(Permissions.WorkshopsManage)))
        {
            return Content(string.Empty);
        }

        return View(await _service.OverCapacityAsync());
    }
}
