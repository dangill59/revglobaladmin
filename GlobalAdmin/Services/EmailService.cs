using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace GlobalAdmin.Services;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendWelcomeEmailAsync(string toEmail, string workspaceName, string resetPin, string loginUrl)
    {
        var smtpHost = _config["MailSettings:SmtpHost"];
        if (string.IsNullOrEmpty(smtpHost))
        {
            _logger.LogWarning("SMTP not configured, skipping welcome email for {Email}", toEmail);
            return false;
        }

        try
        {
            var smtpPort = _config.GetValue<int>("MailSettings:SmtpPort", 587);
            var useTls = _config.GetValue<bool>("MailSettings:UseTls", true);
            var username = _config["MailSettings:Username"];
            var password = _config["MailSettings:Password"];
            var fromEmail = _config["MailSettings:FromEmail"] ?? "noreply@scanrev.com";
            var fromName = _config["MailSettings:FromName"] ?? "ScanRev";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, fromEmail));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = $"Welcome to {workspaceName} - Set Your Password";

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = GetWelcomeEmailHtml(toEmail, workspaceName, resetPin, loginUrl),
                TextBody = GetWelcomeEmailText(toEmail, workspaceName, resetPin, loginUrl)
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            var secureSocketOptions = useTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions);

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Welcome email sent to {Email} for workspace {Workspace}", toEmail, workspaceName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send welcome email to {Email}", toEmail);
            return false;
        }
    }

    private string GetWelcomeEmailHtml(string email, string workspaceName, string resetPin, string loginUrl)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #2196F3; color: white; padding: 20px; text-align: center; }}
        .content {{ padding: 20px; background-color: #f9f9f9; }}
        .pin {{ font-size: 32px; font-weight: bold; color: #2196F3; text-align: center;
                padding: 20px; background-color: #e3f2fd; margin: 20px 0; letter-spacing: 8px; }}
        .steps {{ background-color: white; padding: 15px; margin: 15px 0; border-left: 4px solid #2196F3; }}
        .footer {{ text-align: center; padding: 20px; color: #666; font-size: 12px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Welcome to {workspaceName}</h1>
        </div>
        <div class='content'>
            <p>Hello,</p>
            <p>A workspace has been created for you on ScanRev. To get started, you'll need to set your password.</p>

            <p><strong>Your login PIN:</strong></p>
            <div class='pin'>{resetPin}</div>

            <div class='steps'>
                <p><strong>To set your password:</strong></p>
                <ol>
                    <li>Go to <a href='{loginUrl}'>{loginUrl}</a></li>
                    <li>Enter your email: <strong>{email}</strong></li>
                    <li>Enter your desired password</li>
                    <li>Enter the PIN shown above: <strong>{resetPin}</strong></li>
                    <li>Click ""Sign In""</li>
                </ol>
            </div>

            <p>If you did not expect this email, please contact support@scanrev.com</p>
        </div>
        <div class='footer'>
            <p>&copy; ScanRev - Document Management System</p>
        </div>
    </div>
</body>
</html>";
    }

    private string GetWelcomeEmailText(string email, string workspaceName, string resetPin, string loginUrl)
    {
        return $@"
Welcome to {workspaceName}

A workspace has been created for you on ScanRev. To get started, you'll need to set your password.

Your login PIN: {resetPin}

To set your password:
1. Go to {loginUrl}
2. Enter your email: {email}
3. Enter your desired password
4. Enter the PIN: {resetPin}
5. Click ""Sign In""

If you did not expect this email, please contact support@scanrev.com

--
ScanRev - Document Management System
";
    }
}
