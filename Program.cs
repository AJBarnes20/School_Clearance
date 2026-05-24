using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))
    ));

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Home}/{action=Login}/{id?}");

app.Run();
