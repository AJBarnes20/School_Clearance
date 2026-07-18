using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using OnlineClearanceSystem.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.MaxDepth = 64);

builder.Services.AddSingleton<OnlineClearanceSystem.Data.EmailService>();

builder.WebHost.ConfigureKestrel(k =>
    k.Limits.MaxRequestBodySize = 10 * 1024 * 1024);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/Home/Login";
        options.LogoutPath       = "/Home/Logout";
        options.AccessDeniedPath = "/Home/Login";
        options.ExpireTimeSpan   = TimeSpan.FromHours(8);
    });

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

var connectionString = DbHelper.GetConnectionString(builder.Configuration);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Ensure clearance_messages.is_read column exists and has no NULLs
try
{
    using var conn = new MySqlConnection(connectionString);
    conn.Open();
    // Add column if missing (IF NOT EXISTS requires MySQL 8.0; use safe IGNORE pattern)
    new MySqlCommand(@"
        ALTER TABLE clearance_messages
        ADD COLUMN IF NOT EXISTS is_read TINYINT(1) NOT NULL DEFAULT 0", conn).ExecuteNonQuery();
    // Fix any pre-existing NULL values
    new MySqlCommand(
        "UPDATE clearance_messages SET is_read = 0 WHERE is_read IS NULL", conn).ExecuteNonQuery();
}
catch (Exception ex) { Console.Error.WriteLine($"[startup] clearance_messages migration: {ex.Message}"); }

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// The app port is exposed only to the private Compose network; Nginx is the
// sole ingress and may have a dynamically assigned bridge address.
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

if (builder.Configuration.GetValue("App:UseHttpsRedirection", false))
    app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();
