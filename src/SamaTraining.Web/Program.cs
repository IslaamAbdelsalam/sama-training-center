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
//   <site root>/private/appsettings.Production.json   (next to the wwwroot folder; MonsterASP keeps "private" off the web)
// Override the folder with the SAMA_SETTINGS_PATH environment variable if needed.
var privateSettingsDir = Environment.GetEnvironmentVariable("SAMA_SETTINGS_PATH")
    ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "private"));
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddJsonFile(Path.Combine(privateSettingsDir, "appsettings.Production.json"), optional: true, reloadOnChange: false);
    // E-mail (SMTP) settings in their own file, so they can be added without touching the database settings.
    builder.Configuration.AddJsonFile(Path.Combine(privateSettingsDir, "smtp.json"), optional: true, reloadOnChange: true);
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
    // Production is HTTPS only: the sign-in cookie is never sent over plain HTTP or readable by scripts.
    options.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
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

builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

// ---- Abuse protection: rate limits per IP, robot check on the public form, request size limits ----
builder.Services.AddAbuseProtection();
builder.Services.AddSingleton<FormBotGuard>();
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "SamaTraining.Csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
const long maxBody = 2 * 1024 * 1024; // 2 MB: no uploads, the largest form is a workshop with its agenda
builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Limits.MaxRequestBodySize = maxBody; k.Limits.MaxRequestHeadersTotalSize = 32 * 1024; });
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(o => o.MaxRequestBodySize = maxBody);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.ValueCountLimit = 500;
    o.ValueLengthLimit = 256 * 1024;
    o.KeyLengthLimit = 256;
    o.MultipartBodyLengthLimit = maxBody;
});

// Performance: compress pages, CSS and JS (gzip/brotli) and keep small lookups in memory.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection(SmtpSettings.Section));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// US-11: password reset links expire after this many minutes (default 60).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
    o.TokenLifespan = TimeSpan.FromMinutes(builder.Configuration.GetValue("App:PasswordResetMinutes", 60)));
builder.Services.AddSingleton<PublicText>();
builder.Services.AddSingleton<PhoneNumberService>();

// Write Arabic text as-is in the HTML instead of escaping every character.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

// Arabic / English staff screens: one in-memory dictionary for views, validation messages and [Display] names.
builder.Services.AddSingleton<UiText>();
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, UiStringLocalizerFactory>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
    options.ModelMetadataDetailsProviders.Add(new DefaultValidationMessages());
    var m = options.ModelBindingMessageProvider;
    m.SetValueMustNotBeNullAccessor(_ => UiText.Get("This field is required."));
    m.SetAttemptedValueIsInvalidAccessor((v, f) => UiText.Get("The value is not valid."));
    m.SetValueIsInvalidAccessor(_ => UiText.Get("The value is not valid."));
    m.SetValueMustBeANumberAccessor(_ => UiText.Get("Enter a number."));
    m.SetMissingKeyOrValueAccessor(() => UiText.Get("This field is required."));
})
.AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(UiText)));

var app = builder.Build();

// No password is ever kept in the code or GitHub: when none is configured, the first admin's temporary password
// is generated and written to the private folder (outside the website).
await InitializeDatabaseAsync(app, app.Environment.IsEnvironment("Testing") ? null : Path.Combine(privateSettingsDir, "first-admin-password.txt"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseProbeBlocker();
app.UseHttpsRedirection();
app.UseResponseCompression();
// Browsers keep theme files for 30 days; files linked with ?v=... (asp-append-version) for a year, since the link changes when the file does.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var versioned = ctx.Context.Request.Query.ContainsKey("v");
        ctx.Context.Response.Headers.CacheControl = versioned ? "public,max-age=31536000,immutable" : "public,max-age=2592000";
    }
});

app.UseMiddleware<UiLanguageMiddleware>();
app.UseRouting();
app.UseRateLimiter();

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

static async Task InitializeDatabaseAsync(WebApplication app, string? adminPasswordFile)
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
    seed.PasswordFile ??= adminPasswordFile;
    await DbSeeder.SeedAsync(services, seed, logger);
}

/// <summary>Exposed so integration tests can start the app.</summary>
public partial class Program
{
}
