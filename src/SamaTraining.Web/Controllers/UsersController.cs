using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Models;
using SamaTraining.Web.Security;

namespace SamaTraining.Web.Controllers;

/// <summary>US-17 / US-18: system users.</summary>
[HasPermission(Permissions.UsersManage)]
public class UsersController : AppController
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;

    public UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _users.Users.OrderBy(u => u.FullName).ToListAsync();
        var list = new List<UserListItem>();
        foreach (var u in users)
        {
            list.Add(new UserListItem
            {
                Id = u.Id,
                UserName = u.UserName ?? "",
                FullName = u.FullName,
                Email = u.Email,
                IsActive = u.IsActive,
                IsLockedOut = await _users.IsLockedOutAsync(u),
                Roles = (await _users.GetRolesAsync(u)).OrderBy(r => r).ToList()
            });
        }

        return View(list);
    }

    public async Task<IActionResult> Create()
    {
        var model = new UserFormModel();
        await LoadRolesAsync(model);
        return View("Form", model);
    }

    public async Task<IActionResult> Edit(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();

        var model = new UserFormModel
        {
            Id = u.Id,
            FullName = u.FullName,
            UserName = u.UserName ?? "",
            Email = u.Email ?? "",
            PhoneNumber = u.PhoneNumber,
            IsActive = u.IsActive,
            SelectedRoles = (await _users.GetRolesAsync(u)).ToList()
        };
        await LoadRolesAsync(model);
        return View("Form", model);
    }

    [HttpPost]
    public async Task<IActionResult> Save(UserFormModel model)
    {
        var isNew = string.IsNullOrEmpty(model.Id);
        if (isNew && string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "Set an initial password. The user will be asked to change it at first login.");
        }

        var validRoles = await _roles.Roles.Select(r => r.Name!).ToListAsync();
        model.SelectedRoles = model.SelectedRoles.Where(validRoles.Contains).Distinct().ToList();
        if (model.SelectedRoles.Count == 0)
        {
            ModelState.AddModelError(nameof(model.SelectedRoles), "Assign at least one role.");
        }

        var currentUserId = _users.GetUserId(User);
        if (!isNew && model.Id == currentUserId)
        {
            if (!model.IsActive)
                ModelState.AddModelError(nameof(model.IsActive), "You cannot deactivate your own account.");
            if (!model.SelectedRoles.Contains(Roles.Administrator) && (await _users.GetRolesAsync((await _users.FindByIdAsync(model.Id!))!)).Contains(Roles.Administrator))
                ModelState.AddModelError(nameof(model.SelectedRoles), "You cannot remove the Administrator role from your own account.");
        }

        if (!ModelState.IsValid)
        {
            await LoadRolesAsync(model);
            return View("Form", model);
        }

        ApplicationUser? user;
        IdentityResult result;
        if (isNew)
        {
            user = new ApplicationUser
            {
                UserName = model.UserName.Trim(),
                Email = model.Email.Trim(),
                FullName = model.FullName.Trim(),
                PhoneNumber = model.PhoneNumber,
                IsActive = model.IsActive,
                EmailConfirmed = true,
                MustChangePassword = true
            };
            result = await _users.CreateAsync(user, model.Password!);
        }
        else
        {
            user = await _users.FindByIdAsync(model.Id!);
            if (user == null) return NotFound();
            var wasActive = user.IsActive;
            user.UserName = model.UserName.Trim();
            user.Email = model.Email.Trim();
            user.FullName = model.FullName.Trim();
            user.PhoneNumber = model.PhoneNumber;
            user.IsActive = model.IsActive;
            result = await _users.UpdateAsync(user);
            if (result.Succeeded && wasActive && !user.IsActive)
            {
                // Signs the user out everywhere within a minute.
                await _users.UpdateSecurityStampAsync(user);
            }
        }

        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            await LoadRolesAsync(model);
            return View("Form", model);
        }

        var current = await _users.GetRolesAsync(user);
        var toRemove = current.Except(model.SelectedRoles).ToList();
        var toAdd = model.SelectedRoles.Except(current).ToList();
        if (toRemove.Count > 0) await _users.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0) await _users.AddToRolesAsync(user, toAdd);
        if (toRemove.Count > 0 || toAdd.Count > 0) await _users.UpdateSecurityStampAsync(user);

        Success(isNew ? "User added" : "User updated",
            isNew ? $"{user.FullName} can sign in as \"{user.UserName}\" and will set a new password at first login." : $"{user.FullName}'s account is saved.");
        Highlight(user.Id);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ResetPassword(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        return View(new ResetPasswordViewModel { Id = u.Id, UserName = u.UserName });
    }

    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var u = await _users.FindByIdAsync(model.Id);
        if (u == null) return NotFound();
        model.UserName = u.UserName;
        if (!ModelState.IsValid) return View(model);

        var token = await _users.GeneratePasswordResetTokenAsync(u);
        var result = await _users.ResetPasswordAsync(u, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        u.MustChangePassword = true;
        await _users.UpdateAsync(u);
        await _users.SetLockoutEndDateAsync(u, null);
        await _users.ResetAccessFailedCountAsync(u);

        Success("Password reset", $"Share the temporary password with {u.UserName} securely. They must change it at next login.");
        Highlight(u.Id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Unlock(string id)
    {
        var u = await _users.FindByIdAsync(id);
        if (u == null) return NotFound();
        await _users.SetLockoutEndDateAsync(u, null);
        await _users.ResetAccessFailedCountAsync(u);
        Success("Account unlocked", $"{u.UserName} can sign in again.");
        Highlight(u.Id);
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadRolesAsync(UserFormModel model)
    {
        model.AllRoles = await _roles.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync();
    }
}

/// <summary>US-18: roles and the permissions each role grants.</summary>
[HasPermission(Permissions.UsersManage)]
public class RolesController : AppController
{
    private readonly RoleManager<IdentityRole> _roles;
    private readonly UserManager<ApplicationUser> _users;

    public RolesController(RoleManager<IdentityRole> roles, UserManager<ApplicationUser> users)
    {
        _roles = roles;
        _users = users;
    }

    public async Task<IActionResult> Index()
    {
        var roles = await _roles.Roles.OrderBy(r => r.Name).ToListAsync();
        var list = new List<RoleListItem>();
        foreach (var r in roles)
        {
            list.Add(new RoleListItem
            {
                Id = r.Id,
                Name = r.Name ?? "",
                Users = (await _users.GetUsersInRoleAsync(r.Name!)).Count,
                Permissions = (await _roles.GetClaimsAsync(r)).Count(c => c.Type == Permissions.ClaimType)
            });
        }

        return View(list);
    }

    public IActionResult Create() => View("Form", new RoleFormModel());

    public async Task<IActionResult> Edit(string id)
    {
        var r = await _roles.FindByIdAsync(id);
        if (r == null) return NotFound();
        var claims = await _roles.GetClaimsAsync(r);
        return View("Form", new RoleFormModel
        {
            Id = r.Id,
            Name = r.Name ?? "",
            IsSystemAdminRole = r.Name == Roles.Administrator,
            SelectedPermissions = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> Save(RoleFormModel model)
    {
        model.SelectedPermissions = model.SelectedPermissions.Where(Permissions.IsValid).Distinct().ToList();
        IdentityRole? role = null;

        if (!string.IsNullOrEmpty(model.Id))
        {
            role = await _roles.FindByIdAsync(model.Id);
            if (role == null) return NotFound();
            model.IsSystemAdminRole = role.Name == Roles.Administrator;
            if (model.IsSystemAdminRole)
            {
                // The Administrator role always keeps everything, so nobody can lock themselves out.
                model.Name = Roles.Administrator;
                model.SelectedPermissions = Permissions.All.Select(p => p.Key).ToList();
            }
        }

        var existing = await _roles.FindByNameAsync(model.Name.Trim());
        if (existing != null && existing.Id != model.Id)
        {
            ModelState.AddModelError(nameof(model.Name), "A role with this name already exists.");
        }

        if (!ModelState.IsValid) return View("Form", model);

        if (role == null)
        {
            role = new IdentityRole(model.Name.Trim());
            var created = await _roles.CreateAsync(role);
            if (!created.Succeeded)
            {
                foreach (var e in created.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return View("Form", model);
            }
        }
        else if (!model.IsSystemAdminRole && role.Name != model.Name.Trim())
        {
            await _roles.SetRoleNameAsync(role, model.Name.Trim());
            await _roles.UpdateAsync(role);
        }

        var claims = (await _roles.GetClaimsAsync(role)).Where(c => c.Type == Permissions.ClaimType).ToList();
        foreach (var c in claims.Where(c => !model.SelectedPermissions.Contains(c.Value)))
        {
            await _roles.RemoveClaimAsync(role, c);
        }

        foreach (var p in model.SelectedPermissions.Where(p => claims.All(c => c.Value != p)))
        {
            await _roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p));
        }

        // Refresh signed-in users of this role so the change applies within a minute.
        foreach (var u in await _users.GetUsersInRoleAsync(role.Name!))
        {
            await _users.UpdateSecurityStampAsync(u);
        }

        Success(string.IsNullOrEmpty(model.Id) ? "Role added" : "Role updated", $"Permissions for \"{role.Name}\" apply to its users within a minute.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(string id)
    {
        var r = await _roles.FindByIdAsync(id);
        if (r == null) return NotFound();

        if (r.Name == Roles.Administrator)
        {
            Error("Role can't be deleted", "The Administrator role is required by the system.");
            return RedirectToAction(nameof(Index));
        }

        if ((await _users.GetUsersInRoleAsync(r.Name!)).Count > 0)
        {
            Error("Role can't be deleted", $"\"{r.Name}\" still has users. Move them to another role first.");
            return RedirectToAction(nameof(Index));
        }

        await _roles.DeleteAsync(r);
        Success("Role deleted", $"\"{r.Name}\" was removed.");
        return RedirectToAction(nameof(Index));
    }
}
