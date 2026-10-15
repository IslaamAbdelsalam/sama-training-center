using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SamaTraining.Web.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Workshop> Workshops => Set<Workshop>();
    public DbSet<WorkshopStatusHistory> WorkshopStatusHistory => Set<WorkshopStatusHistory>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<WorkshopExpense> WorkshopExpenses => Set<WorkshopExpense>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ExpenseCategory>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<Hall>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<Workshop>(e =>
        {
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.RegistrationToken).IsUnique().HasFilter("[RegistrationToken] IS NOT NULL");
            e.HasIndex(x => x.StartDateTime);
            e.HasOne(x => x.Hall).WithMany(h => h.Workshops).HasForeignKey(x => x.HallId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkshopStatusHistory>(e =>
        {
            e.Property(x => x.FromStatus).HasConversion<int>();
            e.Property(x => x.ToStatus).HasConversion<int>();
            e.HasOne(x => x.Workshop).WithMany(w => w.StatusHistory).HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Registration>(e =>
        {
            // US-13: one registration per email per workshop.
            e.HasIndex(x => new { x.WorkshopId, x.Email }).IsUnique();
            e.HasOne(x => x.Workshop).WithMany(w => w.Registrations).HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WorkshopExpense>(e =>
        {
            e.HasOne(x => x.Workshop).WithMany(w => w.Expenses).HasForeignKey(x => x.WorkshopId).OnDelete(DeleteBehavior.Restrict);
            // US-01: a category in use cannot be deleted.
            e.HasOne(x => x.ExpenseCategory).WithMany(c => c.Expenses).HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
