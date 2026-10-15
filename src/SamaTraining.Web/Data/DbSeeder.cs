using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Security;

namespace SamaTraining.Web.Data;

public class SeedOptions
{
    public const string Section = "SeedAdmin";

    public string UserName { get; set; } = "admin";
    public string Email { get; set; } = "admin@sama.local";
    public string FullName { get; set; } = "System Administrator";

    /// <summary>Initial password. The admin must change it on first login.</summary>
    public string Password { get; set; } = string.Empty;
}

public static class DbSeeder
{
    /// <summary>
    /// Creates the default roles with their permissions, the first administrator,
    /// and a few starter lookup values. Safe to run on every start-up.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services, SeedOptions options, ILogger logger)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (roleName, permissions) in Roles.Defaults)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var role = new IdentityRole(roleName);
            await roleManager.CreateAsync(role);
            foreach (var permission in permissions)
            {
                await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, permission));
            }
        }

        // The Administrator role always keeps every permission, including ones added in later versions.
        var adminRole = await roleManager.FindByNameAsync(Roles.Administrator);
        if (adminRole != null)
        {
            var existing = (await roleManager.GetClaimsAsync(adminRole))
                .Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToHashSet();
            foreach (var p in Permissions.All.Where(p => !existing.Contains(p.Key)))
            {
                await roleManager.AddClaimAsync(adminRole, new System.Security.Claims.Claim(Permissions.ClaimType, p.Key));
            }
        }

        if (!await userManager.Users.AnyAsync())
        {
            if (string.IsNullOrWhiteSpace(options.Password))
            {
                logger.LogWarning("No users exist and SeedAdmin:Password is empty, so no administrator was created. Set SeedAdmin:Password and restart.");
            }
            else
            {
                var admin = new ApplicationUser
                {
                    UserName = options.UserName,
                    Email = options.Email,
                    FullName = options.FullName,
                    EmailConfirmed = true,
                    IsActive = true,
                    MustChangePassword = true
                };

                var result = await userManager.CreateAsync(admin, options.Password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, Roles.Administrator);
                    logger.LogInformation("Created initial administrator '{UserName}'. The password must be changed at first login.", admin.UserName);
                }
                else
                {
                    logger.LogError("Could not create the initial administrator: {Errors}",
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }
        }

        if (!await db.ExpenseCategories.AnyAsync())
        {
            db.ExpenseCategories.AddRange(
                new ExpenseCategory { Name = "Trainer Fees" },
                new ExpenseCategory { Name = "Hall Rental" },
                new ExpenseCategory { Name = "Catering" },
                new ExpenseCategory { Name = "Printing & Materials" },
                new ExpenseCategory { Name = "Transportation" },
                new ExpenseCategory { Name = "Certificates" });
        }

        if (!await db.Halls.AnyAsync())
        {
            db.Halls.AddRange(
                new Hall { Name = "Main Hall", Capacity = 60 },
                new Hall { Name = "Training Room 1", Capacity = 25 });
        }

        await db.SaveChangesAsync();
    }
}
