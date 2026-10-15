using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;

namespace SamaTraining.Web.Controllers;

public class AccountController : AppController
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
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
}
