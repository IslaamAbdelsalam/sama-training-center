using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SamaTraining.Web.Data;
using SamaTraining.Web.Services;

namespace SamaTraining.Web.Models;

// ---------- Account ----------

public class LoginViewModel
{
    [Required, Display(Name = "Username or email")]
    public string UserNameOrEmail { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool IsFirstLogin { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter your username or email."), Display(Name = "Username or email")]
    public string UserNameOrEmail { get; set; } = string.Empty;
}

public class SetNewPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "New password")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "The password must be at least 8 characters.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

// ---------- Dashboard ----------

public class DashboardViewModel
{
    public int Pending { get; set; }
    public int Ongoing { get; set; }
    public int Completed { get; set; }
    public int OpenRegistrations { get; set; }
    public int TotalRegistrations { get; set; }

    public bool ShowExpenses { get; set; }

    /// <summary>US-19: income, collections and profit (needs the Collections.View permission).</summary>
    public bool ShowIncome { get; set; }

    /// <summary>One block per currency (SDG, EGP). Amounts are never converted or added across currencies.</summary>
    public List<DashboardCurrencySummary> Currencies { get; set; } = new();

    public List<Workshop> Upcoming { get; set; } = new();

    /// <summary>Expense total per upcoming workshop, in that workshop's own currency.</summary>
    public Dictionary<int, decimal> UpcomingExpenses { get; set; } = new();
}

public class DashboardCurrencySummary
{
    public string Currency { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public int Workshops { get; set; }
    public int Pending { get; set; }
    public int Ongoing { get; set; }
    public int Completed { get; set; }
    public int Students { get; set; }
    public decimal TotalExpenses { get; set; }
    public decimal ExpensesThisMonth { get; set; }
    public string? TopCategory { get; set; }
    public decimal TopCategoryAmount { get; set; }

    /// <summary>US-19: expected income, collected, outstanding and net profit in this currency.</summary>
    public FinancialSummary Finance { get; set; } = new();

    /// <summary>Total expenses ÷ students, in this currency.</summary>
    public decimal? CostPerStudent => Students > 0 ? Math.Round(TotalExpenses / Students, 2) : null;
}

// ---------- Settings ----------

public class ExpenseCategoryFormModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Name (English)")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the Arabic name. It is shown when Arabic is selected."), StringLength(100), Display(Name = "الاسم (Arabic)")]
    public string? NameAr { get; set; }

    [StringLength(250)]
    public string? Description { get; set; }

    /// <summary>Number of expenses using this category; when above zero it can't be deleted (US-01).</summary>
    public int UsedBy { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class HallFormModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Hall name (English)")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the Arabic name. It is shown on the Arabic registration page."), StringLength(100), Display(Name = "اسم القاعة (Arabic)")]
    public string? NameAr { get; set; }

    [StringLength(250), Display(Name = "Location / address (English)")]
    public string? Location { get; set; }

    [StringLength(250), Display(Name = "الموقع / العنوان (Arabic)")]
    public string? LocationAr { get; set; }

    /// <summary>Number of workshops using this hall; when above zero it can't be deleted (US-01).</summary>
    public int UsedBy { get; set; }

    [Range(1, 100000)]
    public int? Capacity { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

// ---------- Workshops ----------

public class WorkshopFilterModel
{
    public string? Search { get; set; }
    public WorkshopStatus? Status { get; set; }
    public WorkshopType? Type { get; set; }
    public string? Country { get; set; }

    [DataType(DataType.Date)]
    public DateTime? From { get; set; }

    [DataType(DataType.Date)]
    public DateTime? To { get; set; }
}

public class WorkshopListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public WorkshopType Type { get; set; }
    public string Country { get; set; } = string.Empty;
    public string? HallName { get; set; }
    public WorkshopStatus Status { get; set; }
    public bool RegistrationOpen { get; set; }
    public int Registrations { get; set; }
}

public class WorkshopIndexViewModel
{
    public WorkshopFilterModel Filter { get; set; } = new();
    public List<WorkshopListItem> Items { get; set; } = new();
}

public class WorkshopFormModel
{
    public int Id { get; set; }

