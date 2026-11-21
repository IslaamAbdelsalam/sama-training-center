using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SamaTraining.Web.Data;

namespace SamaTraining.Tests;

/// <summary>Runs the real app against an in-memory SQLite database.</summary>
public class TestAppFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Test@Admin123";
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestAppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "unused",
                ["Database:Mode"] = "EnsureCreated",
                ["SeedAdmin:Password"] = AdminPassword,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.Single(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            services.Remove(descriptor);
            services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static string AntiForgery(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        if (!m.Success) throw new InvalidOperationException("Antiforgery token not found in page.");
        return m.Groups[1].Value;
    }

    /// <summary>Signs in as the seeded admin and completes the forced password change.</summary>
    private string _adminPassword = AdminPassword;
    private readonly SemaphoreSlim _adminLock = new(1, 1);

    public async Task<HttpClient> SignedInAdminAsync()
    {
        const string changedPassword = "Admin@Changed1";
        await _adminLock.WaitAsync();
        try
        {
            var client = CreateBrowser();
            var loginPage = await client.GetStringAsync("/Account/Login");
            var res = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserNameOrEmail"] = "admin",
                ["Password"] = _adminPassword,
                ["__RequestVerificationToken"] = AntiForgery(loginPage)
            }));

            if ((int)res.StatusCode != 302) throw new InvalidOperationException("Admin login failed.");

            if (res.Headers.Location?.ToString().Contains("ChangePassword") == true)
            {
                var page = await client.GetStringAsync("/Account/ChangePassword");
                var changed = await client.PostAsync("/Account/ChangePassword", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["CurrentPassword"] = _adminPassword,
                    ["NewPassword"] = changedPassword,
                    ["ConfirmPassword"] = changedPassword,
                    ["__RequestVerificationToken"] = AntiForgery(page)
                }));
                if ((int)changed.StatusCode != 302) throw new InvalidOperationException("Password change failed: " + await changed.Content.ReadAsStringAsync());
                _adminPassword = changedPassword;
            }

            return client;
        }
        finally
        {
            _adminLock.Release();
        }
    }

    public async Task<T> WithDbAsync<T>(Func<ApplicationDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }

    public async Task<T> WithServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Creates (once) and signs in a user with the given role.</summary>
    public async Task<HttpClient> SignedInUserAsync(string userName, string role, string password = "User@Pass123")
    {
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByNameAsync(userName) == null)
            {
                var u = new ApplicationUser { UserName = userName, Email = userName + "@sama.local", FullName = userName, IsActive = true };
                var created = await users.CreateAsync(u, password);
                if (!created.Succeeded) throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
                await users.AddToRoleAsync(u, role);
            }
        }

        var client = CreateBrowser();
        var page = await client.GetStringAsync("/Account/Login");
        var res = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserNameOrEmail"] = userName,
            ["Password"] = password,
            ["__RequestVerificationToken"] = AntiForgery(page)
        }));
        if ((int)res.StatusCode != 302) throw new InvalidOperationException("Login failed for " + userName);
        return client;
    }
}
