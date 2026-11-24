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
            return OperationResult.Fail(UiText.Get("Workshop not found."));
        }

        var forward = NextStatus(workshop.Status) == target;
        var backward = PreviousStatus(workshop.Status) == target;
        if (!forward && !(backward && allowReverse))
        {
            return OperationResult.Fail(backward
                ? "You don't have permission to reverse a workshop status."
                : UiText.Format("A {0} workshop can't move to {1}. Status moves one step at a time: Pending ↔ Ongoing ↔ Completed.", UiText.Status(workshop.Status), UiText.Status(target)));
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
            return OperationResult.Fail(UiText.Get("Workshop not found."));
        }

        if (workshop.Status == WorkshopStatus.Completed)
        {
            return OperationResult.Fail(UiText.Get("Registration cannot be opened for a completed workshop."));
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
            return OperationResult.Fail(UiText.Get("Workshop not found."));
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
            return OperationResult.Fail(UiText.Get("Workshop not found."));
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
        DuplicatePhone,

        /// <summary>This person's registration for the workshop was cancelled; they can't register again.</summary>
        CancelledBefore
    }

    /// <summary>Result of cancelling or restoring a registration.</summary>
    public record ParticipantChange(bool Success, string? Error = null, string? Name = null, bool OverCapacity = false, int Active = 0, int? Capacity = null);

    /// <summary>
    /// Cancels a registration: its seat becomes free and it no longer counts anywhere (seats, participants, money),
    /// but it stays on the list so the same person can't register for this workshop again.
    /// Not allowed once the participant has paid, or after the workshop is completed.
    /// </summary>
    public async Task<ParticipantChange> CancelRegistrationAsync(int registrationId, string? userName, string? reason, int? workshopId = null)
    {
        // Row check: the registration must belong to the workshop the request came from.
        var r = await _db.Registrations.Include(x => x.Workshop)
            .FirstOrDefaultAsync(x => x.Id == registrationId && (workshopId == null || x.WorkshopId == workshopId));
        if (r == null) return new ParticipantChange(false, UiText.Get("Registration not found."));
        if (r.IsCancelled) return new ParticipantChange(true, Name: r.FullName);
        if (r.Workshop!.Status == WorkshopStatus.Completed)
            return new ParticipantChange(false, UiText.Get("The workshop is completed, so its participants can't change."), r.FullName);
        if (await _db.FeeCollections.AnyAsync(c => c.RegistrationId == r.Id))
            return new ParticipantChange(false, UiText.Format("{0} has paid, so the registration can't be cancelled. Delete the payment first if it was recorded by mistake.", r.FullName), r.FullName);

        var now = DateTime.UtcNow;
        r.IsCancelled = true;
        r.CancelledAt = now;
        r.CancelledBy = userName;
        r.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() is var t && t.Length > 250 ? t[..250] : reason.Trim();
        _db.WorkshopStatusHistory.Add(new WorkshopStatusHistory
        {
            WorkshopId = r.WorkshopId, Action = WorkshopHistoryAction.ParticipantCancelled,
            FromStatus = r.Workshop.Status, ToStatus = r.Workshop.Status, ChangedBy = userName, ChangedAt = now,
            Note = r.FullName + (r.CancelReason == null ? "" : " — " + r.CancelReason)
        });
        await _db.SaveChangesAsync();
        return new ParticipantChange(true, Name: r.FullName);
    }

    /// <summary>
    /// Undoes a cancellation made by mistake. Always allowed (before completion), even when the seat was taken
    /// meanwhile: the result then reports OverCapacity so an administrator can sort it out.
    /// </summary>
    public async Task<ParticipantChange> RestoreRegistrationAsync(int registrationId, string? userName, int? workshopId = null)
    {
        var r = await _db.Registrations.Include(x => x.Workshop)
            .FirstOrDefaultAsync(x => x.Id == registrationId && (workshopId == null || x.WorkshopId == workshopId));
        if (r == null) return new ParticipantChange(false, UiText.Get("Registration not found."));
        if (r.Workshop!.Status == WorkshopStatus.Completed)
            return new ParticipantChange(false, UiText.Get("The workshop is completed, so its participants can't change."), r.FullName);

        if (r.IsCancelled)
        {
            var now = DateTime.UtcNow;
            r.IsCancelled = false;
            r.CancelledAt = null;
            r.CancelledBy = null;
            r.CancelReason = null;
            _db.WorkshopStatusHistory.Add(new WorkshopStatusHistory
            {
                WorkshopId = r.WorkshopId, Action = WorkshopHistoryAction.ParticipantRestored,
                FromStatus = r.Workshop.Status, ToStatus = r.Workshop.Status, ChangedBy = userName, ChangedAt = now, Note = r.FullName
            });
            await _db.SaveChangesAsync();
        }

        var active = await CountRegistrationsAsync(r.WorkshopId);
        var over = r.Workshop.Capacity.HasValue && active > r.Workshop.Capacity.Value;
        return new ParticipantChange(true, Name: r.FullName, OverCapacity: over, Active: active, Capacity: r.Workshop.Capacity);
    }

    /// <summary>Workshops (not completed) with more active participants than seats — shown to administrators as alerts.</summary>
    public async Task<List<(int Id, string Name, int Active, int Capacity)>> OverCapacityAsync()
    {
        var rows = await _db.Workshops.AsNoTracking()
            .Where(w => w.Status != WorkshopStatus.Completed && w.Capacity != null)
            .Select(w => new { w.Id, w.Name, Capacity = w.Capacity!.Value, Active = w.Registrations.Count(r => !r.IsCancelled) })
            .Where(x => x.Active > x.Capacity)
            .OrderBy(x => x.Name)
            .ToListAsync();
        return rows.Select(x => (x.Id, x.Name, x.Active, x.Capacity)).ToList();
    }

    public Task<Workshop?> FindByTokenAsync(string token) =>
        _db.Workshops.Include(w => w.Hall)
            .FirstOrDefaultAsync(w => w.RegistrationToken == token);

    /// <summary>Active (not cancelled) registrations: the seats taken.</summary>
    public async Task<int> CountRegistrationsAsync(int workshopId) =>
        await _db.Registrations.CountAsync(r => r.WorkshopId == workshopId && !r.IsCancelled);

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

            // A cancelled registration still blocks the same person (email or phone) from registering again.
            if (await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.IsCancelled && (r.Email == email || r.Phone == phone)))
            {
                return RegisterOutcome.CancelledBefore;
            }

            if (await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.Email == email))
            {
                return RegisterOutcome.Duplicate;
            }

            if (await _db.Registrations.AnyAsync(r => r.WorkshopId == current.Id && r.Phone == phone))
            {
                return RegisterOutcome.DuplicatePhone;
            }

            // Cancelled registrations free their seats.
            if (current.Capacity.HasValue && await _db.Registrations.CountAsync(r => r.WorkshopId == current.Id && !r.IsCancelled) >= current.Capacity.Value)
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
