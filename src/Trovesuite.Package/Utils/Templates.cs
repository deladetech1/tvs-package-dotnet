namespace Trovesuite.Package.Utils;

public static class Templates
{
    public const string OtpTextTemplate =
        "Your Trovesuite OTP code is: {otp_code}\n" +
        "Please do not share this code with anyone.\n" +
        "This code will expire in 1 minute.\n" +
        "Sent at: {cdate}, {ctime}";

    public const string OtpHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px;">
    <div style="max-width: 500px; margin: auto; background: white; border-radius: 12px; padding: 25px; box-shadow: 0 2px 6px rgba(0,0,0,0.1);">
      <h2 style="color: #2e86de; text-align: center;">🔐 {message_header}</h2>
      <p style="font-size: 16px; color: #333;">Dear {user_name},</p>
      <p style="font-size: 15px; color: #333;">
        Please use the following <b>One-Time Password (OTP)</b> to complete your verification:
      </p>
      <div style="text-align: center; margin: 30px 0;">
        <div style="display: inline-block; background-color: #2e86de; color: white; padding: 15px 30px; border-radius: 8px; font-size: 28px; letter-spacing: 4px;">
          {otp_code}
        </div>
      </div>
      <p style="font-size: 14px; color: #555;">
        This code will expire in <b>1 minute</b>. Please do not share this code with anyone.
      </p>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <p style="font-size: 12px; color: #888; text-align: center;">
        Trovesuite Security System<br>
        Sent at: {cdate}, {ctime}
      </p>
    </div>
  </body>
</html>
""";

    public const string PasswordChangeHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px;">
    <div style="max-width: 500px; margin: auto; background: white; border-radius: 12px; padding: 25px; box-shadow: 0 2px 6px rgba(0,0,0,0.1);">
      <h2 style="color: #2e86de; text-align: center;">🔒 Password Change Alert</h2>
      <p style="font-size: 16px; color: #333;">Hello {user_name},</p>
      <p style="font-size: 15px; color: #333;">
        We noticed that the password for your <b>Trovesuite</b> account was recently changed.
      </p>
      <p style="font-size: 15px; color: #333;">
        If you made this change, you can safely ignore this message.
      </p>
      <p style="font-size: 15px; color: #333;">
        However, if you did <b>not</b> make this change, please take immediate action to secure your account.
      </p>
      <div style="margin-top: 30px; padding: 15px; background-color: #f1f3f6; border-radius: 8px;">
        <p style="font-size: 13px; color: #555; margin: 0;">
          Date: <b>{cdate}</b><br>
          Time: <b>{ctime}</b>
        </p>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <p style="font-size: 12px; color: #888; text-align: center;">
        Trovesuite Security System<br>
        Ensuring your data safety always.<br>
        Sent at: {cdate}, {ctime}
      </p>
    </div>
  </body>
</html>
""";

    public const string PasswordChangeTextTemplate =
        "Hello {user_name},\n\n" +
        "We wanted to let you know that the password for your Trovesuite account was just changed.\n\n" +
        "If you made this change, you can safely ignore this message.\n" +
        "If you did NOT make this change, please take immediate action to secure your account.\n\n" +
        "Date: {cdate}\n" +
        "Time: {ctime}\n" +
        "— The Trovesuite Security Team";

