namespace SamaTraining.Web.Security;

/// <summary>
/// Fine-grained permissions. They are stored as role claims, so an administrator
/// can change what each role may do from the Roles screen (US-18).
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    public const string DashboardView = "Dashboard.View";
    public const string SettingsManage = "Settings.Manage";
    public const string WorkshopsView = "Workshops.View";
    public const string WorkshopsManage = "Workshops.Manage";
    public const string WorkshopsDelete = "Workshops.Delete";
    public const string WorkshopsChangeStatus = "Workshops.ChangeStatus";
    public const string RegistrationsManage = "Registrations.Manage";
    public const string RegistrationsView = "Registrations.View";
    public const string ExpensesView = "Expenses.View";
    public const string ExpensesManage = "Expenses.Manage";
    public const string ReportsView = "Reports.View";
    public const string UsersManage = "Users.Manage";

    public record PermissionInfo(string Key, string Group, string Description);

    public static readonly IReadOnlyList<PermissionInfo> All = new List<PermissionInfo>
    {
        new(DashboardView, "General", "View the dashboard"),
        new(SettingsManage, "Settings", "Manage expense categories and halls"),
        new(WorkshopsView, "Workshops", "View workshops"),
        new(WorkshopsManage, "Workshops", "Create and edit workshops"),
        new(WorkshopsDelete, "Workshops", "Delete pending workshops"),
        new(WorkshopsChangeStatus, "Workshops", "Change workshop status"),
        new(RegistrationsManage, "Registrations", "Open/close registration and share the link"),
        new(RegistrationsView, "Registrations", "View registered students"),
        new(ExpensesView, "Expenses", "View workshop expenses"),
        new(ExpensesManage, "Expenses", "Add, edit and delete expenses"),
        new(ReportsView, "Reports", "View reports and export them"),
        new(UsersManage, "Administration", "Manage users, roles and permissions"),
    };

    public static bool IsValid(string permission) => All.Any(p => p.Key == permission);
}

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Coordinator = "Workshop Coordinator";
    public const string Finance = "Finance Officer";

    /// <summary>Default permissions applied when a role is first created.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Defaults = new Dictionary<string, string[]>
    {
        [Administrator] = Permissions.All.Select(p => p.Key).ToArray(),
        [Coordinator] = new[]
        {
            Permissions.DashboardView, Permissions.WorkshopsView, Permissions.WorkshopsManage,
            Permissions.WorkshopsChangeStatus, Permissions.RegistrationsManage, Permissions.RegistrationsView,
            Permissions.ExpensesView, Permissions.ReportsView
        },
        [Finance] = new[]
        {
            Permissions.DashboardView, Permissions.WorkshopsView, Permissions.ExpensesView, Permissions.ExpensesManage, Permissions.ReportsView
        },
    };
}
