using System.Globalization;
using System.Text.Json;
using MVCVideoGuide.Data.Entities;
using System.Threading.RateLimiting;
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
        options.Cookie.HttpOnly = true; // JavaScript kan ikke læse login-cookien.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Kun over HTTPS i produktion.
    });

// Højst 5 login-forsøg pr. minut pr. IP-adresse, så kodeordet ikke kan gættes ved at prøve tusindvis.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, ct) =>
        new ValueTask(context.HttpContext.Response.WriteAsync("Too many login attempts. Wait a minute and try again.", ct));
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// Sikkerheds-headers på alle svar.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY"; // Siden må ikke vises i en iframe (clickjacking).
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Scripts må kun komme fra siden selv. Billeder må komme fra alle HTTPS-adresser (Bing, NASA og brugernes links).
    headers.ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

// Databasen (en SQLite-fil) oprettes ét sted ved opstart i stedet for i hver controller-metode.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<VideoDbContext>();
    db.Database.EnsureCreated();

    // En tom database får nogle startbilleder til Browse (dit eget billede + public domain fra NASA).
    var seedFile = Path.Combine(AppContext.BaseDirectory, "Data", "seed-wallpapers.json");
    if (!db.Products.Any() && File.Exists(seedFile))
    {
        var seed = JsonSerializer.Deserialize<List<Product>>(File.ReadAllText(seedFile)) ?? [];
        db.Products.AddRange(seed.Select(p => { p.Id = 0; return p; }));
        db.SaveChanges();
    }
}

// Når siden ligger i en undermappe (fx /starchwallpapers/ på GitHub Pages), skal alle links starte med den.
var pathBase = app.Configuration["PathBase"];
if (!string.IsNullOrEmpty(pathBase))
{
    app.UsePathBase(pathBase);
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
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
