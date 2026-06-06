using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize(Roles = "Staff")]
    public class StaffController : Controller
    {
        private readonly IConfiguration _config;

        public StaffController(IConfiguration config)
        {
            _config = config;
        }

        // ── Dashboard ─────────────────────────────────────────────────────
        public IActionResult Dashboard()
        {
            var userId    = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var firstName = User.FindFirst("FirstName")?.Value ?? "";
            var lastName  = User.FindFirst("LastName")?.Value  ?? "";

            var model = new StaffDashboardViewModel
            {
                StaffName     = $"{firstName} {lastName}".Trim(),
                Announcements = new List<AnnouncementItem>()
            };

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                int.TryParse(Request.Cookies["StaffPeriodId"], out var cookiePid);
                var (staffPid, periodLabel) = ResolvePeriod(conn, cookiePid > 0 ? cookiePid : (int?)null);
                model.ActivePeriod = periodLabel;

                var appCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Cleared'
                      AND (@pid = 0 OR co.period_id = @pid)", conn);
                appCmd.Parameters.AddWithValue("@uid", userId);
                appCmd.Parameters.AddWithValue("@pid", staffPid);
                model.Approved = Convert.ToInt32(appCmd.ExecuteScalar() ?? 0);

                var penCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Pending'
                      AND (@pid = 0 OR co.period_id = @pid)", conn);
                penCmd.Parameters.AddWithValue("@uid", userId);
                penCmd.Parameters.AddWithValue("@pid", staffPid);
                model.Pending = Convert.ToInt32(penCmd.ExecuteScalar() ?? 0);

                var decCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Declined'
                      AND (@pid = 0 OR co.period_id = @pid)", conn);
                decCmd.Parameters.AddWithValue("@uid", userId);
                decCmd.Parameters.AddWithValue("@pid", staffPid);
                model.Declined = Convert.ToInt32(decCmd.ExecuteScalar() ?? 0);
                model.TotalRequests = model.Approved + model.Pending + model.Declined;

                LoadAnnouncements(conn, model.Announcements);
            }
            catch { }

            return View(model);
        }

        // ── Signatories ───────────────────────────────────────────────────
        public IActionResult Signatories(int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<SignatoryViewModel>();

            // ── Resolve & persist the active period ───────────────────────
            int pid = 0;
            string periodLabel = "—";

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!periodId.HasValue || periodId.Value <= 0)
                { int.TryParse(Request.Cookies["StaffPeriodId"], out var cp); if (cp > 0) periodId = cp; }

                (pid, periodLabel) = ResolvePeriod(conn, periodId);
                if (pid > 0) Response.Cookies.Append("StaffPeriodId", pid.ToString(),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
                ViewData["ActivePeriodId"] = pid;
                ViewData["ActivePeriod"]   = periodLabel;

                // ── Pending requests ──────────────────────────────────────
                var cmd = new MySqlCommand(@"
                    SELECT
                        co.id                                                   AS Id,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                        stu.student_number                                      AS StudentId,
                        COALESCE(
                            CONCAT(c.course_code, '-', cu.year_level, cu.section),
                            '—'
                        )                                                       AS Course,
                        COALESCE(co.status, 'Pending')                         AS Status,
                        co.position                                             AS Position,
                        co.requested_at                                         AS RequestedAt
                    FROM clearance_organization co
                    JOIN organizations  o   ON o.position_title  = co.position
                    JOIN users          stu ON stu.student_number = co.student_number
                    LEFT JOIN curriculum cu ON cu.id             = stu.curriculum_id
                    LEFT JOIN courses    c  ON c.id              = cu.course_id
                    WHERE o.user_id = @uid
                      AND (@pid = 0 OR co.period_id = @pid)
                      AND co.status = 'Pending'
                    ORDER BY co.id", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@pid", pid);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new SignatoryViewModel
                    {
                        Id          = r.GetInt32("Id"),
                        StudentName = r.GetString("StudentName"),
                        StudentId   = r.IsDBNull(r.GetOrdinal("StudentId"))   ? "—" : r.GetString("StudentId"),
                        Course      = r.IsDBNull(r.GetOrdinal("Course"))      ? "—" : r.GetString("Course"),
                        Status      = r.GetString("Status"),
                        Position    = r.IsDBNull(r.GetOrdinal("Position"))    ? "" : r.GetString("Position"),
                        RequestedAt = r.IsDBNull(r.GetOrdinal("RequestedAt")) ? null : r.GetDateTime("RequestedAt")
                    });
                }
            }
            catch { }

            // ── Signed clearances (Cleared / Declined) ────────────────────
            // FIX: filter by the SAME resolved period id so Signed History
            //      changes when the user switches the period dropdown.
            var signedItems = new List<StaffSignedClearance>();
            try
            {
                using var conn2 = DbHelper.GetConnection(_config);
                conn2.Open();

                var uid2 = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

                var signedCmd = new MySqlCommand(@"
                    SELECT
                        co.student_number                                       AS StudentId,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                        COALESCE(
                            CONCAT(c.course_code, '-', cu.year_level, cu.section),
                            '—'
                        )                                                       AS StudentCourse,
                        co.position                                             AS Description,
                        CASE WHEN co.status = 'Cleared' THEN 'Approved'
                             ELSE 'Declined' END                                AS Status,
                        co.requested_at                                         AS RequestedAt,
                        co.signed_at                                            AS SignedAt
                    FROM   clearance_organization co
                    JOIN   organizations  o   ON o.position_title  = co.position
                    JOIN   users          stu ON stu.student_number = co.student_number
                    LEFT JOIN curriculum  cu  ON cu.id             = stu.curriculum_id
                    LEFT JOIN courses     c   ON c.id              = cu.course_id
                    WHERE  o.user_id = @uid2
                      AND  co.status IN ('Cleared', 'Declined')
                      AND  (@pid2 = 0 OR co.period_id = @pid2)
                    ORDER BY co.signed_at DESC", conn2);

                signedCmd.Parameters.AddWithValue("@uid2", uid2);
                signedCmd.Parameters.AddWithValue("@pid2", pid);   // <-- THE FIX

                using var sr = signedCmd.ExecuteReader();
                while (sr.Read())
                    signedItems.Add(new StaffSignedClearance {
                        StudentId     = sr.IsDBNull(sr.GetOrdinal("StudentId"))     ? "—" : sr.GetString("StudentId"),
                        StudentName   = sr.IsDBNull(sr.GetOrdinal("StudentName"))   ? "—" : sr.GetString("StudentName"),
                        StudentCourse = sr.IsDBNull(sr.GetOrdinal("StudentCourse")) ? "—" : sr.GetString("StudentCourse"),
                        Description   = sr.IsDBNull(sr.GetOrdinal("Description"))   ? "—" : sr.GetString("Description"),
                        Status        = sr.GetString("Status"),
                        RequestedAt   = sr.IsDBNull(sr.GetOrdinal("RequestedAt"))   ? null : sr.GetDateTime("RequestedAt"),
                        SignedAt      = sr.IsDBNull(sr.GetOrdinal("SignedAt"))      ? DateTime.MinValue : sr.GetDateTime("SignedAt")
                    });
            }
            catch { }
            ViewBag.SignedItems = signedItems;

            return View(items);
        }

        // ── Chat: Get Messages ────────────────────────────────────────────
        [HttpGet]
        public IActionResult GetClearanceMessages(string studentNumber, string key, string type)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT sender_id, message, sent_at
                    FROM   clearance_messages
                    WHERE  student_number = @sn
                      AND  clearance_type = @type
                      AND  clearance_key  = @key
                    ORDER BY sent_at ASC", conn);
                cmd.Parameters.AddWithValue("@sn",   studentNumber ?? "");
                cmd.Parameters.AddWithValue("@type", type          ?? "");
                cmd.Parameters.AddWithValue("@key",  key           ?? "");
                var messages = new List<object>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    messages.Add(new { mine = r.GetInt32("sender_id") == userId, text = r.GetString("message"), time = r.GetDateTime("sent_at").ToString("O") });
                return Json(new { success = true, messages });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message, messages = Array.Empty<object>() }); }
        }

        // ── Chat: Send Message ────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult SendClearanceMessage([FromBody] InstructorSendMessageDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Message))
                return Json(new { success = false, error = "Message is empty." });
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    INSERT INTO clearance_messages
                        (sender_id, student_number, clearance_type, clearance_key, message, sent_at, is_read)
                    VALUES (@sid, @sn, @type, @key, @msg, NOW(), 0)", conn);
                cmd.Parameters.AddWithValue("@sid",  userId);
                cmd.Parameters.AddWithValue("@sn",   dto.StudentNumber  ?? "");
                cmd.Parameters.AddWithValue("@type", dto.ClearanceType  ?? "");
                cmd.Parameters.AddWithValue("@key",  dto.ClearanceKey   ?? "");
                cmd.Parameters.AddWithValue("@msg",  dto.Message.Trim());
                cmd.ExecuteNonQuery();
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        // ── Chat: Mark Messages as Read ───────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult MarkMessagesRead([FromBody] InstructorSendMessageDto dto)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    UPDATE clearance_messages
                    SET    is_read = 1
                    WHERE  student_number = @sn
                      AND  clearance_type = @type
                      AND  clearance_key  = @key
                      AND  sender_id      != @uid
                      AND  is_read        = 0", conn);
                cmd.Parameters.AddWithValue("@sn",   dto.StudentNumber ?? "");
                cmd.Parameters.AddWithValue("@type", dto.ClearanceType ?? "");
                cmd.Parameters.AddWithValue("@key",  dto.ClearanceKey  ?? "");
                cmd.Parameters.AddWithValue("@uid",  userId);
                cmd.ExecuteNonQuery();
                return Json(new { success = true });
            }
            catch { return Json(new { success = true }); }
        }

        // ── Chat: Unread Counts ───────────────────────────────────────────
        [HttpGet]
        public IActionResult GetUnreadCounts()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT cm.student_number, cm.clearance_key, cm.clearance_type
                    FROM   clearance_messages cm
                    WHERE  cm.sender_id != @uid
                      AND  cm.is_read   =  0
                      AND  EXISTS (
                          SELECT 1 FROM clearance_messages
                          WHERE  sender_id     = @uid2
                            AND  student_number = cm.student_number
                            AND  clearance_key  = cm.clearance_key
                            AND  clearance_type = cm.clearance_type
                      )
                    GROUP BY cm.student_number, cm.clearance_key, cm.clearance_type", conn);
                cmd.Parameters.AddWithValue("@uid",  userId);
                cmd.Parameters.AddWithValue("@uid2", userId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { studentNumber = r.GetString("student_number"), clearanceKey = r.GetString("clearance_key"), clearanceType = r.GetString("clearance_type") });
            }
            catch { }
            return Json(items);
        }

        // ── Approve ───────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Approve(int id)
        {
            UpdateOrgStatus(id, "Cleared");
            TempData["SuccessMessage"] = "Student clearance approved.";
            return RedirectToAction(nameof(Signatories));
        }

        // ── Decline ───────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Decline(int id)
        {
            UpdateOrgStatus(id, "Declined");
            TempData["SuccessMessage"] = "Student clearance declined.";
            return RedirectToAction(nameof(Signatories));
        }

        // ── Signed Clearance (standalone page, kept as-is) ───────────────
        public IActionResult SignedClearance(string filter = "all")
        {
            ViewData["Filter"] = filter;
            var items = new List<StaffSignedClearance>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var where = filter switch
                {
                    "approved" => "WHERE sc.status = 'Approved'",
                    "rejected" => "WHERE sc.status = 'Rejected'",
                    _          => ""
                };

                var cmd = new MySqlCommand($@"
                    SELECT sc.student_number   AS StudentId,
                           sc.student_name     AS StudentName,
                           sc.course           AS StudentCourse,
                           sc.department       AS Description,
                           sc.status           AS Status,
                           sc.signed_at        AS SignedAt
                    FROM signed_clearances sc
                    {where}
                    ORDER BY sc.signed_at DESC", conn);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new StaffSignedClearance
                    {
                        StudentId     = r.IsDBNull(r.GetOrdinal("StudentId"))     ? "—" : r.GetString("StudentId"),
                        StudentName   = r.IsDBNull(r.GetOrdinal("StudentName"))   ? "—" : r.GetString("StudentName"),
                        StudentCourse = r.IsDBNull(r.GetOrdinal("StudentCourse")) ? "—" : r.GetString("StudentCourse"),
                        Description   = r.IsDBNull(r.GetOrdinal("Description"))   ? "—" : r.GetString("Description"),
                        Status        = r.GetString("Status"),
                        SignedAt      = r.GetDateTime("SignedAt")
                    });
                }
            }
            catch { }

            return View(items);
        }

        // ── Profile GET ───────────────────────────────────────────────────
        public IActionResult Profile()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model  = new StaffProfileViewModel();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    SELECT u.first_name, u.middle_initial, u.last_name,
                           u.id_number, u.email, sig.signature_data
                    FROM users u
                    LEFT JOIN user_signatures sig ON sig.user_id = u.id AND sig.position IS NULL
                    WHERE u.id = @uid LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    model.FirstName       = r.IsDBNull(r.GetOrdinal("first_name"))      ? "" : r.GetString("first_name");
                    model.MiddleInitial   = r.IsDBNull(r.GetOrdinal("middle_initial"))  ? "" : r.GetString("middle_initial");
                    model.LastName        = r.IsDBNull(r.GetOrdinal("last_name"))       ? "" : r.GetString("last_name");
                    model.StaffId         = r.IsDBNull(r.GetOrdinal("id_number"))       ? "—" : r.GetString("id_number");
                    model.Email           = r.IsDBNull(r.GetOrdinal("email"))            ? "" : r.GetString("email");
                    model.SignatureBase64  = r.IsDBNull(r.GetOrdinal("signature_data")) ? null : r.GetString("signature_data");
                    model.Password        = "";
                }
                r.Close();

                var posCmd = new MySqlCommand(
                    "SELECT position_title FROM organizations WHERE user_id = @uid ORDER BY id", conn);
                posCmd.Parameters.AddWithValue("@uid", userId);
                using var pr = posCmd.ExecuteReader();
                while (pr.Read())
                    if (!pr.IsDBNull(0)) model.Positions.Add(pr.GetString(0));
            }
            catch { }

            return View(model);
        }

        // ── Save Signature (AJAX) ─────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult SaveSignature([FromBody] SaveSignatureDto dto)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    INSERT INTO user_signatures (user_id, signature_data)
                    VALUES (@uid, @sd)
                    ON DUPLICATE KEY UPDATE signature_data = @sd", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@sd",  dto.SignatureData ?? "");
                cmd.ExecuteNonQuery();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Profile POST ──────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult SaveStaffProfile(StaffProfileViewModel model)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    var hash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                    var cmd  = new MySqlCommand(
                        "UPDATE users SET first_name=@fn, middle_initial=@mi, last_name=@ln, password=@pw WHERE id=@id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@pw", hash);
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    var cmd = new MySqlCommand(
                        "UPDATE users SET first_name=@fn, middle_initial=@mi, last_name=@ln WHERE id=@id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }

                TempData["ProfileSaved"] = "Profile updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["ProfileSaved"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Profile));
        }

        // ── Academic Periods API ──────────────────────────────────────────
        [HttpGet("/api/staff/periods")]
        public IActionResult GetPeriods()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, year_label, semester FROM academic_periods ORDER BY id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), ay = r.GetString("year_label"), sem = r.GetString("semester") });
            }
            catch { }
            return Json(items);
        }

        // ── Period resolution helper ──────────────────────────────────────
        private (int id, string label) ResolvePeriod(MySqlConnection conn, int? periodId)
        {
            MySqlCommand cmd;
            if (periodId.HasValue && periodId.Value > 0)
            {
                cmd = new MySqlCommand(
                    "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
                    "FROM academic_periods WHERE id = @pid LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@pid", periodId.Value);
            }
            else
            {
                cmd = new MySqlCommand(
                    "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
                    "FROM academic_periods ORDER BY is_active DESC, id DESC LIMIT 1", conn);
            }
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return (r.GetInt32("id"), r.IsDBNull(1) ? "—" : r.GetString("lbl"));
            return (0, "—");
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void UpdateOrgStatus(int id, string status)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE clearance_organization SET status = @s, signed_at = NOW() WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@s",  status);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private static void LoadAnnouncements(MySqlConnection conn, List<AnnouncementItem> list)
        {
            var cmd = new MySqlCommand(
                "SELECT title, body AS content, type, posted_at AS created_at " +
                "FROM announcements ORDER BY posted_at DESC LIMIT 10", conn);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AnnouncementItem
                {
                    Title   = r.GetString("title"),
                    Content = r.GetString("content"),
                    Type    = r.IsDBNull(r.GetOrdinal("type")) ? "General" : r.GetString("type"),
                    Date    = r.GetDateTime("created_at").ToString("MMMM d, yyyy")
                });
            }
        }
    }
}