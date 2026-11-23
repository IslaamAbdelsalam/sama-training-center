using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SamaTraining.Web.Data;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

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
                     "/Halls", "/Halls/Create", "/Users", "/Users/Create", "/Roles", "/Roles/Create", "/Account/ChangePassword", "/Reports" })
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
            ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = "Project Management Essentials",
            ["NameAr"] = "ورشة Project Management Essentials",
            ["Date"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00",
            ["EndTime"] = "13:00",
            ["Type"] = "Online",
            ["Country"] = "SD",
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
        Assert.Contains("registered!", done);

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

        // US-08: expenses can be added before the workshop starts (Pending)
        var beforeStart = await client.GetAsync($"/Expenses/Create?workshopId={id}");
        Assert.Equal(HttpStatusCode.OK, beforeStart.StatusCode);

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
        Assert.Contains("SDG", details);
        Assert.Contains("Expense added", details); // toast after redirect

        // Country (and so currency) is locked once expenses exist
        var editPage = await client.GetStringAsync($"/Workshops/Edit/{id}");
        var changeCountry = await client.PostAsync("/Workshops/Save", Form(editPage, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = id.ToString(),
            ["Name"] = "Project Management Essentials",
            ["NameAr"] = "ورشة Project Management Essentials",
            ["Date"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00",
            ["EndTime"] = "13:00",
            ["Type"] = "Online",
            ["Country"] = "EG"
        }));
        Assert.Equal(HttpStatusCode.OK, changeCountry.StatusCode);
        Assert.Contains("can&#x27;t change", await changeCountry.Content.ReadAsStringAsync());
        Assert.Equal("SDG", await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.CurrencyCode).SingleAsync()));

        // Reports include the workshop and its SDG expenses
        var report = await client.GetStringAsync($"/Reports?From={DateTime.Today.AddDays(-1):yyyy-MM-dd}&To={DateTime.Today.AddDays(30):yyyy-MM-dd}");
        Assert.Contains("Project Management Essentials", report);
        Assert.Contains("125.50 SDG", report);
        var reportCsv = await client.GetAsync($"/Reports/Export?From={DateTime.Today.AddDays(-1):yyyy-MM-dd}&To={DateTime.Today.AddDays(30):yyyy-MM-dd}");
        Assert.Equal("text/csv", reportCsv.Content.Headers.ContentType!.MediaType);
        Assert.Contains("125.50", await reportCsv.Content.ReadAsStringAsync());

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
        Assert.Contains("Registration is closed", closed);

        // US-09: no new expenses after completion; existing ones stay visible
        var afterComplete = await client.GetAsync($"/Expenses/Create?workshopId={id}");
        Assert.Equal(HttpStatusCode.Redirect, afterComplete.StatusCode);
        Assert.Contains("125.50", await client.GetStringAsync(detailsUrl + "?tab=expenses"));

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
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = "Bad workshop",
            ["NameAr"] = "ورشة Bad workshop",
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

    [Fact] // Currency follows the workshop's country: Egypt → EGP, Sudan → SDG
    public async Task WorkshopCurrency_FollowsCountry()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = "Cairo Volunteers Training",
            ["NameAr"] = "ورشة Cairo Volunteers Training",
            ["Date"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            ["StartTime"] = "10:00",
            ["EndTime"] = "12:00",
            ["Type"] = "Online",
            ["Country"] = "EG"
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var id = int.Parse(res.Headers.Location!.ToString().Split('/').Last().Split('?')[0]);
        var currency = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.CurrencyCode).SingleAsync());
        Assert.Equal("EGP", currency);

        var details = await client.GetStringAsync(res.Headers.Location!.ToString());
        Assert.Contains("Workshop created", details); // success toast
        Assert.Contains("Egypt · EGP", details);
    }

    [Fact]
    public async Task UnknownCountry_IsRejected()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = "Bad country",
            ["NameAr"] = "ورشة Bad country",
            ["Date"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            ["StartTime"] = "10:00",
            ["EndTime"] = "12:00",
            ["Type"] = "Online",
            ["Country"] = "XX"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Choose a country from the list", html);
        Assert.Contains("Not saved yet", html); // validation toast
    }

    private async Task<(HttpClient Client, string Token, string DetailsUrl)> OpenWorkshopAsync(string name, string nameAr)
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = name,
            ["NameAr"] = nameAr,
            ["Date"] = DateTime.Today.AddDays(8).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00",
            ["EndTime"] = "12:00",
            ["Type"] = "Online",
            ["Country"] = "SD"
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var detailsUrl = res.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last().Split('?')[0]);
        var details = await client.GetStringAsync(detailsUrl);
        await client.PostAsync("/Workshops/OpenRegistration", Form(details, new() { ["id"] = id.ToString() }));
        var token = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.RegistrationToken!).SingleAsync());
        return (client, token, detailsUrl);
    }

    [Fact] // Arabic page shows the Arabic name, English page the English name
    public async Task RegistrationPage_ShowsNameInPageLanguage()
    {
        var (_, token, _) = await OpenWorkshopAsync("Leadership Skills", "مهارات القيادة");
        var student = _app.CreateBrowser();

        var ar = await student.GetStringAsync($"/register/{token}?lang=ar");
        Assert.Contains("مهارات القيادة", ar);
        Assert.DoesNotContain("Leadership Skills", ar);

        var en = await student.GetStringAsync($"/register/{token}?lang=en");
        Assert.Contains("Leadership Skills", en);
        Assert.DoesNotContain("مهارات القيادة", en);
    }

    [Fact]
    public async Task ArabicName_IsRequired()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0",
            ["Name"] = "No Arabic name",
            ["Date"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"),
            ["StartTime"] = "10:00",
            ["EndTime"] = "12:00",
            ["Type"] = "Online",
            ["Country"] = "SD"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Enter the Arabic name", await res.Content.ReadAsStringAsync());
    }

    [Fact] // Phone: fixed 00, country code + exact length, one registration per phone per workshop
    public async Task Phone_IsValidated_AndCannotRegisterTwice()
    {
        var (client, token, detailsUrl) = await OpenWorkshopAsync("Phone Rules Workshop", "ورشة قواعد الهاتف");
        var student = _app.CreateBrowser();

        async Task<string> Submit(string email, string phone, string lang = "en")
        {
            var page = await student.GetStringAsync($"/register/{token}?lang={lang}");
            var res = await student.PostAsync($"/register/{token}?lang={lang}", Form(page, new()
            {
                ["FullName"] = "Test Student",
                ["Email"] = email,
                ["Phone"] = phone
            }));
            if (res.StatusCode == HttpStatusCode.Redirect) return "REDIRECT";
            return await res.Content.ReadAsStringAsync();
        }

        var page0 = await student.GetStringAsync($"/register/{token}?lang=en");
        Assert.Contains(">00</span>", page0); // fixed prefix

        Assert.Contains("Sudan numbers are 249 followed by 9 digits", await Submit("p1@x.com", "24991234567"));    // too short
        Assert.Contains("Remove the 0 after the country code", await Submit("p1@x.com", "2490912345678"));      // trunk 0
        Assert.Contains("Start with your country code", await Submit("p1@x.com", "0912345678"));                // no code
        Assert.Contains("Egypt numbers are 20 followed by 10 digits", await Submit("p1@x.com", "20100123456")); // too short
        Assert.Contains("رقم السودان يتكون من 249 ثم 9 أرقام", await Submit("p1@x.com", "2499123456", "ar"));

        // Valid, even when typed with 00, + or spaces
        Assert.Equal("REDIRECT", await Submit("p1@x.com", "00249 91 234 5678"));
        Assert.Equal("249912345678", await _app.WithDbAsync(db => db.Registrations.Where(r => r.Email == "p1@x.com").Select(r => r.Phone).SingleAsync()));

        // Same phone, different email -> blocked
        Assert.Contains("phone number is already registered", await Submit("p2@x.com", "+249912345678"));

        // Staff list shows it formatted with 00
        var details = await client.GetStringAsync(detailsUrl + "?tab=registrations");
        Assert.Contains("00249 912345678", details);
    }

    [Fact] // Dashboard groups money by SDG and EGP separately; no conversion, no mixed total
    public async Task Dashboard_GroupsAmountsByCurrency()
    {
        var client = await _app.SignedInAdminAsync();
        var html = await client.GetStringAsync("/");
        Assert.Contains("By currency", html);
        Assert.Contains(">SDG</span>", html);
        Assert.Contains(">EGP</span>", html);
        Assert.Contains("Sudan", html);
        Assert.Contains("Egypt", html);
        Assert.DoesNotContain("USD", html);
    }

    // ---------- Backlog v2 (US-01 .. US-19) ----------

    [Fact] // US-01 / US-02 / US-03: Arabic name required; delete from the edit screen only when not in use
    public async Task Halls_RequireArabicName_AndDeleteFromEditScreen()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Halls/Create");
        var noArabic = await client.PostAsync("/Halls/Save", Form(page, new() { ["Id"] = "0", ["Name"] = "Hall Without Arabic", ["IsActive"] = "true" }));
        Assert.Equal(HttpStatusCode.OK, noArabic.StatusCode);
        Assert.Contains("Enter the Arabic name", await noArabic.Content.ReadAsStringAsync());

        var saved = await client.PostAsync("/Halls/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "Conference Room B", ["NameAr"] = "قاعة المؤتمرات ب",
            ["Location"] = "Khartoum, Street 15", ["LocationAr"] = "الخرطوم، شارع 15", ["IsActive"] = "true"
        }));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var hallId = await _app.WithDbAsync(db => db.Halls.Where(h => h.Name == "Conference Room B").Select(h => h.Id).SingleAsync());

        // Full-width edit screen with a Delete action
        var edit = await client.GetStringAsync($"/Halls/Edit/{hallId}");
        Assert.Contains("قاعة المؤتمرات ب", edit);
        Assert.Contains("Delete this hall?", edit);
        Assert.DoesNotContain("max-width: 640px", edit);

        // A hall used by a workshop can't be deleted
        var usedHallId = await _app.WithDbAsync(db => db.Halls.Where(h => h.Name == "Main Hall").Select(h => h.Id).SingleAsync());
        var wPage = await client.GetStringAsync("/Workshops/Create");
        await client.PostAsync("/Workshops/Save", Form(wPage, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0", ["Name"] = "Uses Main Hall", ["NameAr"] = "تستخدم القاعة الرئيسية",
            ["Date"] = DateTime.Today.AddDays(4).ToString("yyyy-MM-dd"), ["StartTime"] = "09:00", ["EndTime"] = "11:00",
            ["Type"] = "OnSite", ["HallId"] = usedHallId.ToString(), ["Country"] = "SD"
        }));
        var usedEdit = await client.GetStringAsync($"/Halls/Edit/{usedHallId}");
        Assert.Contains("so it can&#x27;t be deleted", usedEdit);
        var blocked = await client.PostAsync($"/Halls/Delete/{usedHallId}?fromEdit=true", Form(usedEdit, new()));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Contains($"/Halls/Edit/{usedHallId}", blocked.Headers.Location!.ToString());
        Assert.True(await _app.WithDbAsync(db => db.Halls.AnyAsync(h => h.Id == usedHallId)));

        // An unused one can
        var del = await client.PostAsync($"/Halls/Delete/{hallId}?fromEdit=true", Form(edit, new()));
        Assert.Equal(HttpStatusCode.Redirect, del.StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.Halls.AnyAsync(h => h.Id == hallId)));
    }

    [Fact] // US-03: expense categories need an Arabic name too
    public async Task ExpenseCategory_RequiresArabicName()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/ExpenseCategories/Create");
        var res = await client.PostAsync("/ExpenseCategories/Save", Form(page, new() { ["Id"] = "0", ["Name"] = "Internet", ["IsActive"] = "true" }));
        Assert.Contains("Enter the Arabic name", await res.Content.ReadAsStringAsync());
        res = await client.PostAsync("/ExpenseCategories/Save", Form(page, new() { ["Id"] = "0", ["Name"] = "Internet", ["NameAr"] = "الإنترنت", ["IsActive"] = "true" }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("الإنترنت", await _app.WithDbAsync(db => db.ExpenseCategories.Where(c => c.Name == "Internet").Select(c => c.NameAr).SingleAsync()));
    }

    [Fact] // US-05: end date can't be before the start date
    public async Task DateRange_EndDateCannotBeBeforeStart()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Capacity"] = "50", ["Fee"] = "0",
            ["Id"] = "0", ["Name"] = "Backwards", ["NameAr"] = "عكسي", ["IsMultiDay"] = "true",
            ["Date"] = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"), ["EndDate"] = DateTime.Today.AddDays(8).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00", ["EndTime"] = "12:00", ["Type"] = "Online", ["Country"] = "SD"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("can&#x27;t be earlier than the start date", await res.Content.ReadAsStringAsync());
    }

    [Fact] // US-04, US-05, US-06, US-12, US-13, US-14, US-16, US-18, US-19 end to end
    public async Task PaidMultiDayWorkshop_RegistrationPaymentsAndFinance()
    {
        var client = await _app.SignedInAdminAsync();
        var hallId = await _app.WithDbAsync(async db =>
        {
            var h = new Hall { Name = "Omdurman Centre", NameAr = "مركز أم درمان", Location = "Omdurman", LocationAr = "أم درمان، الحي الأول", IsActive = true };
            db.Halls.Add(h);
            await db.SaveChangesAsync();
            return h.Id;
        });

        var start = DateTime.Today.AddDays(20);
        var page = await client.GetStringAsync("/Workshops/Create");
        Assert.Contains("data-rich-editor", page);
        var save = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "Data Analysis Bootcamp", ["NameAr"] = "معسكر تحليل البيانات",
            ["IsMultiDay"] = "true", ["Date"] = start.ToString("yyyy-MM-dd"), ["EndDate"] = start.AddDays(2).ToString("yyyy-MM-dd"),
            ["StartTime"] = "09:00", ["EndTime"] = "14:00", ["Type"] = "OnSite", ["HallId"] = hallId.ToString(), ["Country"] = "SD",
            ["Capacity"] = "2", ["Fee"] = "1500",
            ["Content"] = "<h2>Day one</h2><p><strong>Excel</strong> basics</p><ul><li>Pivot tables</li></ul><script>alert('x')</script>",
            ["ContentAr"] = "<h2>اليوم الأول</h2><p><strong>إكسل</strong></p>"
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var detailsUrl = save.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last().Split('?')[0]);

        var stored = await _app.WithDbAsync(db => db.Workshops.SingleAsync(w => w.Id == id));
        Assert.Equal(start.AddDays(2).Date, stored.EndDateTime.Date);
        Assert.Equal(1500m, stored.Fee);
        Assert.DoesNotContain("<script", stored.Content);
        Assert.Contains("<strong>Excel</strong>", stored.Content);

        var details = await client.GetStringAsync(detailsUrl);
        await client.PostAsync("/Workshops/OpenRegistration", Form(details, new() { ["id"] = id.ToString() }));
        var token = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.RegistrationToken!).SingleAsync());

        // Registration page: Arabic details, fee and currency, formatted agenda, no seat count
        var student = _app.CreateBrowser();
        var ar = await student.GetStringAsync($"/register/{token}?lang=ar");
        Assert.Contains("مركز أم درمان", ar);
        Assert.Contains("أم درمان، الحي الأول", ar);
        Assert.Contains("1,500.00 جنيه سوداني", ar);
        Assert.Contains("حضوري", ar);
        Assert.Contains("<strong>إكسل</strong>", ar);
        Assert.Contains("3 أيام", ar);
        Assert.DoesNotContain("المقاعد المتبقية", ar);
        var en = await student.GetStringAsync($"/register/{token}?lang=en");
        Assert.Contains("1,500.00 SDG", en);
        Assert.Contains("<strong>Excel</strong>", en);
        Assert.Contains("(3 days)", en);
        Assert.DoesNotContain("alert(", en);
        Assert.DoesNotContain("Seats left", en);

        async Task<HttpResponseMessage> Register(string name, string email, string phone, string lang = "en")
        {
            var p = await student.GetStringAsync($"/register/{token}?lang={lang}");
            return await student.PostAsync($"/register/{token}?lang={lang}", Form(p, new() { ["FullName"] = name, ["Email"] = email, ["Phone"] = phone }));
        }

        Assert.Equal(HttpStatusCode.Redirect, (await Register("Hiba Osman", "hiba@example.com", "249911111111")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Register("Yousif Ali", "yousif@example.com", "249922222222")).StatusCode);

        // US-16: full — the page apologises in the applicant's language
        var full = await student.GetStringAsync($"/register/{token}?lang=ar");
        Assert.Contains("نعتذر", full);
        Assert.Equal(2, await _app.WithDbAsync(db => db.Registrations.CountAsync(r => r.WorkshopId == id)));

        // US-18: record payments (one full, one partial)
        var regs = await _app.WithDbAsync(db => db.Registrations.Where(r => r.WorkshopId == id).OrderBy(r => r.FullName).Select(r => r.Id).ToListAsync());
        var payPage = await client.GetStringAsync($"/Collections/Create?workshopId={id}&registrationId={regs[0]}");
        Assert.Contains("Record Payment", payPage);
        var pay1 = await client.PostAsync("/Collections/Save", Form(payPage, new()
        {
            ["Id"] = "0", ["WorkshopId"] = id.ToString(), ["RegistrationId"] = regs[0].ToString(), ["Amount"] = "1500",
            ["CollectedOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Method"] = "BankTransfer", ["Reference"] = "TRX-1"
        }));
        Assert.Equal(HttpStatusCode.Redirect, pay1.StatusCode);
        await client.PostAsync("/Collections/Save", Form(payPage, new()
        {
            ["Id"] = "0", ["WorkshopId"] = id.ToString(), ["RegistrationId"] = regs[1].ToString(), ["Amount"] = "500",
            ["CollectedOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Method"] = "Cash"
        }));

        // An expense before the workshop starts (US-08)
        var categoryId = await _app.WithDbAsync(db => db.ExpenseCategories.Where(c => c.Name == "Hall Rental").Select(c => c.Id).SingleAsync());
        var expensePage = await client.GetStringAsync($"/Expenses/Create?workshopId={id}");
        await client.PostAsync("/Expenses/Save", Form(expensePage, new()
        {
            ["Id"] = "0", ["WorkshopId"] = id.ToString(), ["ExpenseCategoryId"] = categoryId.ToString(), ["Amount"] = "1200",
            ["ExpenseDate"] = DateTime.Today.ToString("yyyy-MM-dd")
        }));

        // US-19 on the workshop page: expected 3,000 · collected 2,000 · outstanding 1,000 · expenses 1,200 · profit 800
        details = await client.GetStringAsync(detailsUrl + "?tab=payments");
        Assert.Contains("3,000.00", details);
        Assert.Contains("2,000.00", details);
        Assert.Contains("1,000.00", details);
        Assert.Contains("800.00 SDG", details);
        Assert.Contains("Partially paid", await client.GetStringAsync(detailsUrl + "?tab=registrations"));

        // ... in reports and on the dashboard
        var report = await client.GetStringAsync($"/Reports?From={DateTime.Today.AddDays(-1):yyyy-MM-dd}&To={start.AddDays(5):yyyy-MM-dd}");
        Assert.Contains("Net profit", report);
        Assert.Contains("800.00 SDG", report);
        var csv = await (await client.GetAsync($"/Reports/Export?From={DateTime.Today.AddDays(-1):yyyy-MM-dd}&To={start.AddDays(5):yyyy-MM-dd}")).Content.ReadAsStringAsync();
        Assert.Contains("Expected income,Collected,Outstanding", csv);
        Assert.Contains("3000.00,2000.00,1000.00", csv);
        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("Net profit", dashboard);
        Assert.Contains("Target if all", dashboard);

        // US-10 reverse; US-09 no new payments once completed
        details = await client.GetStringAsync(detailsUrl);
        await client.PostAsync("/Workshops/AdvanceStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Ongoing" }));
        var reversed = await client.PostAsync("/Workshops/ReverseStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Pending" }));
        Assert.Equal(HttpStatusCode.Redirect, reversed.StatusCode);
        Assert.Equal(WorkshopStatus.Pending, await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.Status).SingleAsync()));
        var history = await client.GetStringAsync(detailsUrl + "?tab=history");
        Assert.Contains("Status reversed", history);
        Assert.Contains("Registration opened", history);

        await client.PostAsync("/Workshops/AdvanceStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Ongoing" }));
        await client.PostAsync("/Workshops/AdvanceStatus", Form(details, new() { ["id"] = id.ToString(), ["target"] = "Completed" }));
        var lateCollection = await client.GetAsync($"/Collections/Create?workshopId={id}");
        Assert.Equal(HttpStatusCode.Redirect, lateCollection.StatusCode);
        history = await client.GetStringAsync(detailsUrl + "?tab=history");
        Assert.Contains("Registration closed", history);
        Assert.Contains("2,000.00", await client.GetStringAsync(detailsUrl + "?tab=payments")); // still visible
    }

    [Fact] // US-18: finance officers can record payments; coordinators can't
    public async Task Collections_FollowPermissions()
    {
        var coordinator = await _app.SignedInUserAsync("coord.pay", Roles.Coordinator);
        var res = await coordinator.GetAsync("/Collections/Create?workshopId=1");
        Assert.True(res.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
        if (res.StatusCode == HttpStatusCode.Redirect) Assert.Contains("AccessDenied", res.Headers.Location!.ToString());
    }

    [Fact] // US-11: forgot password page is reachable from login and never reveals accounts
    public async Task ForgotPassword_IsAvailableFromLogin()
    {
        var browser = _app.CreateBrowser();
        var login = await browser.GetStringAsync("/Account/Login");
        Assert.Contains("/Account/ForgotPassword", login);

        var page = await browser.GetStringAsync("/Account/ForgotPassword");
        Assert.Contains("Send reset link", page);

        // Unknown username or e-mail: a clear message
        var res = await browser.PostAsync("/Account/ForgotPassword", Form(page, new() { ["UserNameOrEmail"] = "nobody.here" }));
        Assert.Contains("No account was found with this username or e-mail", await res.Content.ReadAsStringAsync());
        res = await browser.PostAsync("/Account/ForgotPassword", Form(page, new() { ["UserNameOrEmail"] = "ghost@gmail.com" }));
        Assert.Contains("No account was found with this username or e-mail", await res.Content.ReadAsStringAsync());

        // Found, but a placeholder address (admin@sama.local) can't receive mail
        res = await browser.PostAsync("/Account/ForgotPassword", Form(page, new() { ["UserNameOrEmail"] = "admin" }));
        Assert.Contains("no working e-mail address", await res.Content.ReadAsStringAsync());

        // Found by e-mail with a real address; no SMTP in tests → says so clearly
        await _app.WithServicesAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByNameAsync("mail.user") == null)
                await users.CreateAsync(new ApplicationUser { UserName = "mail.user", Email = "mail.user@gmail.com", FullName = "Mail User", IsActive = true }, "Mail@Pass123");
            return true;
        });
        res = await browser.PostAsync("/Account/ForgotPassword", Form(page, new() { ["UserNameOrEmail"] = "mail.user@gmail.com" }));
        Assert.Contains("Your account was found, but sending e-mails isn&#x27;t set up", await res.Content.ReadAsStringAsync());

        // A tampered reset link doesn't work
        var reset = await browser.GetStringAsync("/Account/ResetPassword?userId=nope&token=bad");
        var bad = await browser.PostAsync("/Account/ResetPassword", Form(reset, new()
        {
            ["UserId"] = "nope", ["Token"] = "bad", ["NewPassword"] = "New@Pass123", ["ConfirmPassword"] = "New@Pass123"
        }));
        Assert.Contains("invalid or has expired", await bad.Content.ReadAsStringAsync());
    }

    [Fact] // US-11: a valid reset token sets the new password and clears a lockout
    public async Task ResetToken_SetsNewPassword()
    {
        var browser = _app.CreateBrowser();
        var (userId, token) = await _app.WithServicesAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var u = new ApplicationUser { UserName = "reset.me", Email = "reset.me@sama.local", FullName = "Reset Me", IsActive = true };
            await users.CreateAsync(u, "Old@Pass123");
            await users.SetLockoutEndDateAsync(u, DateTimeOffset.UtcNow.AddMinutes(10));
            return (u.Id, await users.GeneratePasswordResetTokenAsync(u));
        });

        var page = await browser.GetStringAsync($"/Account/ResetPassword?userId={userId}&token={Uri.EscapeDataString(token)}");
        var res = await browser.PostAsync("/Account/ResetPassword", Form(page, new()
        {
            ["UserId"] = userId, ["Token"] = token, ["NewPassword"] = "Brand@New123", ["ConfirmPassword"] = "Brand@New123"
        }));
        Assert.Contains("Your password has been reset", await res.Content.ReadAsStringAsync());

        var loginPage = await browser.GetStringAsync("/Account/Login");
        var login = await browser.PostAsync("/Account/Login", Form(loginPage, new() { ["UserNameOrEmail"] = "reset.me", ["Password"] = "Brand@New123" }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.DoesNotContain("ChangePassword", login.Headers.Location!.ToString());
    }

    [Fact] // Capacity and fee are required when adding a workshop
    public async Task Workshop_RequiresCapacityAndFee()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "No seats", ["NameAr"] = "بلا مقاعد",
            ["Date"] = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"), ["StartTime"] = "09:00", ["EndTime"] = "11:00",
            ["Type"] = "Online", ["Country"] = "SD"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Enter the number of seats (capacity).", html);
        Assert.Contains("Enter the registration fee (0 for a free workshop).", html);
        Assert.False(await _app.WithDbAsync(db => db.Workshops.AnyAsync(w => w.Name == "No seats")));
    }

    [Fact] // Financial figures show on the dashboard and in reports before anyone registers or any expense is added
    public async Task Finance_ShowsTargetBeforeRegistrationsOrExpenses()
    {
        var client = await _app.SignedInAdminAsync();
        var start = DateTime.Today.AddDays(40);
        var page = await client.GetStringAsync("/Workshops/Create");
        var save = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "Cairo Finance Basics", ["NameAr"] = "أساسيات المالية بالقاهرة",
            ["Date"] = start.ToString("yyyy-MM-dd"), ["StartTime"] = "10:00", ["EndTime"] = "13:00",
            ["Type"] = "Online", ["Country"] = "EG", ["Capacity"] = "40", ["Fee"] = "2500"
        }));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var detailsUrl = save.Headers.Location!.ToString();

        // Workshop page: 40 seats × 2,500 = 100,000 target, projected profit 100,000 with no expenses yet
        var details = await client.GetStringAsync(detailsUrl);
        Assert.Contains("100,000.00", details);
        Assert.Contains("Projected profit if full", details);

        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("Target if all", dashboard);
        Assert.Contains("Projected profit", dashboard);

        var csv = await (await client.GetAsync($"/Reports/Export?From={start.AddDays(-1):yyyy-MM-dd}&To={start.AddDays(1):yyyy-MM-dd}")).Content.ReadAsStringAsync();
        Assert.Contains("Target income (capacity x fee)", csv);
        Assert.Contains("2500.00,100000.00,0.00,0.00,0.00", csv);
        Assert.Contains(",0.00,100000.00", csv); // net profit 0, projected profit 100,000
        var report = await client.GetStringAsync($"/Reports?From={start.AddDays(-1):yyyy-MM-dd}&To={start.AddDays(1):yyyy-MM-dd}");
        Assert.Contains("100,000.00 EGP", report);
        Assert.Contains("Projected profit", report);
    }

    [Fact] // Cancel a registration: seat freed, not counted, shown as cancelled, can't re-register; undo can go over capacity
    public async Task CancelRegistration_FreesSeat_BlocksReRegistration_AndUndoAlertsOverCapacity()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var save = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "One Seat Clinic", ["NameAr"] = "عيادة المقعد الواحد",
            ["Date"] = DateTime.Today.AddDays(12).ToString("yyyy-MM-dd"), ["StartTime"] = "09:00", ["EndTime"] = "10:00",
            ["Type"] = "Online", ["Country"] = "SD", ["Capacity"] = "1", ["Fee"] = "300"
        }));
        var detailsUrl = save.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last().Split('?')[0]);
        var details = await client.GetStringAsync(detailsUrl);
        await client.PostAsync("/Workshops/OpenRegistration", Form(details, new() { ["id"] = id.ToString() }));
        var token = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.RegistrationToken!).SingleAsync());

        var student = _app.CreateBrowser();
        async Task<string> Register(string name, string email, string phone)
        {
            var p = await student.GetStringAsync($"/register/{token}?lang=en");
            var res = await student.PostAsync($"/register/{token}?lang=en", Form(p, new() { ["FullName"] = name, ["Email"] = email, ["Phone"] = phone }));
            return res.StatusCode == HttpStatusCode.Redirect ? "ok" : await res.Content.ReadAsStringAsync();
        }

        Assert.Equal("ok", await Register("Amna Ali", "amna@example.com", "249911000001"));
        Assert.Contains("this workshop is full", await student.GetStringAsync($"/register/{token}?lang=en"));
        var amnaId = await _app.WithDbAsync(db => db.Registrations.Where(r => r.Email == "amna@example.com" && r.WorkshopId == id).Select(r => r.Id).SingleAsync());

        // Cancel Amna
        details = await client.GetStringAsync(detailsUrl + "?tab=registrations");
        Assert.Contains("cancel-registration", details);
        var cancel = await client.PostAsync("/Workshops/CancelRegistration", Form(details, new()
        {
            ["id"] = id.ToString(), ["registrationId"] = amnaId.ToString(), ["reason"] = "Travelling"
        }));
        Assert.Equal(HttpStatusCode.Redirect, cancel.StatusCode);
        var amna = await _app.WithDbAsync(db => db.Registrations.SingleAsync(r => r.Id == amnaId));
        Assert.True(amna.IsCancelled);
        Assert.Equal("Travelling", amna.CancelReason);
        Assert.Equal("admin", amna.CancelledBy);

        // Shown as cancelled; not counted in finance (expected income 0)
        details = await client.GetStringAsync(detailsUrl + "?tab=registrations");
        Assert.Contains("Cancelled", details);
        Assert.Contains("0 registered · 1 cancelled", details);
        var fin = (await _app.WithServicesAsync(sp => sp.GetRequiredService<FinanceService>().ForWorkshopsAsync(new[] { id })))[id];
        Assert.Equal(0m, fin.ExpectedIncome);
        Assert.Equal(0, fin.Participants);

        // Same person can't register again (same phone, new email) — but someone else can take the freed seat
        Assert.Contains("was cancelled", await Register("Amna Ali", "amna.new@example.com", "249911000001"));
        Assert.Equal("ok", await Register("Badr Omer", "badr@example.com", "249911000002"));

        // Badr pays → can't be cancelled
        var badrId = await _app.WithDbAsync(db => db.Registrations.Where(r => r.Email == "badr@example.com").Select(r => r.Id).SingleAsync());
        var payPage = await client.GetStringAsync($"/Collections/Create?workshopId={id}&registrationId={badrId}");
        await client.PostAsync("/Collections/Save", Form(payPage, new()
        {
            ["Id"] = "0", ["WorkshopId"] = id.ToString(), ["RegistrationId"] = badrId.ToString(), ["Amount"] = "300",
            ["CollectedOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["Method"] = "Cash"
        }));
        details = await client.GetStringAsync(detailsUrl + "?tab=registrations");
        await client.PostAsync("/Workshops/CancelRegistration", Form(details, new() { ["id"] = id.ToString(), ["registrationId"] = badrId.ToString() }));
        Assert.False(await _app.WithDbAsync(db => db.Registrations.Where(r => r.Id == badrId).Select(r => r.IsCancelled).SingleAsync()));

        // Undo Amna's cancellation → 2 participants for 1 seat: danger alert on the page and in the navbar bell
        var undo = await client.PostAsync("/Workshops/RestoreRegistration", Form(details, new() { ["id"] = id.ToString(), ["registrationId"] = amnaId.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, undo.StatusCode);
        Assert.False(await _app.WithDbAsync(db => db.Registrations.Where(r => r.Id == amnaId).Select(r => r.IsCancelled).SingleAsync()));
        details = await client.GetStringAsync(detailsUrl + "?tab=registrations");
        Assert.Contains("Over capacity: 2 participants for 1 seats", details);
        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("One Seat Clinic", dashboard);
        Assert.Contains("Over capacity: 2 participants for 1 seats", dashboard);

        var history = await client.GetStringAsync(detailsUrl + "?tab=history");
        Assert.Contains("Registration cancelled: Amna Ali — Travelling", history);
        Assert.Contains("Cancellation undone: Amna Ali", history);
    }

    [Fact] // Cancelling is for coordinators (workshop admins) and administrators, not finance officers
    public async Task CancelRegistration_FollowsPermissions()
    {
        var finance = await _app.SignedInUserAsync("fin.cancel", Roles.Finance);
        var page = await finance.GetStringAsync("/");
        var res = await finance.PostAsync("/Workshops/CancelRegistration", Form(page, new() { ["id"] = "1", ["registrationId"] = "1" }));
        Assert.True(res.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden);
        if (res.StatusCode == HttpStatusCode.Redirect) Assert.Contains("AccessDenied", res.Headers.Location!.ToString());

        var coordinator = await _app.SignedInUserAsync("coord.cancel", Roles.Coordinator);
        Assert.Contains("Alerts", await coordinator.GetStringAsync("/"));
    }

    [Fact] // Staff screens in Arabic: switch, right-to-left layout, translated menu, labels and validation messages
    public async Task StaffScreens_SwitchToArabic()
    {
        var client = await _app.SignedInAdminAsync();

        // English by default
        var en = await client.GetStringAsync("/");
        Assert.Contains("dir=\"ltr\"", en);
        Assert.Contains("core.css", en);
        Assert.Contains(">Dashboard<", en);

        // Switch keeps the page and remembers the choice
        var sw = await client.GetAsync("/Language/Set?lang=ar&returnUrl=%2FWorkshops");
        Assert.Equal(HttpStatusCode.Redirect, sw.StatusCode);
        Assert.Equal("/Workshops", sw.Headers.Location!.ToString());

        var ar = await client.GetStringAsync("/");
        Assert.Contains("lang=\"ar\" dir=\"rtl\"", ar);
        Assert.Contains("core.rtl.css", ar);
        Assert.Contains("لوحة التحكم", ar);
        Assert.Contains("الورش", ar);
        Assert.Contains("English", ar); // switch back

        // A form with Arabic labels and Arabic validation messages
        var page = await client.GetStringAsync("/Workshops/Create");
        Assert.Contains("ورشة جديدة", page);
        var res = await client.PostAsync("/Workshops/Save", Form(page, new() { ["Id"] = "0", ["Type"] = "Online", ["Country"] = "SD" }));
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("أدخل عدد المقاعد (السعة).", html);
        Assert.Contains("اختر تاريخ الورشة.", html);

        // An external link can't be used as the return address
        var evil = await client.GetAsync("/Language/Set?lang=en&returnUrl=https%3A%2F%2Fevil.example");
        Assert.Equal("/", evil.Headers.Location!.ToString());
        Assert.Contains(">Dashboard<", await client.GetStringAsync("/"));
    }
}
