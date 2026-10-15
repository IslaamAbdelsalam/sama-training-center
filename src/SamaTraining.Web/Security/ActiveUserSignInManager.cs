using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SamaTraining.Web.Data;

namespace SamaTraining.Web.Security;

/// <summary>Blocks sign-in for deactivated users (US-17).</summary>
public class ActiveUserSignInManager : SignInManager<ApplicationUser>
{
    public ActiveUserSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!user.IsActive)
        {
            return false;
        }

        return await base.CanSignInAsync(user);
    }
}

/// <summary>
/// Sends users who still have their initial password to the change-password page (US-20).
/// </summary>
public class MustChangePasswordMiddleware
{
    public const string ClaimType = "must_change_password";
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.User.HasClaim(ClaimType, "true"))
        {
            var path = context.Request.Path;
            var allowed = path.StartsWithSegments("/Account/ChangePassword")
                          || path.StartsWithSegments("/Account/Logout")
                          || path.StartsWithSegments("/assets")
                          || path.StartsWithSegments("/lib")
                          || path.StartsWithSegments("/css")
                          || path.StartsWithSegments("/js")
                          || path.StartsWithSegments("/register");

            if (!allowed)
            {
                context.Response.Redirect("/Account/ChangePassword");
                return;
            }
        }

        await _next(context);
    }
}

/// <summary>Adds the must-change-password flag to the user's cookie.</summary>
public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public AppClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<System.Security.Claims.ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new System.Security.Claims.Claim("full_name", string.IsNullOrWhiteSpace(user.FullName) ? user.UserName ?? "" : user.FullName));
        if (user.MustChangePassword)
        {
            identity.AddClaim(new System.Security.Claims.Claim(MustChangePasswordMiddleware.ClaimType, "true"));
        }

        return identity;
    }
}
