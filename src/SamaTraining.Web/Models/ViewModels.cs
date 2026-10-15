using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SamaTraining.Web.Data;

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

// ---------- Dashboard ----------

public class DashboardViewModel
{
    public int Pending { get; set; }
    public int Ongoing { get; set; }
    public int Completed { get; set; }
    public int OpenRegistrations { get; set; }
    public int TotalRegistrations { get; set; }

    /// <summary>Expense totals per currency, e.g. SDG → 1,200,000; EGP → 35,000.</summary>
    public Dictionary<string, decimal> ExpensesByCurrency { get; set; } = new();
    public bool ShowExpenses { get; set; }
    public List<Workshop> Upcoming { get; set; } = new();
}

// ---------- Settings ----------

public class ExpenseCategoryFormModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Category name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Description { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;
}

public class HallFormModel
{
    public int Id { get; set; }

    [Required, StringLength(100), Display(Name = "Hall name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Location { get; set; }

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

    [Required, StringLength(200), Display(Name = "Workshop name")]
    public string Name { get; set; } = string.Empty;

    [Required, DataType(DataType.Date), Display(Name = "Date")]
    public DateTime? Date { get; set; }

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

    [StringLength(1000), Display(Name = "Short description")]
    public string? Description { get; set; }

    [Display(Name = "Content / agenda")]
    public string? Content { get; set; }

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
    public WorkshopStatus? NextStatus { get; set; }
    public List<WorkshopExpense> Expenses { get; set; } = new();
    public List<Registration> Registrations { get; set; } = new();
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

// ---------- Public registration ----------

public class PublicRegistrationViewModel
{
    public string Token { get; set; } = string.Empty;
    public string Lang { get; set; } = "en";
    public Workshop? Workshop { get; set; }
    public int? SeatsLeft { get; set; }

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
