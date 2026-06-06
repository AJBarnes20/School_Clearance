using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;
using System.Text.Json;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        private readonly IConfiguration _config;

        // ── Canonical position order (1 = first displayed) ───────────────
        private static readonly Dictionary<string, int> _positionOrder =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "Computer Laboratory In-Charge", 1 },
                { "SSG Treasurer",                 2 },
                { "Organization Adviser",          3 },
                { "Class Adviser",                 4 },
                { "Department Chairperson",        5 },
            };

        // Positions that are NOT in the dictionary get this rank (sorted last).
        private const int _defaultRank = 99;

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
                ActivePeriod      = "",
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

                int.TryParse(Request.Cookies["StudentPeriodId"], out var cookiePid);
                var (dashPid, dashLabel) = ResolvePeriod(conn, cookiePid > 0 ? cookiePid : (int?)null);
                if (!string.IsNullOrEmpty(dashLabel)) model.ActivePeriod = dashLabel;

                var subjCmd = new MySqlCommand(@"
                    SELECT
                        COUNT(*)                                                    AS total,
                        SUM(CASE WHEN status = 'Cleared'  THEN 1 ELSE 0 END)       AS cleared,
                        SUM(CASE WHEN status != 'Cleared' THEN 1 ELSE 0 END)       AS incomplete
                    FROM clearance_subjects
                    WHERE student_number = @sn
                      AND (@pid = 0 OR period_id = @pid)", conn);
                subjCmd.Parameters.AddWithValue("@sn", studentNumber);
                subjCmd.Parameters.AddWithValue("@pid", dashPid);

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

                bool hasOrgPidCol = false;
                try { new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn).ExecuteNonQuery(); hasOrgPidCol = true; } catch { }
                var orgPidFilter = hasOrgPidCol ? "AND (@pid = 0 OR co.period_id = @pid)" : "";

                var orgCmd = new MySqlCommand($@"
                    SELECT
                        COUNT(*)                                                   AS total,
                        SUM(CASE WHEN co.status = 'Cleared' THEN 1 ELSE 0 END)    AS cleared
                    FROM clearance_organization co
                    WHERE co.student_number = @sn
                      {orgPidFilter}", conn);
                orgCmd.Parameters.AddWithValue("@sn", studentNumber);
                if (hasOrgPidCol) orgCmd.Parameters.AddWithValue("@pid", dashPid);

                using var or2 = orgCmd.ExecuteReader();
                if (or2.Read() && !or2.IsDBNull(0))
                {
                    model.TotalOrgs  = or2.GetInt32("total");
                    model.OrgCleared = or2.IsDBNull(or2.GetOrdinal("cleared"))
                                        ? 0 : Convert.ToInt32(or2["cleared"]);
                }
                or2.Close();

                model.PendingRequests = model.SubjectIncomplete + (model.TotalOrgs - model.OrgCleared);

                try
                {
                    var posListCmd = new MySqlCommand(
                        "SELECT position FROM user_signatures WHERE user_id = @uid AND position IS NOT NULL AND position != ''", conn);
                    posListCmd.Parameters.AddWithValue("@uid", userId);
                    var positions = new List<string>();
                    using var plr = posListCmd.ExecuteReader();
                    while (plr.Read()) positions.Add(plr.GetString("position"));
                    plr.Close();

                    if (positions.Count > 0)
                    {
                        model.HasOrgPosition = true;
                        var posParams = positions.Select((_, i) => $"@pos{i}").ToList();
                        var inClause  = string.Join(",", posParams);
                        var toSignCmd = new MySqlCommand(
                            $"SELECT COUNT(*) FROM clearance_organization WHERE position IN ({inClause}) AND status = 'Pending'", conn);
                        for (int i = 0; i < positions.Count; i++)
                            toSignCmd.Parameters.AddWithValue($"@pos{i}", positions[i]);
                        model.PendingToSign = Convert.ToInt32(toSignCmd.ExecuteScalar());
                    }
                }
                catch { }

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
                            (student_number, mis_code, status, period_id, requested_at)
                        VALUES (@sn, @mc, 'Pending', @pid, NOW())", conn);
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
                if (!periodId.HasValue || periodId.Value <= 0)
                { int.TryParse(Request.Cookies["StudentPeriodId"], out var cp); if (cp > 0) periodId = cp; }

                var (activePeriodId, periodLbl) = ResolvePeriod(conn, periodId);
                if (activePeriodId > 0) Response.Cookies.Append("StudentPeriodId", activePeriodId.ToString(),
                    new CookieOptions { MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax });
                ViewData["ActivePeriodId"] = activePeriodId;
                if (!string.IsNullOrEmpty(periodLbl)) ViewData["ActivePeriod"] = periodLbl;

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
                // PART A — Subject Clearance rows
                // ════════════════════════════════════════════════════════════════════
                var subjCmd = new MySqlCommand(@"
                    SELECT
                        cs.mis_code                                                     AS MisCode,
                        COALESCE(s.subject_code, cs.mis_code)                          AS SubjectCode,
                        COALESCE(s.description, '—')                                   AS Description,
                        COALESCE(CONCAT(u.first_name,' ',u.last_name), 'TBA')          AS InstructorName,
                        COALESCE(cs.status, 'Pending')                                 AS Status,
                        cs.requested_at                                                 AS RequestedAt,
                        cs.signed_at                                                    AS SignedAt
                    FROM clearance_subjects cs
                    LEFT JOIN subject_offerings so  ON so.mis_code COLLATE utf8mb4_unicode_ci = cs.mis_code COLLATE utf8mb4_unicode_ci
                    LEFT JOIN subjects          s   ON s.id        = so.subject_id
                    LEFT JOIN users             u   ON u.id        = so.user_id
                    WHERE cs.student_number COLLATE utf8mb4_unicode_ci = @sn
                      AND (@pid = 0 OR cs.period_id = @pid)
                      AND s.id IS NOT NULL
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
                            Status         = r.GetString("Status"),
                            RequestedAt    = r.IsDBNull(r.GetOrdinal("RequestedAt")) ? null : r.GetDateTime("RequestedAt"),
                            SignedAt       = r.IsDBNull(r.GetOrdinal("SignedAt"))    ? null : r.GetDateTime("SignedAt")
                        });
                    }
                }

                // ════════════════════════════════════════════════════════════════════
                // STEP 1 — Load clearance statuses + timestamps for selected period
                // ════════════════════════════════════════════════════════════════════
                var orgStatuses = new Dictionary<string, (string Status, DateTime? RequestedAt, DateTime? SignedAt)>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(studentNumber))
                {
                    if (activePeriodId > 0)
                    {
                        try
                        {
                            var stCmd = new MySqlCommand(@"
                                SELECT position, status, requested_at, signed_at
                                FROM   clearance_organization
                                WHERE  student_number = @sn
                                  AND  period_id      = @pid
                                ORDER BY id ASC", conn);
                            stCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                            stCmd.Parameters.AddWithValue("@pid", activePeriodId);
                            using var sr2 = stCmd.ExecuteReader();
                            while (sr2.Read())
                            {
                                var pos      = sr2.GetString("position");
                                var st       = sr2.GetString("status");
                                var reqAt    = sr2.IsDBNull(sr2.GetOrdinal("requested_at")) ? (DateTime?)null : sr2.GetDateTime("requested_at");
                                var signedAt = sr2.IsDBNull(sr2.GetOrdinal("signed_at"))    ? (DateTime?)null : sr2.GetDateTime("signed_at");
                                orgStatuses[pos] = (st, reqAt, signedAt);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        try
                        {
                            var stCmd = new MySqlCommand(@"
                                SELECT position, status, requested_at, signed_at
                                FROM   clearance_organization
                                WHERE  student_number = @sn
                                ORDER BY id ASC", conn);
                            stCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                            using var sr2 = stCmd.ExecuteReader();
                            while (sr2.Read())
                            {
                                var pos      = sr2.GetString("position");
                                var st       = sr2.GetString("status");
                                var reqAt    = sr2.IsDBNull(sr2.GetOrdinal("requested_at")) ? (DateTime?)null : sr2.GetDateTime("requested_at");
                                var signedAt = sr2.IsDBNull(sr2.GetOrdinal("signed_at"))    ? (DateTime?)null : sr2.GetDateTime("signed_at");
                                orgStatuses[pos] = (st, reqAt, signedAt);
                            }
                        }
                        catch { }
                    }
                }

                // ════════════════════════════════════════════════════════════════════
                // PART B — Class Adviser
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

                            orgStatuses.TryGetValue("Class Adviser", out var advData);
                            model.ClassAdviser = new OrganizationSignatory
                            {
                                OrgName         = "Class Adviser",
                                OrgRole         = $"{course} — {ylLabel}{(string.IsNullOrEmpty(section) ? "" : $", Section {section}")}",
                                PersonName      = advRdr.IsDBNull(advRdr.GetOrdinal("AdviserName")) ? "—" : advRdr.GetString("AdviserName"),
                                Status          = advData.Status ?? "",
                                RequestedAt     = advData.RequestedAt,
                                SignedAt        = advData.SignedAt,
                                IsSelfSignatory = false
                            };
                        }
                    }
                    catch { }
                }

                // ════════════════════════════════════════════════════════════════════
                // PART C — All active org positions except Class Adviser
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

                            orgStatuses.TryGetValue(role, out var orgData);
                            model.OrgItems.Add(new OrganizationSignatory
                            {
                                OrgName         = role,
                                OrgRole         = role,
                                PersonName      = or.IsDBNull(or.GetOrdinal("PersonName")) ? "—" : or.GetString("PersonName"),
                                Status          = orgData.Status ?? "",
                                RequestedAt     = orgData.RequestedAt,
                                SignedAt        = orgData.SignedAt,
                                IsSelfSignatory = signatoryUserId == userId
                            });
                        }
                    }
                    catch { }
                }

                // ════════════════════════════════════════════════════════════════════
                // PART C2 — Student org signatories (SSG positions in user_signatures)
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

                        orgStatuses.TryGetValue(role, out var stuSigData);
                        model.OrgItems.Add(new OrganizationSignatory
                        {
                            OrgName         = role,
                            OrgRole         = role,
                            PersonName      = stuSigRdr.IsDBNull(stuSigRdr.GetOrdinal("PersonName")) ? "—" : stuSigRdr.GetString("PersonName"),
                            Status          = stuSigData.Status ?? "",
                            RequestedAt     = stuSigData.RequestedAt,
                            SignedAt        = stuSigData.SignedAt,
                            IsSelfSignatory = signatoryUserId == userId
                        });
                    }
                }
                catch { }

                // ════════════════════════════════════════════════════════════════════
                // PART D — Positions the student personally holds (self-signatory)
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

                        orgStatuses.TryGetValue(role, out var ssData);
                        model.OrgItems.Add(new OrganizationSignatory
                        {
                            OrgName         = role,
                            OrgRole         = role,
                            PersonName      = ssr.IsDBNull(ssr.GetOrdinal("PersonName")) ? "—" : ssr.GetString("PersonName"),
                            Status          = ssData.Status ?? "",
                            RequestedAt     = ssData.RequestedAt,
                            SignedAt        = ssData.SignedAt,
                            IsSelfSignatory = true
                        });
                    }
                }
                catch { }

                // ════════════════════════════════════════════════════════════════════
                // ── Sort OrgItems by canonical position order ─────────────────────
                // Order: Computer Laboratory In-Charge → SSG Treasurer →
                //        Organization Adviser → Class Adviser → Department Chairperson
                // Any position not listed in _positionOrder sorts to the end (rank 99).
                // NOTE: ClassAdviser lives in model.ClassAdviser (its own property) and
                //       is NOT in OrgItems, so it is handled by the view separately.
                //       We still include it in the sort table so that if it ever appears
                //       in OrgItems (e.g. fallback path) it lands in the right slot.
                // ════════════════════════════════════════════════════════════════════
                model.OrgItems = model.OrgItems
                    .OrderBy(x => _positionOrder.TryGetValue(x.OrgName, out var rank) ? rank : _defaultRank)
                    .ThenBy(x => x.OrgName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not load clearance: " + ex.Message;
            }

            // ── Load available subjects for the Add Subject panel ─────────────
            var available = new List<SubjectItem>();
            try
            {
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

            // ── Load periods directly into ViewBag (no AJAX needed) ───────────
            var periodsList = new List<object>();
            try
            {
                using var connP = DbHelper.GetConnection(_config);
                connP.Open();
                var periodCmd = new MySqlCommand(
                    "SELECT id, year_label AS ay, semester AS sem FROM academic_periods ORDER BY id DESC", connP);
                using var pr = periodCmd.ExecuteReader();
                while (pr.Read())
                {
                    periodsList.Add(new {
                        id  = pr.GetInt32("id"),
                        ay  = pr.IsDBNull(pr.GetOrdinal("ay"))  ? "" : pr.GetString("ay"),
                        sem = pr.IsDBNull(pr.GetOrdinal("sem")) ? "" : pr.GetString("sem")
                    });
                }
            }
            catch { }
            ViewBag.Periods = JsonSerializer.Serialize(periodsList);

            // ── Load Approval Requests for students who hold an org position ──────
            model.ActivePeriodId = (int)(ViewData["ActivePeriodId"] ?? 0);
            try
            {
                using var conn3 = DbHelper.GetConnection(_config);
                conn3.Open();

                var posCmd = new MySqlCommand(
                    "SELECT position FROM user_signatures WHERE user_id = @uid AND position IS NOT NULL AND position != ''", conn3);
                posCmd.Parameters.AddWithValue("@uid", userId);
                using (var plr = posCmd.ExecuteReader())
                    while (plr.Read()) model.MyPositions.Add(plr.GetString("position"));

                if (model.MyPositions.Count > 0)
                {
                    bool hasPidCol = false;
                    try { new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn3).ExecuteNonQuery(); hasPidCol = true; } catch { }
                    var pidFilter = hasPidCol ? "AND (@pid3 = 0 OR co.period_id = @pid3)" : "";

                    var posParams = model.MyPositions.Select((_, i) => $"@ppos{i}").ToList();
                    var inClause  = string.Join(",", posParams);

                    string buildQ(string statusFilter) => $@"
                        SELECT co.id,
                               co.position                                          AS Position,
                               CONCAT(stu.first_name, ' ', stu.last_name)          AS StudentName,
                               co.student_number                                    AS StudentNumber,
                               COALESCE(CONCAT(c.course_code,'-',cu.year_level,cu.section),'—') AS Course,
                               co.status                                            AS Status,
                               co.requested_at                                      AS RequestedAt,
                               co.signed_at                                         AS SignedAt
                        FROM   clearance_organization co
                        JOIN   users stu ON COALESCE(stu.student_number, stu.id_number) COLLATE utf8mb4_unicode_ci = co.student_number COLLATE utf8mb4_unicode_ci
                        LEFT JOIN curriculum cu ON cu.id = stu.curriculum_id
                        LEFT JOIN courses    c  ON c.id  = cu.course_id
                        WHERE  co.position COLLATE utf8mb4_unicode_ci IN ({inClause})
                          {statusFilter}
                          {pidFilter}
                        ORDER BY co.id DESC";

                    void addParams(MySqlCommand c)
                    {
                        if (hasPidCol) c.Parameters.AddWithValue("@pid3", model.ActivePeriodId);
                        for (int i = 0; i < model.MyPositions.Count; i++)
                            c.Parameters.AddWithValue($"@ppos{i}", model.MyPositions[i]);
                    }

                    try
                    {
                        var pCmd = new MySqlCommand(buildQ("AND co.status = 'Pending'"), conn3);
                        addParams(pCmd);
                        using var pr2 = pCmd.ExecuteReader();
                        while (pr2.Read())
                            model.PendingToApprove.Add(new StudentOrgOfficerItem
                            {
                                Id            = pr2.GetInt32("id"),
                                Position      = pr2.IsDBNull(pr2.GetOrdinal("Position"))      ? "" : pr2.GetString("Position"),
                                StudentName   = pr2.IsDBNull(pr2.GetOrdinal("StudentName"))   ? "—" : pr2.GetString("StudentName"),
                                StudentNumber = pr2.IsDBNull(pr2.GetOrdinal("StudentNumber")) ? "" : pr2.GetString("StudentNumber"),
                                Course        = pr2.IsDBNull(pr2.GetOrdinal("Course"))        ? "—" : pr2.GetString("Course"),
                                Status        = "Pending",
                                RequestedAt   = pr2.IsDBNull(pr2.GetOrdinal("RequestedAt"))   ? null : pr2.GetDateTime("RequestedAt"),
                                SignedAt      = null
                            });
                    }
                    catch { }

                    try
                    {
                        var sCmd = new MySqlCommand(buildQ("AND co.status != 'Pending'"), conn3);
                        addParams(sCmd);
                        using var sr2 = sCmd.ExecuteReader();
                        while (sr2.Read())
                            model.ApprovedHistory.Add(new StudentOrgOfficerItem
                            {
                                Id            = sr2.GetInt32("id"),
                                Position      = sr2.IsDBNull(sr2.GetOrdinal("Position"))      ? "" : sr2.GetString("Position"),
                                StudentName   = sr2.IsDBNull(sr2.GetOrdinal("StudentName"))   ? "—" : sr2.GetString("StudentName"),
                                StudentNumber = sr2.IsDBNull(sr2.GetOrdinal("StudentNumber")) ? "" : sr2.GetString("StudentNumber"),
                                Course        = sr2.IsDBNull(sr2.GetOrdinal("Course"))        ? "—" : sr2.GetString("Course"),
                                Status        = sr2.IsDBNull(sr2.GetOrdinal("Status"))        ? "" : sr2.GetString("Status"),
                                RequestedAt   = sr2.IsDBNull(sr2.GetOrdinal("RequestedAt"))   ? null : sr2.GetDateTime("RequestedAt"),
                                SignedAt      = sr2.IsDBNull(sr2.GetOrdinal("SignedAt"))      ? null : sr2.GetDateTime("SignedAt")
                            });
                    }
                    catch { }
                }
            }
            catch { }

            return View(model);
        }

        // Redirect old routes to the merged page
        public IActionResult Organization()    => RedirectToAction(nameof(Clearance));
        public IActionResult SignedClearance(int? periodId) => RedirectToAction(nameof(Clearance), new { periodId });

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
                        (student_number, mis_code, status, period_id, requested_at, signed_at)
                    VALUES (@sn, @mis, 'Pending', @pid, NOW(), NULL)
                    ON DUPLICATE KEY UPDATE status = 'Pending', requested_at = NOW(), signed_at = NULL", conn);
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

                var checkOrgCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM organizations
                    WHERE  position_title = @pos
                      AND  COALESCE(is_active, 1) = 1
                      AND  (curriculum_id IS NULL OR curriculum_id = @cid)", conn);
                checkOrgCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                checkOrgCmd.Parameters.AddWithValue("@cid", curriculumId);
                var orgExists = Convert.ToInt32(checkOrgCmd.ExecuteScalar()) > 0;

                var checkSsCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM user_signatures
                    WHERE  user_id   = @uid
                      AND  position  = @pos", conn);
                checkSsCmd.Parameters.AddWithValue("@uid", userId);
                checkSsCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                var isSelfPosition = Convert.ToInt32(checkSsCmd.ExecuteScalar()) > 0;

                var checkStudentSigCmd = new MySqlCommand(@"
                    SELECT COUNT(*) FROM user_signatures
                    WHERE  position = @pos AND position IS NOT NULL AND position != ''", conn);
                checkStudentSigCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                var isStudentSigPosition = Convert.ToInt32(checkStudentSigCmd.ExecuteScalar()) > 0;

                if (!orgExists && !isSelfPosition && !isStudentSigPosition)
                    return Json(new { success = false, error = "You are not allowed to request this position." });

                int activePid = dto.PeriodId > 0 ? dto.PeriodId : 0;
                if (activePid == 0)
                {
                    var periodCmd = new MySqlCommand(
                        "SELECT id FROM academic_periods ORDER BY id DESC LIMIT 1", conn);
                    activePid = Convert.ToInt32(periodCmd.ExecuteScalar() ?? 1);
                }

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

                        var resetCmd = new MySqlCommand(@"
                            UPDATE clearance_organization
                            SET    status = 'Pending', requested_at = NOW(), signed_at = NULL
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
                            (student_number, position, status, period_id, requested_at)
                        VALUES (@sn, @pos, 'Pending', @pid, NOW())", conn);
                    insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    insertCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                    insertCmd.Parameters.AddWithValue("@pid", activePid);
                    insertCmd.ExecuteNonQuery();
                }
                else
                {
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
                            SET    status = 'Pending', requested_at = NOW(), signed_at = NULL
                            WHERE  student_number = @sn
                              AND  position       = @pos", conn);
                        resetCmd.Parameters.AddWithValue("@sn",  studentNumber);
                        resetCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                        resetCmd.ExecuteNonQuery();
                        return Json(new { success = true });
                    }

                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO clearance_organization (student_number, position, status, requested_at)
                        VALUES (@sn, @pos, 'Pending', NOW())", conn);
                    insertCmd.Parameters.AddWithValue("@sn",  studentNumber);
                    insertCmd.Parameters.AddWithValue("@pos", dto.OrgName);
                    insertCmd.ExecuteNonQuery();
                }

                return Json(new { success = true });
            }
            catch (MySqlException mex) when (mex.Number == 1062)
            {
                return Json(new
                {
                    success = false,
                    error   = "This position already has a clearance record. " +
                              "If you're seeing this after switching academic periods, " +
                              "run migration.sql to make uq_co period-aware."
                });
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
                    SET    status = @st, signed_at = NOW()
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

                try
                {
                    var coursesCmd = new MySqlCommand(
                        "SELECT course_code FROM courses ORDER BY course_code", conn);
                    using var cr = coursesCmd.ExecuteReader();
                    while (cr.Read())
                        model.AvailableCourses.Add(cr.GetString("course_code"));
                }
                catch { }

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

        // ── Approve Org Request (Student Officer) ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult ApproveOrgRequest(int id, int? periodId)
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
                    TempData["Error"] = "You are not authorised to approve this request.";
                    return RedirectToAction(nameof(Clearance), new { periodId });
                }

                new MySqlCommand("UPDATE clearance_organization SET status = 'Cleared', signed_at = NOW() WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["Success"] = "Clearance approved.";
            }
            catch (Exception ex) { TempData["Error"] = "Error: " + ex.Message; }
            return RedirectToAction(nameof(Clearance), new { periodId });
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
                    return RedirectToAction(nameof(Clearance), new { periodId });
                }

                new MySqlCommand("UPDATE clearance_organization SET status = 'Declined', signed_at = NOW() WHERE id = @id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                TempData["Success"] = "Request declined.";
            }
            catch (Exception ex) { TempData["Error"] = "Error: " + ex.Message; }
            return RedirectToAction(nameof(Clearance), new { periodId });
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
                }

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
                      AND s.id IS NOT NULL
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

                try
                {
                    var stuSigCmd = new MySqlCommand(@"
                        SELECT signature_data FROM user_signatures
                        WHERE  user_id = @uid
                          AND  signature_data IS NOT NULL AND signature_data != ''
                        ORDER BY id ASC
                        LIMIT  1", conn);
                    stuSigCmd.Parameters.AddWithValue("@uid", userId);
                    var stuSig = stuSigCmd.ExecuteScalar();
                    if (stuSig != null && stuSig != DBNull.Value)
                        model.SignaturePath = stuSig.ToString() ?? "";
                }
                catch { }

                var clearanceMap = new Dictionary<string, (string Status, string Sig)>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    bool hasPidCol = false;
                    try { new MySqlCommand("SELECT period_id FROM clearance_organization LIMIT 0", conn).ExecuteNonQuery(); hasPidCol = true; } catch { }

                    var pidWhere = hasPidCol
                        ? "AND (@pid = 0 OR co.period_id = @pid OR co.period_id IS NULL)"
                        : "";

                    var mapCmd = new MySqlCommand($@"
                        SELECT co.position AS Position, co.status AS Status
                        FROM   clearance_organization co
                        WHERE  co.student_number COLLATE utf8mb4_unicode_ci = @sn
                          {pidWhere}
                        ORDER BY co.id ASC", conn);

                    mapCmd.Parameters.Add(new MySqlParameter("@sn", MySqlDbType.VarChar) { Value = studentNumber });
                    if (hasPidCol) mapCmd.Parameters.AddWithValue("@pid", activePeriodId);

                    using var mr = mapCmd.ExecuteReader();
                    while (mr.Read())
                    {
                        var pos = mr.IsDBNull(mr.GetOrdinal("Position")) ? "" : mr.GetString("Position");
                        var st  = mr.IsDBNull(mr.GetOrdinal("Status"))   ? "" : mr.GetString("Status");
                        if (!string.IsNullOrEmpty(pos))
                            clearanceMap[pos] = (st, "");
                    }
                }
                catch { }

                try
                {
                    var allOrgsCmd = new MySqlCommand(@"
                        SELECT
                            o.position_title                        AS OrgName,
                            CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                            COALESCE(us.signature_data, '')         AS SignatureBase64
                        FROM   organizations o
                        JOIN   users u ON u.id = o.user_id
                        LEFT JOIN user_signatures us
                               ON us.user_id = o.user_id
                              AND us.signature_data IS NOT NULL AND us.signature_data != ''
                        WHERE  COALESCE(o.is_active, 1) = 1
                        ORDER BY o.position_title", conn);

                    using var aor = allOrgsCmd.ExecuteReader();
                    while (aor.Read())
                    {
                        var orgName    = aor.IsDBNull(aor.GetOrdinal("OrgName"))         ? "" : aor.GetString("OrgName");
                        var personName = aor.IsDBNull(aor.GetOrdinal("PersonName"))      ? "" : aor.GetString("PersonName");
                        var orgSig     = aor.IsDBNull(aor.GetOrdinal("SignatureBase64")) ? "" : aor.GetString("SignatureBase64");

                        if (string.IsNullOrEmpty(orgName)) continue;

                        clearanceMap.TryGetValue(orgName, out var clearance);
                        var status  = clearance.Status ?? "None";
                        var showSig = string.Equals(status, "Cleared", StringComparison.OrdinalIgnoreCase) ? orgSig : "";

                        model.Organizations.Add(new PdfOrganizationItem
                        {
                            OrgName         = orgName,
                            Role            = orgName,
                            PersonName      = personName,
                            Status          = status,
                            SignatureBase64 = showSig,
                            IsSelfSignatory = false
                        });
                    }
                }
                catch (Exception ex) { TempData["Error"] = "Could not load org data: " + ex.Message; }

                try
                {
                    var stuSigPositionsCmd = new MySqlCommand(@"
                        SELECT
                            us.position                             AS OrgName,
                            CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                            COALESCE(us.signature_data, '')         AS SignatureBase64
                        FROM   user_signatures us
                        JOIN   users u ON u.id = us.user_id AND u.is_active = 1
                        WHERE  us.position IS NOT NULL AND us.position != ''
                        ORDER BY us.position", conn);

                    using var spr = stuSigPositionsCmd.ExecuteReader();
                    while (spr.Read())
                    {
                        var orgName    = spr.IsDBNull(spr.GetOrdinal("OrgName"))         ? "" : spr.GetString("OrgName");
                        var personName = spr.IsDBNull(spr.GetOrdinal("PersonName"))      ? "" : spr.GetString("PersonName");
                        var posSig     = spr.IsDBNull(spr.GetOrdinal("SignatureBase64")) ? "" : spr.GetString("SignatureBase64");

                        if (string.IsNullOrEmpty(orgName)) continue;
                        if (model.Organizations.Any(o => string.Equals(o.OrgName, orgName, StringComparison.OrdinalIgnoreCase))) continue;

                        clearanceMap.TryGetValue(orgName, out var clearance);
                        var status  = clearance.Status ?? "None";
                        var showSig = string.Equals(status, "Cleared", StringComparison.OrdinalIgnoreCase) ? posSig : "";

                        model.Organizations.Add(new PdfOrganizationItem
                        {
                            OrgName         = orgName,
                            Role            = orgName,
                            PersonName      = personName,
                            Status          = status,
                            SignatureBase64 = showSig,
                            IsSelfSignatory = false
                        });
                    }
                }
                catch { }

                try
                {
                    if (curriculumId > 0 && !model.Organizations.Any(o =>
                        string.Equals(o.OrgName, "Class Adviser", StringComparison.OrdinalIgnoreCase)))
                    {
                        var advCmd = new MySqlCommand(@"
                            SELECT
                                CONCAT(u.first_name, ' ', u.last_name) AS PersonName,
                                COALESCE(us.signature_data, '')         AS SignatureBase64
                            FROM   organizations o
                            JOIN   users u ON u.id = o.user_id
                            LEFT JOIN user_signatures us
                                   ON us.user_id = o.user_id
                                  AND us.signature_data IS NOT NULL AND us.signature_data != ''
                            WHERE  o.curriculum_id = @cid
                              AND  o.position_title COLLATE utf8mb4_unicode_ci = 'Class Adviser'
                              AND  COALESCE(o.is_active, 1) = 1
                            LIMIT  1", conn);
                        advCmd.Parameters.AddWithValue("@cid", curriculumId);

                        string advName = ""; string advSig = "";
                        using (var advr = advCmd.ExecuteReader())
                        {
                            if (advr.Read())
                            {
                                advName = advr.IsDBNull(advr.GetOrdinal("PersonName"))      ? "" : advr.GetString("PersonName");
                                advSig  = advr.IsDBNull(advr.GetOrdinal("SignatureBase64")) ? "" : advr.GetString("SignatureBase64");
                            }
                        }

                        if (!string.IsNullOrEmpty(advName))
                        {
                            clearanceMap.TryGetValue("Class Adviser", out var advClearance);
                            var advStatus  = advClearance.Status ?? "None";
                            var advShowSig = string.Equals(advStatus, "Cleared", StringComparison.OrdinalIgnoreCase) ? advSig : "";

                            model.Organizations.Add(new PdfOrganizationItem
                            {
                                OrgName         = "Class Adviser",
                                Role            = "Class Adviser",
                                PersonName      = advName,
                                Status          = advStatus,
                                SignatureBase64 = advShowSig,
                                IsSelfSignatory = false
                            });
                        }
                    }
                }
                catch { }

                // ── Sort PDF organizations by canonical position order ─────────
                model.Organizations = model.Organizations
                    .OrderBy(x => _positionOrder.TryGetValue(x.OrgName, out var rank) ? rank : _defaultRank)
                    .ThenBy(x => x.OrgName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not load PDF data: " + ex.Message;
            }

            // ── Load periods directly into ViewBag ────────────────────────────
            var pdfPeriodsList = new List<object>();
            try
            {
                using var connPdf = DbHelper.GetConnection(_config);
                connPdf.Open();
                var pdfPeriodCmd = new MySqlCommand(
                    "SELECT id, year_label AS ay, semester AS sem FROM academic_periods ORDER BY id DESC", connPdf);
                using var pdfPr = pdfPeriodCmd.ExecuteReader();
                while (pdfPr.Read())
                {
                    pdfPeriodsList.Add(new {
                        id  = pdfPr.GetInt32("id"),
                        ay  = pdfPr.IsDBNull(pdfPr.GetOrdinal("ay"))  ? "" : pdfPr.GetString("ay"),
                        sem = pdfPr.IsDBNull(pdfPr.GetOrdinal("sem")) ? "" : pdfPr.GetString("sem")
                    });
                }
            }
            catch { }
            ViewBag.Periods = JsonSerializer.Serialize(pdfPeriodsList);

            return View(model);
        }

        // ── Delete Clearance Request (AJAX POST) ──────────────────────────
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
                    var cmd = new MySqlCommand(@"
                        SELECT clearance_type, clearance_key
                        FROM   clearance_messages
                        WHERE  student_number = @sn
                          AND  sender_id      != @uid
                          AND  is_read        = 0
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
                        (sender_id, student_number, clearance_type, clearance_key, message, sent_at, is_read)
                    VALUES (@sid, @sn, @type, @key, @msg, NOW(), 0)", conn);
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

        // ── Academic Periods API ──────────────────────────────────────────
        [HttpGet("/api/student/periods")]
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
                }

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