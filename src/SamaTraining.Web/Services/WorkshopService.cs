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

    /// <summary>Completed workshops are read-only.</summary>
    public static bool CanEdit(Workshop w) => w.Status != WorkshopStatus.Completed;

    /// <summary>US-08 / US-09: expenses can be added and managed from creation until the workshop is completed.</summary>
    public static bool CanAddExpense(Workshop w) => w.Status != WorkshopStatus.Completed;

    /// <summary>US-09 / US-18: fee collections are recorded until the workshop is completed (paid workshops only).</summary>
    public static bool CanCollect(Workshop w) => w.Status != WorkshopStatus.Completed && !w.IsFree;

    /// <summary>The next status going forward: Pending → Ongoing → Completed.</summary>
    public static WorkshopStatus? NextStatus(WorkshopStatus current) => current switch
    {
        WorkshopStatus.Pending => WorkshopStatus.Ongoing,
        WorkshopStatus.Ongoing => WorkshopStatus.Completed,
        _ => null
    };

    /// <summary>US-10: the status a mistaken change can be reversed to: Ongoing → Pending, Completed → Ongoing.</summary>
    public static WorkshopStatus? PreviousStatus(WorkshopStatus current) => current switch
    {
        WorkshopStatus.Ongoing => WorkshopStatus.Pending,
        WorkshopStatus.Completed => WorkshopStatus.Ongoing,
        _ => null
    };

    /// <summary>Only pending workshops with no registrations, expenses or collections can be deleted.</summary>
    public async Task<bool> CanDeleteAsync(Workshop w)
    {
        if (w.Status != WorkshopStatus.Pending)
        {
            return false;
        }

        var hasRegistrations = await _db.Registrations.AnyAsync(r => r.WorkshopId == w.Id);
        var hasExpenses = await _db.WorkshopExpenses.AnyAsync(e => e.WorkshopId == w.Id);
        var hasCollections = await _db.FeeCollections.AnyAsync(c => c.WorkshopId == w.Id);
        return !hasRegistrations && !hasExpenses && !hasCollections;
    }

    // ---------- Status ----------

    /// <summary>Moves the status one step forward (Pending → Ongoing → Completed).</summary>
    public Task<OperationResult> AdvanceStatusAsync(int workshopId, WorkshopStatus target, string? userName) =>
        ChangeStatusAsync(workshopId, target, userName, allowReverse: false);

    /// <summary>
    /// US-10: changes the status one step forward or, when <paramref name="allowReverse"/> is true, one step back.
    /// Every change is written to the workshop history with the previous and new status, time and user.
    /// </summary>
    public async Task<OperationResult> ChangeStatusAsync(int workshopId, WorkshopStatus target, string? userName, bool allowReverse)
    {
        var workshop = await _db.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return OperationResult.Fail("Workshop not found.");
        }

        var forward = NextStatus(workshop.Status) == target;
        var backward = PreviousStatus(workshop.Status) == target;
        if (!forward && !(backward && allowReverse))
        {
            return OperationResult.Fail(backward
                ? "You don't have permission to reverse a workshop status."
                : $"A {workshop.Status} workshop can't move to {target}. Status moves one step at a time: Pending ↔ Ongoing ↔ Completed.");
        }

        var now = DateTime.UtcNow;
        AddHistory(workshop, WorkshopHistoryAction.StatusChanged, userName, now, target);

        // Registration closes automatically when a workshop is completed.
        if (target == WorkshopStatus.Completed && workshop.RegistrationOpen)
        {
            workshop.RegistrationOpen = false;
            AddHistory(workshop, WorkshopHistoryAction.RegistrationClosed, userName, now.AddMilliseconds(1));
        }

        workshop.Status = target;
        workshop.UpdatedBy = userName;
        workshop.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return OperationResult.Ok();
    }

    private void AddHistory(Workshop workshop, WorkshopHistoryAction action, string? userName, DateTime at, WorkshopStatus? newStatus = null)
    {
        _db.WorkshopStatusHistory.Add(new WorkshopStatusHistory
        {
            WorkshopId = workshop.Id,
            Action = action,
            FromStatus = workshop.Status,
            ToStatus = newStatus ?? workshop.Status,
            ChangedBy = userName,
            ChangedAt = at
        });
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

        if (workshop.RegistrationOpen)
        {
            return OperationResult.Ok();
        }

        // US-07: recorded in the workshop history.
        AddHistory(workshop, WorkshopHistoryAction.RegistrationOpened, userName, DateTime.UtcNow);
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

        if (!workshop.RegistrationOpen)
        {
            return OperationResult.Ok();
        }

        // US-07: recorded in the workshop history.
        AddHistory(workshop, WorkshopHistoryAction.RegistrationClosed, userName, DateTime.UtcNow);
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

        AddHistory(workshop, WorkshopHistoryAction.LinkRegenerated, userName, DateTime.UtcNow);
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

        /// <summary>This email is already registered for the workshop.</summary>
        Duplicate,

        /// <summary>This phone number is already registered for the workshop.</summary>
        DuplicatePhone
    }

    public Task<Workshop?> FindByTokenAsync(string token) =>
        _db.Workshops.Include(w => w.Hall)
            .FirstOrDefaultAsync(w => w.RegistrationToken == token);

    public async Task<int> CountRegistrationsAsync(int workshopId) =>
        await _db.Registrations.CountAsync(r => r.WorkshopId == workshopId);

    public async Task<RegisterOutcome> RegisterAsync(string token, Registration input)
    {
        var workshop = await _db.Workshops.AsNoTracking().FirstOrDefaultAsync(w => w.RegistrationToken == token);
        if (workshop == null)
        {
            return RegisterOutcome.NotFound;
        }

        var email = input.Email.Trim().ToLowerInvariant();
        // One registration per phone number per workshop. Stored as digits only, e.g. 249912345678.
        var phone = PhoneNumberService.Clean(input.Phone);

        // US-15: the seat check and the insert run in one transaction that first locks the workshop row,
        // so two people submitting at the same moment can never take the same last seat.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _db.ChangeTracker.Clear();
            await using var tx = await _db.Database.BeginTransactionAsync();

            // Taking a write lock on the workshop row serialises registrations for this workshop.
            await _db.Workshops.Where(w => w.Id == workshop.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.RegistrationOpen, w => w.RegistrationOpen));

            // Re-read the latest state inside the lock.
            var current = await _db.Workshops.AsNoTracking().FirstAsync(w => w.Id == workshop.Id);
            if (!current.RegistrationOpen || current.Status == WorkshopStatus.Completed)
            {
                return RegisterOutcome.Closed;
            }

            if (await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.Email == email))
            {
                return RegisterOutcome.Duplicate;
            }

            if (await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.Phone == phone))
            {
                return RegisterOutcome.DuplicatePhone;
            }

            if (current.Capacity.HasValue && await _db.Registrations.CountAsync(r => r.WorkshopId == current.Id) >= current.Capacity.Value)
            {
                return RegisterOutcome.Full;
            }

            _db.Registrations.Add(new Registration
            {
                WorkshopId = current.Id,
                FullName = input.FullName.Trim(),
                Email = email,
                Phone = phone,
                Organization = input.Organization?.Trim(),
                Notes = input.Notes?.Trim(),
                Language = input.Language == "ar" ? "ar" : "en",
                RegisteredAt = DateTime.UtcNow
            });

            try
            {
                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Safety net: the unique indexes on (workshop, email) and (workshop, phone).
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                return await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.Phone == phone)
                    ? RegisterOutcome.DuplicatePhone
                    : RegisterOutcome.Duplicate;
            }

            return RegisterOutcome.Success;
        });
    }
}
