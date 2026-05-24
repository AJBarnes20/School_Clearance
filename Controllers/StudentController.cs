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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                var periodCmd = new MySqlCommand(
                    "SELECT CONCAT(semester, ', A.Y. ', year_label) " +
                    "FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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
                        "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
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
                        "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
                        "FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                // Show ALL subject offerings regardless of period —
                // only the clearance records are period-scoped
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
                return RedirectToAction(nameof(Clearance));

            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Declared outside try so both catch and the final redirect can read it
            int resolvedPeriodId = 0;

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand(
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                {
                    TempData["Error"] = "Student record not found.";
                    return RedirectToAction(nameof(Clearance));
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
                        "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
                    resolvedPeriodId = Convert.ToInt32(activeCmd.ExecuteScalar() ?? 1);
                }

                foreach (var code in selectedSubjects.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var insertCmd = new MySqlCommand(@"
                        INSERT IGNORE INTO clearance_subjects
                            (student_number, mis_code, status, period_id)
                        VALUES (@sn, @mc, 'Pending', @pid)", conn);
                    insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    insertCmd.Parameters.AddWithValue("@mc",  code.Trim());
                    insertCmd.Parameters.AddWithValue("@pid", resolvedPeriodId);
                    insertCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error saving subjects: " + ex.Message;
                return RedirectToAction(nameof(Clearance), new { periodId });
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
                "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
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
                "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl " +
                "FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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
            "SELECT COALESCE(student_number, id_number) AS student_number, curriculum_id FROM users WHERE id = @uid LIMIT 1", conn);
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
        // STEP 1 — Load clearance statuses for the selected period only.
        //           Each period is fully independent — a "Cleared" in period A
        //           must never bleed into period B.
        //           If period_id column doesn't exist yet, dictionary stays
        //           empty and every position shows "—" (correct: needs migration).
        // ════════════════════════════════════════════════════════════════════
        var orgStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(studentNumber))
        {
            if (activePeriodId > 0)
            {
                // Specific period selected — only load statuses for that period.
                // No fallback: if the column doesn't exist, show "—" for everything.
                try
                {
                    var stCmd = new MySqlCommand(@"
                        SELECT position, status
                        FROM   clearance_organization
                        WHERE  student_number = @sn
                          AND  period_id      = @pid
                        ORDER BY id ASC", conn);
                    stCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                    stCmd.Parameters.AddWithValue("@pid", activePeriodId);
                    using var sr = stCmd.ExecuteReader();
                    while (sr.Read())
                        orgStatuses[sr.GetString("position")] = sr.GetString("status");
                }
                catch { }
            }
            else
            {
                // No period resolved (no academic periods in DB) —
                // show the most recent status per position as a best-effort fallback.
                try
                {
                    var stCmd = new MySqlCommand(@"
                        SELECT position, status
                        FROM   clearance_organization
                        WHERE  student_number = @sn
                        ORDER BY id ASC", conn);
                    stCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                    using var sr = stCmd.ExecuteReader();
                    while (sr.Read())
                        orgStatuses[sr.GetString("position")] = sr.GetString("status");
                }
                catch { }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART B — Class Adviser (positions ALWAYS show; status from dictionary)
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
                        cu.section                             AS Section
                    FROM   organizations o
                    JOIN   users      u  ON u.id  = o.user_id
                    JOIN   curriculum cu ON cu.id = o.curriculum_id
                    JOIN   courses    c  ON c.id  = cu.course_id
                    WHERE  o.curriculum_id  = @cid
                      AND  o.position_title COLLATE utf8mb4_unicode_ci = 'Class Adviser'
                      AND  COALESCE(o.is_active, 1) = 1
                    LIMIT  1", conn);
                advCmd.Parameters.AddWithValue("@cid", curriculumId);

                using var advRdr = advCmd.ExecuteReader();
                if (advRdr.Read())
                {
                    var yl      = advRdr.IsDBNull(advRdr.GetOrdinal("YearLevel")) ? 0  : advRdr.GetInt32("YearLevel");
                    var ylLabel = yl switch { 1 => "1st Year", 2 => "2nd Year", 3 => "3rd Year", _ => $"{yl}th Year" };
                    var course  = advRdr.IsDBNull(advRdr.GetOrdinal("Course"))   ? "" : advRdr.GetString("Course");
                    var section = advRdr.IsDBNull(advRdr.GetOrdinal("Section"))  ? "" : advRdr.GetString("Section");

                    model.ClassAdviser = new OrganizationSignatory
                    {
                        OrgName         = "Class Adviser",
                        OrgRole         = $"{course} — {ylLabel}{(string.IsNullOrEmpty(section) ? "" : $", Section {section}")}",
                        PersonName      = advRdr.IsDBNull(advRdr.GetOrdinal("AdviserName")) ? "—" : advRdr.GetString("AdviserName"),
                        Status          = orgStatuses.TryGetValue("Class Adviser", out var advSt) ? advSt : "",
                        IsSelfSignatory = false
                    };
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART C — ALL active org positions except Class Adviser
        //           (positions ALWAYS show; status from dictionary)
        // ════════════════════════════════════════════════════════════════════
        if (!string.IsNullOrEmpty(studentNumber))
        {
            try
            {
                var orgCmd = new MySqlCommand(@"
                    SELECT
                        o.position_title                        AS OrgRole,
                        CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                        o.user_id                              AS SignatoryUserId
                    FROM   organizations o
                    LEFT JOIN users u ON u.id = o.user_id
                    WHERE  COALESCE(o.is_active, 1) = 1
                      AND  o.position_title COLLATE utf8mb4_unicode_ci != 'Class Adviser'
                    ORDER BY o.position_title", conn);

                using var or = orgCmd.ExecuteReader();
                while (or.Read())
                {
                    var signatoryUserId = or.IsDBNull(or.GetOrdinal("SignatoryUserId")) ? 0  : or.GetInt32("SignatoryUserId");
                    var role            = or.IsDBNull(or.GetOrdinal("OrgRole"))         ? "" : or.GetString("OrgRole");

                    model.OrgItems.Add(new OrganizationSignatory
                    {
                        OrgName         = role,
                        OrgRole         = role,
                        PersonName      = or.IsDBNull(or.GetOrdinal("PersonName")) ? "—" : or.GetString("PersonName"),
                        Status          = orgStatuses.TryGetValue(role, out var orgSt) ? orgSt : "",
                        IsSelfSignatory = signatoryUserId == userId
                    });
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════
        // PART C2 — Student org signatories (SSG positions in user_signatures)
        //            Shows for ALL students, not just the position holder.
        // ════════════════════════════════════════════════════════════════════
        try
        {
            var stuSigCmd = new MySqlCommand(@"
                SELECT
                    us.position                             AS OrgRole,
                    CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                    us.user_id                             AS SignatoryUserId
                FROM   user_signatures us
                JOIN   users u ON u.id = us.user_id AND u.is_active = 1
                WHERE  us.position IS NOT NULL AND us.position != ''", conn);
            using var stuSigRdr = stuSigCmd.ExecuteReader();
            while (stuSigRdr.Read())
            {
                var signatoryUserId = stuSigRdr.IsDBNull(stuSigRdr.GetOrdinal("SignatoryUserId")) ? 0 : stuSigRdr.GetInt32("SignatoryUserId");
                var role            = stuSigRdr.IsDBNull(stuSigRdr.GetOrdinal("OrgRole"))         ? "" : stuSigRdr.GetString("OrgRole");
                if (string.IsNullOrEmpty(role)) continue;
                if (model.OrgItems.Any(x => x.OrgName.Equals(role, StringComparison.OrdinalIgnoreCase)))
                    continue;
                model.OrgItems.Add(new OrganizationSignatory
                {
                    OrgName         = role,
                    OrgRole         = role,
                    PersonName      = stuSigRdr.IsDBNull(stuSigRdr.GetOrdinal("PersonName")) ? "—" : stuSigRdr.GetString("PersonName"),
                    Status          = orgStatuses.TryGetValue(role, out var stuSigSt) ? stuSigSt : "",
                    IsSelfSignatory = signatoryUserId == userId
                });
            }
        }
        catch { }

        // ════════════════════════════════════════════════════════════════════
        // PART D — Positions the student personally holds (self-signatory)
        //           (positions ALWAYS show; status from dictionary)
        // ════════════════════════════════════════════════════════════════════
        try
        {
            var ssCmd = new MySqlCommand(@"
                SELECT
                    us.position                             AS OrgRole,
                    CONCAT(u.first_name, ' ', u.last_name) AS PersonName
                FROM   user_signatures us
                JOIN   users u ON u.id = us.user_id
                WHERE  us.user_id  = @uid
                  AND  us.position IS NOT NULL", conn);
            ssCmd.Parameters.AddWithValue("@uid", userId);

            using var ssr = ssCmd.ExecuteReader();
            while (ssr.Read())
            {
                var role = ssr.IsDBNull(ssr.GetOrdinal("OrgRole")) ? "" : ssr.GetString("OrgRole");
                if (model.OrgItems.Any(x => x.OrgName.Equals(role, StringComparison.OrdinalIgnoreCase)))
                    continue;

                model.OrgItems.Add(new OrganizationSignatory
                {
                    OrgName         = role,
                    OrgRole         = role,
                    PersonName      = ssr.IsDBNull(ssr.GetOrdinal("PersonName")) ? "—" : ssr.GetString("PersonName"),
                    Status          = orgStatuses.TryGetValue(role, out var ssSt) ? ssSt : "",
                    IsSelfSignatory = true
                });
            }
        }
        catch { }
    }
    catch (Exception ex)
    {
        TempData["Error"] = "Could not load clearance: " + ex.Message;
    }

    // ── Load available subjects for the Add Subject panel ─────────────
    var available = new List<SubjectItem>();
    try
    {
        var snForSubj = model.SubjectItems.Count > 0
            ? "" : "";
        using var conn2 = DbHelper.GetConnection(_config);
        conn2.Open();

        var snCmd2 = new MySqlCommand("SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn2);
        snCmd2.Parameters.AddWithValue("@uid", userId);
        var sn2 = snCmd2.ExecuteScalar()?.ToString() ?? "";

        var pid2 = (int)(ViewData["ActivePeriodId"] ?? 0);
        var availCmd = new MySqlCommand(@"
            SELECT so.mis_code AS MisCode, s.subject_code AS SubjectCode,
                   s.description AS Description,
                   COALESCE(CONCAT(u.first_name,' ',u.last_name),'TBA') AS InstructorName,
                   COALESCE(cs.status,'') AS EnrolledStatus
            FROM subject_offerings so
            JOIN subjects s ON s.id = so.subject_id
            LEFT JOIN users u ON u.id = so.user_id
            LEFT JOIN clearance_subjects cs ON cs.mis_code = so.mis_code AND cs.student_number = @sn2
                   AND (@pid2 = 0 OR cs.period_id = @pid2)
            ORDER BY s.subject_code", conn2);
        availCmd.Parameters.AddWithValue("@sn2", sn2);
        availCmd.Parameters.AddWithValue("@pid2", pid2);
        using var ar = availCmd.ExecuteReader();
        while (ar.Read())
        {
            var st = ar.IsDBNull(ar.GetOrdinal("EnrolledStatus")) ? "" : ar.GetString("EnrolledStatus");
            available.Add(new SubjectItem {
                Id              = ar.GetString("MisCode"),
                MisCode         = ar.GetString("MisCode"),
                SubjectCode     = ar.GetString("SubjectCode"),
                Description     = ar.GetString("Description"),
                InstructorName  = ar.GetString("InstructorName"),
                AlreadyEnrolled = !string.IsNullOrEmpty(st),
                EnrolledStatus  = st
            });
        }
    }
    catch { }
    ViewBag.AvailableSubjects = available;

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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                    return Json(new { success = false, error = "Student record not found." });

                var periodCmd = new MySqlCommand(
                    "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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
                    "SELECT COALESCE(student_number, id_number) AS student_number, curriculum_id FROM users WHERE id = @uid LIMIT 1", conn);
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
                      AND  COALESCE(is_active, 1) = 1
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

                // Also allow if position exists as a student signatory (SSG roles in user_signatures)
                var checkStudentSigCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM user_signatures
                    WHERE  position = @pos AND position IS NOT NULL AND position != ''", conn);
                checkStudentSigCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                var isStudentSigPosition = Convert.ToInt32(checkStudentSigCmd.ExecuteScalar()) > 0;

                if (!orgExists && !isSelfPosition && !isStudentSigPosition)
                    return Json(new { success = false, error = "You are not allowed to request this position." });

                // Resolve the period for this request
                int activePid = dto.PeriodId > 0 ? dto.PeriodId : 0;
                if (activePid == 0)
                {
                    var periodCmd = new MySqlCommand(
                        "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
                    activePid = Convert.ToInt32(periodCmd.ExecuteScalar() ?? 1);
                }

                // Probe whether period_id column exists — determines which query path to use
                bool hasPeriodCol = false;
                try
                {
                    new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn)
                        .ExecuteNonQuery();
                    hasPeriodCol = true;
                }
                catch { }

                if (hasPeriodCol)
                {
                    // ── Period-aware path: each academic year is independent ──────
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
                        if (st == "Pending") return Json(new { success = false, error = "Request already pending for this period." });
                        if (st == "Cleared") return Json(new { success = false, error = "Already cleared for this period." });

                        // Declined → allow re-request: reset to Pending
                        var resetCmd = new MySqlCommand(@"
                            UPDATE clearance_organization
                            SET    status = 'Pending'
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
                }
                else
                {
                    // ── Legacy path: period_id column not yet added ───────────────
                    var existCmd = new MySqlCommand(@"
                        SELECT status FROM clearance_organization
                        WHERE  student_number = @sn
                          AND  position       = @pos
                        LIMIT  1", conn);
                    existCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    existCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                    var existStatus = existCmd.ExecuteScalar();

                    if (existStatus != null && existStatus != DBNull.Value)
                    {
                        var st = existStatus.ToString() ?? "";
                        if (st == "Pending") return Json(new { success = false, error = "Request already pending." });
                        if (st == "Cleared") return Json(new { success = false, error = "Already cleared." });

                        var resetCmd = new MySqlCommand(@"
                            UPDATE clearance_organization
                            SET    status = 'Pending'
                            WHERE  student_number = @sn
                              AND  position       = @pos", conn);
                        resetCmd.Parameters.AddWithValue("@sn",  studentNumber);
                        resetCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                        resetCmd.ExecuteNonQuery();
                        return Json(new { success = true });
                    }

                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO clearance_organization (student_number, position, status)
                        VALUES (@sn, @pos, 'Pending')", conn);
                    insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    insertCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                    insertCmd.ExecuteNonQuery();
                }

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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (string.IsNullOrEmpty(studentNumber))
                    return Json(new { success = false, error = "Student record not found." });

                var selfPeriodCmd = new MySqlCommand(
                    "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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
                    SET    status = @st
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
                        "SELECT course_code FROM courses ORDER BY course_code", conn);
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

                // ── Positions from user_signatures ────────────────────────
                try
                {
                    var pos1Cmd = new MySqlCommand(
                        "SELECT position FROM user_signatures " +
                        "WHERE user_id = @uid AND position IS NOT NULL AND position != '' " +
                        "ORDER BY position", conn);
                    pos1Cmd.Parameters.AddWithValue("@uid", userId);
                    using var pr1 = pos1Cmd.ExecuteReader();
                    while (pr1.Read())
                        model.Positions.Add(new OrganizationSignatory
                            { OrgRole = pr1.IsDBNull(0) ? "" : pr1.GetString(0) });
                }
                catch { }

                // ── Positions from organizations (staff/instructor signatories) ──
                try
                {
                    var pos2Cmd = new MySqlCommand(
                        "SELECT position_title FROM organizations " +
                        "WHERE user_id = @uid AND is_active = 1 AND position_title IS NOT NULL " +
                        "ORDER BY position_title", conn);
                    pos2Cmd.Parameters.AddWithValue("@uid", userId);
                    using var pr2 = pos2Cmd.ExecuteReader();
                    while (pr2.Read())
                    {
                        var pt = pr2.IsDBNull(0) ? "" : pr2.GetString(0);
                        if (!string.IsNullOrEmpty(pt) &&
                            !model.Positions.Any(p => p.OrgRole.Equals(pt, StringComparison.OrdinalIgnoreCase)))
                            model.Positions.Add(new OrganizationSignatory { OrgRole = pt });
                    }
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

                // INSERT creates the row if it doesn't exist yet;
                // ON DUPLICATE KEY UPDATE preserves any existing position assignment.
                var cmd = new MySqlCommand(@"
                    INSERT INTO user_signatures (user_id, signature_data)
                    VALUES (@uid, @sd)
                    ON DUPLICATE KEY UPDATE signature_data = @sd", conn);
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

        // ── Signed Clearance (Student as Org Officer) ────────────────────
        public IActionResult SignedClearance(int? periodId)
        {
            SetUserViewData();
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var model  = new StudentSignedClearanceViewModel();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Resolve period
                int pid = 0;
                if (periodId.HasValue && periodId.Value > 0)
                {
                    var lCmd = new MySqlCommand(
                        "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl FROM academic_periods WHERE id = @pid LIMIT 1", conn);
                    lCmd.Parameters.AddWithValue("@pid", periodId.Value);
                    using var lr = lCmd.ExecuteReader();
                    if (lr.Read()) { pid = lr.GetInt32("id"); ViewData["ActivePeriod"] = lr.IsDBNull(1) ? "—" : lr.GetString("lbl"); }
                }
                else
                {
                    var aCmd = new MySqlCommand(
                        "SELECT id, CONCAT(semester, ', A.Y. ', year_label) AS lbl FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
                    using var ar = aCmd.ExecuteReader();
                    if (ar.Read()) { pid = ar.GetInt32("id"); ViewData["ActivePeriod"] = ar.IsDBNull(1) ? "—" : ar.GetString("lbl"); }
                }
                ViewData["ActivePeriodId"] = pid;
                model.ActivePeriodId = pid;

                // Get this student's org positions and student_number
                var snCmd = new MySqlCommand("SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var myStudentNum = snCmd.ExecuteScalar()?.ToString() ?? "";

                var posCmd = new MySqlCommand(
                    "SELECT position FROM user_signatures WHERE user_id = @uid AND position IS NOT NULL AND position != ''", conn);
                posCmd.Parameters.AddWithValue("@uid", userId);
                using (var pr = posCmd.ExecuteReader())
                    while (pr.Read())
                        model.MyPositions.Add(pr.GetString("position"));

                if (model.MyPositions.Count > 0)
                {
                    // Probe period_id column
                    bool hasPidCol = false;
                    try { new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn).ExecuteNonQuery(); hasPidCol = true; } catch { }
                    var pidFilter = hasPidCol ? "AND (@pid = 0 OR co.period_id = @pid)" : "";

                    // Build IN clause for positions
                    var posParams  = model.MyPositions.Select((_, i) => $"@pos{i}").ToList();
                    var inClause   = string.Join(",", posParams);

                    string buildQuery(string statusFilter) => $@"
                        SELECT co.id,
                               co.position                                          AS Position,
                               CONCAT(stu.first_name, ' ', stu.last_name)          AS StudentName,
                               co.student_number                                    AS StudentNumber,
                               COALESCE(CONCAT(c.course_code,'-',cu.year_level,cu.section),'—') AS Course,
                               co.status                                            AS Status
                        FROM   clearance_organization co
                        JOIN   users stu ON stu.student_number COLLATE utf8mb4_unicode_ci = co.student_number COLLATE utf8mb4_unicode_ci
                        LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                        LEFT JOIN courses    c  ON c.id  = cu.course_id
                        WHERE  co.position COLLATE utf8mb4_unicode_ci IN ({inClause})
                          {statusFilter}
                          {pidFilter}
                        ORDER BY FIELD(co.status,'Pending','Declined','Cleared'), co.id DESC";

                    void addParams(MySqlCommand c)
                    {
                        if (hasPidCol) c.Parameters.AddWithValue("@pid", pid);
                        for (int i = 0; i < model.MyPositions.Count; i++)
                            c.Parameters.AddWithValue($"@pos{i}", model.MyPositions[i]);
                    }

                    // Pending requests
                    try
                    {
                        var pCmd = new MySqlCommand(buildQuery("AND co.status = 'Pending'"), conn);
                        addParams(pCmd);
                        using var pr2 = pCmd.ExecuteReader();
                        while (pr2.Read())
                            model.PendingItems.Add(new StudentOrgOfficerItem
                            {
                                Id            = pr2.GetInt32("id"),
                                Position      = pr2.IsDBNull(pr2.GetOrdinal("Position"))      ? "" : pr2.GetString("Position"),
                                StudentName   = pr2.IsDBNull(pr2.GetOrdinal("StudentName"))   ? "—" : pr2.GetString("StudentName"),
                                StudentNumber = pr2.IsDBNull(pr2.GetOrdinal("StudentNumber")) ? "" : pr2.GetString("StudentNumber"),
                                Course        = pr2.IsDBNull(pr2.GetOrdinal("Course"))        ? "—" : pr2.GetString("Course"),
                                Status        = pr2.GetString("Status")
                            });
                    }
                    catch { }

                    // Signed history (Cleared / Declined)
                    try
                    {
                        var hCmd = new MySqlCommand(buildQuery("AND co.status IN ('Cleared','Declined')"), conn);
                        addParams(hCmd);
                        using var hr = hCmd.ExecuteReader();
                        while (hr.Read())
                            model.SignedItems.Add(new StudentOrgOfficerItem
                            {
                                Id            = hr.GetInt32("id"),
                                Position      = hr.IsDBNull(hr.GetOrdinal("Position"))      ? "" : hr.GetString("Position"),
                                StudentName   = hr.IsDBNull(hr.GetOrdinal("StudentName"))   ? "—" : hr.GetString("StudentName"),
                                StudentNumber = hr.IsDBNull(hr.GetOrdinal("StudentNumber")) ? "" : hr.GetString("StudentNumber"),
                                Course        = hr.IsDBNull(hr.GetOrdinal("Course"))        ? "—" : hr.GetString("Course"),
                                Status        = hr.GetString("Status")
                            });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not load signed clearance: " + ex.Message;
            }

            return View(model);
        }

        // ── Approve Org Request (Student Officer) ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult ApproveOrgRequest(int id, int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Verify this student holds the position for this request
                var verifyCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN   user_signatures us
                           ON us.position COLLATE utf8mb4_unicode_ci = co.position COLLATE utf8mb4_unicode_ci
                          AND us.user_id = @uid
                    WHERE  co.id = @id", conn);
                verifyCmd.Parameters.AddWithValue("@uid", userId);
                verifyCmd.Parameters.AddWithValue("@id",  id);
                if (Convert.ToInt32(verifyCmd.ExecuteScalar()) == 0)
                {
                    TempData["Error"] = "You are not authorised to approve this request.";
                    return RedirectToAction(nameof(SignedClearance), new { periodId });
                }

                new MySqlCommand("UPDATE clearance_organization SET status = 'Cleared' WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["Success"] = "Clearance approved.";
            }
            catch (Exception ex) { TempData["Error"] = "Error: " + ex.Message; }
            return RedirectToAction(nameof(SignedClearance), new { periodId });
        }

        // ── Decline Org Request (Student Officer) ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeclineOrgRequest(int id, int? periodId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var verifyCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN   user_signatures us
                           ON us.position COLLATE utf8mb4_unicode_ci = co.position COLLATE utf8mb4_unicode_ci
                          AND us.user_id = @uid
                    WHERE  co.id = @id", conn);
                verifyCmd.Parameters.AddWithValue("@uid", userId);
                verifyCmd.Parameters.AddWithValue("@id",  id);
                if (Convert.ToInt32(verifyCmd.ExecuteScalar()) == 0)
                {
                    TempData["Error"] = "You are not authorised to decline this request.";
                    return RedirectToAction(nameof(SignedClearance), new { periodId });
                }

                new MySqlCommand("UPDATE clearance_organization SET status = 'Declined' WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["Success"] = "Request declined.";
            }
            catch (Exception ex) { TempData["Error"] = "Error: " + ex.Message; }
            return RedirectToAction(nameof(SignedClearance), new { periodId });
        }

        // ── Pending Request Count (for nav badge) ─────────────────────────
        [HttpGet]
        public IActionResult GetMyPendingRequestCount()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var snCmd = new MySqlCommand("SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var myNum = snCmd.ExecuteScalar()?.ToString() ?? "";
                var cmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM clearance_organization co
                    JOIN   user_signatures us
                           ON us.position COLLATE utf8mb4_unicode_ci = co.position COLLATE utf8mb4_unicode_ci
                          AND us.user_id = @uid
                    WHERE  co.status = 'Pending'
                      AND  co.student_number COLLATE utf8mb4_unicode_ci != @mysn", conn);
                cmd.Parameters.AddWithValue("@uid",  userId);
                cmd.Parameters.AddWithValue("@mysn", myNum);
                var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
                return Json(new { count });
            }
            catch { return Json(new { count = 0 }); }
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
                        model.AySemester = $"{sem}, A.Y. {ay}";
                    }
                }
                else
                {
                    var activeCmd = new MySqlCommand(
                        "SELECT id, year_label, semester " +
                        "FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
                    using var ar = activeCmd.ExecuteReader();
                    if (ar.Read())
                    {
                        activePeriodId   = ar.GetInt32("id");
                        var ay           = ar.IsDBNull(1) ? "" : ar.GetString("year_label");
                        var sem          = ar.IsDBNull(2) ? "" : ar.GetString("semester");
                        model.AySemester = $"{sem}, A.Y. {ay}";
                    }
                }
                if (string.IsNullOrEmpty(model.AySemester))
                    model.AySemester = "—";
                model.ActivePeriodId = activePeriodId;

                // ── Student info ──────────────────────────────────────────
                var infoCmd = new MySqlCommand(@"
                    SELECT
                        CONCAT(u.last_name, ', ', u.first_name,
                               IF(u.middle_initial IS NOT NULL AND u.middle_initial != '',
                                  CONCAT(' ', u.middle_initial, '.'), '')) AS full_name,
                        u.student_number,
                        u.curriculum_id,
                        c.course_code,
                        cu.year_level,
                        cu.section
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
                        model.Section    = ir.IsDBNull(ir.GetOrdinal("section")) ? "" : ir.GetString("section");
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

                // ── Org clearances — query from clearance_organization directly ──
                // This is more reliable than joining from organizations because the
                // cleared data is already in clearance_organization regardless of
                // how the admin set up the position or whether period_id exists.
                try
                {
                    // Probe period_id column
                    bool hasPidCol = false;
                    try
                    {
                        new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn)
                            .ExecuteNonQuery();
                        hasPidCol = true;
                    }
                    catch { }

                    var pidWhere = hasPidCol
                        ? "AND (@pid = 0 OR co.period_id = @pid OR co.period_id IS NULL)"
                        : "";

                    var orgCmd = new MySqlCommand($@"
                        SELECT
                            co.position AS OrgName,
                            co.position AS Role,
                            CASE
                                WHEN o.user_id IS NOT NULL
                                    THEN COALESCE(CONCAT(org_u.first_name,' ',org_u.last_name), 'TBA')
                                WHEN self_sig.user_id IS NOT NULL
                                    THEN COALESCE(CONCAT(stu.first_name,' ',stu.last_name), 'TBA')
                                ELSE 'TBA'
                            END                                                         AS PersonName,
                            co.status                                                   AS Status,
                            CASE
                                WHEN org_sig.signature_data IS NOT NULL AND org_sig.signature_data != ''
                                    THEN org_sig.signature_data
                                WHEN self_sig.signature_data IS NOT NULL AND self_sig.signature_data != ''
                                    THEN self_sig.signature_data
                                ELSE ''
                            END                                                         AS SignatureBase64
                        FROM clearance_organization co
                        JOIN  users          stu      ON stu.id       = @uid
                        LEFT JOIN organizations o    ON o.position_title COLLATE utf8mb4_unicode_ci
                                                         = co.position  COLLATE utf8mb4_unicode_ci
                                                        AND o.is_active = 1
                        LEFT JOIN users      org_u   ON org_u.id      = o.user_id
                        LEFT JOIN user_signatures org_sig
                                                     ON org_sig.user_id = o.user_id
                                                        AND org_sig.position IS NULL
                        LEFT JOIN user_signatures self_sig
                                                     ON self_sig.user_id = @uid
                                                        AND self_sig.position COLLATE utf8mb4_unicode_ci
                                                            = co.position COLLATE utf8mb4_unicode_ci
                        WHERE co.student_number COLLATE utf8mb4_unicode_ci = @sn
                          {pidWhere}
                        ORDER BY co.position", conn);

                    orgCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                    orgCmd.Parameters.AddWithValue("@uid", userId);
                    if (hasPidCol)
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
                            IsSelfSignatory = false
                        });
                    }
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Could not load org data: " + ex.Message;
                }
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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
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
                            "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
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

        // ── Mark Messages as Read (AJAX POST) ────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult MarkMessagesRead([FromBody] SendClearanceMessageDto dto)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var snCmd = new MySqlCommand("SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var sn = snCmd.ExecuteScalar()?.ToString() ?? "";
                var cmd = new MySqlCommand(@"
                    UPDATE clearance_messages
                    SET    is_read = 1
                    WHERE  student_number = @sn
                      AND  clearance_type = @type
                      AND  clearance_key  = @key
                      AND  sender_id      != @uid
                      AND  is_read        = 0", conn);
                cmd.Parameters.AddWithValue("@sn",   sn);
                cmd.Parameters.AddWithValue("@type", dto.ClearanceType ?? "");
                cmd.Parameters.AddWithValue("@key",  dto.ClearanceKey  ?? "");
                cmd.Parameters.AddWithValue("@uid",  userId);
                cmd.ExecuteNonQuery();
                return Json(new { success = true });
            }
            catch { return Json(new { success = true }); }
        }

        // ── Unread Message Counts for Student ─────────────────────────────
        [HttpGet]
        public IActionResult GetUnreadCounts()
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var items  = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var snCmd = new MySqlCommand("SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
                snCmd.Parameters.AddWithValue("@uid", userId);
                var sn = snCmd.ExecuteScalar()?.ToString() ?? "";

                if (!string.IsNullOrEmpty(sn))
                {
                    var readFilter = "AND is_read = 0";
                    var cmd = new MySqlCommand($@"
                        SELECT clearance_type, clearance_key
                        FROM   clearance_messages
                        WHERE  student_number = @sn
                          AND  sender_id      != @uid
                          {readFilter}
                        GROUP BY clearance_type, clearance_key", conn);
                    cmd.Parameters.AddWithValue("@sn",  sn);
                    cmd.Parameters.AddWithValue("@uid", userId);
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        items.Add(new { clearanceType = r.GetString("clearance_type"), clearanceKey = r.GetString("clearance_key") });
                }
            }
            catch { }
            return Json(items);
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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
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
                    "SELECT COALESCE(student_number, id_number) AS student_number FROM users WHERE id = @uid LIMIT 1", conn);
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
                    "SELECT id, year_label, semester FROM academic_periods ORDER BY id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    items.Add(new
                    {
                        id  = r.GetInt32("id"),
                        ay  = r.GetString("year_label"),
                        sem = r.GetString("semester")
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

                using (var r = cmd.ExecuteReader())
                {
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
                } // reader closed before second query

                // Check if student has any position assigned
                var posCmd = new MySqlCommand(
                    "SELECT COUNT(*) FROM user_signatures WHERE user_id = @uid AND position IS NOT NULL AND position != ''", conn);
                posCmd.Parameters.AddWithValue("@uid", uid);
                ViewData["HasPosition"] = Convert.ToInt32(posCmd.ExecuteScalar()) > 0;
            }
            catch { }
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────
    public class RequestSubjectDto  { public string? MisCode { get; set; } }
    public class SelfApproveOrgDto  { public string? OrgName { get; set; } public bool Approve { get; set; } }
}
