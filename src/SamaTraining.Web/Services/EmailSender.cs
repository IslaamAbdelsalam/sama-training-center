using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace SamaTraining.Web.Services;

/// <summary>SMTP settings (section "Smtp"). Leave Host empty to turn e-mail off.</summary>
public class SmtpSettings
{
    public const string Section = "Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "SAMA Training Center";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress)
        && !FromAddress.Contains("PASTE_", StringComparison.Ordinal)
        && !(Password ?? "").Contains("PASTE_", StringComparison.Ordinal);
}

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<SmtpSettings> _monitor;

    // IOptionsMonitor: edits to private/smtp.json apply without restarting the site.
    public SmtpEmailSender(IOptionsMonitor<SmtpSettings> settings) => _monitor = settings;

    private SmtpSettings _settings => _monitor.CurrentValue;

    public bool IsConfigured => _settings.IsConfigured;

    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        if (!IsConfigured) throw new InvalidOperationException("E-mail is not configured (Smtp:Host and Smtp:FromAddress).");

        using var message = new MailMessage
        {
            From = new MailAddress(_settings.FromAddress!, _settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        using var client = new SmtpClient(_settings.Host, _settings.Port) { EnableSsl = _settings.EnableSsl };
        if (!string.IsNullOrEmpty(_settings.UserName))
        {
            client.Credentials = new NetworkCredential(_settings.UserName, _settings.Password);
        }

        await client.SendMailAsync(message);
    }
}
