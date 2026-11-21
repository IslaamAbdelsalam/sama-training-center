using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace SamaTraining.Web.Data;

/// <summary>A staff member who signs in to the system.</summary>
public class ApplicationUser : IdentityUser
{
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>When true, the user is sent to the change-password page until they set their own password.</summary>
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum WorkshopType
{
    [Display(Name = "Online")]
    Online = 1,

    [Display(Name = "On-site")]
    OnSite = 2
}

public enum WorkshopStatus
{
    Pending = 1,
    Ongoing = 2,
    Completed = 3
}

/// <summary>What a workshop history entry records (US-07, US-10).</summary>
public enum WorkshopHistoryAction
{
    StatusChanged = 0,
    RegistrationOpened = 1,
    RegistrationClosed = 2,
    LinkRegenerated = 3,
    ParticipantCancelled = 4,
    ParticipantRestored = 5
}

/// <summary>How a registration fee was paid (US-18).</summary>
public enum PaymentMethod
{
    [Display(Name = "Cash")]
    Cash = 1,

    [Display(Name = "Bank transfer")]
    BankTransfer = 2,

    [Display(Name = "Mobile wallet")]
    MobileWallet = 3,

    [Display(Name = "Other")]
    Other = 4
}

/// <summary>A registration's payment position, calculated from the fee and its collections (US-18).</summary>
public enum PaymentStatus
{
    Free,
    Unpaid,
    PartiallyPaid,
    Paid
}

/// <summary>Lookup: expense categories (US-01).</summary>
public class ExpenseCategory
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Arabic name (US-03), shown when Arabic is selected.</summary>
    [MaxLength(100)]
    public string? NameAr { get; set; }

    [MaxLength(250)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public string LocalName(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;

    public ICollection<WorkshopExpense> Expenses { get; set; } = new List<WorkshopExpense>();
}

/// <summary>Workshop configuration: halls and locations (US-02).</summary>
public class Hall
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Arabic name (US-03), shown on the Arabic registration page.</summary>
    [MaxLength(100)]
    public string? NameAr { get; set; }

    [MaxLength(250)]
    public string? Location { get; set; }

    /// <summary>Arabic location / address (US-03, US-12).</summary>
    [MaxLength(250)]
    public string? LocationAr { get; set; }

    public int? Capacity { get; set; }

    public string LocalName(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;

    public string? LocalLocation(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(LocationAr) ? LocationAr : Location;

    public bool IsActive { get; set; } = true;

    public ICollection<Workshop> Workshops { get; set; } = new List<Workshop>();
}

public class Workshop
{
    public int Id { get; set; }

    /// <summary>English name, shown on the English registration page and in the staff system.</summary>
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Arabic name, shown on the Arabic registration page.</summary>
    [MaxLength(200)]
    public string? NameAr { get; set; }

    public DateTime StartDateTime { get; set; }

    public DateTime EndDateTime { get; set; }

    public WorkshopType Type { get; set; }

    /// <summary>Country where the workshop runs (e.g. "SD", "EG"). Decides the currency.</summary>
    [Required, MaxLength(2)]
    public string Country { get; set; } = "SD";

    /// <summary>Currency for this workshop's expenses (e.g. "SDG", "EGP"), set from the country.</summary>
    [Required, MaxLength(3)]
    public string CurrencyCode { get; set; } = "SDG";

    public int? HallId { get; set; }
    public Hall? Hall { get; set; }

    [MaxLength(500)]
    public string? MeetingLink { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(1000)]
    public string? DescriptionAr { get; set; }

    public string? Content { get; set; }

    public string? ContentAr { get; set; }

    /// <summary>Name in the visitor's language; falls back to the English name.</summary>
    public string LocalName(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(NameAr) ? NameAr! : Name;

    public string? LocalDescription(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(DescriptionAr) ? DescriptionAr : Description;

    public string? LocalContent(string lang) => lang == "ar" && !string.IsNullOrWhiteSpace(ContentAr) ? ContentAr : Content;

    /// <summary>Optional maximum number of registrations.</summary>
    public int? Capacity { get; set; }

    /// <summary>Registration fee per participant in the workshop's currency (US-06). Empty or 0 = free.</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Fee { get; set; }

    public bool IsFree => Fee is null or <= 0;

    /// <summary>US-05: a workshop runs on one day or over a date range (same daily hours).</summary>
    public bool IsMultiDay => EndDateTime.Date > StartDateTime.Date;

    public int Days => (EndDateTime.Date - StartDateTime.Date).Days + 1;

    public WorkshopStatus Status { get; set; } = WorkshopStatus.Pending;

    public bool RegistrationOpen { get; set; }

    /// <summary>Random token used in the public registration link. Regenerated each time registration opens.</summary>
    [MaxLength(64)]
    public string? RegistrationToken { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
    public ICollection<WorkshopExpense> Expenses { get; set; } = new List<WorkshopExpense>();
    public ICollection<WorkshopStatusHistory> StatusHistory { get; set; } = new List<WorkshopStatusHistory>();
    public ICollection<FeeCollection> Collections { get; set; } = new List<FeeCollection>();
}

/// <summary>Audit trail: status changes (US-10) and registration opening/closing (US-07).</summary>
public class WorkshopStatusHistory
{
    public int Id { get; set; }

    public int WorkshopId { get; set; }
    public Workshop? Workshop { get; set; }

    public WorkshopHistoryAction Action { get; set; } = WorkshopHistoryAction.StatusChanged;

    /// <summary>Extra detail, e.g. the participant's name for a cancellation.</summary>
    [MaxLength(300)]
    public string? Note { get; set; }

    public WorkshopStatus FromStatus { get; set; }
    public WorkshopStatus ToStatus { get; set; }

    [MaxLength(256)]
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>An external student's application through the public link (US-11..US-13).</summary>
public class Registration
{
    public int Id { get; set; }

    public int WorkshopId { get; set; }
    public Workshop? Workshop { get; set; }

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Organization { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(5)]
    public string Language { get; set; } = "en";

    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// A cancelled registration frees its seat and is left out of every count and money figure,
    /// but stays on the list (marked Cancelled) so the same person can't register again for this workshop.
    /// </summary>
    public bool IsCancelled { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(256)]
    public string? CancelledBy { get; set; }

    [MaxLength(250)]
    public string? CancelReason { get; set; }

    public ICollection<FeeCollection> Collections { get; set; } = new List<FeeCollection>();
}

/// <summary>A registration fee payment received from a participant (US-18).</summary>
public class FeeCollection
{
    public int Id { get; set; }

    public int WorkshopId { get; set; }
    public Workshop? Workshop { get; set; }

    public int RegistrationId { get; set; }
    public Registration? Registration { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>Always the workshop's currency at the time of collection.</summary>
    [Required, MaxLength(3)]
    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime CollectedOn { get; set; }

    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;

    [MaxLength(100)]
    public string? Reference { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class WorkshopExpense
{
    public int Id { get; set; }

    public int WorkshopId { get; set; }
    public Workshop? Workshop { get; set; }

    public int ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public DateTime ExpenseDate { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(256)]
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
