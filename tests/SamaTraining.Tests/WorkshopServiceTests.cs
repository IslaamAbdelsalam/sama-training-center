using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Services;

namespace SamaTraining.Tests;

/// <summary>Business rules from the user stories, tested without the web layer.</summary>
public class WorkshopServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly WorkshopService _service;

    public WorkshopServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _service = new WorkshopService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Workshop> AddWorkshopAsync(int? capacity = null)
    {
        var w = new Workshop
        {
            Name = "Leadership Basics",
            StartDateTime = DateTime.Today.AddDays(3).AddHours(9),
            EndDateTime = DateTime.Today.AddDays(3).AddHours(13),
            Type = WorkshopType.Online,
            Capacity = capacity
        };
        _db.Workshops.Add(w);
        await _db.SaveChangesAsync();
        return w;
    }

    /// <summary>A student whose phone is derived from the email, so different emails get different phones.</summary>
    private static Registration Student(string email, string? phone = null)
    {
        var key = email.Trim().ToLowerInvariant();
        var n = (uint)key.Aggregate(17, (h, c) => unchecked(h * 31 + c)) % 100_000_000;
        return new() { FullName = "Student", Email = email, Phone = phone ?? $"2499{n:D8}" };
    }

    [Fact] // US-03
    public async Task NewWorkshop_IsPending_WithRegistrationClosed()
    {
        var w = await AddWorkshopAsync();
        Assert.Equal(WorkshopStatus.Pending, w.Status);
        Assert.False(w.RegistrationOpen);
    }

    [Fact] // US-08
    public async Task Status_MovesForwardOneStepOnly()
    {
        var w = await AddWorkshopAsync();

        Assert.False((await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Completed, "u")).Success);
        Assert.True((await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Ongoing, "u")).Success);
        Assert.False((await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Pending, "u")).Success);
        Assert.True((await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Completed, "u")).Success);
        Assert.Null(WorkshopService.NextStatus(WorkshopStatus.Completed));

        var history = await _db.WorkshopStatusHistory.Where(h => h.WorkshopId == w.Id && h.Action == WorkshopHistoryAction.StatusChanged).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal("u", h.ChangedBy));
    }

    [Fact] // US-09
    public async Task OpeningRegistration_CreatesLink_AndCompletingClosesIt()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();
        Assert.True(w.RegistrationOpen);
        Assert.False(string.IsNullOrEmpty(w.RegistrationToken));

        await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Ongoing, "u");
        await _service.AdvanceStatusAsync(w.Id, WorkshopStatus.Completed, "u");
        await _db.Entry(w).ReloadAsync();
        Assert.False(w.RegistrationOpen);

        Assert.False((await _service.OpenRegistrationAsync(w.Id, "u")).Success);
    }

    [Fact] // US-12
    public async Task ClosedLink_RejectsRegistration()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();
        var token = w.RegistrationToken!;
        await _service.CloseRegistrationAsync(w.Id, "u");

        Assert.Equal(WorkshopService.RegisterOutcome.Closed, await _service.RegisterAsync(token, Student("a@x.com")));
        Assert.Equal(WorkshopService.RegisterOutcome.NotFound, await _service.RegisterAsync("nope", Student("a@x.com")));
    }

    [Fact] // US-13
    public async Task DuplicateEmail_IsBlocked_CaseInsensitive()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();

        Assert.Equal(WorkshopService.RegisterOutcome.Success, await _service.RegisterAsync(w.RegistrationToken!, Student("Sara@Example.com")));
        Assert.Equal(WorkshopService.RegisterOutcome.Duplicate, await _service.RegisterAsync(w.RegistrationToken!, Student("sara@example.com ")));
        Assert.Equal(1, await _service.CountRegistrationsAsync(w.Id));
    }

    [Fact]
    public async Task Capacity_StopsRegistrationWhenFull()
    {
        var w = await AddWorkshopAsync(capacity: 1);
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();

        Assert.Equal(WorkshopService.RegisterOutcome.Success, await _service.RegisterAsync(w.RegistrationToken!, Student("a@x.com")));
        Assert.Equal(WorkshopService.RegisterOutcome.Full, await _service.RegisterAsync(w.RegistrationToken!, Student("b@x.com")));
    }

    [Fact] // US-07
    public async Task OnlyEmptyPendingWorkshops_CanBeDeleted()
    {
        var w = await AddWorkshopAsync();
        Assert.True(await _service.CanDeleteAsync(w));

        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();
        await _service.RegisterAsync(w.RegistrationToken!, Student("a@x.com"));
        Assert.False(await _service.CanDeleteAsync(w));

        var w2 = await AddWorkshopAsync();
        await _service.AdvanceStatusAsync(w2.Id, WorkshopStatus.Ongoing, "u");
        await _db.Entry(w2).ReloadAsync();
        Assert.False(await _service.CanDeleteAsync(w2));
    }

    [Fact] // US-08 / US-09: expenses and collections from creation until completion
    public void EditExpenseAndCollectionRules_FollowStatus()
    {
        var w = new Workshop { Status = WorkshopStatus.Pending, Fee = 100 };
        Assert.True(WorkshopService.CanEdit(w));
        Assert.True(WorkshopService.CanAddExpense(w));
        Assert.True(WorkshopService.CanCollect(w));

        w.Status = WorkshopStatus.Ongoing;
        Assert.True(WorkshopService.CanEdit(w));
        Assert.True(WorkshopService.CanAddExpense(w));
        Assert.True(WorkshopService.CanCollect(w));

        w.Status = WorkshopStatus.Completed;
        Assert.False(WorkshopService.CanEdit(w));
        Assert.False(WorkshopService.CanAddExpense(w));
        Assert.False(WorkshopService.CanCollect(w));

        // Nothing to collect on a free workshop
        Assert.False(WorkshopService.CanCollect(new Workshop { Status = WorkshopStatus.Ongoing, Fee = null }));
    }

    [Fact] // US-10: reverse a status change, recorded with previous/new status and user
    public async Task Status_CanBeReversed_OnlyWhenAllowed()
    {
        var w = await AddWorkshopAsync();
        Assert.True((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Ongoing, "admin", allowReverse: false)).Success);
        Assert.False((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Pending, "coord", allowReverse: false)).Success);
        Assert.True((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Pending, "admin", allowReverse: true)).Success);

        Assert.True((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Ongoing, "admin", allowReverse: false)).Success);
        Assert.True((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Completed, "admin", allowReverse: false)).Success);
        Assert.True((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Ongoing, "admin", allowReverse: true)).Success);

        var reversal = await _db.WorkshopStatusHistory
            .Where(h => h.WorkshopId == w.Id && h.FromStatus == WorkshopStatus.Ongoing && h.ToStatus == WorkshopStatus.Pending)
            .SingleAsync();
        Assert.Equal(WorkshopHistoryAction.StatusChanged, reversal.Action);
        Assert.Equal("admin", reversal.ChangedBy);
    }

    [Fact]
    public async Task Status_CannotSkipSteps()
    {
        var w = await AddWorkshopAsync();
        Assert.False((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Completed, "admin", allowReverse: true)).Success);
        await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Ongoing, "admin", allowReverse: false);
        await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Completed, "admin", allowReverse: false);
        Assert.False((await _service.ChangeStatusAsync(w.Id, WorkshopStatus.Pending, "admin", allowReverse: true)).Success);
    }

    [Fact] // US-07: opening and closing registration are recorded in history
    public async Task RegistrationOpenClose_IsRecordedInHistory()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "coord");
        await _service.CloseRegistrationAsync(w.Id, "coord");
        await _service.CloseRegistrationAsync(w.Id, "coord"); // already closed: not recorded twice

        var actions = await _db.WorkshopStatusHistory.Where(h => h.WorkshopId == w.Id).OrderBy(h => h.Id).ToListAsync();
        Assert.Equal(new[] { WorkshopHistoryAction.RegistrationOpened, WorkshopHistoryAction.RegistrationClosed }, actions.Select(a => a.Action));
        Assert.All(actions, a => Assert.Equal("coord", a.ChangedBy));
        Assert.All(actions, a => Assert.True(a.ChangedAt > DateTime.UtcNow.AddMinutes(-5)));
    }

    [Fact] // US-15: the last seat goes to only one applicant when several submit at once
    public async Task Capacity_IsNeverExceeded_UnderConcurrentRegistrations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sama-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={path};Default Timeout=30";
        try
        {
            int workshopId;
            string token;
            await using (var setup = NewFileDb(cs))
            {
                await setup.Database.EnsureCreatedAsync();
                var w = new Workshop
                {
                    Name = "Last seat", StartDateTime = DateTime.Today.AddDays(2).AddHours(9), EndDateTime = DateTime.Today.AddDays(2).AddHours(12),
                    Type = WorkshopType.Online, Capacity = 3, RegistrationOpen = true, RegistrationToken = WorkshopService.NewToken()
                };
                setup.Workshops.Add(w);
                await setup.SaveChangesAsync();
                workshopId = w.Id;
                token = w.RegistrationToken!;
            }

            var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(async () =>
            {
                await using var db = NewFileDb(cs);
                var service = new WorkshopService(db);
                for (var attempt = 0; ; attempt++)
                {
                    try { return await service.RegisterAsync(token, Student($"p{i}@x.com")); }
                    catch (Exception) when (attempt < 20) { await Task.Delay(20 * (attempt + 1)); } // SQLite "database is locked"
                }
            })).ToList();
            var outcomes = await Task.WhenAll(tasks);

            await using var check = NewFileDb(cs);
            Assert.Equal(3, await check.Registrations.CountAsync(r => r.WorkshopId == workshopId));
            Assert.Equal(3, outcomes.Count(o => o == WorkshopService.RegisterOutcome.Success));
            Assert.Equal(9, outcomes.Count(o => o == WorkshopService.RegisterOutcome.Full));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    private static ApplicationDbContext NewFileDb(string cs) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(cs).Options);

    [Fact] // US-19: financial definitions
    public async Task Finance_FollowsTheAgreedDefinitions()
    {
        var w = await AddWorkshopAsync();
        w.Fee = 500;
        w.CurrencyCode = "SDG";
        await _db.SaveChangesAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();
        await _service.RegisterAsync(w.RegistrationToken!, Student("a@x.com"));
        await _service.RegisterAsync(w.RegistrationToken!, Student("b@x.com"));
        await _service.RegisterAsync(w.RegistrationToken!, Student("c@x.com"));
        _db.ChangeTracker.Clear();

        var regs = await _db.Registrations.Where(r => r.WorkshopId == w.Id).OrderBy(r => r.Email).ToListAsync();
        var category = new ExpenseCategory { Name = "Catering" };
        _db.ExpenseCategories.Add(category);
        _db.FeeCollections.Add(new FeeCollection { WorkshopId = w.Id, RegistrationId = regs[0].Id, Amount = 500, CurrencyCode = "SDG", CollectedOn = DateTime.Today });
        _db.FeeCollections.Add(new FeeCollection { WorkshopId = w.Id, RegistrationId = regs[1].Id, Amount = 200, CurrencyCode = "SDG", CollectedOn = DateTime.Today });
        _db.WorkshopExpenses.Add(new WorkshopExpense { WorkshopId = w.Id, ExpenseCategory = category, Amount = 300, ExpenseDate = DateTime.Today });
        await _db.SaveChangesAsync();

        var f = (await new FinanceService(_db).ForWorkshopsAsync(new[] { w.Id }))[w.Id];
        Assert.Equal(1500m, f.ExpectedIncome);   // 3 × 500
        Assert.Equal(700m, f.Collected);         // 500 + 200
        Assert.Equal(800m, f.Outstanding);       // 1500 − 700
        Assert.Equal(300m, f.TotalExpenses);
        Assert.Equal(400m, f.NetProfit);         // 700 − 300

        Assert.Equal(PaymentStatus.Paid, FinanceService.StatusOf(500, 500));
        Assert.Equal(PaymentStatus.PartiallyPaid, FinanceService.StatusOf(500, 200));
        Assert.Equal(PaymentStatus.Unpaid, FinanceService.StatusOf(500, 0));
        Assert.Equal(PaymentStatus.Free, FinanceService.StatusOf(null, 0));
    }

    [Fact] // US-04: agenda formatting is kept, unsafe markup is removed
    public void RichText_KeepsFormatting_RemovesScripts()
    {
        var clean = RichText.Clean("<h2>Day 1</h2><p><strong>Bold</strong> <a href=\"https://sama.org\">link</a></p><ul><li>One</li></ul><script>alert(1)</script><p onclick=\"x()\">Hi</p><a href=\"javascript:alert(1)\">bad</a>");
        Assert.NotNull(clean);
        Assert.Contains("<h2>Day 1</h2>", clean);
        Assert.Contains("<strong>Bold</strong>", clean);
        Assert.Contains("<ul><li>One</li></ul>", clean);
        Assert.Contains("rel=\"noopener noreferrer\"", clean);
        Assert.DoesNotContain("script", clean);
        Assert.DoesNotContain("onclick", clean);
        Assert.DoesNotContain("javascript:", clean);
        Assert.Null(RichText.Clean("<p><br></p>"));

        // Older plain-text agendas keep their line breaks and are encoded
        Assert.Equal("<p>Line 1<br>&lt;b&gt;</p>", RichText.ToHtml("Line 1\n<b>").ToString());
    }

    [Fact] // US-09: regenerating the link invalidates the old one
    public async Task RegeneratedLink_ReplacesOldOne()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();
        var oldToken = w.RegistrationToken!;

        await _service.RegenerateLinkAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();

        Assert.NotEqual(oldToken, w.RegistrationToken);
        Assert.Equal(WorkshopService.RegisterOutcome.NotFound, await _service.RegisterAsync(oldToken, Student("a@x.com")));
    }

    [Fact] // Same phone number can't register twice for the same workshop, even with another email
    public async Task DuplicatePhone_IsBlocked()
    {
        var w = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w.Id, "u");
        await _db.Entry(w).ReloadAsync();

        Assert.Equal(WorkshopService.RegisterOutcome.Success, await _service.RegisterAsync(w.RegistrationToken!, Student("a@x.com", "00249 912 345 678")));
        Assert.Equal(WorkshopService.RegisterOutcome.DuplicatePhone, await _service.RegisterAsync(w.RegistrationToken!, Student("b@x.com", "+249912345678")));

        // Same phone is fine for a different workshop
        var w2 = await AddWorkshopAsync();
        await _service.OpenRegistrationAsync(w2.Id, "u");
        await _db.Entry(w2).ReloadAsync();
        Assert.Equal(WorkshopService.RegisterOutcome.Success, await _service.RegisterAsync(w2.RegistrationToken!, Student("b@x.com", "249912345678")));
    }
}
