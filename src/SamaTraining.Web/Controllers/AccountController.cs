using SamaTraining.Web.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Controllers;

public class AccountController : AppController
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;
    private readonly IEmailSender _email;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger, IEmailSender email)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
        _email = email;
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting(AbuseProtection.Auth)]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var input = model.UserNameOrEmail.Trim();
        var user = input.Contains('@')
            ? await _userManager.FindByEmailAsync(input)
            : await _userManager.FindByNameAsync(input);

        if (user == null)
        {
            ModelState.AddModelError(string.Empty, UiText.Get("Invalid username or password."));
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            _logger.LogInformation("User {User} signed in.", user.UserName);
            if (user.MustChangePassword)
            {
                return RedirectToAction(nameof(ChangePassword));
            }

            return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {User} is locked out.", user.UserName);
            ModelState.AddModelError(string.Empty, UiText.Get("This account is locked after too many failed attempts. Try again in 15 minutes or ask an administrator to unlock it."));
            return View(model);
        }

        if (result.IsNotAllowed)
        {
            // The password was correct but the account is deactivated (see ActiveUserSignInManager).
            ModelState.AddModelError(string.Empty, UiText.Get("This account is deactivated. Contact your system administrator."));
            return View(model);
        }

        ModelState.AddModelError(string.Empty, UiText.Get("Invalid username or password."));
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> ChangePassword()
    {
        var user = await _userManager.GetUserAsync(User);
        return View(new ChangePasswordViewModel { IsFirstLogin = user?.MustChangePassword == true });
    }

    [HttpPost, EnableRateLimiting(AbuseProtection.Auth)]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction(nameof(Login));
        }

        model.IsFirstLogin = user.MustChangePassword;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _signInManager.RefreshSignInAsync(user);

        Success("Password changed", "Use your new password next time you sign in.");
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() => View();

    // ---------- US-11: forgot password ----------

    [AllowAnonymous, HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewBag.EmailEnabled = _email.IsConfigured;
        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting(AbuseProtection.Auth)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewBag.EmailEnabled = _email.IsConfigured;
        if (!ModelState.IsValid) return View(model);

        // 1. Does the account exist? Look it up by username first, then by e-mail.
        var input = model.UserNameOrEmail.Trim();
        var user = await _userManager.FindByNameAsync(input) ?? await _userManager.FindByEmailAsync(input);
        if (user == null)
        {
            ModelState.AddModelError(nameof(model.UserNameOrEmail), UiText.Get("No account was found with this username or e-mail. Check the spelling and try again."));
            return View(model);
        }

        // 2. Can this account receive a reset link?
        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, UiText.Get("This account is deactivated. Ask your system administrator to reactivate it."));
            return View(model);
        }

        if (!HasDeliverableEmail(user.Email))
        {
            ModelState.AddModelError(string.Empty, UiText.Get("This account has no working e-mail address, so a reset link can't be sent. Ask your system administrator to add your e-mail under Users, or to reset your password."));
            return View(model);
        }

        if (!_email.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, UiText.Get("Your account was found, but sending e-mails isn't set up on the system yet. Ask your system administrator to reset your password."));
            return View(model);
        }

        // 3. Send the link.
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = Url.Action(nameof(ResetPassword), "Account", new { userId = user.Id, token }, Request.Scheme)!;
        var minutes = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue("App:PasswordResetMinutes", 60);
        var body =
            $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>" +
            "<p>We received a request to reset your SAMA Training Center password. Click the link below to choose a new one:</p>" +
            $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Reset my password</a></p>" +
            $"<p>This link expires in {minutes} minutes and can be used once. If you didn't ask for this, ignore this e-mail; your password stays the same.</p>";
        try
        {
            await _email.SendAsync(user.Email!, "Reset your SAMA Training Center password", body);
            _logger.LogInformation("Password reset link sent to user {User}.", user.UserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the password reset e-mail.");
            ModelState.AddModelError(string.Empty, UiText.Get("The reset e-mail couldn't be sent right now. Try again in a few minutes or ask your system administrator."));
            return View(model);
        }

        ViewBag.SentTo = MaskEmail(user.Email!);
        ViewBag.Minutes = minutes;
        return View("ForgotPasswordSent");
    }

    /// <summary>A real address: not empty and not a placeholder such as admin@sama.local.</summary>
    private static bool HasDeliverableEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@')
        && !email.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        && !email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
        && !email.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>"hiba.osman@gmail.com" → "hi*******@gmail.com".</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return email;
        var name = email[..at];
        var keep = Math.Min(2, name.Length);
        return name[..keep] + new string('*', Math.Max(3, name.Length - keep)) + email[at..];
    }

    [AllowAnonymous, HttpGet]
    public IActionResult ResetPassword(string? userId, string? token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            return RedirectToAction(nameof(ForgotPassword));
        }

        return View(new SetNewPasswordViewModel { UserId = userId, Token = token });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting(AbuseProtection.Auth)]
    public async Task<IActionResult> ResetPassword(SetNewPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, UiText.Get("This reset link is invalid or has expired. Request a new one."));
            return View(model);
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Code == "InvalidToken"
                    ? "This reset link is invalid or has expired. Request a new one."
                    : error.Description);
            }

            return View(model);
        }

        // The user chose this password, so no forced change; clear any lockout from failed attempts.
        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        _logger.LogInformation("User {User} reset their password.", user.UserName);
        return View("ResetPasswordDone");
    }
}
