using ExpenseTracker.Database;
using ExpenseTracker.Models;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

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

// (Optional) If you have a MongoDBService class that depends on FinanceContext
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

app.Run();
