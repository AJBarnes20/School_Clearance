using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Models;
using OnlineClearanceSystem.Data;

namespace OnlineClearanceSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _config;
        private readonly EmailService   _email;

        public HomeController(IConfiguration config, EmailService email)
        {
            _config = config;
            _email  = email;
        }

        // ── GET /Home/Index ────────────────────────────────────
        public IActionResult Index() => View();

        // ── GET /Home/Login ────────────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectBasedOnRole();

            if (TempData["RegisterSuccess"] != null)
                ViewBag.SuccessMessage = TempData["RegisterSuccess"];

            return View(new LoginViewModel());
        }

        // ── POST /Home/Login ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                var cmd = new MySqlCommand(@"
                    SELECT id, id_number, password, first_name,
                           last_name, role, is_active
                    FROM users
                    WHERE id_number = @idnum LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@idnum", model.IdNumber);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    ViewBag.ErrorMessage = "Invalid ID Number or password.";
                    return View(model);
                }

                var id        = r.GetInt32("id");
                var hash      = r.GetString("password");
                var firstName = r.GetString("first_name");
                var lastName  = r.GetString("last_name");
                var isActive  = r.GetBoolean("is_active");
                var role      = r.IsDBNull(r.GetOrdinal("role"))
                                    ? null
                                    : r.GetString("role");
                r.Close();

                if (!isActive || role == null || role == "Pending")
                {
                    ViewBag.ErrorMessage =
                        "Your account is pending activation. " +
                        "Please wait for the Admin to assign your role " +
                        "and activate your account. You will be notified via email.";
                    return View(model);
                }

                bool valid = hash.StartsWith("$2")
                    ? BCrypt.Net.BCrypt.Verify(model.Password, hash)
                    : hash == model.Password;

                if (!valid)
                {
                    ViewBag.ErrorMessage = "Invalid ID Number or password.";
                    return View(model);
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                    new Claim(ClaimTypes.Name,  $"{firstName} {lastName}"),
                    new Claim(ClaimTypes.Role,  role),
                    new Claim("FirstName",      firstName),
                    new Claim("LastName",       lastName),
                    new Claim(ClaimTypes.Surname, lastName),
                };

                var identity  = new ClaimsIdentity(
                    claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { IsPersistent = model.RememberMe });

                return RedirectBasedOnRole();
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Connection error: " + ex.Message;
                return View(model);
            }
        }

        // ── GET /Home/Register ─────────────────────────────────
        [HttpGet]
        public IActionResult Register()
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectBasedOnRole();

            var model = new RegisterViewModel();
            PopulateCourseOptions(model);
            return View(model);
        }

        // ── POST /Home/Register ────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                PopulateCourseOptions(model);
                return View(model);
            }

            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Duplicate ID Number check
                var checkId = new MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE id_number = @id", conn);
                checkId.Parameters.AddWithValue("@id", model.IdNumber);
                if (Convert.ToInt32(checkId.ExecuteScalar()) > 0)
                {
                    ModelState.AddModelError(nameof(model.IdNumber),
                        "That ID Number is already registered.");
                    PopulateCourseOptions(model);
                    return View(model);
                }

                // Duplicate Email check
                var checkEmail = new MySqlCommand(
                    "SELECT COUNT(*) FROM users WHERE email = @email", conn);
                checkEmail.Parameters.AddWithValue("@email", model.Email.Trim().ToLower());
                if (Convert.ToInt32(checkEmail.ExecuteScalar()) > 0)
                {
                    ModelState.AddModelError(nameof(model.Email),
                        "That email address is already registered.");
                    PopulateCourseOptions(model);
                    return View(model);
                }

                var hash = BCrypt.Net.BCrypt.HashPassword(model.Password);

                // Resolve curriculum_id if course/year/section were provided (optional)
                int? curriculumId = null;
                if (!string.IsNullOrWhiteSpace(model.Course) && model.YearLevel.HasValue)
                {
                    try
                    {
                        var courseCmd = new MySqlCommand(
                            "SELECT id FROM courses WHERE course_name = @c LIMIT 1", conn);
                        courseCmd.Parameters.AddWithValue("@c", model.Course.Trim());
                        var courseId = courseCmd.ExecuteScalar();

                        if (courseId != null && courseId != DBNull.Value)
                        {
                            var section = model.Section?.Trim() ?? "";
                            var findCur = new MySqlCommand(@"
                                SELECT id FROM curriculum
                                WHERE course_id  = @cid
                                  AND year_level = @yl
                                  AND section    = @sec
                                LIMIT 1", conn);
                            findCur.Parameters.AddWithValue("@cid", Convert.ToInt32(courseId));
                            findCur.Parameters.AddWithValue("@yl",  model.YearLevel.Value);
                            findCur.Parameters.AddWithValue("@sec", section);
                            var existing = findCur.ExecuteScalar();

                            if (existing != null && existing != DBNull.Value)
                            {
                                curriculumId = Convert.ToInt32(existing);
                            }
                            else if (!string.IsNullOrEmpty(section))
                            {
                                var insCur = new MySqlCommand(@"
                                    INSERT INTO curriculum (course_id, year_level, section)
                                    VALUES (@cid, @yl, @sec);
                                    SELECT LAST_INSERT_ID();", conn);
                                insCur.Parameters.AddWithValue("@cid", Convert.ToInt32(courseId));
                                insCur.Parameters.AddWithValue("@yl",  model.YearLevel.Value);
                                insCur.Parameters.AddWithValue("@sec", section);
                                curriculumId = Convert.ToInt32(insCur.ExecuteScalar());
                            }
                        }
                    }
                    catch { /* curriculum is optional — skip if anything fails */ }
                }

                // Insert user — note: users table does NOT have course/year_level/section
                // columns; those live in the curriculum table via curriculum_id.
                var cmd = new MySqlCommand(@"
                    INSERT INTO users
                        (id_number, email, password,
                         first_name, middle_initial, last_name, suffix_name,
                         curriculum_id,
                         role, is_active, created_at)
                    VALUES
                        (@idnum, @email, @p,
                         @fn, @mi, @ln, @sx,
                         @cid,
                         'Pending', 0, NOW())", conn);

                cmd.Parameters.AddWithValue("@idnum", model.IdNumber.Trim());
                cmd.Parameters.AddWithValue("@email", model.Email.Trim().ToLower());
                cmd.Parameters.AddWithValue("@p",     hash);
                cmd.Parameters.AddWithValue("@fn",    model.FirstName.Trim());
                cmd.Parameters.AddWithValue("@mi",    (object?)model.MiddleInitial?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ln",    model.LastName.Trim());
                cmd.Parameters.AddWithValue("@sx",    (object?)model.Suffix?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cid",   curriculumId.HasValue ? (object)curriculumId.Value : DBNull.Value);
                cmd.ExecuteNonQuery();

                TempData["RegisterSuccess"] =
                    $"Account registered for {model.FirstName} {model.LastName}. " +
                    "Please wait for the Admin to activate your account. " +
                    $"You will be notified at {model.Email} once your account is activated.";

                return RedirectToAction(nameof(Login));
            }
            catch (Exception ex)
            {
                ViewBag.ErrorMessage = "Error saving account: " + ex.Message;
                PopulateCourseOptions(model);
                return View(model);
            }
        }

        // ── GET /Home/GetSections ──────────────────────────────
        [HttpGet]
        public IActionResult GetSections(string courseId)
        {
            var items = new List<object>();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();

                // Try sections table first; fall back to curriculum table
                try
                {
                    var cmd = new MySqlCommand(@"
                        SELECT s.id AS val, s.section_name AS txt, s.year_level
                        FROM   sections s
                        JOIN   courses  c ON c.id = s.course_id
                        WHERE  c.course_name = @cid AND s.is_active = 1
                        ORDER BY s.year_level, s.section_name", conn);
                    cmd.Parameters.AddWithValue("@cid", courseId ?? "");
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        items.Add(new
                        {
                            value     = r.GetString("txt"),
                            text      = $"Year {r.GetInt32("year_level")} – {r.GetString("txt")}",
                            yearLevel = r.GetInt32("year_level")
                        });
                }
                catch
                {
                    // Fallback: derive sections from curriculum table
                    var cmd2 = new MySqlCommand(@"
                        SELECT DISTINCT cu.section, cu.year_level
                        FROM   curriculum cu
                        JOIN   courses    c  ON c.id = cu.course_id
                        WHERE  c.course_name = @cid
                          AND  cu.section IS NOT NULL AND cu.section != ''
                        ORDER BY cu.year_level, cu.section", conn);
                    cmd2.Parameters.AddWithValue("@cid", courseId ?? "");
                    using var r2 = cmd2.ExecuteReader();
                    while (r2.Read())
                        items.Add(new
                        {
                            value     = r2.GetString("section"),
                            text      = $"Year {r2.GetInt32("year_level")} – {r2.GetString("section")}",
                            yearLevel = r2.GetInt32("year_level")
                        });
                }
            }
            catch { }
            return Json(items);
        }

        // ── POST /Home/Logout ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        // ── GET /Home/AccessDenied ─────────────────────────────
        public IActionResult AccessDenied() => View();

        // ── Helper: populate course dropdown ───────────────────
        private void PopulateCourseOptions(RegisterViewModel model)
        {
            model.CourseOptions.Clear();
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, course_name FROM courses WHERE is_active = 1 ORDER BY course_name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    model.CourseOptions.Add(new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                    {
                        Value = r.GetString("course_name"),
                        Text  = r.GetString("course_name")
                    });
            }
            catch { }
        }

        // ── OTP: send code before password change ──────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SendPasswordOtp()
        {
            if (!User.Identity!.IsAuthenticated) return Json(new { success = false });
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var infoCmd = new MySqlCommand(
                    "SELECT CONCAT(first_name,' ',last_name) AS name, email FROM users WHERE id=@id LIMIT 1", conn);
                infoCmd.Parameters.AddWithValue("@id", userId);
                string name = "", email = "";
                using (var r = infoCmd.ExecuteReader())
                    if (r.Read()) { name = r.IsDBNull(0) ? "" : r.GetString(0); email = r.IsDBNull(1) ? "" : r.GetString(1); }

                if (string.IsNullOrEmpty(email))
                    return Json(new { success = false, error = "No email on file." });

                var otp    = new Random().Next(100000, 999999).ToString();
                var expiry = DateTime.Now.AddMinutes(10);
                var upd = new MySqlCommand(
                    "UPDATE users SET otp_code=@otp, otp_expiry=@exp WHERE id=@id", conn);
                upd.Parameters.AddWithValue("@otp", otp);
                upd.Parameters.AddWithValue("@exp", expiry);
                upd.Parameters.AddWithValue("@id",  userId);
                upd.ExecuteNonQuery();

                await _email.SendOtpAsync(email, name, otp);
                return Json(new { success = true, maskedEmail = MaskEmail(email) });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult VerifyPasswordOtp([FromBody] VerifyOtpDto dto)
        {
            if (!User.Identity!.IsAuthenticated) return Json(new { success = false });
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT otp_code, otp_expiry FROM users WHERE id=@id LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@id", userId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return Json(new { success = false, error = "User not found." });
                var stored = r.IsDBNull(0) ? "" : r.GetString(0);
                var expiry = r.IsDBNull(1) ? DateTime.MinValue : r.GetDateTime(1);
                r.Close();
                if (stored != dto.Otp)     return Json(new { success = false, error = "Incorrect code." });
                if (DateTime.Now > expiry) return Json(new { success = false, error = "Code has expired." });
                // Clear OTP
                new MySqlCommand("UPDATE users SET otp_code=NULL, otp_expiry=NULL WHERE id=@id", conn)
                    .Also(c => { c.Parameters.AddWithValue("@id", userId); c.ExecuteNonQuery(); });
                return Json(new { success = true });
            }
            catch (Exception ex) { return Json(new { success = false, error = ex.Message }); }
        }

        // ── Forgot Password — Step 1: send OTP ─────────────────
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id, CONCAT(first_name,' ',last_name) AS name FROM users WHERE email=@e AND is_active=1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@e", email.Trim().ToLower());
                string name = ""; int userId = 0;
                using (var r = cmd.ExecuteReader())
                    if (r.Read()) { userId = r.GetInt32("id"); name = r.IsDBNull(1) ? "" : r.GetString(1); }

                if (userId == 0)
                {
                    TempData["ForgotError"] = "No account found with that email address.";
                    return RedirectToAction(nameof(ForgotPassword));
                }

                var otp    = new Random().Next(100000, 999999).ToString();
                var expiry = DateTime.Now.AddMinutes(10);
                var upd = new MySqlCommand(
                    "UPDATE users SET otp_code=@otp, otp_expiry=@exp WHERE id=@id", conn);
                upd.Parameters.AddWithValue("@otp", otp);
                upd.Parameters.AddWithValue("@exp", expiry);
                upd.Parameters.AddWithValue("@id",  userId);
                upd.ExecuteNonQuery();
                await _email.SendOtpAsync(email.Trim().ToLower(), name, otp);

                TempData["ForgotEmail"]   = email.Trim().ToLower();
                TempData["ForgotOtpSent"] = "true";
            }
            catch (Exception ex)
            {
                TempData["ForgotError"] = $"Failed to send code: {ex.Message}";
            }
            return RedirectToAction(nameof(ForgotPassword));
        }

        // ── Forgot Password — Step 2: verify OTP only ─────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult VerifyForgotOtp(string email, string otp)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id FROM users WHERE email=@e AND otp_code=@otp AND otp_expiry > NOW() LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@e",   email.Trim().ToLower());
                cmd.Parameters.AddWithValue("@otp", otp.Trim());
                var id = cmd.ExecuteScalar();
                if (id == null)
                {
                    TempData["ForgotEmail"]   = email;
                    TempData["ForgotOtpSent"] = "true";
                    TempData["ForgotError"]   = "Incorrect or expired code. Please try again.";
                    return RedirectToAction(nameof(ForgotPassword));
                }
                // OTP valid — generate a short-lived verified token
                var token  = Guid.NewGuid().ToString("N");
                var expiry = DateTime.Now.AddMinutes(15);
                var upd = new MySqlCommand(
                    "UPDATE users SET otp_code=NULL, otp_expiry=NULL, reset_token=@t, reset_expiry=@exp WHERE id=@id", conn);
                upd.Parameters.AddWithValue("@t",   token);
                upd.Parameters.AddWithValue("@exp", expiry);
                upd.Parameters.AddWithValue("@id",  Convert.ToInt32(id));
                upd.ExecuteNonQuery();

                TempData["ForgotVerified"] = "true";
                TempData["ForgotToken"]    = token;
                TempData["ForgotEmail"]    = email;
            }
            catch (Exception ex)
            {
                TempData["ForgotEmail"]   = email;
                TempData["ForgotOtpSent"] = "true";
                TempData["ForgotError"]   = $"Error: {ex.Message}";
            }
            return RedirectToAction(nameof(ForgotPassword));
        }

        // ── Forgot Password — Step 3: set new password ─────────────
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult ResetWithOtp(string token, string password)
        {
            try
            {
                using var conn = DbHelper.GetConnection(_config);
                conn.Open();
                var cmd = new MySqlCommand(
                    "SELECT id FROM users WHERE reset_token=@t AND reset_expiry > NOW() LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@t", token);
                var id = cmd.ExecuteScalar();
                if (id == null)
                {
                    TempData["ForgotError"] = "Session expired. Please start again.";
                    return RedirectToAction(nameof(ForgotPassword));
                }
                var hash = BCrypt.Net.BCrypt.HashPassword(password);
                var upd  = new MySqlCommand(
                    "UPDATE users SET password=@p, reset_token=NULL, reset_expiry=NULL WHERE id=@id", conn);
                upd.Parameters.AddWithValue("@p",  hash);
                upd.Parameters.AddWithValue("@id", Convert.ToInt32(id));
                upd.ExecuteNonQuery();
                TempData["LoginMsg"] = "Password reset successfully. You can now log in.";
                return RedirectToAction(nameof(Login));
            }
            catch (Exception ex)
            {
                TempData["ForgotVerified"] = "true";
                TempData["ForgotToken"]    = token;
                TempData["ForgotError"]    = $"Error: {ex.Message}";
                return RedirectToAction(nameof(ForgotPassword));
            }
        }

        // ── Helper ─────────────────────────────────────────────
        private static string MaskEmail(string email)
        {
            var parts = email.Split('@');
            if (parts.Length != 2) return email;
            var name   = parts[0];
            var domain = parts[1];
            var masked = name.Length <= 2 ? name : name[..2] + new string('*', name.Length - 2);
            return $"{masked}@{domain}";
        }

        private IActionResult RedirectBasedOnRole()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            return role switch
            {
                "Admin"      => RedirectToAction("Dashboard", "Admin"),
                "Instructor" => RedirectToAction("Dashboard", "Instructor"),
                "Student"    => RedirectToAction("Dashboard", "Student"),
                "Staff"      => RedirectToAction("Dashboard", "Staff"),
                _            => RedirectToAction(nameof(Login))
            };
        }
    }

    public class VerifyOtpDto { public string Otp { get; set; } = ""; }
}