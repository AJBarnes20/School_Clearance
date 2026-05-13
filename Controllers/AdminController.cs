using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;
using System.Security.Claims;
using System.Text.Json;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly IConfiguration _config;

        public AdminController(IConfiguration config)
        {
            _config = config;
        }

        // ── Views ─────────────────────────────────────────────────────────
        public IActionResult Dashboard()
        {
            ViewData["AdminName"] = $"{User.FindFirst("FirstName")?.Value} {User.FindFirst("LastName")?.Value}".Trim();
            return View();
        }

        public IActionResult Announcement() => View();
        public IActionResult UserManagement() => View();
        public IActionResult Student()       { ViewData["SubView"] = "all";        return View(); }
        public IActionResult StudentCleared()   { ViewData["SubView"] = "cleared";    return View("Student"); }
        public IActionResult StudentIncomplete(){ ViewData["SubView"] = "incomplete"; return View("Student"); }
        public IActionResult StudentAssign()    { ViewData["SubView"] = "assign";     return View("Student"); }
        public IActionResult Instructor()    => View();
        public IActionResult InstructorDetail() => View();
        public IActionResult Subjects()      => View();
        public IActionResult Academic()      => View();
        public IActionResult Staff()         => View();
        public IActionResult InstructorAssign() { ViewData["SubView"] = "assign"; return View("Instructor"); }

        // ── Dashboard API ─────────────────────────────────────────────────
        [HttpGet("/api/admin/stats")]
        public IActionResult Stats()
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                return Ok(new
                {
                    students    = GetCount(conn, "SELECT COUNT(*) FROM users WHERE role='Student' AND is_active=1"),
                    instructors = GetCount(conn, "SELECT COUNT(*) FROM users WHERE role='Instructor' AND is_active=1"),
                    staff       = GetCount(conn, "SELECT COUNT(*) FROM users WHERE role='Admin' AND is_active=1"),
                    signatories = GetCount(conn, "SELECT COUNT(DISTINCT user_id) FROM organizations WHERE user_id IS NOT NULL")
                });
            }
            catch { return Ok(new { students = 0, instructors = 0, staff = 0, signatories = 0 }); }
        }

        [HttpGet("/api/admin/test-db")]
        public IActionResult TestDatabase()
        {
            var results = new { raw_connection = "Not tested", ef_connection = "Not tested", raw_user_count = 0, ef_user_count = 0 };
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                results = results with { raw_connection = "Connected", raw_user_count = GetCount(conn, "SELECT COUNT(*) FROM users") };
            }
            catch (Exception ex) { results = results with { raw_connection = $"Raw Error: {ex.Message}" }; }
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                results = results with { ef_connection = "Connected", ef_user_count = db.Users.Count() };
            }
            catch (Exception ex) { results = results with { ef_connection = $"EF Error: {ex.Message}" }; }
            return Ok(results);
        }

        [HttpGet("/api/admin/active-period")]
        public IActionResult ActivePeriod()
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, year_label, semester FROM academic_periods WHERE is_active=1 LIMIT 1", conn);
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    return Ok(new { id = r.GetInt32("id"), ay = r.GetString("year_label"), sem = r.GetString("semester") });
            }
            catch { }
            return Ok(new { id = 0, ay = (string?)null, sem = (string?)null });
        }

        [HttpGet("/api/admin/pending-users")]
        public IActionResult PendingUsers()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT id, CONCAT(first_name,' ',last_name) AS name, COALESCE(id_number,'—') AS id_number
                    FROM users WHERE role='Pending' OR is_active=0 ORDER BY created_at DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("name"), idNumber = r.GetString("id_number") });
            }
            catch { }
            return Ok(items);
        }

        [HttpPost("/api/admin/approve-user")]
        public IActionResult ApproveUser([FromBody] JsonElement body)
        {
            try
            {
                var id = body.GetProperty("id").GetInt32();
                var role = body.GetProperty("role").GetString() ?? "Student";
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("UPDATE users SET role=@r, is_active=1 WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@r", role); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPost("/api/admin/decline-user")]
        public IActionResult DeclineUser([FromBody] JsonElement body)
        {
            try
            {
                var id = body.GetProperty("id").GetInt32();
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("DELETE FROM users WHERE id=@id AND is_active=0", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Announcements API ─────────────────────────────────────────────
        [HttpGet("/api/admin/announcements")]
        public IActionResult GetAnnouncements()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, title, body, type, posted_at FROM announcements ORDER BY posted_at DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new
                    {
                        id     = r.GetInt32("id"),
                        title  = r.GetString("title"),
                        body   = r.IsDBNull(r.GetOrdinal("body")) ? "" : r.GetString("body"),
                        type   = r.IsDBNull(r.GetOrdinal("type")) ? "General" : r.GetString("type"),
                        date   = r.GetDateTime("posted_at").ToString("MMMM d, yyyy"),
                        pinned = false
                    });
            }
            catch { }
            return Ok(items);
        }

        [HttpPost("/api/admin/announcements")]
        public IActionResult CreateAnnouncement([FromBody] JsonElement body)
        {
            try
            {
                var title   = body.GetProperty("title").GetString() ?? "";
                var content = body.GetProperty("body").GetString()  ?? "";
                var type    = body.GetProperty("type").GetString()  ?? "General";
                var userId  = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    INSERT INTO announcements (title, body, type, posted_by_id)
                    VALUES (@t, @b, @tp, @a); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@t",  title);
                cmd.Parameters.AddWithValue("@b",  content);
                cmd.Parameters.AddWithValue("@tp", type);
                cmd.Parameters.AddWithValue("@a",  userId);
                var newId = Convert.ToInt32(cmd.ExecuteScalar());
                return Ok(new { success = true, id = newId });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPut("/api/admin/announcements/{id}")]
        public IActionResult UpdateAnnouncement(int id, [FromBody] JsonElement body)
        {
            try
            {
                var title   = body.GetProperty("title").GetString() ?? "";
                var content = body.GetProperty("body").GetString()  ?? "";
                var type    = body.GetProperty("type").GetString()  ?? "General";
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "UPDATE announcements SET title=@t, body=@b, type=@tp WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@t",  title);
                cmd.Parameters.AddWithValue("@b",  content);
                cmd.Parameters.AddWithValue("@tp", type);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/announcements/{id}")]
        public IActionResult DeleteAnnouncement(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("DELETE FROM announcements WHERE id=@id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Academic Periods API ──────────────────────────────────────────
        [HttpGet("/api/admin/academic")]
        public IActionResult GetAcademic()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, year_label, semester, is_active FROM academic_periods ORDER BY id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), ay = r.GetString("year_label"), sem = r.GetString("semester"), start = "", end = "", status = r.GetBoolean("is_active") ? "Active" : "Completed" });
            }
            catch { }
            return Ok(items);
        }

        [HttpPost("/api/admin/academic")]
        public IActionResult CreateAcademic([FromBody] JsonElement body)
        {
            try
            {
                var ay     = body.GetProperty("ay").GetString()     ?? "";
                var sem    = body.GetProperty("sem").GetString()    ?? "";
                var active = (body.GetProperty("status").GetString() ?? "") == "Active" ? 1 : 0;
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                if (active == 1) new MySqlCommand("UPDATE academic_periods SET is_active=0", conn).ExecuteNonQuery();
                var cmd = new MySqlCommand(@"
                    INSERT INTO academic_periods (year_label, semester, is_active, start_date, end_date)
                    VALUES (@ay, @sem, @act, CURDATE(), DATE_ADD(CURDATE(), INTERVAL 6 MONTH));
                    SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@ay", ay); cmd.Parameters.AddWithValue("@sem", sem); cmd.Parameters.AddWithValue("@act", active);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPut("/api/admin/academic/{id}")]
        public IActionResult UpdateAcademic(int id, [FromBody] JsonElement body)
        {
            try
            {
                var ay     = body.GetProperty("ay").GetString()     ?? "";
                var sem    = body.GetProperty("sem").GetString()    ?? "";
                var active = (body.GetProperty("status").GetString() ?? "") == "Active" ? 1 : 0;
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                if (active == 1)
                {
                    var deact = new MySqlCommand("UPDATE academic_periods SET is_active=0 WHERE id != @id", conn);
                    deact.Parameters.AddWithValue("@id", id); deact.ExecuteNonQuery();
                }
                var cmd = new MySqlCommand(
                    "UPDATE academic_periods SET year_label=@ay, semester=@sem, is_active=@act WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@ay", ay); cmd.Parameters.AddWithValue("@sem", sem);
                cmd.Parameters.AddWithValue("@act", active); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/academic/{id}")]
        public IActionResult DeleteAcademic(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                new MySqlCommand("DELETE FROM academic_periods WHERE id=@id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); });
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Subjects API ──────────────────────────────────────────────────
        [HttpGet("/api/admin/subjects")]
        public IActionResult GetSubjects()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, mis_code, subject_code, description, lec_units, lab_units FROM subjects ORDER BY subject_code", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), mis = r.IsDBNull(r.GetOrdinal("mis_code")) ? "" : r.GetString("mis_code"), code = r.IsDBNull(r.GetOrdinal("subject_code")) ? "" : r.GetString("subject_code"), desc = r.IsDBNull(r.GetOrdinal("description")) ? "" : r.GetString("description"), lec = r.IsDBNull(r.GetOrdinal("lec_units")) ? 0 : r.GetInt32("lec_units"), lab = r.IsDBNull(r.GetOrdinal("lab_units")) ? 0 : r.GetInt32("lab_units") });
            }
            catch (Exception ex) { Console.WriteLine("GetSubjects error: " + ex.Message); }
            return Ok(items);
        }

        [HttpPost("/api/admin/subjects")]
        public IActionResult CreateSubject([FromBody] JsonElement body)
        {
            try
            {
                var mis = body.GetProperty("mis").GetString() ?? ""; var code = body.GetProperty("code").GetString() ?? "";
                var desc = body.GetProperty("desc").GetString() ?? ""; var lec = body.GetProperty("lec").GetInt32(); var lab = body.GetProperty("lab").GetInt32();
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("INSERT INTO subjects (mis_code, subject_code, description, lec_units, lab_units) VALUES (@mis, @code, @desc, @lec, @lab); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@mis", mis); cmd.Parameters.AddWithValue("@code", code); cmd.Parameters.AddWithValue("@desc", desc); cmd.Parameters.AddWithValue("@lec", lec); cmd.Parameters.AddWithValue("@lab", lab);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPut("/api/admin/subjects/{id}")]
        public IActionResult UpdateSubject(int id, [FromBody] JsonElement body)
        {
            try
            {
                var mis = body.GetProperty("mis").GetString() ?? ""; var code = body.GetProperty("code").GetString() ?? "";
                var desc = body.GetProperty("desc").GetString() ?? ""; var lec = body.GetProperty("lec").GetInt32(); var lab = body.GetProperty("lab").GetInt32();
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("UPDATE subjects SET mis_code=@mis, subject_code=@code, description=@desc, lec_units=@lec, lab_units=@lab WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@mis", mis); cmd.Parameters.AddWithValue("@code", code); cmd.Parameters.AddWithValue("@desc", desc); cmd.Parameters.AddWithValue("@lec", lec); cmd.Parameters.AddWithValue("@lab", lab); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/subjects/{id}")]
        public IActionResult DeleteSubject(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM subjects WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Subject Offerings API ─────────────────────────────────────────
        [HttpGet("/api/admin/subject-offerings")]
        public IActionResult GetSubjectOfferings()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT so.id, so.mis_code, so.user_id AS instructor_uid,
                           s.subject_code, s.description AS title, s.lec_units, s.lab_units,
                           CONCAT(u.first_name, ' ', u.last_name) AS instructor,
                           COALESCE(u.id_number, '—') AS instructor_id_number, so.is_active
                    FROM subject_offerings so
                    JOIN subjects s ON s.id = so.subject_id
                    LEFT JOIN users u ON u.id = so.user_id
                    ORDER BY so.id DESC", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), mis = r.IsDBNull(r.GetOrdinal("mis_code")) ? "" : r.GetString("mis_code"), code = r.IsDBNull(r.GetOrdinal("subject_code")) ? "" : r.GetString("subject_code"), desc = r.IsDBNull(r.GetOrdinal("title")) ? "" : r.GetString("title"), lec = r.IsDBNull(r.GetOrdinal("lec_units")) ? 0 : r.GetInt32("lec_units"), lab = r.IsDBNull(r.GetOrdinal("lab_units")) ? 0 : r.GetInt32("lab_units"), instructorId = r.IsDBNull(r.GetOrdinal("instructor_id_number")) ? "" : r.GetString("instructor_id_number"), inst = r.IsDBNull(r.GetOrdinal("instructor")) ? "—" : r.GetString("instructor"), active = r.GetBoolean("is_active") });
            }
            catch (Exception ex) { Console.WriteLine("GetSubjectOfferings error: " + ex.Message); }
            return Ok(items);
        }

        [HttpPost("/api/admin/subject-offerings")]
public IActionResult CreateOffering([FromBody] JsonElement body)
{
    try
    {
        var mis          = body.GetProperty("mis").GetString() ?? "";
        var instructorId = body.GetProperty("inst").GetInt32();
        if (instructorId == 0) return Ok(new { success = false, error = "Invalid instructor selected." });

        using var conn = DbHelper.GetConnection(_config);
        conn.Open();

        var subjectCmd = new MySqlCommand("SELECT id FROM subjects WHERE mis_code=@mis LIMIT 1", conn);
        subjectCmd.Parameters.AddWithValue("@mis", mis);
        var subjectId = Convert.ToInt32(subjectCmd.ExecuteScalar() ?? 0);
        if (subjectId == 0) return Ok(new { success = false, error = "Subject not found." });

        var checkCmd = new MySqlCommand("SELECT id FROM subject_offerings WHERE subject_id=@sid LIMIT 1", conn);
        checkCmd.Parameters.AddWithValue("@sid", subjectId);
        var existingId = checkCmd.ExecuteScalar();

        if (existingId != null)
        {
            var upd = new MySqlCommand("UPDATE subject_offerings SET user_id=@uid, is_active=1 WHERE subject_id=@sid2", conn);
            upd.Parameters.AddWithValue("@uid",  instructorId);
            upd.Parameters.AddWithValue("@sid2", subjectId);
            upd.ExecuteNonQuery();
        }
        else
        {
            var ins = new MySqlCommand("INSERT INTO subject_offerings (mis_code, subject_id, user_id, is_active) VALUES (@mis2, @sid3, @uid2, 1)", conn);
            ins.Parameters.AddWithValue("@mis2", mis);
            ins.Parameters.AddWithValue("@sid3", subjectId);
            ins.Parameters.AddWithValue("@uid2", instructorId);
            ins.ExecuteNonQuery();
        }
        return Ok(new { success = true });
    }
    catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
}
        [HttpPut("/api/admin/subject-offerings/{id}")]
        public IActionResult UpdateOffering(int id, [FromBody] JsonElement body)
        {
            try
            {
                var active = body.GetProperty("active").GetBoolean();
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("UPDATE subject_offerings SET is_active=@active WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@active", active ? 1 : 0); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/subject-offerings/{id}")]
        public IActionResult DeleteOffering(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM subject_offerings WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Courses API ───────────────────────────────────────────────────
        [HttpGet("/api/admin/courses")]
        public IActionResult GetCourses()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("SELECT id, course_code, course_name FROM courses ORDER BY course_code", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), code = r.GetString("course_code"), name = r.IsDBNull(r.GetOrdinal("course_name")) ? "" : r.GetString("course_name"), sections = new List<object>() });
            }
            catch { }
            return Ok(items);
        }

        [HttpPost("/api/admin/courses")]
        public IActionResult CreateCourse([FromBody] JsonElement body)
        {
            try
            {
                var code = body.GetProperty("code").GetString() ?? ""; var name = body.GetProperty("name").GetString() ?? "";
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("INSERT INTO courses (course_code, course_name) VALUES (@c, @n); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@c", code); cmd.Parameters.AddWithValue("@n", name);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPut("/api/admin/courses/{id}")]
        public IActionResult UpdateCourse(int id, [FromBody] JsonElement body)
        {
            try
            {
                var code = body.GetProperty("code").GetString() ?? ""; var name = body.GetProperty("name").GetString() ?? "";
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("UPDATE courses SET course_code=@c, course_name=@n WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@c", code); cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/courses/{id}")]
        public IActionResult DeleteCourse(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM courses WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Sections API ──────────────────────────────────────────────────
        [HttpGet("/api/admin/sections")]
        public IActionResult GetSections(int courseId)
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("SELECT id, section_name, year_level FROM sections WHERE course_id=@cid ORDER BY id", conn);
                cmd.Parameters.AddWithValue("@cid", courseId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("section_name"), year = r.IsDBNull(r.GetOrdinal("year_level")) ? 0 : r.GetInt32("year_level") });
            }
            catch { }
            return Ok(items);
        }

        [HttpPost("/api/admin/sections")]
        public IActionResult CreateSection([FromBody] JsonElement body)
        {
            try
            {
                var courseId = body.GetProperty("courseId").GetInt32();
                var name     = body.GetProperty("name").GetString() ?? "";
                var year     = body.GetProperty("year").ValueKind == JsonValueKind.Number
                               ? body.GetProperty("year").GetInt32()
                               : int.TryParse(body.GetProperty("year").GetString(), out var y) ? y : 0;
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("INSERT INTO sections (course_id, section_name, year_level) VALUES (@cid, @n, @y); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@cid", courseId); cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@y", year);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpPut("/api/admin/sections/{id}")]
        public IActionResult UpdateSection(int id, [FromBody] JsonElement body)
        {
            try
            {
                var name = body.GetProperty("name").GetString() ?? "";
                var year = body.GetProperty("year").ValueKind == JsonValueKind.Number
                           ? body.GetProperty("year").GetInt32()
                           : int.TryParse(body.GetProperty("year").GetString(), out var y) ? y : 0;
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("UPDATE sections SET section_name=@n, year_level=@y WHERE id=@id", conn);
                cmd.Parameters.AddWithValue("@n", name); cmd.Parameters.AddWithValue("@y", year); cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return Ok(new { success = true });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/sections/{id}")]
        public IActionResult DeleteSection(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM sections WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpGet("/api/admin/sections/count")]
        public IActionResult GetSectionCount()
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); return Ok(new { count = GetCount(conn, "SELECT COUNT(*) FROM sections") }); }
            catch (Exception ex) { return Ok(new { count = 0, error = ex.Message }); }
        }

        [HttpGet("/api/admin/sections/all")]
        public IActionResult GetAllSections()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT s.id, s.section_name, s.year_level, c.course_code
                    FROM sections s JOIN courses c ON c.id = s.course_id
                    ORDER BY c.course_code, s.year_level, s.section_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("section_name"), year = r.IsDBNull(r.GetOrdinal("year_level")) ? 0 : r.GetInt32("year_level"), courseCode = r.GetString("course_code") });
            }
            catch (Exception ex) { Console.WriteLine("GetAllSections error: " + ex.Message); }
            return Ok(items);
        }

        // ── Curriculums API ───────────────────────────────────────────────
        [HttpGet("/api/admin/curriculums")]
        public IActionResult GetCurriculums()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT s.id AS sectionId, c.course_code AS courseCode,
                           s.year_level AS yearLevel, s.section_name AS section, cu.id AS curriculumId
                    FROM sections s
                    JOIN courses c ON c.id = s.course_id
                    LEFT JOIN curriculum cu
                        ON  cu.course_id  = s.course_id
                        AND cu.year_level = s.year_level
                        AND cu.section    = s.section_name
                    WHERE s.is_active = 1
                    ORDER BY c.course_code, s.year_level, s.section_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new {
                        id            = r.IsDBNull(r.GetOrdinal("curriculumId")) ? r.GetInt32("sectionId") * -1 : r.GetInt32("curriculumId"),
                        sectionId     = r.GetInt32("sectionId"),
                        courseCode    = r.GetString("courseCode"),
                        yearLevel     = r.GetInt32("yearLevel"),
                        section       = r.GetString("section"),
                        hasCurriculum = !r.IsDBNull(r.GetOrdinal("curriculumId"))
                    });
            }
            catch (Exception ex) { Console.WriteLine("GetCurriculums error: " + ex.Message); }
            return Ok(items);
        }

        // ── Instructors API ───────────────────────────────────────────────
        [HttpGet("/api/admin/instructors")]
        public IActionResult GetInstructors()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT id, CONCAT(first_name, ' ', last_name) AS name,
                           COALESCE(id_number, '—') AS id_number, created_at AS joinedDate
                    FROM users WHERE role = 'Instructor' AND is_active = 1 ORDER BY first_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("name"), employeeId = r.GetString("id_number"), joinedDate = r.GetDateTime("joinedDate").ToString("MMMM d, yyyy") });
            }
            catch (Exception ex) { return Ok(new { error = ex.Message }); }
            return Ok(items);
        }

        [HttpGet("/api/admin/instructors/{id}")]
        public IActionResult GetInstructorDetail(int id)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("SELECT id, first_name AS firstName, last_name AS lastName, COALESCE(id_number, '—') AS id_number FROM users WHERE id = @id AND role = 'Instructor'", conn);
                cmd.Parameters.AddWithValue("@id", id);
                string firstName = "—", lastName = "—", idNumber = "—";
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return Ok(new { error = "Instructor not found." });
                    firstName = r.IsDBNull(r.GetOrdinal("firstName")) ? "—" : r.GetString("firstName");
                    lastName  = r.IsDBNull(r.GetOrdinal("lastName"))  ? "—" : r.GetString("lastName");
                    idNumber  = r.IsDBNull(r.GetOrdinal("id_number")) ? "—" : r.GetString("id_number");
                }
                var positions = new List<string>();
                var posCmd = new MySqlCommand("SELECT position_title FROM organizations WHERE user_id = @uid ORDER BY id", conn);
                posCmd.Parameters.AddWithValue("@uid", id);
                using (var pr = posCmd.ExecuteReader()) { while (pr.Read()) if (!pr.IsDBNull(0)) positions.Add(pr.GetString(0)); }
                return Ok(new { firstName, lastName, employeeId = idNumber, positions });
            }
            catch (Exception ex) { return Ok(new { error = ex.Message }); }
        }

        // ── Students API ──────────────────────────────────────────────────
        [HttpGet("/api/admin/students")]
        public IActionResult GetStudents()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT u.id,
                           CONCAT(u.first_name,' ',u.last_name)         AS name,
                           COALESCE(u.student_number, u.id_number, '—') AS student_number,
                           COALESCE(c.course_code,'—')                  AS course_code,
                           COALESCE(cu.year_level, 0)                   AS year_level,
                           COALESCE(cu.section,'—')                     AS section,
                           CASE
                               WHEN u.student_number IS NULL AND u.id_number IS NULL THEN 'Pending'
                               WHEN (SELECT COUNT(*) FROM clearance_subjects cs2
                                     WHERE cs2.student_number = COALESCE(u.student_number, u.id_number)) = 0 THEN 'Pending'
                               WHEN (SELECT COUNT(*) FROM clearance_subjects cs
                                     WHERE cs.student_number = COALESCE(u.student_number, u.id_number)
                                       AND cs.status != 'Cleared') = 0 THEN 'Cleared'
                               ELSE 'Pending'
                           END AS cs
                    FROM users u
                    LEFT JOIN curriculum cu ON cu.id = u.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE u.role = 'Student' AND u.is_active = 1
                    ORDER BY u.first_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("name"), idNum = r.GetString("student_number"), course = r.GetString("course_code"), year = r.GetInt32("year_level"), section = r.GetString("section"), cs = r.GetString("cs") });
            }
            catch (Exception ex) { return Ok(new { error = ex.Message }); }
            return Ok(items);
        }

        // ── Student Signatories API ───────────────────────────────────────
        [HttpGet("/api/admin/student-signatories")]
        public IActionResult GetStudentSignatories()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT us.id,
                           u.id                                          AS userId,
                           CONCAT(u.first_name,' ',u.last_name)         AS name,
                           COALESCE(u.student_number, u.id_number, '—') AS student_number,
                           COALESCE(c.course_code,'—')                  AS course,
                           COALESCE(cu.year_level, 0)                   AS year_level,
                           COALESCE(cu.section,'—')                     AS section,
                           us.position
                    FROM user_signatures us
                    JOIN  users      u  ON u.id  = us.user_id
                    LEFT JOIN curriculum cu ON cu.id = u.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE us.position IS NOT NULL
                    ORDER BY us.id", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), userId = r.GetInt32("userId"), name = r.GetString("name"), idNum = r.IsDBNull(r.GetOrdinal("student_number")) ? "—" : r.GetString("student_number"), course = r.GetString("course"), year = r.GetInt32("year_level"), section = r.GetString("section"), position = r.GetString("position") });
            }
            catch (Exception ex) { Console.WriteLine("GetStudentSignatories error: " + ex.Message); }
            return Ok(items);
        }

        [HttpPost("/api/admin/student-signatories")]
        public IActionResult CreateStudentSignatory([FromBody] JsonElement body)
        {
            try
            {
                var userId   = body.GetProperty("userId").GetInt32();
                var position = body.GetProperty("position").GetString() ?? "";
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("INSERT INTO user_signatures (user_id, position) VALUES (@uid, @pos); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@uid", userId); cmd.Parameters.AddWithValue("@pos", position);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/student-signatories/{id}")]
        public IActionResult DeleteStudentSignatory(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM user_signatures WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Staff API ─────────────────────────────────────────────────────
        [HttpGet("/api/admin/staff")]
        public IActionResult GetStaff()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT u.id, CONCAT(u.first_name,' ',u.last_name) AS name, COALESCE(u.id_number, '—') AS id_number
                    FROM users u WHERE u.role IN ('Staff', 'Admin') AND u.is_active = 1 ORDER BY u.first_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("name"), id_number = r.IsDBNull(r.GetOrdinal("id_number")) ? "—" : r.GetString("id_number"), approved = 0, pending = 0 });
            }
            catch (Exception ex) { Console.WriteLine("GetStaff error: " + ex.Message); return Ok(new { error = ex.Message }); }
            return Ok(items);
        }

        // ── Staff Positions API ───────────────────────────────────────────
        [HttpGet("/api/admin/staff-positions")]
        public IActionResult GetStaffPositions()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT
                        o.id,
                        CONCAT(u.first_name,' ',u.last_name) AS name,
                        COALESCE(u.id_number, '—')           AS eid,
                        o.position_title                     AS pos,
                        SUM(CASE WHEN co.status='Cleared' THEN 1 ELSE 0 END) AS approved,
                        SUM(CASE WHEN co.status='Pending' THEN 1 ELSE 0 END) AS pending
                    FROM organizations o
                    JOIN users u ON u.id = o.user_id
                    LEFT JOIN clearance_organization co ON co.position = o.position_title
                    GROUP BY o.id, u.first_name, u.last_name, u.id_number, o.position_title
                    ORDER BY u.first_name, o.id", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), name = r.GetString("name"), eid = r.IsDBNull(r.GetOrdinal("eid")) ? "—" : r.GetString("eid"), pos = r.IsDBNull(r.GetOrdinal("pos")) ? "—" : r.GetString("pos"), approved = r.IsDBNull(r.GetOrdinal("approved")) ? 0 : Convert.ToInt32(r["approved"]), pending = r.IsDBNull(r.GetOrdinal("pending")) ? 0 : Convert.ToInt32(r["pending"]) });
            }
            catch (Exception ex) { Console.WriteLine("GetStaffPositions error: " + ex.Message); }
            return Ok(items);
        }

        [HttpPost("/api/admin/staff-positions")]
        public IActionResult CreateStaffPosition([FromBody] JsonElement body)
        {
            try
            {
                var staffUserId = body.GetProperty("staffId").GetInt32();
                var pos         = body.GetProperty("pos").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(pos))
                    return Ok(new { success = false, error = "Position is required." });
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand("INSERT INTO organizations (user_id, position_title) VALUES (@uid, @pos); SELECT LAST_INSERT_ID();", conn);
                cmd.Parameters.AddWithValue("@uid", staffUserId); cmd.Parameters.AddWithValue("@pos", pos);
                return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/staff-positions/{id}")]
        public IActionResult DeleteStaffPosition(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM organizations WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── Org Signatories API ───────────────────────────────────────────
        [HttpGet("/api/admin/org-signatories")]
        public IActionResult GetOrgSignatories()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT o.id, u.id AS userId, CONCAT(u.first_name,' ',u.last_name) AS name,
                           COALESCE(u.id_number, '—') AS eid, COALESCE(o.position_title, '—') AS pos,
                           o.curriculum_id AS curriculumId, c.course_code AS courseCode,
                           cu.year_level AS yearLevel, cu.section
                    FROM organizations o
                    JOIN users u ON u.id = o.user_id
                    LEFT JOIN curriculum cu ON cu.id = o.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    ORDER BY u.first_name, o.id", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new { id = r.GetInt32("id"), userId = r.GetInt32("userId"), name = r.GetString("name"), eid = r.GetString("eid"), pos = r.GetString("pos"), curriculumId = r.IsDBNull(r.GetOrdinal("curriculumId")) ? (int?)null : r.GetInt32("curriculumId"), courseCode = r.IsDBNull(r.GetOrdinal("courseCode")) ? "" : r.GetString("courseCode"), yearLevel = r.IsDBNull(r.GetOrdinal("yearLevel")) ? 0 : r.GetInt32("yearLevel"), section = r.IsDBNull(r.GetOrdinal("section")) ? "" : r.GetString("section") });
            }
            catch (Exception ex) { Console.WriteLine("GetOrgSignatories error: " + ex.Message); }
            return Ok(items);
        }

        [HttpPost("/api/admin/org-signatories")]
        public IActionResult CreateOrgSignatory([FromBody] JsonElement body)
        {
            try
            {
                var userId = body.GetProperty("userId").GetInt32();
                var pos    = body.GetProperty("pos").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(pos))
                    return Ok(new { success = false, error = "Position is required." });

                if (pos == "Class Adviser")
                {
                    if (!body.TryGetProperty("curriculumId", out var cidProp) || cidProp.ValueKind == JsonValueKind.Null)
                        return Ok(new { success = false, error = "curriculumId is required for Class Adviser." });
                    var rawId = cidProp.GetInt32();
                    using var conn = DbHelper.GetConnection(_config);
                    conn.Open();
                    int curriculumId;
                    if (rawId < 0)
                    {
                        var secCmd = new MySqlCommand("SELECT s.course_id, s.year_level, s.section_name FROM sections s WHERE s.id = @sid", conn);
                        secCmd.Parameters.AddWithValue("@sid", rawId * -1);
                        using var sr = secCmd.ExecuteReader();
                        if (!sr.Read()) return Ok(new { success = false, error = "Section not found." });
                        var courseId = sr.GetInt32("course_id"); var yearLevel = sr.GetInt32("year_level"); var sectionName = sr.GetString("section_name");
                        sr.Close();
                        var insCmd = new MySqlCommand("INSERT INTO curriculum (course_id, year_level, section) VALUES (@cid, @yl, @sec); SELECT LAST_INSERT_ID();", conn);
                        insCmd.Parameters.AddWithValue("@cid", courseId); insCmd.Parameters.AddWithValue("@yl", yearLevel); insCmd.Parameters.AddWithValue("@sec", sectionName);
                        curriculumId = Convert.ToInt32(insCmd.ExecuteScalar());
                    }
                    else { curriculumId = rawId; }

                    var dupCmd = new MySqlCommand("SELECT id FROM organizations WHERE curriculum_id=@cid LIMIT 1", conn);
                    dupCmd.Parameters.AddWithValue("@cid", curriculumId);
                    if (dupCmd.ExecuteScalar() != null)
                        return Ok(new { success = false, error = "A Class Adviser is already assigned to this section." });

                    var cmd = new MySqlCommand("INSERT INTO organizations (user_id, position_title, curriculum_id) VALUES (@uid, 'Class Adviser', @cid); SELECT LAST_INSERT_ID();", conn);
                    cmd.Parameters.AddWithValue("@uid", userId); cmd.Parameters.AddWithValue("@cid", curriculumId);
                    return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
                }
                else
                {
                    using var conn = DbHelper.GetConnection(_config);
                    conn.Open();
                    var cmd = new MySqlCommand("INSERT INTO organizations (user_id, position_title, curriculum_id) VALUES (@uid, @pos, NULL); SELECT LAST_INSERT_ID();", conn);
                    cmd.Parameters.AddWithValue("@uid", userId); cmd.Parameters.AddWithValue("@pos", pos);
                    return Ok(new { success = true, id = Convert.ToInt32(cmd.ExecuteScalar()) });
                }
            }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        [HttpDelete("/api/admin/org-signatories/{id}")]
        public IActionResult DeleteOrgSignatory(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM organizations WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); return Ok(new { success = true }); }
            catch (Exception ex) { return Ok(new { success = false, error = ex.Message }); }
        }

        // ── All Users API (for assign-position dropdown) ─────────────────
        [HttpGet("/api/admin/all-users")]
        public IActionResult GetAllUsers()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT u.id,
                           CONCAT(u.first_name,' ',u.last_name) AS name,
                           u.role,
                           COALESCE(u.student_number, u.id_number, '—') AS id_num
                    FROM users u
                    WHERE u.is_active = 1
                      AND u.role NOT IN ('Pending')
                    ORDER BY u.role, u.first_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new
                    {
                        id     = r.GetInt32("id"),
                        name   = r.GetString("name"),
                        role   = r.GetString("role"),
                        idNum  = r.IsDBNull(r.GetOrdinal("id_num")) ? "—" : r.GetString("id_num")
                    });
            }
            catch (Exception ex) { Console.WriteLine("GetAllUsers error: " + ex.Message); }
            return Ok(items);
        }

        // ── Org Clearance Requests (by position + period) ─────────────────
        [HttpGet("/api/admin/org-clearance")]
        public IActionResult GetOrgClearance([FromQuery] string position, [FromQuery] int periodId = 0)
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT
                        co.id,
                        co.student_number,
                        CONCAT(u.first_name,' ',u.last_name) AS student_name,
                        co.status,
                        co.period_id,
                        COALESCE(CONCAT('A.Y. ', ap.year_label, ', ', ap.semester), '—') AS period_label,
                        co.created_at
                    FROM clearance_organization co
                    JOIN  users           u  ON u.student_number = co.student_number COLLATE utf8mb4_unicode_ci
                    LEFT JOIN academic_periods ap ON ap.id = co.period_id
                    WHERE co.position COLLATE utf8mb4_unicode_ci = @pos
                      AND (@pid = 0 OR co.period_id = @pid)
                    ORDER BY co.created_at DESC", conn);
                cmd.Parameters.AddWithValue("@pos", position ?? "");
                cmd.Parameters.AddWithValue("@pid", periodId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new
                    {
                        id            = r.GetInt32("id"),
                        studentNumber = r.IsDBNull(r.GetOrdinal("student_number")) ? "—" : r.GetString("student_number"),
                        studentName   = r.IsDBNull(r.GetOrdinal("student_name"))   ? "—" : r.GetString("student_name"),
                        status        = r.IsDBNull(r.GetOrdinal("status"))         ? "—" : r.GetString("status"),
                        periodId      = r.IsDBNull(r.GetOrdinal("period_id"))      ? 0   : r.GetInt32("period_id"),
                        periodLabel   = r.IsDBNull(r.GetOrdinal("period_label"))   ? "—" : r.GetString("period_label"),
                        createdAt     = r.IsDBNull(r.GetOrdinal("created_at"))     ? ""  : r.GetDateTime("created_at").ToString("MMM d, yyyy")
                    });
            }
            catch (Exception ex) { Console.WriteLine("GetOrgClearance error: " + ex.Message); }
            return Ok(items);
        }

        // ── All Org Positions unified (user_signatures + organizations) ────
        [HttpGet("/api/admin/all-org-positions")]
        public IActionResult GetAllOrgPositions()
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(@"
                    SELECT
                        'sig'   AS source,
                        us.id,
                        u.id    AS userId,
                        CONCAT(u.first_name,' ',u.last_name) AS name,
                        u.role,
                        COALESCE(u.student_number, u.id_number, '—') AS id_num,
                        us.position AS pos_title,
                        NULL    AS course_code,
                        NULL    AS year_level,
                        NULL    AS section
                    FROM user_signatures us
                    JOIN users u ON u.id = us.user_id
                    WHERE us.position IS NOT NULL

                    UNION ALL

                    SELECT
                        'org'   AS source,
                        o.id,
                        u.id    AS userId,
                        CONCAT(u.first_name,' ',u.last_name) AS name,
                        u.role,
                        COALESCE(u.student_number, u.id_number, '—') AS id_num,
                        o.position_title AS pos_title,
                        c.course_code,
                        cu.year_level,
                        cu.section
                    FROM organizations o
                    JOIN users      u  ON u.id  = o.user_id
                    LEFT JOIN curriculum cu ON cu.id = o.curriculum_id
                    LEFT JOIN courses    c  ON c.id  = cu.course_id
                    WHERE o.is_active = 1

                    ORDER BY name, pos_title", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    items.Add(new
                    {
                        source     = r.GetString("source"),
                        id         = r.GetInt32("id"),
                        userId     = r.GetInt32("userId"),
                        name       = r.GetString("name"),
                        role       = r.GetString("role"),
                        idNum      = r.IsDBNull(r.GetOrdinal("id_num"))      ? "—" : r.GetString("id_num"),
                        posTitle   = r.IsDBNull(r.GetOrdinal("pos_title"))   ? "—" : r.GetString("pos_title"),
                        courseCode = r.IsDBNull(r.GetOrdinal("course_code")) ? ""  : r.GetString("course_code"),
                        yearLevel  = r.IsDBNull(r.GetOrdinal("year_level"))  ? 0   : r.GetInt32("year_level"),
                        section    = r.IsDBNull(r.GetOrdinal("section"))     ? ""  : r.GetString("section")
                    });
            }
            catch (Exception ex) { Console.WriteLine("GetAllOrgPositions error: " + ex.Message); }
            return Ok(items);
        }

        // ── Form Posts ────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult ActivateUser(int id, string role)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); var cmd = new MySqlCommand("UPDATE users SET role=@r, is_active=1 WHERE id=@id", conn); cmd.Parameters.AddWithValue("@r", role); cmd.Parameters.AddWithValue("@id", id); cmd.ExecuteNonQuery(); } catch { }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeactivateUser(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("UPDATE users SET is_active=0 WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); } catch { }
            return RedirectToAction(nameof(Dashboard));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int id)
        {
            try { using var conn = DbHelper.GetConnection(_config); conn.Open(); new MySqlCommand("DELETE FROM users WHERE id=@id", conn).Also(c => { c.Parameters.AddWithValue("@id", id); c.ExecuteNonQuery(); }); } catch { }
            return RedirectToAction(nameof(Dashboard));
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static int GetCount(MySqlConnection conn, string sql) =>
            Convert.ToInt32(new MySqlCommand(sql, conn).ExecuteScalar() ?? 0);
    }
}