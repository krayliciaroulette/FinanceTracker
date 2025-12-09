using ExpenseTracker.Database;
using ExpenseTracker.Models;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System;

var builder = WebApplication.CreateBuilder(args);

// ✅ Ensure configuration reads from appsettings.json
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Add MVC support
builder.Services.AddControllersWithViews();

// Add session support
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ✅ Register FinanceContext properly and pass configuration
builder.Services.AddSingleton<FinanceContext>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new FinanceContext(configuration);
});

// Register MongoDbService if used
builder.Services.AddSingleton<MongoDbService>();

var app = builder.Build();

// ✅ Startup background task (optional cleanup logic)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<FinanceContext>();

    var timer = new System.Timers.Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
    timer.Elapsed += (sender, e) =>
    {
        var now = DateTime.Now;
        if (now.Hour == 0 && now.Minute == 0)
        {
            var toDelete = context.Expenses.Find(x => x.Date < DateTime.Today).ToList();
            if (toDelete.Any())
            {
                context.Expenses.DeleteMany(x => x.Date < DateTime.Today);
            }
        }
    };
    timer.Start();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// ✅ Bind to Render's PORT environment variable
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
app.Urls.Add($"http://0.0.0.0:{port}");

app.Run();
