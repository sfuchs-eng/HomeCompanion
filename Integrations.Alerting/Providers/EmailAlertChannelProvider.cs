using HomeCompanion.Alerting;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace HomeCompanion.Integrations.Alerting.Providers;

/// <summary>
/// E-mail alert provider backed by MailKit SMTP transport.
/// </summary>
public sealed class EmailAlertChannelProvider : IAlertChannelProvider
{
    private readonly AlertingIntegrationOptions _options;
    private readonly ILogger<EmailAlertChannelProvider> _logger;

    /// <inheritdoc/>
    public AlertPath Path => AlertPath.Email;

    /// <inheritdoc/>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Email.Smtp.Host);

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public EmailAlertChannelProvider(
        IOptions<AlertingIntegrationOptions> options,
        ILogger<EmailAlertChannelProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AlertPathDispatchResult> DispatchAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "email-not-configured",
                Message = "SMTP host is not configured.",
            };
        }

        var recipients = request.RecipientOverride?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? [];

        if (recipients.Length == 0 && request.Severity == AlertSeverity.Warning)
        {
            recipients = _options.Email.WarningRecipients
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (recipients.Length == 0)
        {
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "email-no-recipients",
                Message = "No recipients available for e-mail dispatch.",
            };
        }

        var smtpOptions = _options.Email.Smtp;
        var message = BuildMessage(request, recipients, smtpOptions.From, _options.Email.Templates);

        try
        {
            using var client = new SmtpClient();
            var socketOptions = smtpOptions.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(smtpOptions.Host, smtpOptions.Port, socketOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(smtpOptions.User))
                await client.AuthenticateAsync(smtpOptions.User, smtpOptions.Password, cancellationToken);

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation(
                "Sent e-mail alert with severity {Severity} to {RecipientCount} recipients.",
                request.Severity,
                recipients.Length);

            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = true,
                Message = "E-mail sent successfully.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "E-mail dispatch failed for severity {Severity}.", request.Severity);
            return new AlertPathDispatchResult
            {
                Path = Path,
                IsSuccess = false,
                ErrorCode = "email-send-failed",
                Message = ex.Message,
            };
        }
    }

    private static MimeMessage BuildMessage(
        AlertRequest request,
        IReadOnlyCollection<string> recipients,
        string fromAddress,
        EmailTemplateOptions templates)
    {
        var message = new MimeMessage();

        var fromMailbox = MailboxAddress.TryParse(fromAddress, out var parsedFrom)
            ? parsedFrom
            : new MailboxAddress("HomeCompanion", fromAddress);

        message.From.Add(fromMailbox);

        foreach (var recipient in recipients)
        {
            if (MailboxAddress.TryParse(recipient, out var mailbox))
                message.To.Add(mailbox);
        }

        message.Subject = RenderTemplate(
            request.Severity switch
            {
                AlertSeverity.Warning => templates.WarningSubject,
                AlertSeverity.Critical or AlertSeverity.Emergency => templates.CriticalSubject,
                _ => templates.InfoSubject,
            },
            request);

        var bodyText = RenderTemplate(templates.Body, request);
        message.Body = new TextPart("plain") { Text = bodyText };

        return message;
    }

    private static string RenderTemplate(string template, AlertRequest request)
    {
        var metadataText = request.Metadata is { Count: > 0 }
            ? string.Join(Environment.NewLine, request.Metadata.Select(kv => $"- {kv.Key}: {kv.Value}"))
            : "-";

        return (template ?? string.Empty)
            .Replace("{Severity}", request.Severity.ToString(), StringComparison.Ordinal)
            .Replace("{AlertKey}", request.AlertKey ?? string.Empty, StringComparison.Ordinal)
            .Replace("{MessageShort}", request.MessageShort, StringComparison.Ordinal)
            .Replace("{MessageLong}", request.MessageLong ?? string.Empty, StringComparison.Ordinal)
            .Replace("{CorrelationId}", request.CorrelationId?.ToString() ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Metadata}", metadataText, StringComparison.Ordinal);
    }
}
