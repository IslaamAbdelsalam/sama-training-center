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

        var history = await _db.WorkshopStatusHistory.Where(h => h.WorkshopId == w.Id).ToListAsync();
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

    [Fact] // US-05 / US-14
    public void EditAndExpenseRules_FollowStatus()
    {
        var w = new Workshop { Status = WorkshopStatus.Pending };
        Assert.True(WorkshopService.CanEdit(w));
        Assert.False(WorkshopService.CanAddExpense(w));

        w.Status = WorkshopStatus.Ongoing;
        Assert.True(WorkshopService.CanEdit(w));
        Assert.True(WorkshopService.CanAddExpense(w));

        w.Status = WorkshopStatus.Completed;
        Assert.False(WorkshopService.CanEdit(w));
        Assert.True(WorkshopService.CanAddExpense(w));
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
