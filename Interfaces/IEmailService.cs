namespace DIP.Backend.Interfaces;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
    Task SendConfirmationEmailAsync(string to, string userName, string confirmationToken, CancellationToken ct = default);
    Task SendPasswordResetEmailAsync(string to, string userName, string resetToken, CancellationToken ct = default);
}
