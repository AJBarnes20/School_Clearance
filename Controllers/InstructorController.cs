using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;

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

            // Get the actual employee ID (id_number) from the database
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

        // ── Dashboard ─────────────────────────────────────────────────────
        public IActionResult Dashboard()
        {
            var userId    = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var firstName = User.FindFirst("FirstName")?.Value ?? "";
            var lastName  = User.FindFirst("LastName")?.Value  ?? "";

            var model = new InstructorDashboardViewModel
            {
                EmployeeId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"),
                InstructorId   = userId,
                InstructorName = $"{firstName} {lastName}".Trim(),
                ActivePeriod   = "—",
                Announcements  = new List<AnnouncementItem>()
            };

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var periodCmd = new MySqlCommand(
                    "SELECT CONCAT('A.Y. ', year_label, ', ', semester) " +
                    "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                var period = periodCmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(period)) model.ActivePeriod = period;

                var subjCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM subject_offerings
                    WHERE user_id = @uid
                    AND period_id = (SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1)", conn);
                subjCmd.Parameters.AddWithValue("@uid", userId);
                model.SubjectAssigned = Convert.ToInt32(subjCmd.ExecuteScalar() ?? 0);

                var statsCmd = new MySqlCommand(@"
                    SELECT
                        COUNT(*)                                                    AS total,
                        SUM(CASE WHEN cs.status = 'Cleared'  THEN 1 ELSE 0 END)   AS cleared,
                        SUM(CASE WHEN cs.status = 'Pending'  THEN 1 ELSE 0 END)   AS pending
                    FROM clearance_subjects cs
                    JOIN subject_offerings so ON so.mis_code = cs.mis_code
                    WHERE so.user_id = @uid
                    AND so.period_id = (SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1)", conn);
                statsCmd.Parameters.AddWithValue("@uid", userId);

                using var sr = statsCmd.ExecuteReader();
                if (sr.Read() && !sr.IsDBNull(0))
                {
                    model.TotalStudents   = sr.GetInt32("total");
                    model.ClearedStudents = sr.IsDBNull(sr.GetOrdinal("cleared")) ? 0 : Convert.ToInt32(sr["cleared"]);
                    model.PendingStudents = sr.IsDBNull(sr.GetOrdinal("pending")) ? 0 : Convert.ToInt32(sr["pending"]);
                }
                sr.Close();

                LoadAnnouncements(conn, model.Announcements);
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(model);
        }

        // ── Assigned Subjects ─────────────────────────────────────────────
        public IActionResult SubjectOfferings()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<SubjectOfferingDto>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    SELECT
                        so.mis_code    AS MisCode,
                        s.subject_code AS SubjectCode,
                        s.description  AS Description,
                        s.lab_units    AS LabUnit,
                        s.lec_units    AS LecUnit
                    FROM subject_offerings so
                    JOIN subjects s ON s.id = so.subject_id
                    WHERE so.user_id = @uid
                    AND so.period_id = (SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1)
                    ORDER BY s.subject_code", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

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
        public IActionResult SubjectClearance()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<ClearanceRequest>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

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
                        cs.student_number                                       AS StudentNumber
                    FROM clearance_subjects cs
                    JOIN subject_offerings so  ON so.mis_code        = cs.mis_code
                    JOIN subjects          s   ON s.id               = so.subject_id
                    JOIN users             stu ON stu.student_number = cs.student_number
                    LEFT JOIN curriculum   cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses      c   ON c.id               = cu.course_id
                    WHERE so.user_id  = @uid
                      AND cs.status   = 'Pending'
                    ORDER BY cs.id", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

                using var r = cmd.ExecuteReader();
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
                        StudentNumber = r.GetString("StudentNumber")
                    });
                }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = ex.Message;
            }

            return View(items);
        }

        // ── Approve Subject Clearance ─────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Approve(int id)
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
                    "UPDATE clearance_subjects SET status = 'Cleared' WHERE id = @id", conn);
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

            return RedirectToAction(nameof(SubjectClearance));
        }

        // ── Decline Subject Clearance ─────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Decline(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE clearance_subjects SET status = 'Declined' WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                TempData["SuccessMessage"] = "Student clearance declined.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(SubjectClearance));
        }

        // ── Organization Requests ─────────────────────────────────────────
        public IActionResult Organization()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<OrganizationRequest>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // PATH A: instructor is a direct org signatory
                var pathACmd = new MySqlCommand(@"
                    SELECT
                        co.id                                                   AS Id,
                        o.position_title                                        AS Position,
                        CONCAT(stu.first_name, ' ', stu.last_name)             AS StudentName,
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
                    JOIN   users                     stu ON stu.student_number = co.student_number
                    LEFT JOIN curriculum             cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses                c   ON c.id               = cu.course_id
                    WHERE  co.status = 'Pending'
                    ORDER BY co.id DESC", conn);
                pathACmd.Parameters.AddWithValue("@uid", userId);

                using (var r1 = pathACmd.ExecuteReader())
                {
                    while (r1.Read())
                    {
                        items.Add(new OrganizationRequest
                        {
                            Id          = r1.GetInt32("Id"),
                            Position    = r1.IsDBNull(r1.GetOrdinal("Position"))    ? "—" : r1.GetString("Position"),
                            StudentName = r1.IsDBNull(r1.GetOrdinal("StudentName")) ? "—" : r1.GetString("StudentName"),
                            Course      = r1.IsDBNull(r1.GetOrdinal("Course"))      ? "—" : r1.GetString("Course"),
                            Status      = r1.GetString("Status")
                        });
                    }
                }

                // PATH B: instructor is a Class Adviser
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
                            COALESCE(
                                CONCAT(c.course_code, ' ', cu.year_level, cu.section),
                                '—'
                            )                                                       AS Course,
                            COALESCE(co.status, 'Pending')                         AS Status
                        FROM   clearance_organization   co
                        JOIN   users                     stu
                                   ON  stu.student_number  = co.student_number
                                  AND  stu.curriculum_id  IN ({inList})
                        LEFT JOIN curriculum             cu  ON cu.id = stu.curriculum_id
                        LEFT JOIN courses                c   ON c.id  = cu.course_id
                        WHERE  co.position = 'Class Adviser'
                          AND  co.status   = 'Pending'
                        ORDER BY co.id DESC", conn);

                    using var r2 = pathBCmd.ExecuteReader();
                    while (r2.Read())
                    {
                        var rowId = r2.GetInt32("Id");
                        if (items.Any(x => x.Id == rowId)) continue;

                        items.Add(new OrganizationRequest
                        {
                            Id          = rowId,
                            Position    = "Class Adviser",
                            StudentName = r2.IsDBNull(r2.GetOrdinal("StudentName")) ? "—" : r2.GetString("StudentName"),
                            Course      = r2.IsDBNull(r2.GetOrdinal("Course"))      ? "—" : r2.GetString("Course"),
                            Status      = r2.GetString("Status")
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
        public IActionResult ApproveOrg(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("UPDATE clearance_organization SET status = 'Cleared' WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["SuccessMessage"] = "Student cleared successfully.";
            }
            catch (Exception ex) { TempData["ErrorMessage"] = "Error: " + ex.Message; }

            return RedirectToAction(nameof(Organization));
        }

        // ── DeclineOrg POST ───────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeclineOrg(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("UPDATE clearance_organization SET status = 'Declined' WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["SuccessMessage"] = "Request declined.";
            }
            catch (Exception ex) { TempData["ErrorMessage"] = "Error: " + ex.Message; }

            return RedirectToAction(nameof(Organization));
        }

        // ── Signed Clearance ──────────────────────────────────────────────
        public IActionResult SignedClearance()
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
                        CASE WHEN cs.status = 'Cleared' THEN 'Approved' ELSE 'Declined' END AS Status
                    FROM clearance_subjects cs
                    JOIN subject_offerings so  ON so.mis_code        = cs.mis_code
                    JOIN subjects          s   ON s.id               = so.subject_id
                    JOIN users             stu ON stu.student_number = cs.student_number
                    LEFT JOIN curriculum   cu  ON cu.id              = stu.curriculum_id
                    LEFT JOIN courses      c   ON c.id               = cu.course_id
                    WHERE so.user_id = @uid
                    {statusFilter}
                    ORDER BY cs.id DESC", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

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
                        Status        = r.GetString("Status")
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
                           u.id_number, sig.signature_data
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

        // ── Trigger Org Clearance (called when all subjects cleared) ──────
        private void TriggerOrgClearance(MySqlConnection conn, string studentNumber)
        {
            var stuCmd = new MySqlCommand(
                "SELECT curriculum_id FROM users WHERE student_number = @sn LIMIT 1", conn);
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
                        (student_number, position, status)
                    VALUES (@sn, @pos, 'Pending')", conn);
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