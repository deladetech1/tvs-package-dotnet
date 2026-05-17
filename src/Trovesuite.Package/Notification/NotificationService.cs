using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Notification;

public sealed class NotificationService : INotificationService
{
    private readonly TrovesuiteOptions _options;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(IOptions<TrovesuiteOptions> options, ILogger<NotificationService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Respons<NotificationEmailServiceReadDto>> SendEmailAsync(NotificationEmailServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        if (data.ReceiverEmail is null || data.ReceiverEmail.Count == 0)
            return Respons<NotificationEmailServiceReadDto>.Fail("INVALID_RECIPIENT", "At least one receiver_email is required", 400);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(data.SenderEmail));
            foreach (var to in data.ReceiverEmail)
                message.To.Add(MailboxAddress.Parse(to));
            message.Subject = data.Subject;

            var bodyBuilder = new BodyBuilder
            {
                TextBody = data.TextMessage,
            };
            if (!string.IsNullOrEmpty(data.HtmlMessage))
                bodyBuilder.HtmlBody = data.HtmlMessage;
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            var secureSocket = _options.Mail.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(_options.Mail.SmtpHost, _options.Mail.SmtpPort, secureSocket, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(data.SenderEmail, data.Password, cancellationToken).ConfigureAwait(false);
            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

            return Respons<NotificationEmailServiceReadDto>.Ok(
                detail: $"Email successfully sent to {data.ReceiverEmail.Count} recipient(s)",
                statusCode: 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email");
            return Respons<NotificationEmailServiceReadDto>.Fail(ex.Message, "An error occurred while sending the email", 500);
        }
    }

    public Task<Respons<NotificationSMSServiceReadDto>> SendSmsAsync(NotificationSMSServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Respons<NotificationSMSServiceReadDto>.Fail("NOT_IMPLEMENTED", "SMS sending is not implemented", 501));
    }
}
