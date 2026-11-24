namespace SamaTraining.Web.Security;

/// <summary>
/// Browser-side protection added to every response:
/// - Content-Security-Policy: scripts only from this site (no inline or injected scripts), no framing, forms post only here.
/// - No sniffing, no referrer leaks, no camera/microphone/location, and no search-engine indexing (staff system).
/// </summary>
public class SecurityHeadersMiddleware
{
    // Styles allow 'unsafe-inline' because the theme and editors set style attributes; scripts never do.
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "upgrade-insecure-requests";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["Content-Security-Policy"] = Csp;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            h["Cross-Origin-Resource-Policy"] = "same-origin";
            h["X-Robots-Tag"] = "noindex, nofollow";
            h.Remove("Server");
            h.Remove("X-Powered-By");

            // Pages with personal or financial data are never stored by browsers or proxies.
            var type = context.Response.ContentType ?? "";
            if (type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) || type.StartsWith("text/csv", StringComparison.OrdinalIgnoreCase))
            {
                h.CacheControl = "no-store, max-age=0";
                h.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        return _next(context);
    }
}
