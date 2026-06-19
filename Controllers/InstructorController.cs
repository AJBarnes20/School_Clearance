using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;
using System.Text.Json;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize(Roles = "Instructor")]
    public class InstructorController : Controller
    {
        private readonly IConfiguration _config;

        public InstructorController(IConfiguration config)
        {
            _config = config;
        }

        private void SetUserViewData()
        {
            var firstName = User.FindFirst("FirstName")?.Value ?? "";
            var lastName  = User.FindFirst("LastName")?.Value  ?? "";
            ViewData["InstructorName"] = $"{firstName} {lastName}".Trim();

            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("SELECT COALESCE(id_number, '—') FROM users WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", userId);
                ViewData["InstructorId"] = cmd.ExecuteScalar()?.ToString() ?? "—";
            }
            catch
            {
                ViewData["InstructorId"] = "—";
            }
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            SetUserViewData();
            base.OnActionExecuting(context);
        }

        // Loads the academic periods list into ViewBag.Periods (JSON) so the
        // period dropdown in the views can render. Without this the dropdown
        // shows "No academic periods found."
        private void LoadPeriodsIntoViewBag(MySqlConnection conn)
        {
            var periodsList = new List<object>();
            try
            {
                var cmd = new MySqlCommand(
                    "SELECT id, year_label AS ay, semester AS sem FROM academic_periods ORDER BY id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    periodsList.Add(new
                    {
                        id  = r.GetInt32("id"),
                        ay  = r.IsDBNull(r.GetOrdinal("ay"))  ? "" : r.GetString("ay"),
                        sem = r.IsDBNull(r.GetOrdinal("sem")) ? "" : r.GetString("sem")
                    });
                }
            }
            catch { }
            ViewBag.Periods = JsonSerializer.Serialize(periodsList);
        }

        // ── Dashboard ─────────────────────────────────────────────────────
        public IActionResult Dashboard()
        {
            var userId    = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var firstName = User.FindFirst("FirstName")?.Value ?? "";
            var lastName  = User.FindFirst("LastName")?.Value  ?? "";

            var model = new InstructorDashboardViewModel
            {
                EmployeeId     = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"),
                InstructorId   = userId,
                InstructorName = $"{firstName} {lastName}".Trim(),
                ActivePeriod   = "—",
                Announcements  = new List<AnnouncementItem>()
            };

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                int.TryParse(Request.Cookies["InstructorPeriodId"], out var cookiePid);
                var (pid, periodLabel) = ResolvePeriod(conn, cookiePid > 0 ? cookiePid : (int?)null);
                model.ActivePeriod = periodLabel;

                var pendingCmd = new MySqlCommand(@"
                    SELECT COUNT(*)
                    FROM   clearance_subjects cs
                    JOIN   subject_offerings  so ON so.mis_code = cs.mis_code
                    WHERE  so.user_id = @uid
                      AND  cs.status  = 'Pending'
                      AND  (@pid = 0 OR cs.period_id = @pid)", conn);
                pendingCmd.Parameters.AddWithValue("@uid", userId);
                pendingCmd.Parameters.AddWithValue("@pid", pid);
                model.PendingSubjects = Convert.ToInt32(pendingCmd.ExecuteScalar() ?? 0);

                var totalCmd = new MySqlCommand(@"
                    SELECT COUNT(DISTINCT cs.student_number)
                    FROM   clearance_subjects cs
                    JOIN   subject_offerings  so ON so.mis_code = cs.mis_code
                    WHERE  so.user_id = @uid
                      AND  (@pid = 0 OR cs.period_id = @pid)", conn);
                totalCmd.Parameters.AddWithValue("@uid", userId);
                totalCmd.Parameters.AddWithValue("@pid", pid);
                model.TotalStudents = Convert.ToInt32(totalCmd.ExecuteScalar() ?? 0);

                var clearedCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM (
                        SELECT cs.student_number
                        FROM   clearance_subjects cs
                        JOIN   subject_offerings  so ON so.mis_code = cs.mis_code
                        WHERE  so.user_id = @uid
                          AND  (@pid = 0 OR cs.period_id = @pid)
                        GROUP  BY cs.student_number
                        HAVING SUM(CASE WHEN cs.status != 'Cleared' THEN 1 ELSE 0 END) = 0
                    ) AS fully_cleared", conn);
                clearedCmd.Parameters.AddWithValue("@uid", userId);
                clearedCmd.Parameters.AddWithValue("@pid", pid);
                model.ClearedStudents = Convert.ToInt32(clearedCmd.ExecuteScalar() ?? 0);

                var orgPendingCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM (
                        SELECT co.id FROM clearance_organization co
                        JOIN organizations o
                             ON o.position_title = co.position AND o.user_id = @opuid
                             AND o.position_title != 'Class Adviser'
                        WHERE co.status = 'Pending'
                          AND (@opid = 0 OR co.period_id = @opid)
                        UNION
                        SELECT co2.id FROM clearance_organization co2
                        JOIN users stu ON stu.id_number = co2.student_number
                        JOIN organizations org2
                             ON org2.user_id = @opuid2
                             AND org2.position_title = 'Class Adviser'
                             AND org2.curriculum_id = stu.curriculum_id
                        WHERE co2.position = 'Class Adviser' AND co2.status = 'Pending'
                          AND (@opid2 = 0 OR co2.period_id = @opid2)
                    ) AS combined", conn);
                orgPendingCmd.Parameters.AddWithValue("@opuid",  userId);
                orgPendingCmd.Parameters.AddWithValue("@opuid2", userId);
                orgPendingCmd.Parameters.AddWithValue("@opid",   pid);
                orgPendingCmd.Parameters.AddWithValue("@opid2",  pid);
                model.PendingOrg = Convert.ToInt32(orgPendingCmd.ExecuteScalar() ?? 0);

                LoadAnnouncements(conn, model.Announcements);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(model);
        }

        // ── Academic Periods API ──────────────────────────────────────────
        [HttpGet("/api/instructor/periods")]
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
                    items.Add(new
                    {
                        id  = r.GetInt32("id"),
                        ay  = r.GetString("year_label"),
                        sem = r.GetString("semester")
                    });
            }
            catch { }
            return Json(items);
        }

        // ── Get Clearance Messages ────────────────────────────────────────
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
                {
                    messages.Add(new
                    {
                        mine = r.GetInt32("sender_id") == userId,
                        text = r.GetString("message"),
                        time = r.GetDateTime("sent_at").ToString("O")
                    });
                }
                return Json(new { success = true, messages });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message, messages = Array.Empty<object>() });
            }
        }

        // ── Mark Messages as Read ─────────────────────────────────────────
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

        // ── Unread Counts ─────────────────────────────────────────────────
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
                    items.Add(new
                    {
                        studentNumber = r.GetString("student_number"),
                        clearanceKey  = r.GetString("clearance_key"),
                        clearanceType = r.GetString("clearance_type")
                    });
            }
            catch { }
            return Json(items);
        }

        // ── Send Clearance Message ────────────────────────────────────────
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
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
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

        // ── Assigned Subjects ─────────────────────────────────────────────
        public IActionResult SubjectOfferings(int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<SubjectOfferingDto>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var (pid, lbl) = ResolvePeriod(conn, periodId);
                ViewData["ActivePeriodId"] = pid;
                ViewData["ActivePeriod"]   = lbl;
                LoadPeriodsIntoViewBag(conn);

                var cmd = new MySqlCommand(@"
                    SELECT
                        so.mis_code    AS MisCode,
                        s.subject_code AS SubjectCode,
                        s.description  AS Description,
                        s.lab_units    AS LabUnit,
                        s.lec_units    AS LecUnit
                    FROM subject_offerings so
                    JOIN subjects s ON s.id = so.subject_id
                    WHERE so.user_id  = @uid
                      AND (@pid = 0 OR so.period_id = @pid OR so.period_id IS NULL)
                    ORDER BY s.subject_code", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@pid", pid);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new SubjectOfferingDto
                    {
                        MisCode     = r.GetString("MisCode"),
                        SubjectCode = r.GetString("SubjectCode"),
                        Description = r.GetString("Description"),
                        LabUnit     = r.GetInt32("LabUnit"),
                        LecUnit     = r.GetInt32("LecUnit")
                    });
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(items);
        }

        // ── Subject Clearance Requests ────────────────────────────────────
        public IActionResult SubjectClearance(int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<ClearanceRequest>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!periodId.HasValue || periodId.Value <= 0)
                { int.TryParse(Request.Cookies["InstructorPeriodId"], out var cp); if (cp > 0) periodId = cp; }

                var (pid, lbl) = ResolvePeriod(conn, periodId);
                if (pid > 0) Response.Cookies.Append("InstructorPeriodId", pid.ToString(),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
                ViewData["ActivePeriodId"] = pid;
                ViewData["ActivePeriod"]   = lbl;
                LoadPeriodsIntoViewBag(conn);

                // ── Subject requests (Pending only) ───────────────────────
                var cmd = new MySqlCommand(@"
                    SELECT
                        cs.id                                                   AS Id,
                        cs.mis_code                                             AS MisCode,
                        s.subject_code                                          AS SubjectCode,
                        COALESCE(s.description, '—')                            AS Description,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                        COALESCE(
                            CONCAT(c.course_code, '-', cu.year_level, cu.section),
                            '—'
                        )                                                       AS StudentCourse,
                        cs.student_number                                       AS StudentNumber,
                        cs.status                                               AS Status,
                        cs.requested_at                                         AS RequestedAt
                    FROM clearance_subjects cs
                    JOIN subject_offerings so  ON so.mis_code        = cs.mis_code
                    JOIN subjects          s   ON s.id               = so.subject_id
                    JOIN users             stu ON stu.id_number      = cs.student_number
                    LEFT JOIN curriculum   cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses      c   ON c.id               = cu.course_id
                    WHERE so.user_id = @uid
                      AND (@pid = 0 OR cs.period_id = @pid)
                      AND cs.status = 'Pending'
                    ORDER BY cs.id", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@pid", pid);

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        items.Add(new ClearanceRequest
                        {
                            Id            = r.GetInt32("Id"),
                            MisCode       = r.GetString("MisCode"),
                            SubjectCode   = r.GetString("SubjectCode"),
                            Description   = r.GetString("Description"),
                            StudentName   = r.GetString("StudentName"),
                            StudentCourse = r.GetString("StudentCourse"),
                            StudentNumber = r.GetString("StudentNumber"),
                            Status        = r.GetString("Status"),
                            RequestedAt   = r.IsDBNull(r.GetOrdinal("RequestedAt")) ? null : r.GetDateTime("RequestedAt")
                        });
                    }
                }

                // ── Sidebar: assigned subjects ─────────────────────────────
                var subjectList = new List<SubjectOfferingDto>();
                var subCmd = new MySqlCommand(@"
                    SELECT so.mis_code    AS MisCode,
                           s.subject_code AS SubjectCode,
                           s.description  AS Description,
                           s.lab_units    AS LabUnit,
                           s.lec_units    AS LecUnit
                    FROM subject_offerings so
                    JOIN subjects s ON s.id = so.subject_id
                    WHERE so.user_id = @uid2
                    ORDER BY s.subject_code", conn);
                subCmd.Parameters.AddWithValue("@uid2", userId);
                using (var sr = subCmd.ExecuteReader())
                {
                    while (sr.Read())
                        subjectList.Add(new SubjectOfferingDto
                        {
                            MisCode     = sr.GetString("MisCode"),
                            SubjectCode = sr.GetString("SubjectCode"),
                            Description = sr.GetString("Description"),
                            LabUnit     = sr.GetInt32("LabUnit"),
                            LecUnit     = sr.GetInt32("LecUnit")
                        });
                }
                ViewBag.AssignedSubjects = subjectList;

                // ── Org requests (Pending ONLY → Requests tab) ────────────
                var orgItems = new List<OrganizationRequest>();

                // Path A: non-adviser positions, Pending only
                var orgPathACmd = new MySqlCommand(@"
                    SELECT co.id AS Id, o.position_title AS Position,
                           CONCAT(stu.first_name,' ',stu.last_name) AS StudentName,
                           co.student_number AS StudentNumber,
                           COALESCE(CONCAT(c.course_code,' ',cu.year_level,cu.section),'—') AS Course,
                           COALESCE(co.status,'Pending') AS Status,
                           co.requested_at AS RequestedAt
                    FROM   clearance_organization co
                    JOIN   organizations o ON o.position_title = co.position
                                         AND o.user_id = @ouid
                                         AND o.position_title != 'Class Adviser'
                    JOIN   users stu ON stu.id_number = co.student_number
                    LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE  co.status = 'Pending'
                      AND  (@opid = 0 OR co.period_id = @opid)
                    ORDER BY co.id DESC", conn);
                orgPathACmd.Parameters.AddWithValue("@ouid", userId);
                orgPathACmd.Parameters.AddWithValue("@opid", pid);
                using (var r1 = orgPathACmd.ExecuteReader())
                {
                    while (r1.Read())
                        orgItems.Add(new OrganizationRequest {
                            Id            = r1.GetInt32("Id"),
                            Position      = r1.IsDBNull(r1.GetOrdinal("Position"))      ? "—" : r1.GetString("Position"),
                            StudentName   = r1.IsDBNull(r1.GetOrdinal("StudentName"))   ? "—" : r1.GetString("StudentName"),
                            StudentNumber = r1.IsDBNull(r1.GetOrdinal("StudentNumber")) ? ""  : r1.GetString("StudentNumber"),
                            Course        = r1.IsDBNull(r1.GetOrdinal("Course"))        ? "—" : r1.GetString("Course"),
                            Status        = r1.GetString("Status"),
                            RequestedAt   = r1.IsDBNull(r1.GetOrdinal("RequestedAt"))   ? null : r1.GetDateTime("RequestedAt")
                        });
                }

                // Path B: Class Adviser, Pending only
                var advCmd = new MySqlCommand(
                    "SELECT curriculum_id FROM organizations WHERE user_id=@auid AND position_title='Class Adviser' AND curriculum_id IS NOT NULL", conn);
                advCmd.Parameters.AddWithValue("@auid", userId);
                var advIds = new List<int>();
                using (var advR = advCmd.ExecuteReader())
                { while (advR.Read()) advIds.Add(advR.GetInt32("curriculum_id")); }

                if (advIds.Count > 0)
                {
                    var inList = string.Join(",", advIds);
                    var orgPathBCmd = new MySqlCommand($@"
                        SELECT co.id AS Id, 'Class Adviser' AS Position,
                               CONCAT(stu.first_name,' ',stu.last_name) AS StudentName,
                               co.student_number AS StudentNumber,
                               COALESCE(CONCAT(c.course_code,' ',cu.year_level,cu.section),'—') AS Course,
                               COALESCE(co.status,'Pending') AS Status,
                               co.requested_at AS RequestedAt
                        FROM   clearance_organization co
                        JOIN   users stu ON stu.id_number = co.student_number
                                       AND stu.curriculum_id IN ({inList})
                        LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                        LEFT JOIN courses    c  ON c.id  = cu.course_id
                        WHERE  co.position = 'Class Adviser'
                          AND  co.status = 'Pending'
                          AND  (@bpid = 0 OR co.period_id = @bpid)
                        ORDER BY co.id DESC", conn);
                    orgPathBCmd.Parameters.AddWithValue("@bpid", pid);
                    using (var r2 = orgPathBCmd.ExecuteReader())
                    {
                        while (r2.Read())
                        {
                            var rowId = r2.GetInt32("Id");
                            if (orgItems.Any(x => x.Id == rowId)) continue;
                            orgItems.Add(new OrganizationRequest {
                                Id            = rowId,
                                Position      = "Class Adviser",
                                StudentName   = r2.IsDBNull(r2.GetOrdinal("StudentName"))   ? "—" : r2.GetString("StudentName"),
                                StudentNumber = r2.IsDBNull(r2.GetOrdinal("StudentNumber")) ? ""  : r2.GetString("StudentNumber"),
                                Course        = r2.IsDBNull(r2.GetOrdinal("Course"))        ? "—" : r2.GetString("Course"),
                                Status        = r2.GetString("Status"),
                                RequestedAt   = r2.IsDBNull(r2.GetOrdinal("RequestedAt"))   ? null : r2.GetDateTime("RequestedAt")
                            });
                        }
                    }
                }
                ViewBag.OrgItems = orgItems;

                // ── Signed subject clearances (Cleared / Declined) ─────────
                var signedItems = new List<SignedClearance>();
                var signedCmd = new MySqlCommand(@"
                    SELECT cs.mis_code AS MisCode, s.subject_code AS SubjectCode,
                           COALESCE(s.description,'—') AS Description,
                           CONCAT(stu.first_name,' ',stu.last_name) AS StudentName,
                           COALESCE(CONCAT(c.course_code,'-',cu.year_level,cu.section),'—') AS StudentCourse,
                           CASE WHEN cs.status='Cleared' THEN 'Approved' ELSE 'Declined' END AS Status,
                           cs.requested_at AS RequestedAt,
                           cs.signed_at    AS SignedAt
                    FROM   clearance_subjects cs
                    JOIN   subject_offerings so  ON so.mis_code = cs.mis_code
                    JOIN   subjects          s   ON s.id        = so.subject_id
                    JOIN   users             stu ON stu.id_number = cs.student_number
                    LEFT JOIN curriculum     cu  ON cu.id = stu.curriculum_id
                    LEFT JOIN courses        c   ON c.id  = cu.course_id
                    WHERE  so.user_id = @suid
                      AND  (@spid = 0 OR cs.period_id = @spid)
                      AND  cs.status IN ('Cleared','Declined')
                    ORDER BY cs.id DESC", conn);
                signedCmd.Parameters.AddWithValue("@suid", userId);
                signedCmd.Parameters.AddWithValue("@spid", pid);
                using (var sr2 = signedCmd.ExecuteReader())
                {
                    while (sr2.Read())
                        signedItems.Add(new SignedClearance {
                            MisCode       = sr2.GetString("MisCode"),
                            SubjectCode   = sr2.GetString("SubjectCode"),
                            Description   = sr2.GetString("Description"),
                            StudentName   = sr2.GetString("StudentName"),
                            StudentCourse = sr2.GetString("StudentCourse"),
                            Status        = sr2.GetString("Status"),
                            RequestedAt   = sr2.IsDBNull(sr2.GetOrdinal("RequestedAt")) ? null : sr2.GetDateTime("RequestedAt"),
                            SignedAt      = sr2.IsDBNull(sr2.GetOrdinal("SignedAt"))    ? null : sr2.GetDateTime("SignedAt")
                        });
                }
                ViewBag.SignedItems = signedItems;

                // ── Org signed clearances (Cleared / Declined → Signed tab) ──
                var orgSignedItems = new List<OrganizationRequest>();

                // Path A signed: non-adviser positions
                var orgSignedPathACmd = new MySqlCommand(@"
                    SELECT co.id AS Id, o.position_title AS Position,
                           CONCAT(stu.first_name,' ',stu.last_name) AS StudentName,
                           co.student_number AS StudentNumber,
                           COALESCE(CONCAT(c.course_code,' ',cu.year_level,cu.section),'—') AS Course,
                           co.status AS Status,
                           co.requested_at AS RequestedAt,
                           co.signed_at    AS SignedAt
                    FROM   clearance_organization co
                    JOIN   organizations o ON o.position_title = co.position
                                         AND o.user_id = @osuid
                                         AND o.position_title != 'Class Adviser'
                    JOIN   users stu ON stu.id_number = co.student_number
                    LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE  co.status IN ('Cleared','Declined')
                      AND  (@ospid = 0 OR co.period_id = @ospid)
                    ORDER BY co.id DESC", conn);
                orgSignedPathACmd.Parameters.AddWithValue("@osuid", userId);
                orgSignedPathACmd.Parameters.AddWithValue("@ospid", pid);
                using (var osr1 = orgSignedPathACmd.ExecuteReader())
                {
                    while (osr1.Read())
                        orgSignedItems.Add(new OrganizationRequest {
                            Id            = osr1.GetInt32("Id"),
                            Position      = osr1.IsDBNull(osr1.GetOrdinal("Position"))      ? "—" : osr1.GetString("Position"),
                            StudentName   = osr1.IsDBNull(osr1.GetOrdinal("StudentName"))   ? "—" : osr1.GetString("StudentName"),
                            StudentNumber = osr1.IsDBNull(osr1.GetOrdinal("StudentNumber")) ? ""  : osr1.GetString("StudentNumber"),
                            Course        = osr1.IsDBNull(osr1.GetOrdinal("Course"))        ? "—" : osr1.GetString("Course"),
                            Status        = osr1.GetString("Status"),
                            RequestedAt   = osr1.IsDBNull(osr1.GetOrdinal("RequestedAt"))   ? null : osr1.GetDateTime("RequestedAt"),
                            SignedAt      = osr1.IsDBNull(osr1.GetOrdinal("SignedAt"))      ? null : osr1.GetDateTime("SignedAt")
                        });
                }

                // Path B signed: Class Adviser
                if (advIds.Count > 0)
                {
                    var inList = string.Join(",", advIds);
                    var orgSignedPathBCmd = new MySqlCommand($@"
                        SELECT co.id AS Id, 'Class Adviser' AS Position,
                               CONCAT(stu.first_name,' ',stu.last_name) AS StudentName,
                               co.student_number AS StudentNumber,
                               COALESCE(CONCAT(c.course_code,' ',cu.year_level,cu.section),'—') AS Course,
                               co.status AS Status,
                               co.requested_at AS RequestedAt,
                               co.signed_at    AS SignedAt
                        FROM   clearance_organization co
                        JOIN   users stu ON stu.id_number = co.student_number
                                       AND stu.curriculum_id IN ({inList})
                        LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                        LEFT JOIN courses    c  ON c.id  = cu.course_id
                        WHERE  co.position = 'Class Adviser'
                          AND  co.status IN ('Cleared','Declined')
                          AND  (@bspid = 0 OR co.period_id = @bspid)
                        ORDER BY co.id DESC", conn);
                    orgSignedPathBCmd.Parameters.AddWithValue("@bspid", pid);
                    using (var osr2 = orgSignedPathBCmd.ExecuteReader())
                    {
                        while (osr2.Read())
                        {
                            var rowId = osr2.GetInt32("Id");
                            if (orgSignedItems.Any(x => x.Id == rowId)) continue;
                            orgSignedItems.Add(new OrganizationRequest {
                                Id            = rowId,
                                Position      = "Class Adviser",
                                StudentName   = osr2.IsDBNull(osr2.GetOrdinal("StudentName"))   ? "—" : osr2.GetString("StudentName"),
                                StudentNumber = osr2.IsDBNull(osr2.GetOrdinal("StudentNumber")) ? ""  : osr2.GetString("StudentNumber"),
                                Course        = osr2.IsDBNull(osr2.GetOrdinal("Course"))        ? "—" : osr2.GetString("Course"),
                                Status        = osr2.GetString("Status"),
                                RequestedAt   = osr2.IsDBNull(osr2.GetOrdinal("RequestedAt"))   ? null : osr2.GetDateTime("RequestedAt"),
                                SignedAt      = osr2.IsDBNull(osr2.GetOrdinal("SignedAt"))      ? null : osr2.GetDateTime("SignedAt")
                            });
                        }
                    }
                }
                ViewBag.OrgSignedItems = orgSignedItems;
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
                ViewBag.AssignedSubjects = new List<SubjectOfferingDto>();
                ViewBag.OrgItems         = new List<OrganizationRequest>();
                ViewBag.SignedItems       = new List<SignedClearance>();
                ViewBag.OrgSignedItems   = new List<OrganizationRequest>();
                if (ViewBag.Periods == null) ViewBag.Periods = "[]";
            }

            return View(items);
        }

        // ── Approve Subject Clearance ─────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Approve(int id, int? periodId)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var infoCmd = new MySqlCommand(
                    "SELECT student_number FROM clearance_subjects WHERE id = @id LIMIT 1", conn);
                infoCmd.Parameters.AddWithValue("@id", id);

                string studentNumber = "";
                using (var r = infoCmd.ExecuteReader())
                {
                    if (r.Read()) studentNumber = r.GetString("student_number");
                }

                var approveCmd = new MySqlCommand(
                    "UPDATE clearance_subjects SET status = 'Cleared', signed_at = NOW() WHERE id = @id", conn);
                approveCmd.Parameters.AddWithValue("@id", id);
                approveCmd.ExecuteNonQuery();

                if (!string.IsNullOrEmpty(studentNumber))
                {
                    var checkCmd = new MySqlCommand(@"
                        SELECT COUNT(*) FROM clearance_subjects
                        WHERE student_number = @sn AND status != 'Cleared'", conn);
                    checkCmd.Parameters.AddWithValue("@sn", studentNumber);
                    if (Convert.ToInt32(checkCmd.ExecuteScalar() ?? 1) == 0)
                        TriggerOrgClearance(conn, studentNumber);
                }

                TempData["SuccessMessage"] = "Student clearance approved.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(SubjectClearance), new { periodId });
        }

        // ── Decline Subject Clearance ─────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Decline(int id, int? periodId)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE clearance_subjects SET status = 'Declined', signed_at = NOW() WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Student clearance declined.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(SubjectClearance), new { periodId });
        }

        // ── Organization Page ─────────────────────────────────────────────
        public IActionResult Organization(int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<OrganizationRequest>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!periodId.HasValue || periodId.Value <= 0)
                { int.TryParse(Request.Cookies["InstructorPeriodId"], out var cp); if (cp > 0) periodId = cp; }

                var (pid, lbl) = ResolvePeriod(conn, periodId);
                if (pid > 0) Response.Cookies.Append("InstructorPeriodId", pid.ToString(),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
                ViewData["ActivePeriodId"] = pid;
                ViewData["ActivePeriod"]   = lbl;
                LoadPeriodsIntoViewBag(conn);

                var pathACmd = new MySqlCommand(@"
                    SELECT
                        co.id                                                   AS Id,
                        o.position_title                                        AS Position,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                        co.student_number                                       AS StudentNumber,
                        COALESCE(
                            CONCAT(c.course_code, ' ', cu.year_level, cu.section),
                            '—'
                        )                                                       AS Course,
                        COALESCE(co.status, 'Pending')                         AS Status
                    FROM   clearance_organization   co
                    JOIN   organizations             o
                               ON  o.position_title  = co.position
                              AND  o.user_id          = @uid
                              AND  o.position_title  != 'Class Adviser'
                    JOIN   users                     stu ON stu.id_number = co.student_number
                    LEFT JOIN curriculum             cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses                c   ON c.id               = cu.course_id
                    WHERE  (@pid = 0 OR co.period_id = @pid)
                    ORDER BY FIELD(co.status,'Pending','Declined','Cleared'), co.id DESC", conn);
                pathACmd.Parameters.AddWithValue("@uid", userId);
                pathACmd.Parameters.AddWithValue("@pid", pid);

                using (var r1 = pathACmd.ExecuteReader())
                {
                    while (r1.Read())
                    {
                        items.Add(new OrganizationRequest
                        {
                            Id            = r1.GetInt32("Id"),
                            Position      = r1.IsDBNull(r1.GetOrdinal("Position"))      ? "—" : r1.GetString("Position"),
                            StudentName   = r1.IsDBNull(r1.GetOrdinal("StudentName"))   ? "—" : r1.GetString("StudentName"),
                            StudentNumber = r1.IsDBNull(r1.GetOrdinal("StudentNumber")) ? "" : r1.GetString("StudentNumber"),
                            Course        = r1.IsDBNull(r1.GetOrdinal("Course"))        ? "—" : r1.GetString("Course"),
                            Status        = r1.GetString("Status")
                        });
                    }
                }

                var advCmd = new MySqlCommand(@"
                    SELECT curriculum_id
                    FROM   organizations
                    WHERE  user_id        = @uid
                      AND  position_title = 'Class Adviser'
                      AND  curriculum_id  IS NOT NULL", conn);
                advCmd.Parameters.AddWithValue("@uid", userId);

                var advCurriculumIds = new List<int>();
                using (var advR = advCmd.ExecuteReader())
                {
                    while (advR.Read())
                        advCurriculumIds.Add(advR.GetInt32("curriculum_id"));
                }

                if (advCurriculumIds.Count > 0)
                {
                    var inList = string.Join(",", advCurriculumIds);

                    var pathBCmd = new MySqlCommand($@"
                        SELECT
                            co.id                                                   AS Id,
                            'Class Adviser'                                         AS Position,
                            CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                            co.student_number                                       AS StudentNumber,
                            COALESCE(
                                CONCAT(c.course_code, ' ', cu.year_level, cu.section),
                                '—'
                            )                                                       AS Course,
                            COALESCE(co.status, 'Pending')                         AS Status
                        FROM   clearance_organization   co
                        JOIN   users                     stu
                                   ON  stu.id_number       = co.student_number
                                  AND  stu.curriculum_id  IN ({inList})
                        LEFT JOIN curriculum             cu  ON cu.id = stu.curriculum_id
                        LEFT JOIN courses                c   ON c.id  = cu.course_id
                        WHERE  co.position = 'Class Adviser'
                          AND  (@pid = 0 OR co.period_id = @pid)
                        ORDER BY FIELD(co.status,'Pending','Declined','Cleared'), co.id DESC", conn);
                    pathBCmd.Parameters.AddWithValue("@pid", pid);

                    using var r2 = pathBCmd.ExecuteReader();
                    while (r2.Read())
                    {
                        var rowId = r2.GetInt32("Id");
                        if (items.Any(x => x.Id == rowId)) continue;

                        items.Add(new OrganizationRequest
                        {
                            Id            = rowId,
                            Position      = "Class Adviser",
                            StudentName   = r2.IsDBNull(r2.GetOrdinal("StudentName"))   ? "—" : r2.GetString("StudentName"),
                            StudentNumber = r2.IsDBNull(r2.GetOrdinal("StudentNumber")) ? "" : r2.GetString("StudentNumber"),
                            Course        = r2.IsDBNull(r2.GetOrdinal("Course"))        ? "—" : r2.GetString("Course"),
                            Status        = r2.GetString("Status")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Could not load org requests: " + ex.Message;
            }

            return View(items);
        }

        // ── ApproveOrg POST ───────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult ApproveOrg(int id, int? periodId)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("UPDATE clearance_organization SET status = 'Cleared', signed_at = NOW() WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["SuccessMessage"] = "Student cleared successfully.";
            }
            catch (Exception ex) { TempData["ErrorMessage"] = "Error: " + ex.Message; }

            return RedirectToAction(nameof(SubjectClearance), new { periodId });
        }

        // ── DeclineOrg POST ───────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeclineOrg(int id, int? periodId)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("UPDATE clearance_organization SET status = 'Declined', signed_at = NOW() WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["SuccessMessage"] = "Request declined.";
            }
            catch (Exception ex) { TempData["ErrorMessage"] = "Error: " + ex.Message; }

            return RedirectToAction(nameof(SubjectClearance), new { periodId });
        }

        // ── Signed Clearance page ─────────────────────────────────────────
        public IActionResult SignedClearance(int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var filter = Request.Query["filter"].ToString().ToLower();
            if (string.IsNullOrEmpty(filter)) filter = "all";
            ViewData["Filter"] = filter;

            var items = new List<SignedClearance>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!periodId.HasValue || periodId.Value <= 0)
                { int.TryParse(Request.Cookies["InstructorPeriodId"], out var cp); if (cp > 0) periodId = cp; }

                var (pid, lbl) = ResolvePeriod(conn, periodId);
                if (pid > 0) Response.Cookies.Append("InstructorPeriodId", pid.ToString(),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
                ViewData["ActivePeriodId"] = pid;
                ViewData["ActivePeriod"]   = lbl;
                LoadPeriodsIntoViewBag(conn);

                var statusFilter = filter switch
                {
                    "approved" => "AND cs.status = 'Cleared'",
                    "rejected" => "AND cs.status = 'Declined'",
                    _          => "AND cs.status IN ('Cleared', 'Declined')"
                };

                var cmd = new MySqlCommand($@"
                    SELECT
                        cs.mis_code                                             AS MisCode,
                        s.subject_code                                          AS SubjectCode,
                        COALESCE(s.description, '—')                            AS Description,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
                        COALESCE(
                            CONCAT(c.course_code, '-', cu.year_level, cu.section),
                            '—'
                        )                                                       AS StudentCourse,
                        CASE WHEN cs.status = 'Cleared' THEN 'Approved' ELSE 'Declined' END AS Status,
                        cs.requested_at AS RequestedAt,
                        cs.signed_at    AS SignedAt
                    FROM clearance_subjects cs
                    JOIN subject_offerings so  ON so.mis_code        = cs.mis_code
                    JOIN subjects          s   ON s.id               = so.subject_id
                    JOIN users             stu ON stu.id_number      = cs.student_number
                    LEFT JOIN curriculum   cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses      c   ON c.id               = cu.course_id
                    WHERE so.user_id = @uid
                      AND (@pid = 0 OR cs.period_id = @pid)
                    {statusFilter}
                    ORDER BY cs.id DESC", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@pid", pid);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new SignedClearance
                    {
                        MisCode       = r.GetString("MisCode"),
                        SubjectCode   = r.GetString("SubjectCode"),
                        Description   = r.GetString("Description"),
                        StudentName   = r.GetString("StudentName"),
                        StudentCourse = r.GetString("StudentCourse"),
                        Status        = r.GetString("Status"),
                        RequestedAt   = r.IsDBNull(r.GetOrdinal("RequestedAt")) ? null : r.GetDateTime("RequestedAt"),
                        SignedAt      = r.IsDBNull(r.GetOrdinal("SignedAt"))    ? null : r.GetDateTime("SignedAt")
                    });
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(items);
        }

        // ── Profile GET ───────────────────────────────────────────────────
        public IActionResult Profile()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model  = new InstructorProfileViewModel();

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
                    model.FirstName      = r.IsDBNull(r.GetOrdinal("first_name"))     ? "" : r.GetString("first_name");
                    model.MiddleInitial  = r.IsDBNull(r.GetOrdinal("middle_initial")) ? "" : r.GetString("middle_initial");
                    model.LastName       = r.IsDBNull(r.GetOrdinal("last_name"))      ? "" : r.GetString("last_name");
                    model.EmployeeId     = r.IsDBNull(r.GetOrdinal("id_number"))      ? "—" : r.GetString("id_number");
                    model.Email          = r.IsDBNull(r.GetOrdinal("email"))           ? "" : r.GetString("email");
                    model.SignatureBase64 = r.IsDBNull(r.GetOrdinal("signature_data")) ? null : r.GetString("signature_data");
                }
                r.Close();

                var posCmd = new MySqlCommand(@"
                    SELECT o.position_title, o.curriculum_id, c.course_code, cu.year_level, cu.section
                    FROM   organizations o
                    LEFT JOIN curriculum cu ON cu.id = o.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE  o.user_id = @uid AND o.is_active = 1
                    ORDER BY o.id", conn);
                posCmd.Parameters.AddWithValue("@uid", userId);

                using var pr = posCmd.ExecuteReader();
                while (pr.Read())
                {
                    if (pr.IsDBNull(0)) continue;
                    var posTitle = pr.GetString("position_title");

                    if (posTitle == "Class Adviser" && !pr.IsDBNull(pr.GetOrdinal("curriculum_id")))
                    {
                        var course  = pr.IsDBNull(pr.GetOrdinal("course_code")) ? "" : pr.GetString("course_code");
                        var yl      = pr.IsDBNull(pr.GetOrdinal("year_level"))  ? 0  : pr.GetInt32("year_level");
                        var section = pr.IsDBNull(pr.GetOrdinal("section"))     ? "" : pr.GetString("section");
                        model.Positions.Add($"Class Adviser – {course} {yl}{section}");
                    }
                    else
                    {
                        model.Positions.Add(posTitle);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(model);
        }

        // ── Save Signature ────────────────────────────────────────────────
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
        public IActionResult SaveInstructorProfile(InstructorProfileViewModel model)
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
                        "UPDATE users SET first_name=@fn, middle_initial=@mi, last_name=@ln, email=@em, password=@pw WHERE id=@id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@em", model.Email?.Trim()         ?? "");
                    cmd.Parameters.AddWithValue("@pw", hash);
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    var cmd = new MySqlCommand(
                        "UPDATE users SET first_name=@fn, middle_initial=@mi, last_name=@ln, email=@em WHERE id=@id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@em", model.Email?.Trim()         ?? "");
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

        // ── Trigger Org Clearance ─────────────────────────────────────────
        private void TriggerOrgClearance(MySqlConnection conn, string studentNumber)
        {
            var stuCmd = new MySqlCommand(
                "SELECT curriculum_id FROM users WHERE id_number = @sn LIMIT 1", conn);
            stuCmd.Parameters.AddWithValue("@sn", studentNumber);

            int curriculumId = 0;
            using (var r = stuCmd.ExecuteReader())
            {
                if (r.Read())
                    curriculumId = r.IsDBNull(r.GetOrdinal("curriculum_id")) ? 0 : r.GetInt32("curriculum_id");
            }

            var orgCmd = new MySqlCommand(@"
                SELECT position_title
                FROM   organizations
                WHERE  is_active = 1
                  AND  (curriculum_id IS NULL OR curriculum_id = @cid)", conn);
            orgCmd.Parameters.AddWithValue("@cid", curriculumId);

            var positions = new List<string>();
            using (var r = orgCmd.ExecuteReader())
            {
                while (r.Read())
                    positions.Add(r.GetString("position_title"));
            }

            if (!positions.Contains("Class Adviser"))
                positions.Add("Class Adviser");

            foreach (var pos in positions)
            {
                var insertCmd = new MySqlCommand(@"
                    INSERT IGNORE INTO clearance_organization
                        (student_number, position, status, requested_at)
                    VALUES (@sn, @pos, 'Pending', NOW())", conn);
                insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                insertCmd.Parameters.AddWithValue("@pos", pos);
                insertCmd.ExecuteNonQuery();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void LoadAnnouncements(MySqlConnection conn, List<AnnouncementItem> list)
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

    internal static class MySqlCommandExtensions
    {
        public static MySqlCommand Also(this MySqlCommand cmd, Action<MySqlCommand> configure)
        {
            configure(cmd);
            return cmd;
        }
    }
}