    public const string AccessGrantedHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px; margin: 0;">
    <div style="max-width: 600px; margin: auto; background: white; border-radius: 12px; padding: 30px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
      <div style="text-align: center; padding-bottom: 25px; border-bottom: 2px solid #2e86de;">
        <h1 style="color: #2e86de; margin: 0; font-size: 28px;">🎉 Welcome to Trovesuite!</h1>
      </div>
      <div style="padding: 25px 0;">
        <p style="font-size: 16px; color: #333; margin-bottom: 15px;">Dear <strong>{user_name}</strong>,</p>
        <p style="font-size: 15px; color: #333; line-height: 1.6;">
          Great news! You have been granted access to <strong>Trovesuite</strong>.
          You can now log in and start exploring all the features available to you.
        </p>
      </div>
      <div style="background-color: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0;">
        <h3 style="color: #2e86de; margin-top: 0; font-size: 18px; margin-bottom: 15px;">📋 Your Login Details</h3>
        <table style="width: 100%; border-collapse: collapse;">
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px; width: 40%;"><strong>App URL:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px;"><a href="{app_url}" style="color: #2e86de; text-decoration: none; font-weight: 500;">{app_url}</a></td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Email:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px;">{user_email}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Password:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px; font-family: 'Courier New', monospace; background-color: #fff; padding: 5px 10px; border-radius: 4px; display: inline-block;">{password}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Login Access:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px;">{login_type_text}</td>
          </tr>
        </table>
      </div>
      <div style="text-align: center; margin: 30px 0;">
        <a href="{app_url}" style="display: inline-block; background-color: #2e86de; color: white; padding: 14px 35px; text-decoration: none; border-radius: 8px; font-size: 16px; font-weight: 600; box-shadow: 0 2px 4px rgba(46, 134, 222, 0.3);">Login Now →</a>
      </div>
      <div style="background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 25px 0; border-radius: 4px;">
        <p style="font-size: 13px; color: #856404; margin: 0; line-height: 1.5;">
          <strong>⚠️ Security Reminder:</strong> For your security, we recommend changing your password upon your first login. Please do not share your credentials with anyone.
        </p>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <div style="text-align: center;">
        <p style="font-size: 12px; color: #888; margin: 5px 0;">Trovesuite - Empowering Your Business<br>Sent on: {cdate} at {ctime}</p>
      </div>
    </div>
  </body>
</html>
""";

    public const string AccessGrantedTextTemplate =
        "Welcome to Trovesuite!\n\n" +
        "Dear {user_name},\n\n" +
        "Great news! You have been granted access to Trovesuite.\n" +
        "You can now log in and start exploring all the features available to you.\n\n" +
        "YOUR LOGIN DETAILS:\n" +
        "==================\n" +
        "App URL: {app_url}\n" +
        "Email: {user_email}\n" +
        "Password: {password}\n" +
        "Login Access: {login_type_text}\n\n" +
        "SECURITY REMINDER:\n" +
        "For your security, we recommend changing your password upon your first login.\n" +
        "Please do not share your credentials with anyone.\n\n" +
        "— The Trovesuite Team\n" +
        "Sent on: {cdate} at {ctime}";

    public const string ResetPasswordTextTemplate =
        "Trovesuite Password Reset\n\n" +
        "Dear {user_name},\n\n" +
        "Your Trovesuite account password has been reset by an administrator.\n\n" +
        "NEW LOGIN DETAILS:\n" +
        "==================\n" +
        "App URL: {app_url}\n" +
        "Email: {user_email}\n" +
        "Temporary Password: {password}\n\n" +
        "SECURITY REMINDER:\n" +
        "Please sign in and change this temporary password immediately. " +
        "Do not share your credentials with anyone.\n\n" +
        "— The Trovesuite Team\n" +
        "Sent on: {cdate} at {ctime}";

    public const string ResetPasswordHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px; margin: 0;">
    <div style="max-width: 600px; margin: auto; background: white; border-radius: 12px; padding: 30px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
      <div style="text-align: center; padding-bottom: 25px; border-bottom: 2px solid #2e86de;">
        <h1 style="color: #2e86de; margin: 0; font-size: 26px;">🔑 Password Reset</h1>
      </div>
      <div style="padding: 25px 0;">
        <p style="font-size: 16px; color: #333; margin-bottom: 15px;">Dear <strong>{user_name}</strong>,</p>
        <p style="font-size: 15px; color: #333; line-height: 1.6;">
          Your Trovesuite account password has been reset by an administrator. Use the credentials below to sign in.
        </p>
      </div>
      <div style="background-color: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0;">
        <h3 style="color: #2e86de; margin-top: 0; font-size: 18px; margin-bottom: 15px;">📋 New Login Details</h3>
        <table style="width: 100%; border-collapse: collapse;">
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px; width: 35%;"><strong>App URL:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px;"><a href="{app_url}" style="color: #2e86de; text-decoration: none; font-weight: 500;">{app_url}</a></td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Email:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px;">{user_email}</td>
          </tr>
          <tr>
            <td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Temporary Password:</strong></td>
            <td style="padding: 8px 0; color: #333; font-size: 14px; font-family: 'Courier New', monospace; background-color: #fff; padding: 5px 10px; border-radius: 4px; display: inline-block;">{password}</td>
          </tr>
        </table>
      </div>
      <div style="background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 15px; margin: 25px 0; border-radius: 4px;">
        <p style="font-size: 13px; color: #856404; margin: 0; line-height: 1.5;">
          <strong>⚠️ Important:</strong> For security reasons, please change this temporary password immediately after logging in.
        </p>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <div style="text-align: center;">
        <p style="font-size: 12px; color: #888; margin: 5px 0;">This is an automated notification from Trovesuite</p>
        <p style="font-size: 12px; color: #888; margin: 5px 0;">Trovesuite - Empowering Your Business<br>Sent on: {cdate} at {ctime}</p>
      </div>
    </div>
  </body>
</html>
""";

