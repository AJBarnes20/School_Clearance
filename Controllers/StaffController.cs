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

                var periodCmd = new MySqlCommand(
                    "SELECT CONCAT(semester, ', A.Y. ', year_label) " +
                    "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                var period = periodCmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(period)) model.ActivePeriod = period;

                var appCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Cleared'", conn);
                appCmd.Parameters.AddWithValue("@uid", userId);
                model.Approved = Convert.ToInt32(appCmd.ExecuteScalar() ?? 0);

                var penCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Pending'", conn);
                penCmd.Parameters.AddWithValue("@uid", userId);
                model.Pending = Convert.ToInt32(penCmd.ExecuteScalar() ?? 0);

                var decCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN organizations o ON o.position_title = co.position
                    WHERE o.user_id = @uid AND co.status = 'Declined'", conn);
                decCmd.Parameters.AddWithValue("@uid", userId);
                model.Declined = Convert.ToInt32(decCmd.ExecuteScalar() ?? 0);

                LoadAnnouncements(conn, model.Announcements);
            }
            catch { }

            return View(model);
        }

        // ── Signatories ───────────────────────────────────────────────────
        public IActionResult Signatories()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<SignatoryViewModel>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

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
                        co.position                                             AS Position
                    FROM clearance_organization co
                    JOIN organizations  o   ON o.position_title  = co.position
                    JOIN users          stu ON stu.student_number = co.student_number
                    LEFT JOIN curriculum cu ON cu.id             = stu.curriculum_id
                    LEFT JOIN courses    c  ON c.id              = cu.course_id
                    WHERE o.user_id = @uid
                    ORDER BY co.id", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new SignatoryViewModel
                    {
                        Id          = r.GetInt32("Id"),
                        StudentName = r.GetString("StudentName"),
                        StudentId   = r.IsDBNull(r.GetOrdinal("StudentId")) ? "—" : r.GetString("StudentId"),
                        Course      = r.IsDBNull(r.GetOrdinal("Course"))    ? "—" : r.GetString("Course"),
                        Status      = r.GetString("Status"),
                        Position    = r.IsDBNull(r.GetOrdinal("Position"))  ? "" : r.GetString("Position")
                    });
                }
            }
            catch { }

            // Load signed clearances for the Signed Clearance tab
            var signedItems = new List<StaffSignedClearance>();
            try
            {
                using var conn2 = DbHelper.GetConnection(_config);
                conn2.Open();
                var signedCmd = new MySqlCommand(@"
                    SELECT student_number AS StudentId, student_name AS StudentName,
                           course AS StudentCourse, department AS Description,
                           status AS Status, signed_at AS SignedAt
                    FROM signed_clearances ORDER BY signed_at DESC", conn2);
                using var sr = signedCmd.ExecuteReader();
                while (sr.Read())
                    signedItems.Add(new StaffSignedClearance {
                        StudentId     = sr.IsDBNull(sr.GetOrdinal("StudentId"))     ? "—" : sr.GetString("StudentId"),
                        StudentName   = sr.IsDBNull(sr.GetOrdinal("StudentName"))   ? "—" : sr.GetString("StudentName"),
                        StudentCourse = sr.IsDBNull(sr.GetOrdinal("StudentCourse")) ? "—" : sr.GetString("StudentCourse"),
                        Description   = sr.IsDBNull(sr.GetOrdinal("Description"))   ? "—" : sr.GetString("Description"),
                        Status        = sr.GetString("Status"),
                        SignedAt      = sr.GetDateTime("SignedAt")
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
                        (sender_id, student_number, clearance_type, clearance_key, message, sent_at)
                    VALUES (@sid, @sn, @type, @key, @msg, NOW())", conn);
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
                cmd.Parameters.AddWithValue("@sn",  dto.StudentNumber ?? "");
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

                var readFilter = "AND cm.is_read = 0";
                var cmd = new MySqlCommand($@"
                    SELECT cm.student_number, cm.clearance_key, cm.clearance_type
                    FROM   clearance_messages cm
                    JOIN   organizations o
                           ON o.position_title = cm.clearance_key
                          AND o.user_id        = @uid
                    WHERE  cm.sender_id      != @uid
                      AND  cm.clearance_type  = 'org'
                      {readFilter}
                    GROUP BY cm.student_number, cm.clearance_key, cm.clearance_type", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
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
            var studentInfo = GetStudentInfoFromOrg(id);
            UpdateOrgStatus(id, "Cleared");
            InsertSignedClearance(studentInfo, "Approved");
            TempData["SuccessMessage"] = "Student clearance approved.";
            return RedirectToAction(nameof(Signatories));
        }

        // ── Decline ───────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Decline(int id)
        {
            var studentInfo = GetStudentInfoFromOrg(id);
            UpdateOrgStatus(id, "Declined");
            InsertSignedClearance(studentInfo, "Rejected");
            TempData["SuccessMessage"] = "Student clearance declined.";
            return RedirectToAction(nameof(Signatories));
        }

        // ── Signed Clearance ──────────────────────────────────────────────
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
                    SELECT
                        sc.student_number   AS StudentId,
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
                    model.FirstName      = r.IsDBNull(r.GetOrdinal("first_name"))      ? "" : r.GetString("first_name");
                    model.MiddleInitial  = r.IsDBNull(r.GetOrdinal("middle_initial"))  ? "" : r.GetString("middle_initial");
                    model.LastName       = r.IsDBNull(r.GetOrdinal("last_name"))       ? "" : r.GetString("last_name");
                    model.StaffId        = r.IsDBNull(r.GetOrdinal("id_number"))       ? "—" : r.GetString("id_number");
                    model.Email          = r.IsDBNull(r.GetOrdinal("email"))            ? "" : r.GetString("email");
                    model.SignatureBase64 = r.IsDBNull(r.GetOrdinal("signature_data")) ? null : r.GetString("signature_data");
                    model.Password       = "";
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

        // ── Helpers ───────────────────────────────────────────────────────
        private void UpdateOrgStatus(int id, string status)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE clearance_organization SET status = @s WHERE id = @id", conn);
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

        private (string studentNumber, string studentName, string course, string department) GetStudentInfoFromOrg(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    SELECT
                        stu.student_number,
                        CONCAT(stu.first_name, ' ', stu.last_name) AS student_name,
                        COALESCE(CONCAT(c.course_code, '-', cu.year_level, cu.section), '—') AS course,
                        co.position AS department
                    FROM clearance_organization co
                    JOIN users       stu ON stu.student_number = co.student_number
                    LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE co.id = @id LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@id", id);

                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return (
                        r.IsDBNull(0) ? "" : r.GetString(0),
                        r.IsDBNull(1) ? "" : r.GetString(1),
                        r.IsDBNull(2) ? "" : r.GetString(2),
                        r.IsDBNull(3) ? "" : r.GetString(3)
                    );
            }
            catch { }

            return ("", "", "", "");
        }

        private void InsertSignedClearance((string studentNumber, string studentName, string course, string department) info, string status)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    INSERT INTO signed_clearances
                        (student_number, student_name, course, department, status, signed_at)
                    VALUES
                        (@sn, @name, @course, @dept, @status, NOW())", conn);
                cmd.Parameters.AddWithValue("@sn",     info.studentNumber);
                cmd.Parameters.AddWithValue("@name",   info.studentName);
                cmd.Parameters.AddWithValue("@course", info.course);
                cmd.Parameters.AddWithValue("@dept",   info.department);
                cmd.Parameters.AddWithValue("@status", status);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}