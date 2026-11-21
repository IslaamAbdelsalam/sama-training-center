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

    /// <summary>
    /// When set and no usable Password is configured, a random temporary password is generated
    /// and written to this file (production: private/first-admin-password.txt).
    /// </summary>
    public string? PasswordFile { get; set; }
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

        // One-time grants of permissions added in later versions to existing default roles.
        const string seedMarker = "seed-version";
        foreach (var (version, grants) in Roles.AddedLater)
        {
            foreach (var (roleName, added) in grants)
            {
                var role = await roleManager.FindByNameAsync(roleName);
                if (role == null) continue;
                var claims = await roleManager.GetClaimsAsync(role);
                if (claims.Any(c => c.Type == seedMarker && c.Value == version)) continue;
                var have = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToHashSet();
                foreach (var p in added.Where(p => !have.Contains(p)))
                {
                    await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(Permissions.ClaimType, p));
                }
                await roleManager.AddClaimAsync(role, new System.Security.Claims.Claim(seedMarker, version));
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
            // No usable password configured (empty or the template placeholder): generate one and
            // write it to a private file on the server, so nobody has to type it into a config file.
            var placeholder = options.Password.StartsWith("TYPE_A_", StringComparison.Ordinal);
            if ((string.IsNullOrWhiteSpace(options.Password) || placeholder) && !string.IsNullOrEmpty(options.PasswordFile))
            {
                options.Password = GeneratePassword();
                try
                {
                    await File.WriteAllTextAsync(options.PasswordFile,
                        $"SAMA Training Center - first administrator{Environment.NewLine}" +
                        $"Username: {options.UserName}{Environment.NewLine}" +
                        $"Temporary password: {options.Password}{Environment.NewLine}" +
                        $"You must change it at first sign-in. Delete this file afterwards.{Environment.NewLine}");
                    logger.LogInformation("Generated the first administrator's temporary password into {File}.", options.PasswordFile);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not write the generated admin password file; no administrator was created.");
                    options.Password = string.Empty;
                }
            }

            if (string.IsNullOrWhiteSpace(options.Password) || options.Password.StartsWith("TYPE_A_", StringComparison.Ordinal))
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
                new ExpenseCategory { Name = "Trainer Fees", NameAr = "أتعاب المدرب" },
                new ExpenseCategory { Name = "Hall Rental", NameAr = "إيجار القاعة" },
                new ExpenseCategory { Name = "Catering", NameAr = "الضيافة" },
                new ExpenseCategory { Name = "Printing & Materials", NameAr = "الطباعة والمواد" },
                new ExpenseCategory { Name = "Transportation", NameAr = "المواصلات" },
                new ExpenseCategory { Name = "Certificates", NameAr = "الشهادات" });
        }

        if (!await db.Halls.AnyAsync())
        {
            db.Halls.AddRange(
                new Hall { Name = "Main Hall", NameAr = "القاعة الرئيسية", Capacity = 60 },
                new Hall { Name = "Training Room 1", NameAr = "قاعة التدريب 1", Capacity = 25 });
        }

        // US-03: give the starter values their Arabic names on databases created before Arabic names existed.
        var starterArabic = new Dictionary<string, string>
        {
            ["Trainer Fees"] = "أتعاب المدرب", ["Hall Rental"] = "إيجار القاعة", ["Catering"] = "الضيافة",
            ["Printing & Materials"] = "الطباعة والمواد", ["Transportation"] = "المواصلات", ["Certificates"] = "الشهادات",
            ["Main Hall"] = "القاعة الرئيسية", ["Training Room 1"] = "قاعة التدريب 1"
        };
        foreach (var c in await db.ExpenseCategories.Where(c => c.NameAr == null || c.NameAr == "").ToListAsync())
        {
            if (starterArabic.TryGetValue(c.Name, out var ar)) c.NameAr = ar;
        }
        foreach (var h in await db.Halls.Where(h => h.NameAr == null || h.NameAr == "").ToListAsync())
        {
            if (starterArabic.TryGetValue(h.Name, out var ar)) h.NameAr = ar;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>A random 16-character password that meets the sign-in password rules.</summary>
    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789", symbols = "!@#$%*?";
        const string all = upper + lower + digits + symbols;
        char Pick(string set) => set[System.Security.Cryptography.RandomNumberGenerator.GetInt32(set.Length)];
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
        while (chars.Count < 16) chars.Add(Pick(all));
        return new string(chars.OrderBy(_ => System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)).ToArray());
    }
}
