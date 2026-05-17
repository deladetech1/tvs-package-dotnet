using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Notification;

public interface INotificationService
{
    Task<Respons<NotificationEmailServiceReadDto>> SendEmailAsync(NotificationEmailServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<NotificationSMSServiceReadDto>> SendSmsAsync(NotificationSMSServiceWriteDto data, CancellationToken cancellationToken = default);
}