    public const string ResourceDeletionHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px; margin: 0;">
    <div style="max-width: 600px; margin: auto; background: white; border-radius: 12px; padding: 30px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
      <div style="text-align: center; padding-bottom: 25px; border-bottom: 2px solid #dc3545;">
        <h1 style="color: #dc3545; margin: 0; font-size: 28px;">🗑️ Resource Deletion Notice</h1>
      </div>
      <div style="padding: 25px 0;">
        <p style="font-size: 16px; color: #333; margin-bottom: 15px;">Dear <strong>{admin_name}</strong>,</p>
        <p style="font-size: 15px; color: #333; line-height: 1.6;">
          This is to notify you that a resource has been deleted in your Trovesuite account.
        </p>
      </div>
      <div style="background-color: #fff3cd; border-left: 4px solid #ffc107; padding: 20px; margin: 20px 0; border-radius: 4px;">
        <h3 style="color: #856404; margin-top: 0; font-size: 18px; margin-bottom: 15px;">📋 Deletion Details</h3>
        <table style="width: 100%; border-collapse: collapse;">
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px; width: 35%;"><strong>Resource Type:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{resource_type}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Resource Name:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{resource_name}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Deleted By:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{deleted_by_name} ({deleted_by_email})</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Deletion Time:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{cdate} at {ctime}</td></tr>
          <tr style="{message_row_style}"><td style="padding: 8px 0; color: #666; font-size: 14px; vertical-align: top;"><strong>Message:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{message}</td></tr>
        </table>
      </div>
      <div style="background-color: #f8f9fa; padding: 15px; margin: 25px 0; border-radius: 4px; border-left: 4px solid #2e86de;">
        <p style="font-size: 13px; color: #333; margin: 0; line-height: 1.5;">
          <strong>ℹ️ Note:</strong> This resource has been soft-deleted and can be restored if needed.
          Please contact the person who deleted it or log in to your account to review this action.
        </p>
      </div>
      <div style="text-align: center; margin: 30px 0;">
        <a href="{app_url}" style="display: inline-block; background-color: #2e86de; color: white; padding: 14px 35px; text-decoration: none; border-radius: 8px; font-size: 16px; font-weight: 600; box-shadow: 0 2px 4px rgba(46, 134, 222, 0.3);">Login to Review →</a>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <div style="text-align: center;">
        <p style="font-size: 12px; color: #888; margin: 5px 0;">This is an automated notification from Trovesuite</p>
        <p style="font-size: 12px; color: #888; margin: 5px 0;">Trovesuite - Empowering Your Business<br>Sent on: {cdate} at {ctime}</p>
      </div>
    </div>
  </body>
</html>
""";

    public const string ResourceDeletionTextTemplate =
        "Resource Deletion Notice\n\n" +
        "Dear {admin_name},\n\n" +
        "This is to notify you that a resource has been deleted in your Trovesuite account.\n\n" +
        "DELETION DETAILS:\n" +
        "==================\n" +
        "Resource Type: {resource_type}\n" +
        "Resource Name: {resource_name}\n" +
        "Deleted By: {deleted_by_name} ({deleted_by_email})\n" +
        "Deletion Time: {cdate} at {ctime}\n" +
        "{message_text}" +
        "\n" +
        "NOTE:\n" +
        "This resource has been soft-deleted and can be restored if needed.\n" +
        "Please contact the person who deleted it or log in to your account to review this action.\n\n" +
        "Login: {app_url}\n\n" +
        "— The Trovesuite Team\n" +
        "Sent on: {cdate} at {ctime}";

    public const string ResourceStatusChangeHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px; margin: 0;">
    <div style="max-width: 600px; margin: auto; background: white; border-radius: 12px; padding: 30px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
      <div style="text-align: center; padding-bottom: 25px; border-bottom: 2px solid {status_color};">
        <h1 style="color: {status_color}; margin: 0; font-size: 28px;">{status_icon} {status_title}</h1>
      </div>
      <div style="padding: 25px 0;">
        <p style="font-size: 16px; color: #333; margin-bottom: 15px;">Dear <strong>{admin_name}</strong>,</p>
        <p style="font-size: 15px; color: #333; line-height: 1.6;">{status_description}</p>
      </div>
      <div style="background-color: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0;">
        <h3 style="color: {status_color}; margin-top: 0; font-size: 18px; margin-bottom: 15px;">📋 Status Details</h3>
        <table style="width: 100%; border-collapse: collapse;">
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px; width: 35%;"><strong>Resource Type:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{resource_type}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Resource Name:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{resource_name}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Status:</strong></td><td style="padding: 8px 0; color: {status_color}; font-size: 14px; font-weight: 600;">{status_display}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Triggered By:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{actor_name} ({actor_email})</td></tr>
          <tr style="{message_row_style}"><td style="padding: 8px 0; color: #666; font-size: 14px; vertical-align: top;"><strong>Message:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{message}</td></tr>
        </table>
      </div>
      <div style="text-align: center; margin: 30px 0;">
        <a href="{app_url}" style="display: inline-block; background-color: #2e86de; color: white; padding: 14px 35px; text-decoration: none; border-radius: 8px; font-size: 16px; font-weight: 600; box-shadow: 0 2px 4px rgba(46, 134, 222, 0.3);">Login to Review →</a>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <div style="text-align: center;">
        <p style="font-size: 12px; color: #888; margin: 5px 0;">This is an automated notification from Trovesuite</p>
        <p style="font-size: 12px; color: #888; margin: 5px 0;">Trovesuite - Empowering Your Business<br>Sent on: {cdate} at {ctime}</p>
      </div>
    </div>
  </body>
</html>
""";

