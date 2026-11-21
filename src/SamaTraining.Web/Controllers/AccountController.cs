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

    [AllowAnonymous, HttpPost]
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
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
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
            ModelState.AddModelError(string.Empty, "This account is locked after too many failed attempts. Try again in 15 minutes or ask an administrator to unlock it.");
            return View(model);
        }

        if (result.IsNotAllowed)
        {
            // The password was correct but the account is deactivated (see ActiveUserSignInManager).
            ModelState.AddModelError(string.Empty, "This account is deactivated. Contact your system administrator.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid username or password.");
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

    [HttpPost]
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

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewBag.EmailEnabled = _email.IsConfigured;
        if (!ModelState.IsValid) return View(model);

        if (!_email.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, "Password reset by e-mail isn't set up yet. Ask your system administrator to reset your password.");
            return View(model);
        }

        var input = model.UserNameOrEmail.Trim();
        var user = input.Contains('@') ? await _userManager.FindByEmailAsync(input) : await _userManager.FindByNameAsync(input);

        // The same answer whether or not the account exists, so nobody can probe for usernames.
        if (user != null && user.IsActive && !string.IsNullOrWhiteSpace(user.Email))
        {
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
                ModelState.AddModelError(string.Empty, "The reset e-mail couldn't be sent right now. Try again later or ask your system administrator.");
                return View(model);
            }
        }

        return View("ForgotPasswordSent");
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

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> ResetPassword(SetNewPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This reset link is invalid or has expired. Request a new one.");
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
