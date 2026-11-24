using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SamaTraining.Web.Data;
using SamaTraining.Web.Security;

namespace SamaTraining.Tests;

/// <summary>Protection against robots, floods, injected scripts, other rows' data and full-table reads.</summary>
public class SecurityTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _app;

    public SecurityTests(TestAppFactory app) => _app = app;

    private static FormUrlEncodedContent Form(string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = TestAppFactory.AntiForgery(page);
        var stamp = Regex.Match(page, "name=\"fs\" value=\"([^\"]+)\"");
        if (stamp.Success && !fields.ContainsKey("fs")) fields["fs"] = WebUtility.HtmlDecode(stamp.Groups[1].Value);
        return new FormUrlEncodedContent(fields);
    }

    [Fact]
    public async Task EveryPage_SendsBrowserProtectionHeaders()
    {
        var res = await _app.CreateBrowser().GetAsync("/Account/Login");
        var csp = res.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("noindex", res.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        Assert.False(res.Headers.Contains("Server"));
        // No inline scripts anywhere (the policy would block them).
        Assert.DoesNotMatch("<script>", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScannerProbes_GetPlain404()
    {
        var client = _app.CreateBrowser();
        foreach (var url in new[] { "/wp-login.php", "/.env", "/.git/config", "/admin.php" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
        Assert.Contains("Disallow: /", await client.GetStringAsync("/robots.txt"));
    }

    [Fact]
    public async Task Robot_FillingHiddenField_IsIgnored_AndNothingIsSaved()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = "Bot Trap", ["NameAr"] = "فخ الروبوت", ["Capacity"] = "10", ["Fee"] = "0",
            ["Date"] = DateTime.Today.AddDays(9).ToString("yyyy-MM-dd"), ["StartTime"] = "09:00", ["EndTime"] = "11:00",
            ["Type"] = "Online", ["Country"] = "SD"
        }));
        var id = int.Parse(res.Headers.Location!.ToString().Split('/').Last().Split('?')[0]);
        var details = await client.GetStringAsync($"/Workshops/Details/{id}");
        await client.PostAsync("/Workshops/OpenRegistration", Form(details, new() { ["id"] = id.ToString() }));
        var token = await _app.WithDbAsync(db => db.Workshops.Where(w => w.Id == id).Select(w => w.RegistrationToken!).SingleAsync());

        var visitor = _app.CreateBrowser();
        var form = await visitor.GetStringAsync($"/register/{token}");
        Assert.Contains("name=\"website\"", form);

        // Robot: fills the hidden field → looks like success, nothing saved.
        var bot = await visitor.PostAsync($"/register/{token}", Form(form, new()
        {
            ["FullName"] = "Spam Bot", ["Email"] = "bot@spam.example", ["Phone"] = "249912345678", ["website"] = "http://spam.example"
        }));
        Assert.Equal(HttpStatusCode.Redirect, bot.StatusCode);

        // Robot without the signed time stamp → also ignored.
        var noStamp = await visitor.PostAsync($"/register/{token}", Form(form, new()
        {
            ["FullName"] = "Script", ["Email"] = "script@spam.example", ["Phone"] = "249912345679", ["fs"] = "forged"
        }));
        Assert.Equal(HttpStatusCode.Redirect, noStamp.StatusCode);
        Assert.Equal(0, await _app.WithDbAsync(db => db.Registrations.CountAsync(r => r.WorkshopId == id)));

        // A person gets through.
        var person = await visitor.PostAsync($"/register/{token}", Form(form, new()
        {
            ["FullName"] = "Real Person", ["Email"] = "real@person.example", ["Phone"] = "249912345670"
        }));
        Assert.Equal(HttpStatusCode.Redirect, person.StatusCode);
        Assert.Equal(1, await _app.WithDbAsync(db => db.Registrations.CountAsync(r => r.WorkshopId == id)));

        // Junk tokens never reach the database query.
        Assert.Contains("Link not found", await visitor.GetStringAsync("/register/' OR 1=1 --"));
    }

    [Fact]
    public async Task Lists_AreReadOnePageAtATime_WithACursor()
    {
        var client = await _app.SignedInAdminAsync();
        await _app.WithDbAsync(async db =>
        {
            for (var i = 0; i < 30; i++)
                db.Workshops.Add(new Workshop
                {
                    Name = $"Paged Workshop {i:00}", Country = "EG", CurrencyCode = "EGP", Type = WorkshopType.Online, Capacity = 10,
                    StartDateTime = new DateTime(2031, 1, 1).AddDays(i), EndDateTime = new DateTime(2031, 1, 1).AddDays(i).AddHours(2)
                });
            return await db.SaveChangesAsync();
        });

        var first = await client.GetStringAsync("/Workshops?Search=Paged+Workshop");
        Assert.Equal(25, Regex.Matches(first, "Paged Workshop \\d\\d").Count);
        Assert.Contains("Paged Workshop 29", first); // newest first
        Assert.DoesNotContain("Paged Workshop 04", first);
        var next = Regex.Match(first, "href=\"([^\"]*cursor=[^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(next));

        var second = await client.GetStringAsync(WebUtility.HtmlDecode(next));
        Assert.Equal(5, Regex.Matches(second, "Paged Workshop \\d\\d").Count);
        Assert.Contains("Paged Workshop 00", second);
        Assert.DoesNotContain("Paged Workshop 29", second);
        Assert.DoesNotMatch("href=\"[^\"]*cursor=", second); // last page: Next is disabled
        Assert.Contains("First page", second);
        Assert.Contains("26–30 of 30", second);

        // Rows per page: only 10 / 25 / 50 / 100 are accepted.
        var ten = await client.GetStringAsync("/Workshops?Search=Paged+Workshop&Size=10");
        Assert.Equal(10, Regex.Matches(ten, "Paged Workshop \\d\\d").Count);
        Assert.Contains("1–10 of 30", ten);
        var huge = await client.GetStringAsync("/Workshops?Search=Paged+Workshop&Size=100000");
        Assert.Equal(25, Regex.Matches(huge, "Paged Workshop \\d\\d").Count); // not allowed → default

        // A damaged or forged cursor just starts again at the first page.
        var forged = await client.GetStringAsync("/Workshops?Search=Paged+Workshop&Cursor=not-a-cursor%27--");
        Assert.Contains("Paged Workshop 29", forged);
    }

    [Fact]
    public async Task CancellingThroughAnotherWorkshop_IsRefused()
    {
        var client = await _app.SignedInAdminAsync();
        var (a, b, regId) = await _app.WithDbAsync(async db =>
        {
            var w1 = new Workshop { Name = "Row A", Country = "SD", CurrencyCode = "SDG", Type = WorkshopType.Online, Capacity = 5, StartDateTime = DateTime.Today.AddDays(20), EndDateTime = DateTime.Today.AddDays(20).AddHours(2) };
            var w2 = new Workshop { Name = "Row B", Country = "SD", CurrencyCode = "SDG", Type = WorkshopType.Online, Capacity = 5, StartDateTime = DateTime.Today.AddDays(21), EndDateTime = DateTime.Today.AddDays(21).AddHours(2) };
            db.Workshops.AddRange(w1, w2);
            await db.SaveChangesAsync();
            var r = new Registration { WorkshopId = w1.Id, FullName = "Row Owner", Email = "row@owner.example", Phone = "249911111111", RegisteredAt = DateTime.UtcNow };
            db.Registrations.Add(r);
            await db.SaveChangesAsync();
            return (w1.Id, w2.Id, r.Id);
        });

        var page = await client.GetStringAsync($"/Workshops/Details/{b}");
        await client.PostAsync("/Workshops/CancelRegistration", Form(page, new() { ["id"] = b.ToString(), ["registrationId"] = regId.ToString() }));
        Assert.False(await _app.WithDbAsync(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.IsCancelled).SingleAsync()));

        page = await client.GetStringAsync($"/Workshops/Details/{a}");
        await client.PostAsync("/Workshops/CancelRegistration", Form(page, new() { ["id"] = a.ToString(), ["registrationId"] = regId.ToString() }));
        Assert.True(await _app.WithDbAsync(db => db.Registrations.Where(r => r.Id == regId).Select(r => r.IsCancelled).SingleAsync()));
    }

    [Fact]
    public async Task OverlongInput_IsRejectedOnTheServer()
    {
        var client = await _app.SignedInAdminAsync();
        var page = await client.GetStringAsync("/Workshops/Create");
        var res = await client.PostAsync("/Workshops/Save", Form(page, new()
        {
            ["Id"] = "0", ["Name"] = new string('x', 201), ["NameAr"] = "اسم", ["Capacity"] = "10", ["Fee"] = "0",
            ["Date"] = DateTime.Today.AddDays(9).ToString("yyyy-MM-dd"), ["StartTime"] = "09:00", ["EndTime"] = "11:00",
            ["Type"] = "Online", ["Country"] = "SD"
        }));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); // form shown again with the error, nothing saved
        Assert.Contains("can be at most 200 characters", await res.Content.ReadAsStringAsync());
    }
}

