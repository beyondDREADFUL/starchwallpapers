using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using MvcMovie.Services;
using MVCVideoGuide.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<VideoDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("VideoDbConnection")));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ImageOfTheDayFetcher>(); // Henter NASA og Bing i baggrunden.

// Rigtigt login med cookie i stedet for at skjule en div med JavaScript.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Home/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
    });

var app = builder.Build();

// Databasen (en SQLite-fil) oprettes ét sted ved opstart i stedet for i hver controller-metode.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<VideoDbContext>().Database.EnsureCreated();
}

// Pris-feltet bruger punktum som decimaltegn uanset serverens sprog (29.95, ikke 29,95).
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = [CultureInfo.InvariantCulture],
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
