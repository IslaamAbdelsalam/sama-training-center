using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SamaTraining.Web.Data;
using SamaTraining.Web.Security;
using SamaTraining.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- Private server settings (production) ----
// Secrets (database connection string, first admin password) live in a file on the server,
// OUTSIDE the website folder, so they are never in GitHub and survive every redeploy:
//   <site root>/settings/appsettings.Production.json   (next to the wwwroot folder)
// Override the folder with the SAMA_SETTINGS_PATH environment variable if needed.
var privateSettingsDir = Environment.GetEnvironmentVariable("SAMA_SETTINGS_PATH")
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "settings"));
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile(Path.Combine(privateSettingsDir, "appsettings.Production.json"), optional: true, reloadOnChange: false);
}

// Keep login cookies valid across app restarts on shared hosting (keys stored beside the settings file).
if (builder.Environment.IsProduction())
{
    try
    {
        var keysDir = Directory.CreateDirectory(Path.Combine(privateSettingsDir, "keys"));
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(keysDir)
            .SetApplicationName("SamaTraining");
    }
    catch (Exception)
    {
        // Folder not writable: fall back to the default key storage.
    }
}

builder.Services.Configure<AppSettings>(builder.Configuration.GetSection(AppSettings.Section));
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.Section));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

// US-17..US-20: secure authentication, lockout and password policy.
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<ActiveUserSignInManager>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "SamaTraining.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(
        builder.Configuration.GetValue("App:SessionTimeoutMinutes", 30));
    options.SlidingExpiration = true;
});

// Re-check the user (deactivation, role or permission changes) every minute.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization(options =>
{
    // Every page needs a signed-in user unless it is marked [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddScoped<WorkshopService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddSingleton<PublicText>();
builder.Services.AddSingleton<PhoneNumberService>();

// Write Arabic text as-is in the HTML instead of escaping every character.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

await InitializeDatabaseAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "public-registration",
    pattern: "register/{token}",
    defaults: new { controller = "PublicRegistration", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var config = services.GetRequiredService<IConfiguration>();

    var db = services.GetRequiredService<ApplicationDbContext>();
    // "Migrate" (default): apply EF Core migrations. "EnsureCreated": create the schema
    // directly (used by tests). "None": the database is managed by hand.
    switch (config.GetValue("Database:Mode", "Migrate"))
    {
        case "EnsureCreated":
            await db.Database.EnsureCreatedAsync();
            break;
        case "None":
            break;
        default:
            if (db.Database.GetMigrations().Any())
            {
                await db.Database.MigrateAsync();
            }
            else
            {
                await db.Database.EnsureCreatedAsync();
            }
            break;
    }

    var seed = config.GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new SeedOptions();
    await DbSeeder.SeedAsync(services, seed, logger);
}

/// <summary>Exposed so integration tests can start the app.</summary>
public partial class Program
{
}