/// <summary>Same app with production-like limits, to prove the limiter answers 429.</summary>
public class RateLimitFactory : TestAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RateLimit:AuthPer5Minutes"] = "3",
        }));
    }
}

public class RateLimitTests : IClassFixture<RateLimitFactory>
{
    private readonly RateLimitFactory _app;

    public RateLimitTests(RateLimitFactory app) => _app = app;

    [Fact]
    public async Task PasswordGuessing_IsCutOffWith429()
    {
        var client = _app.CreateBrowser();
        var page = await client.GetStringAsync("/Account/Login");
        var token = TestAppFactory.AntiForgery(page);
        HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++)
        {
            last = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UserNameOrEmail"] = "admin", ["Password"] = "Guess@" + i, ["__RequestVerificationToken"] = token
            }));
            if (i < 3) Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
        Assert.Contains("Too many requests", await last.Content.ReadAsStringAsync());
    }
}

public class ListFilterTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _app;

    public ListFilterTests(TestAppFactory app) => _app = app;

    [Fact]
    public async Task Users_Halls_Categories_AndWorkshopTabs_HaveFiltersAndPagers()
    {
        var client = await _app.SignedInAdminAsync();
        await _app.SignedInUserAsync("filter.finance", Roles.Finance);

        var users = await client.GetStringAsync("/Users?q=filter.fin");
        Assert.Contains("filter.finance", users);
        Assert.DoesNotContain(">admin<", users);
        Assert.Contains("Rows per page", users);
        Assert.Contains("No users match these filters.", await client.GetStringAsync("/Users?q=zzz-nobody"));
        Assert.Contains("filter.finance", await client.GetStringAsync("/Users?role=" + Uri.EscapeDataString(Roles.Finance)));

        var halls = await client.GetStringAsync("/Halls?q=Main");
        Assert.Contains("Main Hall", halls);
        Assert.DoesNotContain("Training Room 1", halls);
        Assert.Contains("No halls match these filters.", await client.GetStringAsync("/Halls?status=inactive&q=zzz"));

        var cats = await client.GetStringAsync("/ExpenseCategories?q=Catering");
        Assert.Contains("Catering", cats);
        Assert.DoesNotContain("Hall Rental", cats);

        // Workshop page: students tab filter by name and by status.
        var id = await _app.WithDbAsync(async db =>
        {
            var w = new Workshop { Name = "Filter Tabs", Country = "SD", CurrencyCode = "SDG", Type = WorkshopType.Online, Capacity = 50,
                StartDateTime = DateTime.Today.AddDays(30), EndDateTime = DateTime.Today.AddDays(30).AddHours(2) };
            db.Workshops.Add(w);
            await db.SaveChangesAsync();
            for (var i = 0; i < 12; i++)
                db.Registrations.Add(new Registration { WorkshopId = w.Id, FullName = $"Student {i:00}", Email = $"s{i}@x.example", Phone = $"2499100000{i:00}",
                    RegisteredAt = DateTime.UtcNow.AddMinutes(-i), IsCancelled = i == 11 });
            await db.SaveChangesAsync();
            return w.Id;
        });
        var tab = await client.GetStringAsync($"/Workshops/Details/{id}?tab=registrations&rq=Student+03");
        Assert.Contains("Student 03", tab);
        Assert.DoesNotContain("Student 04", tab);
        var cancelled = await client.GetStringAsync($"/Workshops/Details/{id}?tab=registrations&rstatus=cancelled");
        Assert.Contains("Student 11", cancelled);
        Assert.DoesNotContain("Student 01", cancelled);
        var paged = await client.GetStringAsync($"/Workshops/Details/{id}?tab=registrations&rsize=10");
        Assert.Contains("1–10 of 12", paged);
        Assert.Matches("href=\"[^\"]*rcur=", paged);
    }
}

