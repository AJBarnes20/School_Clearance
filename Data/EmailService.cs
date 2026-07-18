using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OnlineClearanceSystem.Data
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(
                _config["Email:SenderName"] ?? "CTUG Clearance",
                _config["Email:SenderEmail"]!));
            msg.To.Add(new MailboxAddress(toName, toEmail));
            msg.Subject = subject;
            msg.Body    = new TextPart("html") { Text = htmlBody };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"]!,
                int.Parse(_config["Email:SmtpPort"] ?? "587"),
                SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(
                _config["Email:SenderEmail"]!,
                _config["Email:Password"]!);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
        }

        // Hosting-neutral base URL. Override App:BaseUrl outside local development.
        private string LoginUrl =>
            (_config["APP_BASE_URL"] ?? _config["App:BaseUrl"] ?? "http://localhost:5183")
                .TrimEnd('/') + "/Home/Login";

        // ── Pre-built templates ───────────────────────────────────────────
        public Task SendApprovalAsync(string toEmail, string name, string role) =>
            SendAsync(toEmail, name, "Your Account Has Been Approved – CTUG Clearance",
                $@"<div style='font-family:sans-serif;max-width:560px;margin:0 auto;'>
                    <div style='background:#1a4731;padding:20px 28px;border-radius:8px 8px 0 0;'>
                        <h2 style='color:white;margin:0;font-size:18px;'>CTUG Online Clearance</h2>
                    </div>
                    <div style='background:#f9fafb;padding:28px;border:1px solid #e5e7eb;border-radius:0 0 8px 8px;'>
                        <p style='font-size:15px;color:#111;'>Hello, <strong>{name}</strong>!</p>
                        <p style='font-size:14px;color:#374151;'>
                            Your account has been <strong style='color:#16a34a;'>approved</strong> by the administrator.
                            You can now log in to the CTUG Online Clearance System as <strong>{role}</strong>.
                        </p>
                        <div style='margin:24px 0;text-align:center;'>
                            <a href='{LoginUrl}'
                               style='background:#1a4731;color:white;padding:12px 32px;border-radius:8px;
                                      text-decoration:none;font-weight:700;font-size:14px;'>
                                Log In Now
                            </a>
                        </div>
                        <p style='font-size:12px;color:#9ca3af;'>
                            If you did not register for this system, please ignore this email.
                        </p>
                    </div>
                </div>");

        public Task SendOtpAsync(string toEmail, string name, string otp) =>
            SendAsync(toEmail, name, "Your Password Change Code – CTUG Clearance",
                $@"<div style='font-family:sans-serif;max-width:560px;margin:0 auto;'>
                    <div style='background:#1a4731;padding:20px 28px;border-radius:8px 8px 0 0;'>
                        <h2 style='color:white;margin:0;font-size:18px;'>CTUG Online Clearance</h2>
                    </div>
                    <div style='background:#f9fafb;padding:28px;border:1px solid #e5e7eb;border-radius:0 0 8px 8px;'>
                        <p style='font-size:15px;color:#111;'>Hello, <strong>{name}</strong>!</p>
                        <p style='font-size:14px;color:#374151;'>
                            You requested to change your password. Use the code below to verify:
                        </p>
                        <div style='margin:24px 0;text-align:center;'>
                            <span style='background:#e8f5e9;color:#1a4731;padding:16px 36px;
                                         border-radius:8px;font-size:32px;font-weight:800;
                                         letter-spacing:8px;display:inline-block;'>{otp}</span>
                        </div>
                        <p style='font-size:13px;color:#6b7280;'>This code expires in <strong>10 minutes</strong>.</p>
                        <p style='font-size:12px;color:#9ca3af;'>
                            If you did not request this, please ignore this email.
                        </p>
                    </div>
                </div>");

        public Task SendPasswordResetAsync(string toEmail, string name, string resetUrl) =>
            SendAsync(toEmail, name, "Reset Your Password – CTUG Clearance",
                $@"<div style='font-family:sans-serif;max-width:560px;margin:0 auto;'>
                    <div style='background:#1a4731;padding:20px 28px;border-radius:8px 8px 0 0;'>
                        <h2 style='color:white;margin:0;font-size:18px;'>CTUG Online Clearance</h2>
                    </div>
                    <div style='background:#f9fafb;padding:28px;border:1px solid #e5e7eb;border-radius:0 0 8px 8px;'>
                        <p style='font-size:15px;color:#111;'>Hello, <strong>{name}</strong>!</p>
                        <p style='font-size:14px;color:#374151;'>
                            We received a request to reset your password. Click the button below to set a new password:
                        </p>
                        <div style='margin:24px 0;text-align:center;'>
                            <a href='{resetUrl}'
                               style='background:#1a4731;color:white;padding:12px 32px;border-radius:8px;
                                      text-decoration:none;font-weight:700;font-size:14px;'>
                                Reset Password
                            </a>
                        </div>
                        <p style='font-size:13px;color:#6b7280;'>This link expires in <strong>30 minutes</strong>.</p>
                        <p style='font-size:12px;color:#9ca3af;'>
                            If you did not request a password reset, please ignore this email.
                        </p>
                    </div>
                </div>");
    }
}
