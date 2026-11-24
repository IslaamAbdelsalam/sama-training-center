using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace SamaTraining.Web.Security;

/// <summary>
/// Protection against robots, password guessing and floods:
/// per-IP request limits, stricter limits on sign-in and the public form, and quick 404s for scanner probes.
/// Limits are read from "RateLimit:*" settings so they can be tuned on the server without a new release.
/// </summary>
public static class AbuseProtection
{
    public const string Auth = "auth";
    public const string PublicForm = "public-form";
    public const string Export = "export";

    public static IServiceCollection AddAbuseProtection(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        // Configured when first used, so the limits come from the final settings (private file, environment, tests).
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration, IHostEnvironment>((o, config, env) =>
        {
            // Integration tests sign in many times from one address, so they get very high limits unless a test sets its own.
            var testing = env.IsEnvironment("Testing");
            int Limit(string key, int normal) => config.GetValue($"RateLimit:{key}", testing ? 1_000_000 : normal);

            var perMinute = Limit("PerMinute", 600);              // any page, per IP (an office shares one IP)
            var auth = Limit("AuthPer5Minutes", 20);              // sign-in / forgot / reset / change password posts, per IP
            var publicForm = Limit("PublicFormPer10Minutes", 30); // registration form posts, per IP
            var export = Limit("ExportPerMinute", 10);            // CSV downloads, per user

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
                }));

            o.AddPolicy(Auth, ctx => RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = auth, Window = TimeSpan.FromMinutes(5), QueueLimit = 0
            }));
            o.AddPolicy(PublicForm, ctx => RateLimitPartition.GetFixedWindowLimiter("form:" + ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = publicForm, Window = TimeSpan.FromMinutes(10), QueueLimit = 0
            }));
            o.AddPolicy(Export, ctx => RateLimitPartition.GetFixedWindowLimiter("export:" + (ctx.User.Identity?.Name ?? ClientKey(ctx)), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = export, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));

            o.OnRejected = async (ctx, token) =>
            {
                var seconds = ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? (int)retry.TotalSeconds : 60;
                var r = ctx.HttpContext.Response;
                r.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                r.ContentType = "text/html; charset=utf-8";
                ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimit")
                    .LogWarning("Too many requests from {Ip} to {Path}", ClientKey(ctx.HttpContext), ctx.HttpContext.Request.Path);
                await r.WriteAsync(
                    "<!doctype html><html><head><meta charset=\"utf-8\"><title>Too many requests</title></head>" +
                    "<body style=\"font-family:sans-serif;max-width:560px;margin:15vh auto;padding:0 16px;text-align:center\">" +
                    "<h2>Too many requests</h2><p>Please wait a few minutes and try again.</p>" +
                    "<h2 dir=\"rtl\">طلبات كثيرة جداً</h2><p dir=\"rtl\">يرجى الانتظار بضع دقائق ثم المحاولة مرة أخرى.</p>" +
                    "</body></html>", token);
            };
        });

        return services;
    }

    /// <summary>The caller's IP address (IPv6 grouped by /64 so one machine can't rotate through addresses).</summary>
    public static string ClientKey(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip == null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var b = ip.GetAddressBytes();
            for (var i = 8; i < 16; i++) b[i] = 0;
            ip = new IPAddress(b);
        }
        return ip.ToString();
    }

    /// <summary>Answers scanner probes (WordPress, PHP, .env, .git …) with a plain 404 before any app code runs.</summary>
    public static IApplicationBuilder UseProbeBlocker(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var p = ctx.Request.Path.Value ?? "";
        if (p.EndsWith(".php", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/wp-", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.env", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.git", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/cgi-bin", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".asp", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
}

/// <summary>
/// Robot check for the public registration form, invisible to people:
/// a hidden "website" field that only robots fill in, and a signed time stamp so instant (scripted) posts are refused.
/// </summary>
public class FormBotGuard
{
    public const string HoneypotField = "website";
    public const string StampField = "fs";

    private readonly IDataProtector _protector;
    private readonly TimeSpan _minimum;
    private static readonly TimeSpan Maximum = TimeSpan.FromHours(12);

    public FormBotGuard(IDataProtectionProvider provider, IConfiguration config, IHostEnvironment env)
    {
        _protector = provider.CreateProtector("SamaTraining.FormBotGuard.v1");
        _minimum = TimeSpan.FromSeconds(config.GetValue("RateLimit:MinFormSeconds", env.IsEnvironment("Testing") ? 0 : 3));
    }

    /// <summary>The signed time stamp put in the form when it is shown.</summary>
    public string NewStamp() => _protector.Protect(DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

    public enum Verdict { Human, Robot, Expired }

    public Verdict Check(IFormCollection form)
    {
        if (!string.IsNullOrEmpty(form[HoneypotField])) return Verdict.Robot;
        try
        {
            var ticks = long.Parse(_protector.Unprotect(form[StampField].ToString()), CultureInfo.InvariantCulture);
            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (age < _minimum) return Verdict.Robot;
            if (age > Maximum) return Verdict.Expired;
            return Verdict.Human;
        }
        catch
        {
            return Verdict.Robot; // missing or forged stamp
        }
    }
}
