using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Options;
using ORSV2.Models;
using System.Net;
using System.Net.Mail;

namespace ORSV2.Services
{
    public class EmailSender : IEmailSender
    {
        private readonly SmtpSettings _smtpSettings;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IOptions<SmtpSettings> smtpSettings, ILogger<EmailSender> logger)
        {
            _smtpSettings = smtpSettings.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrEmpty(_smtpSettings.Host))
            {
                _logger.LogWarning("SMTP host not configured. Email not sent to {Email}", email);
                return;
            }

            try
            {
                // Check if this is a confirmation email and enhance it
                if (subject.Contains("Confirm your email") || subject.Contains("Confirm your account"))
                {
                    htmlMessage = CreateConfirmationEmailTemplate(htmlMessage);
                }
                else if (subject.Contains("Reset your password"))
                {
                    htmlMessage = CreatePasswordResetEmailTemplate(htmlMessage);
                }

                using var client = new SmtpClient(_smtpSettings.Host, _smtpSettings.Port)
                {
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(_smtpSettings.Username, _smtpSettings.Password),
                    EnableSsl = _smtpSettings.EnableSsl
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_smtpSettings.Username, "ORSV2 Team"),
                    Subject = subject,
                    Body = htmlMessage,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(email);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation("Email sent successfully to {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", email);
                throw;
            }
        }

        private string CreateConfirmationEmailTemplate(string originalMessage)
        {
            // Extract the confirmation link from the original message
            var linkStart = originalMessage.IndexOf("href='") + 6;
            var linkEnd = originalMessage.IndexOf("'", linkStart);
            var confirmationUrl = originalMessage.Substring(linkStart, linkEnd - linkStart);

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Confirm Your Account - ORSV2</title>
    <link rel='preconnect' href='https://fonts.googleapis.com'>
    <link rel='preconnect' href='https://fonts.gstatic.com' crossorigin>
    <link href='https://fonts.googleapis.com/css2?family=Poppins:wght@400;600&display=swap' rel='stylesheet'>
    <style>
        body {{
            margin: 0;
            padding: 0;
            background-color: #f8f9fb;
            font-family: 'Poppins', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            -webkit-font-smoothing: antialiased;
            -moz-osx-font-smoothing: grayscale;
        }}
        .email-wrapper {{
            background-color: #f8f9fb;
            padding: 20px;
        }}
        .email-container {{
            max-width: 600px;
            margin: 0 auto;
            background: #ffffff;
            border-radius: 12px;
            overflow: hidden;
            border: 1px solid #e9ecef;
            box-shadow: 0 4px 20px rgba(0,34,197,.15);
        }}
        .header {{
            background: linear-gradient(135deg, #0022C5 0%, #1a4fb8 100%);
            padding: 30px 20px;
            text-align: center;
        }}
        .header h1 {{
            color: #ffffff;
            font-size: 28px;
            font-weight: 600;
            margin: 0;
            letter-spacing: .5px;
        }}
        .content {{
            padding: 30px 40px;
            color: #495057;
            line-height: 1.7;
        }}
        .content h2 {{
            font-size: 22px;
            font-weight: 600;
            color: #0022C5;
            margin-bottom: 15px;
        }}
        .content p {{
            font-size: 16px;
            margin: 0 0 15px;
        }}
        .cta-section {{
            text-align: center;
            margin: 30px 0;
        }}
        .cta-button {{
            display: inline-block;
            background: linear-gradient(135deg, #0022C5 0%, #1a4fb8 100%);
            color: #ffffff !important;
            text-decoration: none;
            padding: 14px 28px;
            border-radius: 8px; /* Corresponds to --btn-radius */
            font-weight: 600;
            font-size: 16px; /* A readable size for email */
            box-shadow: 0 4px 12px rgba(0,34,197,.15);
            transition: all 0.2s ease;
        }}
        .fallback-link {{
            background-color: #f8f9fb;
            border: 1px dashed #e9ecef;
            padding: 15px;
            border-radius: 8px;
            margin-top: 25px;
        }}
        .fallback-link p {{
            font-size: 12px;
            color: #495057;
            margin: 0;
            line-height: 1.5;
            word-break: break-all;
        }}
        .footer {{
            background-color: #f8f9fb;
            padding: 20px 40px;
            text-align: center;
            border-top: 1px solid #e9ecef;
        }}
        .footer p {{
            color: #6c757d;
            font-size: 12px;
            margin: 0;
        }}
        @media (max-width: 600px) {{
            .content {{ padding: 25px 20px; }}
        }}
    </style>
</head>
<body>
    <div class='email-wrapper'>
        <div class='email-container'>
            <div class='header'>
                <h1>Orenda Reporting System Account Confirmation</h1>
            </div>
            <div class='content'>
                <h2>Almost there!</h2>
                <p>Thanks for signing up. Please click the button below to confirm your email address and activate your account.</p>
                <div class='cta-section'>
                    <a href='{confirmationUrl}' class='cta-button'>Confirm Your Account</a>
                </div>
                <div class='fallback-link'>
                    <p>If the button doesn't work, please copy and paste this URL into your browser:<br>
                       <a href='{confirmationUrl}' style='color: #0022C5; text-decoration: underline;'>{confirmationUrl}</a>
                    </p>
                </div>
                <p style='margin-top:20px;'>If you did not create an account, no further action is required.</p>
            </div>
            <div class='footer'>
                <p>&copy; 2025 ORSV2. All Rights Reserved.</p>
            </div>
        </div>
    </div>
</body>
</html>";
        }

        private string CreatePasswordResetEmailTemplate(string originalMessage)
        {
            // Extract the reset link from the original message
            var linkStart = originalMessage.IndexOf("href='") + 6;
            var linkEnd = originalMessage.IndexOf("'", linkStart);
            var resetUrl = originalMessage.Substring(linkStart, linkEnd - linkStart);

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Reset Your Password - ORSV2</title>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            line-height: 1.6;
            color: #333;
            background-color: #f8fafc;
        }}
        
        .email-container {{
            max-width: 600px;
            margin: 20px auto;
            background: #ffffff;
            border-radius: 16px;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.1);
            overflow: hidden;
        }}
        
        .header {{
            background: linear-gradient(135deg, #ff6b6b 0%, #ee5a24 100%);
            padding: 40px 30px;
            text-align: center;
        }}
        
        .logo h1 {{
            color: #ffffff;
            font-size: 32px;
            font-weight: 700;
            margin-bottom: 8px;
        }}
        
        .logo p {{
            color: rgba(255, 255, 255, 0.9);
            font-size: 16px;
        }}
        
        .content {{
            padding: 50px 40px;
        }}
        
        .alert-section {{
            text-align: center;
            margin-bottom: 40px;
        }}
        
        .alert-section h2 {{
            color: #2d3748;
            font-size: 28px;
            font-weight: 600;
            margin-bottom: 16px;
        }}
        
        .alert-section p {{
            color: #718096;
            font-size: 18px;
            line-height: 1.6;
        }}
        
        .cta-section {{
            text-align: center;
            margin: 40px 0;
        }}
        
        .cta-button {{
            display: inline-block;
            background: linear-gradient(135deg, #ff6b6b 0%, #ee5a24 100%);
            color: #ffffff;
            text-decoration: none;
            padding: 18px 36px;
            border-radius: 12px;
            font-weight: 600;
            font-size: 16px;
            transition: all 0.3s ease;
            box-shadow: 0 4px 15px rgba(255, 107, 107, 0.4);
        }}
        
        .cta-button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 8px 25px rgba(255, 107, 107, 0.6);
        }}
        
        .security-notice {{
            background: linear-gradient(135deg, #fff5f5 0%, #fed7d7 100%);
            border-left: 4px solid #f56565;
            padding: 20px;
            margin: 30px 0;
            border-radius: 8px;
        }}
        
        .security-notice h3 {{
            color: #2d3748;
            font-size: 16px;
            font-weight: 600;
            margin-bottom: 8px;
        }}
        
        .security-notice p {{
            color: #4a5568;
            font-size: 14px;
            margin: 0;
        }}
        
        .footer {{
            background: #f7fafc;
            padding: 30px 40px;
            text-align: center;
            border-top: 1px solid #e2e8f0;
        }}
        
        .footer p {{
            color: #718096;
            font-size: 14px;
            margin: 5px 0;
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <div class='logo'>
                <h1>ORSV2</h1>
                <p>Password Reset Request</p>
            </div>
        </div>
        
        <div class='content'>
            <div class='alert-section'>
                <h2>🔐 Reset Your Password</h2>
                <p>We received a request to reset your password. Click the button below to create a new password.</p>
            </div>
            
            <div class='cta-section'>
                <a href='{resetUrl}' class='cta-button'>
                    Reset Password
                </a>
            </div>
            
            <div class='security-notice'>
                <h3>⚠️ Security Notice</h3>
                <p>This password reset link will expire in 1 hour. If you didn't request a password reset, please ignore this email and your password will remain unchanged.</p>
            </div>
        </div>
        
        <div class='footer'>
            <p>This is an automated message from ORSV2.</p>
            <p>&copy; 2024 ORSV2. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";
        }
    }
}