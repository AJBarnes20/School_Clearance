using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        private readonly IConfiguration _config;

        public StudentController(IConfiguration config)
        {
            _config = config;
        }

        // ── Dashboard ─────────────────────────────────────────────────────
        public IActionResult Dashboard()
        {
            SetUserViewData();

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            var model = new StudentDashboardViewModel
            {
                StudentName       = ViewData["Email"]?.ToString() ?? "Student",
                SubjectCleared    = 0,
                SubjectIncomplete = 0,
                OrgCleared        = 0,
                TotalSubjects     = 0,
                TotalOrgs         = 0,
                ActivePeriod      = "A.Y. 2025-2026, 2nd Semester",
                Announcements     = new List<AnnouncementItem>()
            };

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                var periodCmd = new MySqlCommand(
                    "SELECT CONCAT('A.Y. ', year_label, ', ', semester) " +
                    "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                var period = periodCmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(period)) model.ActivePeriod = period;

                var subjCmd = new MySqlCommand(@"
                    SELECT
                        COUNT(*)                                                    AS total,
                        SUM(CASE WHEN status = 'Cleared'  THEN 1 ELSE 0 END)       AS cleared,
                        SUM(CASE WHEN status != 'Cleared' THEN 1 ELSE 0 END)       AS incomplete
                    FROM clearance_subjects
                    WHERE student_number = @sn", conn);
                subjCmd.Parameters.AddWithValue("@sn", studentNumber);

                using var sr = subjCmd.ExecuteReader();
                if (sr.Read() && !sr.IsDBNull(0))
                {
                    model.TotalSubjects     = sr.GetInt32("total");
                    model.SubjectCleared    = sr.IsDBNull(sr.GetOrdinal("cleared"))
                                                ? 0 : Convert.ToInt32(sr["cleared"]);
                    model.SubjectIncomplete = sr.IsDBNull(sr.GetOrdinal("incomplete"))
                                                ? 0 : Convert.ToInt32(sr["incomplete"]);
                }
                sr.Close();

                var orgCmd = new MySqlCommand(@"
                    SELECT
                        COUNT(*)                                                   AS total,
                        SUM(CASE WHEN co.status = 'Cleared' THEN 1 ELSE 0 END)    AS cleared
                    FROM clearance_organization co
                    WHERE co.student_number = @sn", conn);
                orgCmd.Parameters.AddWithValue("@sn", studentNumber);

                using var or2 = orgCmd.ExecuteReader();
                if (or2.Read() && !or2.IsDBNull(0))
                {
                    model.TotalOrgs  = or2.GetInt32("total");
                    model.OrgCleared = or2.IsDBNull(or2.GetOrdinal("cleared"))
                                        ? 0 : Convert.ToInt32(or2["cleared"]);
                }
                or2.Close();

                var annCmd = new MySqlCommand(@"
                    SELECT title, body AS content, type, posted_at AS created_at
                    FROM announcements
                    ORDER BY posted_at DESC
                    LIMIT 10", conn);

                using var ar = annCmd.ExecuteReader();
                while (ar.Read())
                {
                    model.Announcements.Add(new AnnouncementItem
                    {
                        Title   = ar.GetString("title"),
                        Content = ar.GetString("content"),
                        Type    = ar.IsDBNull(ar.GetOrdinal("type")) ? "General" : ar.GetString("type"),
                        Date    = ar.GetDateTime("created_at").ToString("MMMM d, yyyy")
                    });
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Dashboard error: " + ex.Message;
            }

            return View(model);
        }

        // ── Subjects Offered ──────────────────────────────────────────────
        public IActionResult SubjectsOffered(int? periodId)
        {
            SetUserViewData();
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model = new SubjectOfferedViewModel();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Resolve the period to use (explicit selection or active)
                int activePeriodId = 0;
                if (periodId.HasValue && periodId.Value > 0)
                {
                    var labelCmd = new MySqlCommand(
                        "SELECT id, CONCAT('A.Y. ', year_label, ', ', semester) AS lbl " +
                        "FROM academic_periods WHERE id = @pid LIMIT 1", conn);
                    labelCmd.Parameters.AddWithValue("@pid", periodId.Value);
                    using var lr = labelCmd.ExecuteReader();
                    if (lr.Read())
                    {
                        activePeriodId = lr.GetInt32("id");
                        var lbl = lr.IsDBNull(1) ? "" : lr.GetString(1);
                        if (!string.IsNullOrEmpty(lbl)) model.ActivePeriod = lbl;
                    }
                }
                else
                {
                    var activeCmd = new MySqlCommand(
                        "SELECT id, CONCAT('A.Y. ', year_label, ', ', semester) AS lbl " +
                        "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                    using var ar = activeCmd.ExecuteReader();
                    if (ar.Read())
                    {
                        activePeriodId = ar.GetInt32("id");
                        var lbl = ar.IsDBNull(1) ? "" : ar.GetString(1);
                        if (!string.IsNullOrEmpty(lbl)) model.ActivePeriod = lbl;
                    }
                }
                model.ActivePeriodId = activePeriodId;

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                // Filter by period when one is selected; otherwise fall back to is_active=1
                var cmd = new MySqlCommand(@"
                    SELECT
                        so.mis_code                                             AS Id,
                        so.mis_code                                             AS MisCode,
                        s.subject_code                                          AS SubjectCode,
                        s.description                                           AS Description,
                        COALESCE(CONCAT(u.first_name, ' ', u.last_name), 'TBA') AS InstructorName,
                        COALESCE(cs.status, '')                                 AS EnrolledStatus
                    FROM subject_offerings  so
                    JOIN      subjects      s   ON s.id        = so.subject_id
                    LEFT JOIN users         u   ON u.id        = so.user_id
                    LEFT JOIN clearance_subjects cs
                           ON cs.mis_code       = so.mis_code
                          AND cs.student_number = @sn
                          AND (@pid = 0 OR cs.period_id = @pid)
                    WHERE (@pid > 0 AND so.period_id = @pid)
                       OR (@pid = 0 AND so.is_active  = 1)
                    ORDER BY s.subject_code", conn);
                cmd.Parameters.AddWithValue("@sn", studentNumber);
                cmd.Parameters.AddWithValue("@pid", activePeriodId);

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var enrolledStatus = r.IsDBNull(r.GetOrdinal("EnrolledStatus"))
                                            ? "" : r.GetString("EnrolledStatus");
                    model.AvailableSubjects.Add(new SubjectItem
                    {
                        Id              = r.GetString("Id"),
                        MisCode         = r.GetString("MisCode"),
                        SubjectCode     = r.GetString("SubjectCode"),
                        Description     = r.GetString("Description"),
                        InstructorName  = r.GetString("InstructorName"),
                        AlreadyEnrolled = !string.IsNullOrEmpty(enrolledStatus),
                        EnrolledStatus  = enrolledStatus
                    });
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not load subjects: " + ex.Message;
            }

            return View(model);
        }

        // ── Confirm Subjects POST ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ConfirmSubjects(string selectedSubjects, int? periodId)
        {
            if (string.IsNullOrWhiteSpace(selectedSubjects))
                return RedirectToAction(nameof(SubjectsOffered));

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Declared outside try so both catch and the final redirect can read it
            int resolvedPeriodId = 0;

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                {
                    TempData["Error"] = "Student record not found.";
                    return RedirectToAction(nameof(SubjectsOffered));
                }

                // Use the period the student selected; fall back to active period
                if (periodId.HasValue && periodId.Value > 0)
                {
                    var checkCmd = new MySqlCommand(
                        "SELECT id FROM academic_periods WHERE id = @pid LIMIT 1", conn);
                    checkCmd.Parameters.AddWithValue("@pid", periodId.Value);
                    var found = checkCmd.ExecuteScalar();
                    if (found != null) resolvedPeriodId = Convert.ToInt32(found);
                }
                if (resolvedPeriodId == 0)
                {
                    var activeCmd = new MySqlCommand(
                        "SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                    resolvedPeriodId = Convert.ToInt32(activeCmd.ExecuteScalar() ?? 1);
                }

                foreach (var code in selectedSubjects.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO clearance_subjects
                            (student_number, mis_code, status, period_id)
                        SELECT @sn, @mc, 'Pending', @pid
                        WHERE NOT EXISTS (
                            SELECT 1 FROM clearance_subjects
                            WHERE student_number = @sn AND mis_code = @mc
                        )", conn);
                    insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    insertCmd.Parameters.AddWithValue("@mc",  code.Trim());
                    insertCmd.Parameters.AddWithValue("@pid", resolvedPeriodId);
                    insertCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error saving subjects: " + ex.Message;
                return RedirectToAction(nameof(SubjectsOffered), new { periodId });
            }

            return RedirectToAction(nameof(Clearance), new { periodId = resolvedPeriodId });
        }

        // ── Clearance ─────────────────────────────────────────────────────
    public IActionResult Clearance(int? periodId)
{
    SetUserViewData();

    var userId = int.Parse(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    var model = new StudentClearanceViewModel();

    try
    {
        using var conn = DbHelper.GetConnection(_config);
        conn.Open();

        // ── Resolve period ────────────────────────────────────────────────
        int activePeriodId = 0;
        if (periodId.HasValue && periodId.Value > 0)
        {
            var labelCmd = new MySqlCommand(
                "SELECT id, CONCAT('A.Y. ', year_label, ', ', semester) AS lbl " +
                "FROM academic_periods WHERE id = @pid LIMIT 1", conn);
            labelCmd.Parameters.AddWithValue("@pid", periodId.Value);
            using var lr = labelCmd.ExecuteReader();
            if (lr.Read())
            {
                activePeriodId = lr.GetInt32("id");
                var lbl = lr.IsDBNull(1) ? "" : lr.GetString(1);
                if (!string.IsNullOrEmpty(lbl)) ViewData["ActivePeriod"] = lbl;
            }
        }
        else
        {
            var activeCmd = new MySqlCommand(
                "SELECT id, CONCAT('A.Y. ', year_label, ', ', semester) AS lbl " +
                "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
            using var ar = activeCmd.ExecuteReader();
            if (ar.Read())
            {
                activePeriodId = ar.GetInt32("id");
                var lbl = ar.IsDBNull(1) ? "" : ar.GetString(1);
                if (!string.IsNullOrEmpty(lbl)) ViewData["ActivePeriod"] = lbl;
            }
        }
        ViewData["ActivePeriodId"] = activePeriodId;

        // ── Resolve student_number + curriculum_id ────────────────────────
        var stuCmd = new MySqlCommand(
            "SELECT student_number, curriculum_id FROM users WHERE id = @uid LIMIT 1", conn);
        stuCmd.Parameters.AddWithValue("@uid", userId);

        string studentNumber = "";
        int    curriculumId  = 0;

        using (var r = stuCmd.ExecuteReader())
        {
            if (r.Read())
            {
                studentNumber = r.IsDBNull(r.GetOrdinal("student_number"))
                    ? "" : r.GetString("student_number");
                curriculumId = r.IsDBNull(r.GetOrdinal("curriculum_id"))
                    ? 0 : r.GetInt32("curriculum_id");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART A — Subject Clearance rows (filtered by period when available)
        // ════════════════════════════════════════════════════════════════════
        var subjCmd = new MySqlCommand(@"
            SELECT
                cs.mis_code                                                     AS MisCode,
                COALESCE(s.subject_code, cs.mis_code)                          AS SubjectCode,
                COALESCE(s.description, '—')                                   AS Description,
                COALESCE(CONCAT(u.first_name,' ',u.last_name), 'TBA')          AS InstructorName,
                COALESCE(cs.status, 'Pending')                                 AS Status
            FROM clearance_subjects cs
            LEFT JOIN subject_offerings so  ON so.mis_code COLLATE utf8mb4_unicode_ci = cs.mis_code COLLATE utf8mb4_unicode_ci
            LEFT JOIN subjects          s   ON s.id        = so.subject_id
            LEFT JOIN users             u   ON u.id        = so.user_id
            WHERE cs.student_number COLLATE utf8mb4_unicode_ci = @sn
              AND (@pid = 0 OR cs.period_id = @pid)
            ORDER BY cs.mis_code", conn);
        subjCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
        subjCmd.Parameters.AddWithValue("@pid", activePeriodId);

        using (var r = subjCmd.ExecuteReader())
        {
            while (r.Read())
            {
                model.SubjectItems.Add(new StudentClearanceItem
                {
                    MisCode        = r.GetString("MisCode"),
                    SubjectCode    = r.GetString("SubjectCode"),
                    Description    = r.GetString("Description"),
                    InstructorName = r.GetString("InstructorName"),
                    Status         = r.GetString("Status")
                });
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART B — Class Adviser (filtered by period, isolated so it never
        //           blocks subject clearance if period_id column is missing)
        // ════════════════════════════════════════════════════════════════════
        if (curriculumId > 0)
        {
            try
            {
                var advCmd = new MySqlCommand(@"
                    SELECT
                        CONCAT(u.first_name, ' ', u.last_name) AS AdviserName,
                        c.course_code                          AS Course,
                        cu.year_level                          AS YearLevel,
                        cu.section                             AS Section,
                        co.status                              AS CoStatus
                    FROM   organizations o
                    JOIN   users      u   ON u.id   = o.user_id
                    JOIN   curriculum cu  ON cu.id  = o.curriculum_id
                    JOIN   courses    c   ON c.id   = cu.course_id
                    LEFT JOIN clearance_organization co
                           ON co.position       COLLATE utf8mb4_unicode_ci = 'Class Adviser'
                          AND co.student_number COLLATE utf8mb4_unicode_ci = @sn
                          AND (@pid = 0 OR co.period_id = @pid)
                    WHERE  o.curriculum_id  = @cid
                      AND  o.position_title COLLATE utf8mb4_unicode_ci = 'Class Adviser'
                      AND  o.is_active      = 1
                    LIMIT  1", conn);

                advCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                advCmd.Parameters.AddWithValue("@cid", curriculumId);
                advCmd.Parameters.AddWithValue("@pid", activePeriodId);

                using var advRdr = advCmd.ExecuteReader();
                if (advRdr.Read())
                {
                    var yl       = advRdr.IsDBNull(advRdr.GetOrdinal("YearLevel")) ? 0  : advRdr.GetInt32("YearLevel");
                    var ylLabel  = yl switch { 1 => "1st Year", 2 => "2nd Year", 3 => "3rd Year", _ => $"{yl}th Year" };
                    var coStatus = advRdr.IsDBNull(advRdr.GetOrdinal("CoStatus")) ? "" : advRdr.GetString("CoStatus");
                    var course   = advRdr.IsDBNull(advRdr.GetOrdinal("Course"))   ? "" : advRdr.GetString("Course");
                    var section  = advRdr.IsDBNull(advRdr.GetOrdinal("Section"))  ? "" : advRdr.GetString("Section");

                    model.ClassAdviser = new OrganizationSignatory
                    {
                        OrgName         = "Class Adviser",
                        OrgRole         = $"{course} — {ylLabel}{(string.IsNullOrEmpty(section) ? "" : $", Section {section}")}",
                        PersonName      = advRdr.IsDBNull(advRdr.GetOrdinal("AdviserName")) ? "—" : advRdr.GetString("AdviserName"),
                        Status          = string.IsNullOrEmpty(coStatus) ? "" : coStatus,
                        IsSelfSignatory = false
                    };
                }
            }
            catch { /* period_id column may not exist yet — skip adviser status */ }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART C — ALL active org signatory rows except Class Adviser (filtered by period)
        // ════════════════════════════════════════════════════════════════════
        if (!string.IsNullOrEmpty(studentNumber))
        {
            try
            {
                var orgCmd = new MySqlCommand(@"
                    SELECT
                        o.position_title                        AS OrgRole,
                        CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                        o.user_id                              AS SignatoryUserId,
                        co.status                              AS CoStatus
                    FROM   organizations o
                    LEFT JOIN users u  ON u.id = o.user_id
                    LEFT JOIN clearance_organization co
                           ON  co.position       COLLATE utf8mb4_unicode_ci = o.position_title COLLATE utf8mb4_unicode_ci
                          AND  co.student_number COLLATE utf8mb4_unicode_ci = @sn
                          AND  (@pid = 0 OR co.period_id = @pid)
                    WHERE  o.is_active      = 1
                      AND  o.position_title COLLATE utf8mb4_unicode_ci != 'Class Adviser'
                    ORDER BY o.position_title", conn);

                orgCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                orgCmd.Parameters.AddWithValue("@pid", activePeriodId);

                using var or = orgCmd.ExecuteReader();
                while (or.Read())
                {
                    var signatoryUserId = or.IsDBNull(or.GetOrdinal("SignatoryUserId")) ? 0  : or.GetInt32("SignatoryUserId");
                    var coStatus        = or.IsDBNull(or.GetOrdinal("CoStatus"))        ? "" : or.GetString("CoStatus");
                    var role            = or.IsDBNull(or.GetOrdinal("OrgRole"))         ? "" : or.GetString("OrgRole");

                    model.OrgItems.Add(new OrganizationSignatory
                    {
                        OrgName         = role,
                        OrgRole         = role,
                        PersonName      = or.IsDBNull(or.GetOrdinal("PersonName")) ? "—" : or.GetString("PersonName"),
                        Status          = coStatus,
                        IsSelfSignatory = signatoryUserId == userId
                    });
                }
            }
            catch { /* period_id column may not exist yet — org rows show without status */ }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART D — Positions the student personally holds (self-signatory, filtered by period)
        // ════════════════════════════════════════════════════════════════════
        try
        {
            var ssCmd = new MySqlCommand(@"
                SELECT
                    us.position                             AS OrgRole,
                    CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                    co.status                              AS CoStatus
                FROM   user_signatures us
                JOIN   users u ON u.id = us.user_id
                LEFT JOIN clearance_organization co
                       ON  co.position       COLLATE utf8mb4_unicode_ci = us.position COLLATE utf8mb4_unicode_ci
                      AND  co.student_number COLLATE utf8mb4_unicode_ci = @sn
                      AND  (@pid = 0 OR co.period_id = @pid)
                WHERE  us.user_id   = @uid
                  AND  us.position IS NOT NULL", conn);

            ssCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
            ssCmd.Parameters.AddWithValue("@uid", userId);
            ssCmd.Parameters.AddWithValue("@pid", activePeriodId);

            using var ssr = ssCmd.ExecuteReader();
            while (ssr.Read())
            {
                var coStatus = ssr.IsDBNull(ssr.GetOrdinal("CoStatus")) ? "" : ssr.GetString("CoStatus");
                var role     = ssr.IsDBNull(ssr.GetOrdinal("OrgRole"))  ? "" : ssr.GetString("OrgRole");

                if (model.OrgItems.Any(x => x.OrgName.Equals(role, StringComparison.OrdinalIgnoreCase)))
                    continue;

                model.OrgItems.Add(new OrganizationSignatory
                {
                    OrgName         = role,
                    OrgRole         = role,
                    PersonName      = ssr.IsDBNull(ssr.GetOrdinal("PersonName")) ? "—" : ssr.GetString("PersonName"),
                    Status          = coStatus,
                    IsSelfSignatory = true
                });
            }
        }
        catch { /* period_id column may not exist yet */ }
    }
    catch (Exception ex)
    {
        TempData["Error"] = "Could not load clearance: " + ex.Message;
    }

    return View(model);
}

// Redirect old /Student/Organization URLs to the merged page
public IActionResult Organization() => RedirectToAction(nameof(Clearance));
        // ── Request Subject Signature (AJAX POST) ─────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestSubjectSignature([FromBody] RequestSubjectDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.MisCode))
                return Json(new { success = false, error = "Invalid request." });

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                    return Json(new { success = false, error = "Student record not found." });

                var periodCmd = new MySqlCommand(
                    "SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                var periodId = Convert.ToInt32(periodCmd.ExecuteScalar() ?? 1);

                var checkCmd = new MySqlCommand(@"
                    SELECT status FROM clearance_subjects
                    WHERE student_number = @sn AND mis_code = @mis
                    LIMIT 1", conn);
                checkCmd.Parameters.AddWithValue("@sn",  studentNumber);
                checkCmd.Parameters.AddWithValue("@mis", dto.MisCode);
                var existing = checkCmd.ExecuteScalar();

                if (existing != null && existing != DBNull.Value)
                {
                    var existingStatus = existing.ToString() ?? "";
                    if (existingStatus == "Pending")
                        return Json(new { success = false, error = "Request already pending for this subject." });
                    if (existingStatus == "Cleared")
                        return Json(new { success = false, error = "This subject is already cleared." });
                }

                var insertCmd = new MySqlCommand(@"
                    INSERT INTO clearance_subjects
                        (student_number, mis_code, status, period_id)
                    VALUES (@sn, @mis, 'Pending', @pid)
                    ON DUPLICATE KEY UPDATE status = 'Pending'", conn);
                insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                insertCmd.Parameters.AddWithValue("@mis", dto.MisCode);
                insertCmd.Parameters.AddWithValue("@pid", periodId);
                insertCmd.ExecuteNonQuery();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Request Org Signature POST ────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult RequestOrgSignature([FromBody] RequestOrgDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.OrgName))
                return Json(new { success = false, error = "Invalid request." });

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var stuCmd = new MySqlCommand(
                    "SELECT student_number, curriculum_id FROM users WHERE id = @uid LIMIT 1", conn);
                stuCmd.Parameters.AddWithValue("@uid", userId);

                string studentNumber = "";
                int curriculumId = 0;

                using (var r = stuCmd.ExecuteReader())
                {
                    if (!r.Read())
                        return Json(new { success = false, error = "Student record not found." });
                    studentNumber = r.IsDBNull(r.GetOrdinal("student_number")) ? "" : r.GetString("student_number");
                    curriculumId  = r.IsDBNull(r.GetOrdinal("curriculum_id"))  ? 0  : r.GetInt32("curriculum_id");
                }

                // Check if this is a valid org position
                var checkOrgCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM organizations
                    WHERE  position_title = @pos
                      AND  is_active = 1
                      AND  (curriculum_id IS NULL OR curriculum_id = @cid)", conn);
                checkOrgCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                checkOrgCmd.Parameters.AddWithValue("@cid", curriculumId);
                var orgExists = Convert.ToInt32(checkOrgCmd.ExecuteScalar()) > 0;

                // Check if the student holds this position themselves (student signatory)
                var checkSsCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM user_signatures
                    WHERE  user_id   = @uid
                      AND  position  = @pos", conn);
                checkSsCmd.Parameters.AddWithValue("@uid", userId);
                checkSsCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                var isSelfPosition = Convert.ToInt32(checkSsCmd.ExecuteScalar()) > 0;

                if (!orgExists && !isSelfPosition)
                    return Json(new { success = false, error = "You are not allowed to request this position." });

                // Use the period the student is viewing; fall back to active period
                int activePid = dto.PeriodId > 0 ? dto.PeriodId : 0;
                if (activePid == 0)
                {
                    var periodCmd = new MySqlCommand(
                        "SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                    activePid = Convert.ToInt32(periodCmd.ExecuteScalar() ?? 1);
                }

                var existCmd = new MySqlCommand(@"
                    SELECT status FROM clearance_organization
                    WHERE  student_number = @sn
                      AND  position       = @pos
                      AND  period_id      = @pid
                    LIMIT  1", conn);
                existCmd.Parameters.AddWithValue("@sn",  studentNumber);
                existCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                existCmd.Parameters.AddWithValue("@pid", activePid);
                var existStatus = existCmd.ExecuteScalar();

                if (existStatus != null && existStatus != DBNull.Value)
                {
                    var st = existStatus.ToString() ?? "";
                    if (st == "Pending") return Json(new { success = false, error = "Request already pending." });
                    if (st == "Cleared") return Json(new { success = false, error = "Already cleared." });

                    var resetCmd = new MySqlCommand(@"
                        UPDATE clearance_organization
                        SET    status = 'Pending', updated_at = NOW()
                        WHERE  student_number = @sn
                          AND  position       = @pos
                          AND  period_id      = @pid", conn);
                    resetCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    resetCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                    resetCmd.Parameters.AddWithValue("@pid", activePid);
                    resetCmd.ExecuteNonQuery();

                    return Json(new { success = true });
                }

                var insertCmd = new MySqlCommand(@"
                    INSERT INTO clearance_organization
                        (student_number, position, status, period_id)
                    VALUES (@sn, @pos, 'Pending', @pid)", conn);
                insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                insertCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                insertCmd.Parameters.AddWithValue("@pid", activePid);
                insertCmd.ExecuteNonQuery();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Self-Approve / Decline Org Signature ──────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult SelfApproveOrg([FromBody] SelfApproveOrgDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.OrgName))
                return Json(new { success = false, error = "Invalid request." });

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var verifyCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM user_signatures
                    WHERE  user_id  = @uid
                      AND  position = @pos", conn);
                verifyCmd.Parameters.AddWithValue("@uid", userId);
                verifyCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                if (Convert.ToInt32(verifyCmd.ExecuteScalar()) == 0)
                    return Json(new { success = false, error = "You do not hold this position." });

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                    return Json(new { success = false, error = "Student record not found." });

                var selfPeriodCmd = new MySqlCommand(
                    "SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                var selfActivePid = Convert.ToInt32(selfPeriodCmd.ExecuteScalar() ?? 1);

                var checkCmd = new MySqlCommand(@"
                    SELECT status FROM clearance_organization
                    WHERE  student_number = @sn
                      AND  position       = @pos
                      AND  period_id      = @pid
                    LIMIT  1", conn);
                checkCmd.Parameters.AddWithValue("@sn",  studentNumber);
                checkCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                checkCmd.Parameters.AddWithValue("@pid", selfActivePid);
                var existing = checkCmd.ExecuteScalar();

                if (existing == null || existing == DBNull.Value)
                    return Json(new { success = false, error = "No pending request found. Press Request first." });

                if (existing.ToString() != "Pending")
                    return Json(new { success = false, error = "Request is not in Pending state." });

                var newStatus = dto.Approve ? "Cleared" : "Declined";

                var updateCmd = new MySqlCommand(@"
                    UPDATE clearance_organization
                    SET    status = @st, updated_at = NOW()
                    WHERE  student_number = @sn
                      AND  position       = @pos
                      AND  period_id      = @pid", conn);
                updateCmd.Parameters.AddWithValue("@st",  newStatus);
                updateCmd.Parameters.AddWithValue("@sn",  studentNumber);
                updateCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                updateCmd.Parameters.AddWithValue("@pid", selfActivePid);
                updateCmd.ExecuteNonQuery();

                return Json(new { success = true, newStatus });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Profile GET ───────────────────────────────────────────────────
        public IActionResult Profile()
        {
            SetUserViewData();

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model = new StudentProfileViewModel();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // ── Courses ───────────────────────────────────────────────
                try
                {
                    var coursesCmd = new MySqlCommand(
                        "SELECT course_code FROM courses WHERE is_active = 1 ORDER BY course_code", conn);
                    using var cr = coursesCmd.ExecuteReader();
                    while (cr.Read())
                        model.AvailableCourses.Add(cr.GetString("course_code"));
                }
                catch { }

                // ── Sections (derived from curriculum, not the sections table) ──
                try
                {
                    var secCmd = new MySqlCommand(@"
                        SELECT DISTINCT cu.section AS section_name, cu.year_level, c.course_code
                        FROM   curriculum cu
                        JOIN   courses    c  ON c.id = cu.course_id
                        WHERE  cu.section IS NOT NULL AND cu.section != ''
                        ORDER BY c.course_code, cu.year_level, cu.section", conn);
                    using var secR = secCmd.ExecuteReader();
                    while (secR.Read())
                    {
                        model.AvailableSections.Add(new SectionItem
                        {
                            SectionName = secR.GetString("section_name"),
                            YearLevel   = secR.GetInt32("year_level"),
                            CourseCode  = secR.GetString("course_code")
                        });
                    }
                }
                catch { }

                // ── User profile data ─────────────────────────────────────
                var cmd = new MySqlCommand(@"
                    SELECT
                        u.first_name, u.middle_initial,
                        u.last_name,  u.suffix_name, u.email,
                        u.id_number,  u.student_number,
                        u.curriculum_id,
                        c.course_code,
                        cu.year_level,
                        cu.section
                    FROM users u
                    LEFT JOIN curriculum cu ON cu.id = u.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE u.id = @uid LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@uid", userId);

                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        var studentNum = r.IsDBNull(r.GetOrdinal("student_number")) ? null : r.GetString("student_number");
                        var idNumber   = r.IsDBNull(r.GetOrdinal("id_number"))      ? null : r.GetString("id_number");

                        model.StudentId     = studentNum ?? idNumber ?? "";
                        model.FirstName     = r.IsDBNull(r.GetOrdinal("first_name"))     ? "" : r.GetString("first_name");
                        model.MiddleInitial = r.IsDBNull(r.GetOrdinal("middle_initial")) ? "" : r.GetString("middle_initial");
                        model.LastName      = r.IsDBNull(r.GetOrdinal("last_name"))      ? "" : r.GetString("last_name");
                        model.Suffix        = r.IsDBNull(r.GetOrdinal("suffix_name"))    ? "" : r.GetString("suffix_name");
                        model.Email         = r.IsDBNull(r.GetOrdinal("email"))          ? "" : r.GetString("email");
                        model.Course        = r.IsDBNull(r.GetOrdinal("course_code"))    ? "" : r.GetString("course_code");
                        model.Section       = r.IsDBNull(r.GetOrdinal("section"))        ? "" : r.GetString("section");
                        model.Password      = "";

                        if (!r.IsDBNull(r.GetOrdinal("year_level")))
                        {
                            model.YearLevel = r.GetInt32("year_level") switch
                            {
                                1 => "1st Year", 2 => "2nd Year",
                                3 => "3rd Year", _ => "4th Year"
                            };
                        }
                    }
                }

                // ── Positions ─────────────────────────────────────────────
                try
                {
                    var posCmd = new MySqlCommand(@"
                        SELECT position AS role_name
                        FROM   user_signatures
                        WHERE  user_id = @uid AND position IS NOT NULL
                        UNION
                        SELECT position_title AS role_name
                        FROM   organizations
                        WHERE  user_id = @uid AND is_active = 1
                        ORDER BY role_name", conn);
                    posCmd.Parameters.AddWithValue("@uid", userId);
                    using var pr = posCmd.ExecuteReader();
                    while (pr.Read())
                        model.Positions.Add(new OrganizationSignatory
                        {
                            OrgRole = pr.IsDBNull(0) ? "" : pr.GetString(0)
                        });
                }
                catch { }

                // ── Signature ─────────────────────────────────────────────
                try
                {
                    var signatureCmd = new MySqlCommand(@"
                        SELECT signature_data FROM user_signatures
                        WHERE  user_id = @uid
                          AND  signature_data IS NOT NULL AND signature_data != ''
                        LIMIT  1", conn);
                    signatureCmd.Parameters.AddWithValue("@uid", userId);
                    var sig = signatureCmd.ExecuteScalar();
                    if (sig != null && sig != DBNull.Value)
                        model.SignaturePath = sig.ToString();
                }
                catch { }
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Could not connect to database: " + ex.Message;
            }

            return View(model);
        }

        // ── Profile POST ──────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveProfile(StudentProfileViewModel model)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    var hash = BCrypt.Net.BCrypt.HashPassword(model.Password);
                    var cmd  = new MySqlCommand(@"
                        UPDATE users SET
                            first_name = @fn, middle_initial = @mi,
                            last_name  = @ln, suffix_name    = @sx,
                            email      = @em, password       = @pw
                        WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@sx", model.Suffix?.Trim()        ?? "");
                    cmd.Parameters.AddWithValue("@em", model.Email?.Trim()         ?? "");
                    cmd.Parameters.AddWithValue("@pw", hash);
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    var cmd = new MySqlCommand(@"
                        UPDATE users SET
                            first_name = @fn, middle_initial = @mi,
                            last_name  = @ln, suffix_name    = @sx,
                            email      = @em
                        WHERE id = @id", conn);
                    cmd.Parameters.AddWithValue("@fn", model.FirstName?.Trim()     ?? "");
                    cmd.Parameters.AddWithValue("@mi", model.MiddleInitial?.Trim() ?? "");
                    cmd.Parameters.AddWithValue("@ln", model.LastName?.Trim()      ?? "");
                    cmd.Parameters.AddWithValue("@sx", model.Suffix?.Trim()        ?? "");
                    cmd.Parameters.AddWithValue("@em", model.Email?.Trim()         ?? "");
                    cmd.Parameters.AddWithValue("@id", userId);
                    cmd.ExecuteNonQuery();
                }

                var studentNumber = model.StudentId?.Trim() ?? "";
                var courseCode    = model.Course?.Trim()    ?? "";
                var section       = model.Section?.Trim()   ?? "";
                var yearInt = model.YearLevel switch
                {
                    "1st Year" => 1, "2nd Year" => 2,
                    "3rd Year" => 3, "4th Year" => 4, _ => 0
                };

                int curriculumId = 0;
                if (!string.IsNullOrEmpty(courseCode) && yearInt > 0)
                {
                    var courseCmd = new MySqlCommand(
                        "SELECT id FROM courses WHERE course_code = @c LIMIT 1", conn);
                    courseCmd.Parameters.AddWithValue("@c", courseCode);
                    var courseId = Convert.ToInt32(courseCmd.ExecuteScalar() ?? 0);

                    if (courseId > 0)
                    {
                        var findCmd = new MySqlCommand(@"
                            SELECT id FROM curriculum
                            WHERE course_id  = @cid
                              AND year_level = @yl
                              AND section    = @sec
                            LIMIT 1", conn);
                        findCmd.Parameters.AddWithValue("@cid", courseId);
                        findCmd.Parameters.AddWithValue("@yl",  yearInt);
                        findCmd.Parameters.AddWithValue("@sec", section);
                        var existing = findCmd.ExecuteScalar();

                        if (existing != null && existing != DBNull.Value)
                        {
                            curriculumId = Convert.ToInt32(existing);
                        }
                        else
                        {
                            var newCurrCmd = new MySqlCommand(@"
                                INSERT INTO curriculum (course_id, year_level, section)
                                VALUES (@cid, @yl, @sec);
                                SELECT LAST_INSERT_ID();", conn);
                            newCurrCmd.Parameters.AddWithValue("@cid", courseId);
                            newCurrCmd.Parameters.AddWithValue("@yl",  yearInt);
                            newCurrCmd.Parameters.AddWithValue("@sec", section);
                            curriculumId = Convert.ToInt32(newCurrCmd.ExecuteScalar());
                        }
                    }
                }

                // Student fields now live on the users table
                var updateUserCmd = new MySqlCommand(@"
                    UPDATE users SET
                        student_number = @sn,
                        curriculum_id  = @cid
                    WHERE id = @uid", conn);
                updateUserCmd.Parameters.AddWithValue("@sn",  studentNumber);
                updateUserCmd.Parameters.AddWithValue("@cid",
                    curriculumId > 0 ? (object)curriculumId : DBNull.Value);
                updateUserCmd.Parameters.AddWithValue("@uid", userId);
                updateUserCmd.ExecuteNonQuery();

                TempData["ProfileSaved"] = "Profile updated successfully!";
            }
            catch (Exception ex)
            {
                TempData["ProfileSaved"] = "Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Profile));
        }

        // ── Save Signature (AJAX) ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveSignature([FromBody] SaveSignatureDto dto)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    UPDATE user_signatures
                    SET    signature_data = @sd
                    WHERE  user_id = @uid", conn);
                cmd.Parameters.AddWithValue("@sd",  dto.SignatureData ?? "");
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.ExecuteNonQuery();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Download PDF ──────────────────────────────────────────────────
        public IActionResult DownloadPdf(int? periodId)
        {
            SetUserViewData();

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model = new StudentClearancePdfViewModel();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // ── Resolve period ────────────────────────────────────────
                int activePeriodId = 0;
                if (periodId.HasValue && periodId.Value > 0)
                {
                    var labelCmd = new MySqlCommand(
                        "SELECT id, year_label, semester " +
                        "FROM academic_periods WHERE id = @pid LIMIT 1", conn);
                    labelCmd.Parameters.AddWithValue("@pid", periodId.Value);
                    using var lr = labelCmd.ExecuteReader();
                    if (lr.Read())
                    {
                        activePeriodId   = lr.GetInt32("id");
                        var ay           = lr.IsDBNull(1) ? "" : lr.GetString("year_label");
                        var sem          = lr.IsDBNull(2) ? "" : lr.GetString("semester");
                        model.AySemester = $"{ay} / {sem}";
                    }
                }
                else
                {
                    var activeCmd = new MySqlCommand(
                        "SELECT id, year_label, semester " +
                        "FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                    using var ar = activeCmd.ExecuteReader();
                    if (ar.Read())
                    {
                        activePeriodId   = ar.GetInt32("id");
                        var ay           = ar.IsDBNull(1) ? "" : ar.GetString("year_label");
                        var sem          = ar.IsDBNull(2) ? "" : ar.GetString("semester");
                        model.AySemester = $"{ay} / {sem}";
                    }
                }
                if (string.IsNullOrEmpty(model.AySemester))
                    model.AySemester = "—";
                model.ActivePeriodId = activePeriodId;

                // ── Student info ──────────────────────────────────────────
                var infoCmd = new MySqlCommand(@"
                    SELECT
                        CONCAT(u.first_name, ' ', u.last_name) AS full_name,
                        u.student_number,
                        u.curriculum_id,
                        c.course_code,
                        cu.year_level
                    FROM users u
                    LEFT JOIN curriculum cu ON cu.id = u.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE u.id = @uid LIMIT 1", conn);
                infoCmd.Parameters.AddWithValue("@uid", userId);

                string studentNumber = "";
                int    curriculumId  = 0;

                using (var ir = infoCmd.ExecuteReader())
                {
                    if (ir.Read())
                    {
                        model.StudentName = ir.IsDBNull(ir.GetOrdinal("full_name"))      ? "" : ir.GetString("full_name");
                        model.StudentId   = ir.IsDBNull(ir.GetOrdinal("student_number")) ? "" : ir.GetString("student_number");
                        studentNumber     = model.StudentId;
                        curriculumId      = ir.IsDBNull(ir.GetOrdinal("curriculum_id"))  ? 0  : ir.GetInt32("curriculum_id");

                        var course  = ir.IsDBNull(ir.GetOrdinal("course_code")) ? "" : ir.GetString("course_code");
                        var yl      = ir.IsDBNull(ir.GetOrdinal("year_level"))  ? 0  : ir.GetInt32("year_level");
                        var ylLabel = yl switch { 1 => "1st Year", 2 => "2nd Year", 3 => "3rd Year", _ => $"{yl}th Year" };
                        model.CourseYear = $"{course} – {ylLabel}";
                    }
                } // reader closed here — safe to open the next one

                // ── Subject clearances (filtered by period) ───────────────
                var subjCmd = new MySqlCommand(@"
                    SELECT
                        cs.mis_code                                                     AS MisCode,
                        COALESCE(s.subject_code, cs.mis_code)                          AS SubjectCode,
                        COALESCE(s.description, '—')                                   AS Description,
                        COALESCE(CONCAT(u.first_name,' ',u.last_name), 'TBA')          AS InstructorName,
                        COALESCE(cs.status, 'Pending')                                 AS Status,
                        COALESCE(sig.signature_data, '')                               AS SignatureBase64
                    FROM clearance_subjects cs
                    LEFT JOIN subject_offerings so  ON so.mis_code  = cs.mis_code
                    LEFT JOIN subjects          s   ON s.id         = so.subject_id
                    LEFT JOIN users             u   ON u.id         = so.user_id
                    LEFT JOIN user_signatures   sig ON sig.user_id  = so.user_id
                                                   AND sig.position IS NULL
                    WHERE cs.student_number = @sn
                      AND (@pid = 0 OR cs.period_id = @pid)
                    ORDER BY cs.mis_code", conn);
                subjCmd.Parameters.AddWithValue("@sn",  studentNumber);
                subjCmd.Parameters.AddWithValue("@pid", activePeriodId);

                using var sr = subjCmd.ExecuteReader();
                while (sr.Read())
                {
                    model.Subjects.Add(new PdfSubjectItem
                    {
                        MisCode         = sr.IsDBNull(sr.GetOrdinal("MisCode"))         ? "" : sr.GetString("MisCode"),
                        SubjectCode     = sr.IsDBNull(sr.GetOrdinal("SubjectCode"))     ? "" : sr.GetString("SubjectCode"),
                        Description     = sr.IsDBNull(sr.GetOrdinal("Description"))     ? "" : sr.GetString("Description"),
                        InstructorName  = sr.IsDBNull(sr.GetOrdinal("InstructorName"))  ? "" : sr.GetString("InstructorName"),
                        Status          = sr.IsDBNull(sr.GetOrdinal("Status"))          ? "" : sr.GetString("Status"),
                        SignatureBase64 = sr.IsDBNull(sr.GetOrdinal("SignatureBase64")) ? "" : sr.GetString("SignatureBase64")
                    });
                }
                sr.Close();

                // ── Org clearances (all positions, filtered by period) ─────
                try
                {
                    var orgCmd = new MySqlCommand(@"
                        SELECT
                            o.position_title                                            AS OrgName,
                            o.position_title                                            AS Role,
                            COALESCE(CONCAT(u.first_name, ' ', u.last_name), 'TBA')    AS PersonName,
                            COALESCE(co.status, 'None')                                AS Status,
                            COALESCE(sig.signature_data, '')                            AS SignatureBase64,
                            0                                                           AS IsSelfSignatory
                        FROM organizations o
                        LEFT JOIN users             u   ON u.id        = o.user_id
                        LEFT JOIN user_signatures   sig ON sig.user_id = o.user_id
                                                        AND sig.position IS NULL
                        LEFT JOIN clearance_organization co
                               ON co.position      COLLATE utf8mb4_unicode_ci = o.position_title COLLATE utf8mb4_unicode_ci
                              AND co.student_number = @sn
                              AND (@pid = 0 OR co.period_id = @pid)
                        WHERE o.is_active = 1
                          AND (o.curriculum_id IS NULL OR o.curriculum_id = @cid)

                        UNION ALL

                        SELECT
                            us.position                                                 AS OrgName,
                            us.position                                                 AS Role,
                            COALESCE(CONCAT(u2.first_name, ' ', u2.last_name), 'TBA')  AS PersonName,
                            COALESCE(co2.status, 'None')                               AS Status,
                            COALESCE(us.signature_data, '')                            AS SignatureBase64,
                            1                                                           AS IsSelfSignatory
                        FROM user_signatures us
                        JOIN  users u2 ON u2.id = us.user_id
                        LEFT JOIN clearance_organization co2
                               ON co2.position      COLLATE utf8mb4_unicode_ci = us.position COLLATE utf8mb4_unicode_ci
                              AND co2.student_number = @sn
                              AND (@pid = 0 OR co2.period_id = @pid)
                        WHERE us.user_id   = @uid
                          AND us.position IS NOT NULL

                        ORDER BY OrgName", conn);

                    orgCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    orgCmd.Parameters.AddWithValue("@cid", curriculumId > 0 ? (object)curriculumId : DBNull.Value);
                    orgCmd.Parameters.AddWithValue("@uid", userId);
                    orgCmd.Parameters.AddWithValue("@pid", activePeriodId);

                    using var or2 = orgCmd.ExecuteReader();
                    while (or2.Read())
                    {
                        model.Organizations.Add(new PdfOrganizationItem
                        {
                            OrgName         = or2.IsDBNull(or2.GetOrdinal("OrgName"))         ? "—" : or2.GetString("OrgName"),
                            Role            = or2.IsDBNull(or2.GetOrdinal("Role"))            ? "—" : or2.GetString("Role"),
                            PersonName      = or2.IsDBNull(or2.GetOrdinal("PersonName"))      ? "—" : or2.GetString("PersonName"),
                            Status          = or2.IsDBNull(or2.GetOrdinal("Status"))          ? "None" : or2.GetString("Status"),
                            SignatureBase64 = or2.IsDBNull(or2.GetOrdinal("SignatureBase64")) ? ""     : or2.GetString("SignatureBase64"),
                            IsSelfSignatory = or2.GetInt32("IsSelfSignatory") == 1
                        });
                    }
                }
                catch { /* period_id column may not exist yet */ }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not load PDF data: " + ex.Message;
            }

            return View(model);
        }

        // ── Delete Clearance Request (AJAX POST) ──────────────────────────
        // SQL needed (run once in MySQL):
        // CREATE TABLE IF NOT EXISTS clearance_messages (
        //     id INT AUTO_INCREMENT PRIMARY KEY,
        //     sender_id INT NOT NULL,
        //     student_number VARCHAR(50) NOT NULL,
        //     clearance_type VARCHAR(20) NOT NULL,
        //     clearance_key VARCHAR(200) NOT NULL,
        //     message TEXT NOT NULL,
        //     sent_at DATETIME DEFAULT NOW(),
        //     INDEX idx_chat (student_number, clearance_type, clearance_key)
        // );
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeleteClearanceRequest([FromBody] DeleteClearanceDto dto)
        {
            if (dto == null)
                return Json(new { success = false, error = "Invalid request." });

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (dto.Type == "subject")
                {
                    var cmd = new MySqlCommand(@"
                        DELETE FROM clearance_subjects
                        WHERE student_number = @sn AND mis_code = @key AND status = 'Pending'
                          AND (@pid = 0 OR period_id = @pid)", conn);
                    cmd.Parameters.AddWithValue("@sn",  studentNumber);
                    cmd.Parameters.AddWithValue("@key", dto.Key ?? "");
                    cmd.Parameters.AddWithValue("@pid", dto.PeriodId);
                    cmd.ExecuteNonQuery();
                }
                else if (dto.Type == "org" || dto.Type == "adviser")
                {
                    var position = dto.Type == "adviser" ? "Class Adviser" : (dto.Key ?? "");

                    // Resolve which period to delete from
                    int delPid = dto.PeriodId;
                    if (delPid == 0)
                    {
                        var activeCmd = new MySqlCommand(
                            "SELECT id FROM academic_periods WHERE is_active = 1 LIMIT 1", conn);
                        delPid = Convert.ToInt32(activeCmd.ExecuteScalar() ?? 0);
                    }

                    var cmd = new MySqlCommand(@"
                        DELETE FROM clearance_organization
                        WHERE student_number = @sn AND position = @pos AND status = 'Pending'
                          AND (@pid = 0 OR period_id = @pid)", conn);
                    cmd.Parameters.AddWithValue("@sn",  studentNumber);
                    cmd.Parameters.AddWithValue("@pos", position);
                    cmd.Parameters.AddWithValue("@pid", delPid);
                    cmd.ExecuteNonQuery();
                }

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Get Clearance Messages (AJAX GET) ─────────────────────────────
        [HttpGet]
        public IActionResult GetClearanceMessages(string key, string type)
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                var cmd = new MySqlCommand(@"
                    SELECT sender_id, message, sent_at
                    FROM   clearance_messages
                    WHERE  student_number  = @sn
                      AND  clearance_type  = @type
                      AND  clearance_key   = @key
                    ORDER BY sent_at ASC", conn);
                cmd.Parameters.AddWithValue("@sn",   studentNumber);
                cmd.Parameters.AddWithValue("@type", type ?? "");
                cmd.Parameters.AddWithValue("@key",  key  ?? "");

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

        // ── Send Clearance Message (AJAX POST) ────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult SendClearanceMessage([FromBody] SendClearanceMessageDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto?.Message))
                return Json(new { success = false, error = "Message is empty." });

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                var cmd = new MySqlCommand(@"
                    INSERT INTO clearance_messages
                        (sender_id, student_number, clearance_type, clearance_key, message, sent_at)
                    VALUES (@sid, @sn, @type, @key, @msg, NOW())", conn);
                cmd.Parameters.AddWithValue("@sid",  userId);
                cmd.Parameters.AddWithValue("@sn",   studentNumber);
                cmd.Parameters.AddWithValue("@type", dto.ClearanceType ?? "subject");
                cmd.Parameters.AddWithValue("@key",  dto.ClearanceKey  ?? "");
                cmd.Parameters.AddWithValue("@msg",  dto.Message.Trim());
                cmd.ExecuteNonQuery();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        // ── Academic Periods API (student-accessible) ─────────────────────
        [HttpGet("/api/student/periods")]
        public IActionResult GetPeriods()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, year_label, semester, is_active " +
                    "FROM academic_periods ORDER BY id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new
                    {
                        id     = r.GetInt32("id"),
                        ay     = r.GetString("year_label"),
                        sem    = r.GetString("semester"),
                        status = r.GetBoolean("is_active") ? "Active" : "Completed"
                    });
                }
            }
            catch { }
            return Json(items);
        }

        // ── Private helpers ───────────────────────────────────────────────
        private void SetUserViewData()
        {
            var firstName   = User.FindFirst("FirstName")?.Value ?? "";
            var lastName    = User.FindFirst("LastName")?.Value  ?? "";
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";

            ViewData["Email"]       = $"{firstName} {lastName}".Trim();
            ViewData["UserId"]      = "—";
            ViewData["UserCourse"]  = "—";
            ViewData["UserYear"]    = "—";
            ViewData["UserSection"] = "";

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var uid = int.Parse(userIdClaim);

                var cmd = new MySqlCommand(@"
                    SELECT
                        u.student_number,
                        u.id_number,
                        c.course_code,
                        cu.year_level,
                        cu.section
                    FROM users u
                    LEFT JOIN curriculum cu ON cu.id = u.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE u.id = @uid LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@uid", uid);

                using var r = cmd.ExecuteReader();
                if (r.Read())
                {
                    var studentNum = r.IsDBNull(r.GetOrdinal("student_number")) ? null : r.GetString("student_number");
                    var idNumber   = r.IsDBNull(r.GetOrdinal("id_number"))      ? null : r.GetString("id_number");
                    ViewData["UserId"] = studentNum ?? idNumber ?? "—";

                    ViewData["UserCourse"] = r.IsDBNull(r.GetOrdinal("course_code"))
                                                ? "—" : r.GetString("course_code");

                    if (!r.IsDBNull(r.GetOrdinal("year_level")))
                    {
                        ViewData["UserYear"] = r.GetInt32("year_level") switch
                        {
                            1 => "1st Year", 2 => "2nd Year",
                            3 => "3rd Year", _ => $"{r.GetInt32("year_level")}th Year"
                        };
                    }

                    ViewData["UserSection"] = r.IsDBNull(r.GetOrdinal("section"))
                                                ? "" : r.GetString("section");
                }
            }
            catch { }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────
    public class RequestSubjectDto  { public string? MisCode { get; set; } }
    public class SelfApproveOrgDto  { public string? OrgName { get; set; } public bool Approve { get; set; } }
}
