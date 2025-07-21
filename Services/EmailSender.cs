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
    <title>Confirm Your Email - ORSV2</title>
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
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 40px 30px;
            text-align: center;
            position: relative;
        }}
        
        .header::before {{
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: url('data:image/svg+xml,<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 100 100""><defs><pattern id=""grain"" width=""100"" height=""100"" patternUnits=""userSpaceOnUse""><circle cx=""50"" cy=""50"" r=""1"" fill=""rgba(255,255,255,0.1)""/></pattern></defs><rect width=""100"" height=""100"" fill=""url(%23grain)""/></svg>');
            opacity: 0.3;
        }}
        
        .logo {{
            position: relative;
            z-index: 1;
        }}
        
        .logo h1 {{
            color: #ffffff;
            font-size: 32px;
            font-weight: 700;
            margin-bottom: 8px;
            text-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
        }}
        
        .logo p {{
            color: rgba(255, 255, 255, 0.9);
            font-size: 16px;
            margin: 0;
        }}
        
        .content {{
            padding: 50px 40px;
        }}
        
        .welcome-text {{
            text-align: center;
            margin-bottom: 40px;
        }}
        
        .welcome-text h2 {{
            color: #2d3748;
            font-size: 28px;
            font-weight: 600;
            margin-bottom: 16px;
        }}
        
        .welcome-text p {{
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
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #ffffff;
            text-decoration: none;
            padding: 18px 36px;
            border-radius: 12px;
            font-weight: 600;
            font-size: 16px;
            transition: all 0.3s ease;
            box-shadow: 0 4px 15px rgba(102, 126, 234, 0.4);
        }}
        
        .cta-button:hover {{
            transform: translateY(-2px);
            box-shadow: 0 8px 25px rgba(102, 126, 234, 0.6);
        }}
        
        .security-notice {{
            background: linear-gradient(135deg, #e6fffa 0%, #f0fff4 100%);
            border-left: 4px solid #38b2ac;
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
        
        .fallback-link {{
            background: #f7fafc;
            padding: 20px;
            border-radius: 8px;
            margin: 30px 0;
        }}
        
        .fallback-link p {{
            color: #4a5568;
            font-size: 14px;
            margin-bottom: 10px;
        }}
        
        .fallback-link code {{
            background: #edf2f7;
            padding: 8px 12px;
            border-radius: 4px;
            font-size: 12px;
            word-break: break-all;
            display: block;
            color: #2d3748;
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
        
        .social-links {{
            margin: 20px 0 10px;
        }}
        
        .social-links a {{
            display: inline-block;
            margin: 0 10px;
            color: #718096;
            text-decoration: none;
        }}
        
        @media (max-width: 600px) {{
            .email-container {{
                margin: 10px;
                border-radius: 12px;
            }}
            
            .header {{
                padding: 30px 20px;
            }}
            
            .content {{
                padding: 30px 20px;
            }}
            
            .footer {{
                padding: 20px;
            }}
            
            .welcome-text h2 {{
                font-size: 24px;
            }}
            
            .welcome-text p {{
                font-size: 16px;
            }}
        }}
    </style>
</head>
<body>
    <div class='email-container'>
        <div class='header'>
            <div class='logo'>
                <h1>ORSV2</h1>
            </div>
        </div>
        
        <div class='content'>
            <div class='welcome-text'>
                <h2>Welcome aboard! 🎉</h2>
                <p>We're excited to have you join our community. Just one more step to get started.</p>
            </div>
            
            <div class='cta-section'>
                <a href='{confirmationUrl}' class='cta-button'>
                    Confirm Your Email
                </a>
            </div>
            
            <div class='security-notice'>
                <h3>🔒 Security Notice</h3>
                <p>This confirmation link will expire in 24 hours for your security. If you didn't create an account with us, please ignore this email.</p>
            </div>
            
            <div class='fallback-link'>
                <p>If the button above doesn't work, copy and paste this link into your browser:</p>
                <code>{confirmationUrl}</code>
            </div>
        </div>
        
        <div class='footer'>
            <p>This is an automated message from ORSV2.</p>
            <p>&copy; 2025 ORSV2. All rights reserved.</p>
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