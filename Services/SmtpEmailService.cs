using System.Net;
using System.Net.Mail;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using DIP.Backend.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DIP.Backend.Services;

public class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly string _baseUrl;

    public SmtpEmailService(
        IOptions<SmtpSettings> settings,
        ILogger<SmtpEmailService> logger,
        IConfiguration configuration)
    {
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = configuration["AppSettings:BaseUrl"] ?? "http://localhost:5173";
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        try
        {
            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                EnableSsl = _settings.EnableSsl
            };

            var message = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(to);

            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email sent successfully to {To}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", to);
            throw;
        }
    }

    public async Task SendConfirmationEmailAsync(string to, string userName, string confirmationToken, CancellationToken ct = default)
    {
        var confirmUrl = $"{_baseUrl}/confirm-email?email={Uri.EscapeDataString(to)}&token={confirmationToken}";
        var htmlBody = EmailTemplates.ConfirmEmail(userName, confirmUrl);
        await SendEmailAsync(to, "Confirm your email - DIP Platform", htmlBody, ct);
    }

    public async Task SendPasswordResetEmailAsync(string to, string userName, string resetToken, CancellationToken ct = default)
    {
        var resetUrl = $"{_baseUrl}/reset-password?email={Uri.EscapeDataString(to)}&token={resetToken}";
        var htmlBody = EmailTemplates.PasswordReset(userName, resetUrl);
        await SendEmailAsync(to, "Reset your password - DIP Platform", htmlBody, ct);
    }
}
