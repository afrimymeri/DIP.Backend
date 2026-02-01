namespace DIP.Backend.Templates;

public static class EmailTemplates
{
    public static string ConfirmEmail(string userName, string confirmUrl) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Confirm Your Email</title>
    {Styles}
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>DIP Platform</h1>
        </div>
        <div class='content'>
            <h2>Welcome, {userName}!</h2>
            <p>Thank you for registering. Please confirm your email address by clicking the button below:</p>
            <div class='button-container'>
                <a href='{confirmUrl}' class='button'>Confirm Email</a>
            </div>
            <p class='link-text'>Or copy and paste this link into your browser:</p>
            <p class='url'>{confirmUrl}</p>
            <p class='expiry'>This link will expire in 48 hours.</p>
        </div>
        <div class='footer'>
            <p>If you didn't create an account, you can safely ignore this email.</p>
            <p>&copy; {DateTime.UtcNow.Year} DIP Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

    public static string PasswordReset(string userName, string resetUrl) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Reset Your Password</title>
    {Styles}
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>DIP Platform</h1>
        </div>
        <div class='content'>
            <h2>Password Reset Request</h2>
            <p>Hi {userName},</p>
            <p>We received a request to reset your password. Click the button below to set a new password:</p>
            <div class='button-container'>
                <a href='{resetUrl}' class='button'>Reset Password</a>
            </div>
            <p class='link-text'>Or copy and paste this link into your browser:</p>
            <p class='url'>{resetUrl}</p>
            <p class='expiry'>This link will expire in 2 hours.</p>
        </div>
        <div class='footer'>
            <p>If you didn't request a password reset, you can safely ignore this email.</p>
            <p>&copy; {DateTime.UtcNow.Year} DIP Platform. All rights reserved.</p>
        </div>
    </div>
</body>
</html>";

    private const string Styles = @"
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
            line-height: 1.6;
            color: #333;
            background-color: #f5f5f5;
        }
        .container {
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
        }
        .header {
            background-color: #4F46E5;
            color: white;
            padding: 24px;
            text-align: center;
        }
        .header h1 {
            font-size: 24px;
            font-weight: 600;
        }
        .content {
            padding: 32px 24px;
        }
        .content h2 {
            color: #1a1a1a;
            margin-bottom: 16px;
            font-size: 20px;
        }
        .content p {
            margin-bottom: 16px;
            color: #4a4a4a;
        }
        .button-container {
            text-align: center;
            margin: 32px 0;
        }
        .button {
            display: inline-block;
            padding: 14px 32px;
            background-color: #4F46E5;
            color: white !important;
            text-decoration: none;
            border-radius: 6px;
            font-weight: 600;
            font-size: 16px;
        }
        .button:hover {
            background-color: #4338CA;
        }
        .link-text {
            font-size: 14px;
            color: #666;
        }
        .url {
            font-size: 12px;
            color: #4F46E5;
            word-break: break-all;
            background-color: #f5f5f5;
            padding: 12px;
            border-radius: 4px;
        }
        .expiry {
            font-size: 14px;
            color: #888;
            font-style: italic;
        }
        .footer {
            background-color: #f9f9f9;
            padding: 24px;
            text-align: center;
            border-top: 1px solid #eee;
        }
        .footer p {
            font-size: 12px;
            color: #888;
            margin-bottom: 8px;
        }
    </style>";
}
