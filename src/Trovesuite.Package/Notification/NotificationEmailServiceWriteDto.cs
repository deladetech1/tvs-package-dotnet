using System.Text.Json.Serialization;

namespace Trovesuite.Package.Notification;

public class NotificationEmailServiceWriteDto
{
    [JsonPropertyName("sender_email")]
    public string SenderEmail { get; set; } = string.Empty;

    [JsonPropertyName("receiver_email")]
    public List<string> ReceiverEmail { get; set; } = new();

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("text_message")]
    public string TextMessage { get; set; } = string.Empty;

    [JsonPropertyName("html_message")]
    public string? HtmlMessage { get; set; }
}
