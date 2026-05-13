using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Data;
using System.Security.Claims;

namespace OnlineClearanceSystem.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    public class NotificationController : Controller
    {
        private readonly IConfiguration _config;

        public NotificationController(IConfiguration config)
        {
            _config = config;
        }

        // GET /api/notification/list
        [HttpGet("list")]
        public IActionResult List()
        {
            var userId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";

            var notifications = new List<object>();

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Recent announcements (all roles)
                var annCmd = new MySqlCommand(@"
                    SELECT id, title, type, posted_at
                    FROM   announcements
                    ORDER  BY posted_at DESC
                    LIMIT  5", conn);

                using var ar = annCmd.ExecuteReader();
                while (ar.Read())
                {
                    notifications.Add(new
                    {
                        id     = ar.GetInt32("id"),
                        title  = ar.GetString("title"),
                        type   = ar.IsDBNull(ar.GetOrdinal("type")) ? "General" : ar.GetString("type"),
                        source = "announcement",
                        icon   = "fa-bullhorn",
                        time   = ar.GetDateTime("posted_at").ToString("MMM d, h:mm tt")
                    });
                }
                ar.Close();

                // Clearance status updates (students only)
                if (role == "Student")
                {
                    var snCmd = new MySqlCommand(
                        "SELECT student_number FROM users WHERE id = @uid LIMIT 1", conn);
                    snCmd.Parameters.AddWithValue("@uid", userId);
                    var studentNumber = snCmd.ExecuteScalar()?.ToString() ?? "";

                    if (!string.IsNullOrEmpty(studentNumber))
                    {
                        var clrCmd = new MySqlCommand(@"
                            SELECT
                                cs.mis_code,
                                COALESCE(s.subject_code, cs.mis_code) AS subject_code,
                                cs.status,
                                cs.period_id
                            FROM clearance_subjects cs
                            LEFT JOIN subject_offerings so ON so.mis_code = cs.mis_code
                            LEFT JOIN subjects          s  ON s.id        = so.subject_id
                            WHERE cs.student_number = @sn
                              AND cs.status IN ('Cleared', 'Declined')
                            ORDER BY cs.id DESC
                            LIMIT 5", conn);
                        clrCmd.Parameters.AddWithValue("@sn", studentNumber);

                        using var cr = clrCmd.ExecuteReader();
                        while (cr.Read())
                        {
                            var status = cr.GetString("status");
                            notifications.Add(new
                            {
                                id     = 0,
                                title  = $"{cr.GetString("subject_code")} — {status}",
                                type   = status,
                                source = "clearance",
                                icon   = status == "Cleared" ? "fa-check-circle" : "fa-times-circle",
                                time   = "Recent"
                            });
                        }
                    }
                }

                // Pending clearance requests (instructors only)
                if (role == "Instructor")
                {
                    var pendCmd = new MySqlCommand(@"
                        SELECT COUNT(*) AS cnt
                        FROM   clearance_subjects cs
                        JOIN   subject_offerings  so ON so.mis_code = cs.mis_code
                        WHERE  so.user_id = @uid
                          AND  cs.status  = 'Pending'", conn);
                    pendCmd.Parameters.AddWithValue("@uid", userId);
                    var pendingCount = Convert.ToInt32(pendCmd.ExecuteScalar() ?? 0);

                    if (pendingCount > 0)
                    {
                        notifications.Insert(0, new
                        {
                            id     = 0,
                            title  = $"{pendingCount} pending clearance request{(pendingCount > 1 ? "s" : "")}",
                            type   = "Pending",
                            source = "clearance",
                            icon   = "fa-clock",
                            time   = "Now"
                        });
                    }
                }
            }
            catch { }

            return Json(new
            {
                count = notifications.Count,
                items = notifications.Take(10)
            });
        }
    }
}
