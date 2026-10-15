using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SamaTraining.Web.Data;
using SamaTraining.Web.Security;

namespace SamaTraining.Tests;

/// <summary>End-to-end checks through real HTTP requests, views included.</summary>
public class WebFlowTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _app;

    public WebFlowTests(TestAppFactory app) => _app = app;

    private static FormUrlEncodedContent Form(string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = TestAppFactory.AntiForgery(page);
        return new FormUrlEncodedContent(fields);
    }

    [Fact]
    public async Task AnonymousUser_IsSentToLogin()
    {
        var client = _app.CreateBrowser();
        var res = await client.GetAsync("/Workshops");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Contains("/Account/Login", res.Headers.Location!.ToString());
    }

    [Fact] // US-19
    public async Task WrongPassword_ShowsError()
    {
        var client = _app.CreateBrowser();
        var page = await client.GetStringAsync("/Account/Login");
        var res = await client.PostAsync("/Account/Login", Form(page, new()
        {
            ["UserNameOrEmail"] = "nobody",
            ["Password"] = "Wrong@123"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Invalid username or password", await res.Content.ReadAsStringAsync());
    }

    [Fact] // US-20 + all main pages render
    public async Task Admin_ChangesPassword_ThenSeesEveryPage()
    {
        var client = await _app.SignedInAdminAsync();

        foreach (var url in new[] { "/", "/Workshops", "/Workshops/Create", "/ExpenseCategories", "/ExpenseCategories/Create",
                     "/Halls", "/Halls/Create", "/Users", "/Users/Create", "/Roles", "/Roles/Create", "/Account/ChangePassword" })
        {
            var res = await client.GetAsync(url);
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)res.StatusCode}");
            var html = await res.Content.ReadAsStringAsync();
            Assert.Contains("SAMA Training", html);
        }
    }

    [Fact] // US-03, US-09, US-11, US-12, US-13, US-08, US-14, US-16
    public async Task FullWorkshopLifecycle()
    {
        var client = await _app.SignedInAdminAsync();

        // Create workshop
        var createPage = await client.GetStringAsync("/Workshops/Create");
        var save = await client.PostAsync("/Workshops/Save", Form(createPage, new()
        {
            ["Id"] = "0",
            ["Name"] = "Project Management Essentials",
            ["Date"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00",
            ["EndTime"] = "13:00",
            ["Type"] = "Online",
            ["MeetingLink"] = "https://meet.example.com/pm",
            ["Capacity"] = "30",
            ["Description"] = "Introduction to planning and tracking projects."
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var detailsUrl = save.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last().Split('?')[0]);

        // Open registration
        var details = await client.GetStringAsync(detailsUrl);
        Assert.Contains("Open registration", details);
        await client.PostAsync("/Workshops/OpenRegistration", Form(details, new() { ["id"] = id.ToString() }));
        var token = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.RegistrationToken).SingleAsync());
        Assert.NotNull(token);

        // Public page in Arabic, right-to-left
        var student = _app.CreateBrowser();
        var arPage = await student.GetStringAsync($"/register/{token}?lang=ar");
        Assert.Contains("dir=\"rtl\"", arPage);
        Assert.Contains("التسجيل في الورشة", arPage);

        // Invalid submission shows localized errors
        var bad = await student.PostAsync($"/register/{token}?lang=ar", Form(arPage, new()
        {
            ["FullName"] = "",
            ["Email"] = "not-an-email",
            ["Phone"] = "12"
        }));
        var badHtml = await bad.Content.ReadAsStringAsync();
        Assert.Contains("يرجى إدخال بريد إلكتروني صحيح", badHtml);

        // Valid submission
        var enPage = await student.GetStringAsync($"/register/{token}?lang=en");
        var ok = await student.PostAsync($"/register/{token}?lang=en", Form(enPage, new()
        {
            ["FullName"] = "Amna Hassan",
            ["Email"] = "amna@example.com",
            ["Phone"] = "+249 912 000 111",
            ["Organization"] = "University of Khartoum"
        }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var done = await student.GetStringAsync(ok.Headers.Location!.ToString());
        Assert.Contains("Registration received", done);

        // Same email again is blocked
        enPage = await student.GetStringAsync($"/register/{token}?lang=en");
        var dup = await student.PostAsync($"/register/{token}?lang=en", Form(enPage, new()
        {
            ["FullName"] = "Amna Hassan",
            ["Email"] = "AMNA@example.com",
            ["Phone"] = "+249 912 000 111"
        }));
        Assert.Contains("already registered", await dup.Content.ReadAsStringAsync());

        // Staff sees the student and can export
        details = await client.GetStringAsync(detailsUrl);
        Assert.Contains("Amna Hassan", details);
        var csv = await client.GetAsync($"/Workshops/ExportRegistrations/{id}");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("amna@example.com", await csv.Content.ReadAsStringAsync());

        // Expenses are blocked while Pending
        var blocked = await client.GetAsync($"/Expenses/Create?workshopId={id}");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);

        // Start the workshop, then add an expense
        await client.PostAsync("/Workshops/AdvanceStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Ongoing" }));
        var expensePage = await client.GetStringAsync($"/Expenses/Create?workshopId={id}");
        var categoryId = await _app.WithDbAsync(db => db.ExpenseCategories.Where(c => c.Name == "Catering").Select(c => c.Id).SingleAsync());
        var addExpense = await client.PostAsync("/Expenses/Save", Form(expensePage, new()
        {
            ["Id"] = "0",
            ["WorkshopId"] = id.ToString(),
            ["ExpenseCategoryId"] = categoryId.ToString(),
            ["Amount"] = "125.50",
            ["ExpenseDate"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["Description"] = "Coffee break"
        }));
        Assert.Equal(HttpStatusCode.Redirect, addExpense.StatusCode);

        details = await client.GetStringAsync(detailsUrl + "?tab=expenses");
        Assert.Contains("125.50", details);

        // Negative amount is rejected
        expensePage = await client.GetStringAsync($"/Expenses/Create?workshopId={id}");
        var negative = await client.PostAsync("/Expenses/Save", Form(expensePage, new()
        {
            ["Id"] = "0",
            ["WorkshopId"] = id.ToString(),
            ["ExpenseCategoryId"] = categoryId.ToString(),
            ["Amount"] = "-5",
            ["ExpenseDate"] = DateTime.Today.ToString("yyyy-MM-dd")
        }));
        Assert.Equal(HttpStatusCode.OK, negative.StatusCode);
        Assert.Contains("Amount must be greater than zero", await negative.Content.ReadAsStringAsync());

        // Complete: registration closes, edit is blocked, public link shows closed
        await client.PostAsync("/Workshops/AdvanceStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Completed" }));
        var edit = await client.GetAsync($"/Workshops/Edit/{id}");
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        var closed = await student.GetStringAsync($"/register/{token}");
        Assert.Contains("Registration closed", closed);

        // Category in use cannot be deleted
        var catPage = await client.GetStringAsync("/ExpenseCategories");
        await client.PostAsync($"/ExpenseCategories/Delete/{categoryId}", Form(catPage, new()));
        Assert.True(await _app.WithDbAsync(db => db.ExpenseCategories.AnyAsync(c => c.Id == categoryId)));
    }

    [Fact] // US-04
    public async Task OnSiteWorkshop_RequiresHall_AndEndAfterStart()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0",
            ["Name"] = "Bad workshop",
            ["Date"] = DateTime.Today.ToString("yyyy-MM-dd"),
            ["StartTime"] = "14:00",
            ["EndTime"] = "10:00",
            ["Type"] = "OnSite"
        }));
        var html = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("End time must be after the start time", html);
        Assert.Contains("Choose a hall or location", html);
    }

    [Fact] // US-18: permissions control access
    public async Task FinanceOfficer_CannotManageUsersOrWorkshops()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByNameAsync("finance1") == null)
            {
                var u = new ApplicationUser { UserName = "finance1", Email = "finance1@sama.local", FullName = "Finance One", IsActive = true };
                Assert.True((await users.CreateAsync(u, "Finance@123")).Succeeded);
                await users.AddToRoleAsync(u, Roles.Finance);
            }
        }

        var client = _app.CreateBrowser();
        var page = await client.GetStringAsync("/Account/Login");
        var login = await client.PostAsync("/Account/Login", Form(page, new() { ["UserNameOrEmail"] = "finance1", ["Password"] = "Finance@123" }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var users1 = await client.GetAsync("/Users");
        Assert.Equal(HttpStatusCode.Redirect, users1.StatusCode);
        Assert.Contains("AccessDenied", users1.Headers.Location!.ToString());

        var create = await client.GetAsync("/Workshops/Create");
        Assert.Contains("AccessDenied", create.Headers.Location!.ToString());

        var list = await client.GetAsync("/Workshops");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var html = await list.Content.ReadAsStringAsync();
        Assert.DoesNotContain("User Management", html);
    }

    [Fact] // US-17
    public async Task DeactivatedUser_CannotSignIn()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByNameAsync("former") == null)
            {
                var u = new ApplicationUser { UserName = "former", Email = "former@sama.local", FullName = "Former Staff", IsActive = false };
                Assert.True((await users.CreateAsync(u, "Former@123")).Succeeded);
                await users.AddToRoleAsync(u, Roles.Coordinator);
            }
        }

        var client = _app.CreateBrowser();
        var page = await client.GetStringAsync("/Account/Login");
        var res = await client.PostAsync("/Account/Login", Form(page, new() { ["UserNameOrEmail"] = "former", ["Password"] = "Former@123" }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("deactivated", await res.Content.ReadAsStringAsync());
    }

    [Fact] // US-19: lockout after 5 failed attempts
    public async Task FiveWrongPasswords_LockTheAccount()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByNameAsync("locky") == null)
            {
                var u = new ApplicationUser { UserName = "locky", Email = "locky@sama.local", FullName = "Lock Test", IsActive = true };
                Assert.True((await users.CreateAsync(u, "Locky@123")).Succeeded);
                await users.AddToRoleAsync(u, Roles.Coordinator);
            }
        }

        var client = _app.CreateBrowser();
        for (var i = 0; i < 5; i++)
        {
            var p = await client.GetStringAsync("/Account/Login");
            await client.PostAsync("/Account/Login", Form(p, new() { ["UserNameOrEmail"] = "locky", ["Password"] = "Wrong@123" }));
        }

        var page = await client.GetStringAsync("/Account/Login");
        var res = await client.PostAsync("/Account/Login", Form(page, new() { ["UserNameOrEmail"] = "locky", ["Password"] = "Locky@123" }));
        Assert.Contains("locked", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownRegistrationLink_ShowsNotFound()
    {
        var client = _app.CreateBrowser();
        var html = await client.GetStringAsync("/register/doesnotexist");
        Assert.Contains("Link not found", html);
    }
}