    [Required, StringLength(200), Display(Name = "Workshop name (English)")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the Arabic name. It is shown on the Arabic registration page."), StringLength(200), Display(Name = "اسم الورشة (Arabic)")]
    public string? NameAr { get; set; }

    /// <summary>US-05: false = single day, true = date range.</summary>
    [Display(Name = "Schedule")]
    public bool IsMultiDay { get; set; }

    /// <summary>The workshop date (single day) or the start date (date range).</summary>
    [Required(ErrorMessage = "Choose the workshop date."), DataType(DataType.Date), Display(Name = "Date")]
    public DateTime? Date { get; set; }

    /// <summary>US-05: end date of a date-range workshop.</summary>
    [DataType(DataType.Date), Display(Name = "End date")]
    public DateTime? EndDate { get; set; }

    [Required, DataType(DataType.Time), Display(Name = "Start time")]
    public TimeSpan? StartTime { get; set; }

    [Required, DataType(DataType.Time), Display(Name = "End time")]
    public TimeSpan? EndTime { get; set; }

    [Required, Display(Name = "Workshop type")]
    public WorkshopType? Type { get; set; }

    [Required(ErrorMessage = "Choose the country where the workshop runs."), Display(Name = "Country")]
    public string? Country { get; set; }

    /// <summary>True when expenses exist, so the country (and currency) can no longer change.</summary>
    public bool CountryLocked { get; set; }

    [Display(Name = "Hall / location")]
    public int? HallId { get; set; }

    [StringLength(500), Url, Display(Name = "Meeting link")]
    public string? MeetingLink { get; set; }

    [Range(1, 100000), Display(Name = "Capacity (optional)")]
    public int? Capacity { get; set; }

    /// <summary>US-06: registration fee in the workshop's currency. Empty or 0 = free.</summary>
    [Range(typeof(decimal), "0", "999999999", ErrorMessage = "The fee can't be negative."), Display(Name = "Registration fee")]
    public decimal? Fee { get; set; }

    [StringLength(1000), Display(Name = "Short description (English)")]
    public string? Description { get; set; }

    [StringLength(1000), Display(Name = "وصف مختصر (Arabic)")]
    public string? DescriptionAr { get; set; }

    [Display(Name = "Content / agenda (English)")]
    public string? Content { get; set; }

    [Display(Name = "المحتوى / المحاور (Arabic)")]
    public string? ContentAr { get; set; }

    public IEnumerable<SelectListItem> Halls { get; set; } = Enumerable.Empty<SelectListItem>();
}

public class WorkshopDetailsViewModel
{
    public Workshop Workshop { get; set; } = null!;
    public string? RegistrationUrl { get; set; }
    public int RegistrationCount { get; set; }
    public decimal TotalExpenses { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanAddExpense { get; set; }
    public bool CanCollect { get; set; }
    public WorkshopStatus? NextStatus { get; set; }

    /// <summary>US-10: the status a mistaken change can be reversed to.</summary>
    public WorkshopStatus? PreviousStatus { get; set; }

    /// <summary>US-18 / US-19: fee collections and the workshop's money summary.</summary>
    public List<FeeCollection> Collections { get; set; } = new();
    public FinancialSummary Finance { get; set; } = new();

    /// <summary>Total collected per registration id.</summary>
    public Dictionary<int, decimal> PaidByRegistration { get; set; } = new();
    public List<WorkshopExpense> Expenses { get; set; } = new();
    public List<Registration> Registrations { get; set; } = new();

    /// <summary>Phone → number of OTHER workshops this phone registered for (returning students).</summary>
    public Dictionary<string, int> EarlierWorkshopsByPhone { get; set; } = new();
    public List<WorkshopStatusHistory> History { get; set; } = new();
    public string Tab { get; set; } = "overview";
}

// ---------- Expenses ----------

public class ExpenseFormModel
{
    public int Id { get; set; }
    public int WorkshopId { get; set; }
    public string? WorkshopName { get; set; }
    public string? CurrencyCode { get; set; }

    [Required, Display(Name = "Expense type")]
    public int? ExpenseCategoryId { get; set; }

    [Required, Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Required, DataType(DataType.Date), Display(Name = "Expense date")]
    public DateTime? ExpenseDate { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public IEnumerable<SelectListItem> Categories { get; set; } = Enumerable.Empty<SelectListItem>();
}

// ---------- Fee collections (US-18) ----------

public class CollectionFormModel
{
    public int Id { get; set; }
    public int WorkshopId { get; set; }
    public string? WorkshopName { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal Fee { get; set; }

    [Required(ErrorMessage = "Choose the participant who paid."), Display(Name = "Participant")]
    public int? RegistrationId { get; set; }

    [Required, Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Required, DataType(DataType.Date), Display(Name = "Payment date")]
    public DateTime? CollectedOn { get; set; }

    [Required, Display(Name = "Payment method")]
    public PaymentMethod? Method { get; set; } = PaymentMethod.Cash;

    [StringLength(100), Display(Name = "Receipt / reference no. (optional)")]
    public string? Reference { get; set; }

    [StringLength(500), Display(Name = "Notes (optional)")]
    public string? Notes { get; set; }

    public IEnumerable<SelectListItem> Participants { get; set; } = Enumerable.Empty<SelectListItem>();
}

// ---------- Public registration ----------

public class PublicRegistrationViewModel
{
    public string Token { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public Workshop? Workshop { get; set; }

    /// <summary>form | success | closed | full | notfound</summary>
    public string State { get; set; } = "form";

    public string? Error { get; set; }
    public PublicRegistrationForm Form { get; set; } = new();
}

public class PublicRegistrationForm
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Organization { get; set; }
    public string? Notes { get; set; }
}

// ---------- Users & roles ----------

public class UserListItem
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public bool IsLockedOut { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserFormModel
{
    public string? Id { get; set; }

    [Required, StringLength(150), Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required, StringLength(50), RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Use letters, numbers, dots, dashes or underscores only."), Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Phone, Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [DataType(DataType.Password), Display(Name = "Initial password")]
    public string? Password { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    public List<string> SelectedRoles { get; set; } = new();
    public List<string> AllRoles { get; set; } = new();
}

public class ResetPasswordViewModel
{
    public string Id { get; set; } = string.Empty;
    public string? UserName { get; set; }

    [Required, DataType(DataType.Password), Display(Name = "New temporary password")]
    public string NewPassword { get; set; } = string.Empty;
}

public class RoleListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Users { get; set; }
    public int Permissions { get; set; }
}

public class RoleFormModel
{
    public string? Id { get; set; }

    [Required, StringLength(60), Display(Name = "Role name")]
    public string Name { get; set; } = string.Empty;

    public List<string> SelectedPermissions { get; set; } = new();
    public bool IsSystemAdminRole { get; set; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
