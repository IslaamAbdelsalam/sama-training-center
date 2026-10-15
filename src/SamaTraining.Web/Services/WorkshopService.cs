using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;

namespace SamaTraining.Web.Services;

/// <summary>
/// Business rules for the workshop lifecycle, registration and expenses.
/// Controllers call these so the rules live in one place.
/// </summary>
public class WorkshopService
{
    private readonly ApplicationDbContext _db;

    public WorkshopService(ApplicationDbContext db) => _db = db;

    // ---------- Rules ----------

    /// <summary>US-05: completed workshops are read-only.</summary>
    public static bool CanEdit(Workshop w) => w.Status != WorkshopStatus.Completed;

    /// <summary>US-14: expenses only once the workshop has started.</summary>
    public static bool CanAddExpense(Workshop w) => w.Status is WorkshopStatus.Ongoing or WorkshopStatus.Completed;

    /// <summary>US-08: status moves forward one step at a time.</summary>
    public static WorkshopStatus? NextStatus(WorkshopStatus current) => current switch
    {
        WorkshopStatus.Pending => WorkshopStatus.Ongoing,
        WorkshopStatus.Ongoing => WorkshopStatus.Completed,
        _ => null
    };

    /// <summary>US-07: only pending workshops with no registrations or expenses can be deleted.</summary>
    public async Task<bool> CanDeleteAsync(Workshop w)
    {
        if (w.Status != WorkshopStatus.Pending)
        {
            return false;
        }

        var hasRegistrations = await _db.Registrations.AnyAsync(r => r.WorkshopId == w.Id);
        var hasExpenses = await _db.WorkshopExpenses.AnyAsync(e => e.WorkshopId == w.Id);
        return !hasRegistrations && !hasExpenses;
    }

    // ---------- Status ----------

    public async Task<OperationResult> AdvanceStatusAsync(int workshopId, WorkshopStatus target, string? userName)
    {
        var workshop = await _db.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return OperationResult.Fail("Workshop not found.");
        }

        var next = NextStatus(workshop.Status);
        if (next == null || next != target)
        {
            return OperationResult.Fail($"A {workshop.Status} workshop cannot move to {target}. Status only moves Pending → Ongoing → Completed.");
        }

        _db.WorkshopStatusHistory.Add(new WorkshopStatusHistory
        {
            WorkshopId = workshop.Id,
            FromStatus = workshop.Status,
            ToStatus = target,
            ChangedBy = userName,
            ChangedAt = DateTime.UtcNow
        });

        workshop.Status = target;
        workshop.UpdatedBy = userName;
        workshop.UpdatedAt = DateTime.UtcNow;

        // US-09: registration closes automatically when a workshop is completed.
        if (target == WorkshopStatus.Completed)
        {
            workshop.RegistrationOpen = false;
        }

        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    // ---------- Registration link ----------

    public async Task<OperationResult> OpenRegistrationAsync(int workshopId, string? userName)
    {
        var workshop = await _db.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return OperationResult.Fail("Workshop not found.");
        }

        if (workshop.Status == WorkshopStatus.Completed)
        {
            return OperationResult.Fail("Registration cannot be opened for a completed workshop.");
        }

        workshop.RegistrationOpen = true;
        workshop.RegistrationToken ??= NewToken();
        workshop.UpdatedBy = userName;
        workshop.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> CloseRegistrationAsync(int workshopId, string? userName)
    {
        var workshop = await _db.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return OperationResult.Fail("Workshop not found.");
        }

        workshop.RegistrationOpen = false;
        workshop.UpdatedBy = userName;
        workshop.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    /// <summary>Replaces the link so the old one stops working.</summary>
    public async Task<OperationResult> RegenerateLinkAsync(int workshopId, string? userName)
    {
        var workshop = await _db.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return OperationResult.Fail("Workshop not found.");
        }

        workshop.RegistrationToken = NewToken();
        workshop.UpdatedBy = userName;
        workshop.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    public static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ---------- Public registration ----------

    public enum RegisterOutcome
    {
        Success,
        NotFound,
        Closed,
        Full,
        Duplicate
    }

    public Task<Workshop?> FindByTokenAsync(string token) =>
        _db.Workshops.Include(w => w.Hall)
            .FirstOrDefaultAsync(w => w.RegistrationToken == token);

    public async Task<int> CountRegistrationsAsync(int workshopId) =>
        await _db.Registrations.CountAsync(r => r.WorkshopId == workshopId);

    public async Task<RegisterOutcome> RegisterAsync(string token, Registration input)
    {
        var workshop = await _db.Workshops.FirstOrDefaultAsync(w => w.RegistrationToken == token);
        if (workshop == null)
        {
            return RegisterOutcome.NotFound;
        }

        if (!workshop.RegistrationOpen || workshop.Status == WorkshopStatus.Completed)
        {
            return RegisterOutcome.Closed;
        }

        var email = input.Email.Trim().ToLowerInvariant();
        if (await _db.Registrations.AnyAsync(r => r.WorkshopId == workshop.Id && r.Email == email))
        {
            return RegisterOutcome.Duplicate;
        }

        if (workshop.Capacity.HasValue && await CountRegistrationsAsync(workshop.Id) >= workshop.Capacity.Value)
        {
            return RegisterOutcome.Full;
        }

        _db.Registrations.Add(new Registration
        {
            WorkshopId = workshop.Id,
            FullName = input.FullName.Trim(),
            Email = email,
            Phone = input.Phone.Trim(),
            Organization = input.Organization?.Trim(),
            Notes = input.Notes?.Trim(),
            Language = input.Language == "ar" ? "ar" : "en",
            RegisteredAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // A concurrent request registered the same email first (unique index).
            return RegisterOutcome.Duplicate;
        }

        return RegisterOutcome.Success;
    }
}