    public const string ResourceStatusChangeTextTemplate =
        "Resource Status Update\n\n" +
        "Dear {admin_name},\n\n" +
        "{status_description}\n\n" +
        "DETAILS:\n" +
        "========\n" +
        "Resource Type: {resource_type}\n" +
        "Resource Name: {resource_name}\n" +
        "Status: {status_display}\n" +
        "Triggered By: {actor_name} ({actor_email})\n" +
        "{message_text}" +
        "\n" +
        "Login: {app_url}\n\n" +
        "— The Trovesuite Team\n" +
        "Sent on: {cdate} at {ctime}";

    public const string AdminSignupNotificationTextTemplate =
        "Hi,\n\n" +
        "User {fullname} with Email: {email} and contact {contact} have signup for subscription {subscription_name} with Generated OTP: {otp_code} has sign up Successfully. Thank you.\n\n" +
        "Sent at: {cdate}, {ctime}";

    public const string AdminSignupNotificationHtmlTemplate = """
<html>
  <body style="font-family: Arial, sans-serif; background-color: #f8f9fa; padding: 20px;">
    <div style="max-width: 600px; margin: auto; background: white; border-radius: 12px; padding: 30px; box-shadow: 0 2px 8px rgba(0,0,0,0.1);">
      <div style="text-align: center; padding-bottom: 25px; border-bottom: 2px solid #2e86de;">
        <h1 style="color: #2e86de; margin: 0; font-size: 26px;">👤 New User Signup Notification</h1>
      </div>
      <div style="padding: 25px 0;">
        <p style="font-size: 16px; color: #333; margin-bottom: 15px;">Hi,</p>
        <p style="font-size: 15px; color: #333; line-height: 1.6;">
          User <strong>{fullname}</strong> with Email: <strong>{email}</strong> and contact <strong>{contact}</strong> have signup for subscription <strong>{subscription_name}</strong> with Generated OTP: <strong>{otp_code}</strong> has sign up Successfully. Thank you.
        </p>
      </div>
      <div style="background-color: #f8f9fa; border-radius: 8px; padding: 20px; margin: 20px 0;">
        <h3 style="color: #2e86de; margin-top: 0; font-size: 18px; margin-bottom: 15px;">📋 Signup Details</h3>
        <table style="width: 100%; border-collapse: collapse;">
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px; width: 35%;"><strong>Full Name:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{fullname}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Email:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{email}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Contact:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{contact}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Subscription:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px;">{subscription_name}</td></tr>
          <tr><td style="padding: 8px 0; color: #666; font-size: 14px;"><strong>Generated OTP:</strong></td><td style="padding: 8px 0; color: #333; font-size: 14px; font-family: 'Courier New', monospace; background-color: #fff; padding: 5px 10px; border-radius: 4px; display: inline-block;">{otp_code}</td></tr>
        </table>
      </div>
      <hr style="border: none; border-top: 1px solid #eee; margin: 25px 0;">
      <div style="text-align: center;">
        <p style="font-size: 12px; color: #888; margin: 5px 0;">Trovesuite - Empowering Your Business<br>Sent on: {cdate} at {ctime}</p>
      </div>
    </div>
  </body>
</html>
""";
}
