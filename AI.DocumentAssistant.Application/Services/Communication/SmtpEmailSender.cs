using System.IO;
using System.Net.Sockets;
using AI.DocumentAssistant.Application.Abstractions.Communication;
using AI.DocumentAssistant.Application.Common.Exceptions;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace AI.DocumentAssistant.Application.Services.Communication
{
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(
            IOptions<SmtpOptions> options,
            ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(
            string toEmail,
            string subject,
            string htmlBody,
            CancellationToken cancellationToken)
        {
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < _options.MaxRetryAttempts)
            {
                attempt++;

                try
                {
                    using var client = new SmtpClient();

                    var message = CreateMessage(toEmail, subject, htmlBody);

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(_options.ConnectTimeoutMilliseconds + _options.OperationTimeoutMilliseconds);

                    await client.ConnectAsync(
                        _options.Host,
                        _options.Port,
                        _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
                        timeoutCts.Token);

                    await client.AuthenticateAsync(
                        _options.UserName,
                        _options.Password,
                        timeoutCts.Token);

                    await client.SendAsync(message, timeoutCts.Token);

                    await client.DisconnectAsync(true, timeoutCts.Token);

                    _logger.LogInformation(
                        "Email sent successfully to {Email} on attempt {Attempt}",
                        toEmail,
                        attempt);

                    return;
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < _options.MaxRetryAttempts)
                {
                    lastException = ex;

                    var delay = TimeSpan.FromMilliseconds(
                        _options.RetryBaseDelayMilliseconds * Math.Pow(2, attempt - 1));

                    _logger.LogWarning(
                        ex,
                        "Transient SMTP failure while sending email to {Email} on attempt {Attempt}. Retrying in {DelayMs} ms",
                        toEmail,
                        attempt,
                        (int)delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }
                catch (MailKit.Security.AuthenticationException ex)
                {
                    _logger.LogError(
                        ex,
                        "SMTP authentication failed for configured account {UserName}",
                        _options.UserName);

                    throw new ServiceUnavailableException("Email delivery is temporarily unavailable.");
                }
                catch (MailKit.CommandException ex)
                {
                    _logger.LogError(
                        ex,
                        "SMTP command failed permanently while sending email to {Email}",
                        toEmail);

                    throw new ServiceUnavailableException("Email delivery is temporarily unavailable.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SMTP email send failed for {Email}", toEmail);
                    throw new ServiceUnavailableException("Email delivery is temporarily unavailable.");
                }
            }

            _logger.LogError(
                lastException,
                "SMTP email send failed after all retries for {Email}",
                toEmail);

            throw new ServiceUnavailableException("Email delivery is temporarily unavailable.");
        }

        private MimeMessage CreateMessage(string toEmail, string subject, string htmlBody)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };

            message.Body = bodyBuilder.ToMessageBody();

            return message;
        }

        private static bool IsTransient(Exception ex)
        {
            return ex switch
            {
                SocketException => true,
                IOException => true,
                TimeoutException => true,
                MailKit.ServiceNotConnectedException => true,
                MailKit.ServiceNotAuthenticatedException => false,
                MailKit.ProtocolException => true,
                MailKit.CommandException commandException => IsTransientSmtpStatusCode(commandException),
                _ => false
            };
        }

        private static bool IsTransientSmtpStatusCode(MailKit.CommandException ex)
        {
            return ex switch
            {
                MailKit.Net.Smtp.SmtpCommandException smtpEx => smtpEx.StatusCode is
                    MailKit.Net.Smtp.SmtpStatusCode.MailboxBusy or
                    MailKit.Net.Smtp.SmtpStatusCode.InsufficientStorage or
                    MailKit.Net.Smtp.SmtpStatusCode.TransactionFailed or
                    MailKit.Net.Smtp.SmtpStatusCode.ServiceNotAvailable,
                _ => false
            };
        }
    }
}