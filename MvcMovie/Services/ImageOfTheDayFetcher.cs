using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MVCVideoGuide.Data;
using MVCVideoGuide.Data.Entities;

namespace MvcMovie.Services;

// Erstatter den gamle wwwroot/js/fetchAPI.js.
// Før hentede browseren NASA og Bing og sendte alt data tilbage i en URL (Home/Index3?...),
// hvilket gik i stykker ved & og # i teksten, kun virkede fra forsiden og viste API-nøglen til alle.
// Nu henter serveren selv billederne i baggrunden ved opstart og derefter hver 6. time.
public class ImageOfTheDayFetcher : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private const int DaysBack = 7; // Henter en uges billeder, så huller udfyldes hvis siden ikke har kørt.

    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ImageOfTheDayFetcher> _logger;

    public ImageOfTheDayFetcher(IServiceScopeFactory scopes, IHttpClientFactory http, IConfiguration config, ILogger<ImageOfTheDayFetcher> logger)
    {
        _scopes = scopes;
        _http = http;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RefreshAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VideoDbContext>();

        try { await FetchNasaAsync(db, ct); }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Could not fetch NASA Astronomy Picture of the Day");
        }

        try { await FetchBingAsync(db, ct); }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(e, "Could not fetch Bing Image of the Day");
        }
    }

    private async Task FetchNasaAsync(VideoDbContext db, CancellationToken ct)
    {
        var key = _config["Nasa:ApiKey"] ?? "DEMO_KEY";
        // NASA bruger amerikansk tid, så dagens billede findes måske ikke endnu. Derfor hentes et interval.
        var start = DateTime.UtcNow.AddHours(-5).Date.AddDays(-DaysBack).ToString("yyyy-MM-dd");
        var url = $"https://api.nasa.gov/planetary/apod?api_key={Uri.EscapeDataString(key)}&start_date={start}&thumbs=true";

        var client = _http.CreateClient();
        var items = await client.GetFromJsonAsync<List<ApodResponse>>(url, ct) ?? [];

        var existing = await db.NASAIotd.Select(n => n.Date).ToHashSetAsync(ct);
        int added = 0;
        foreach (var a in items)
        {
            if (string.IsNullOrEmpty(a.Date) || existing.Contains(a.Date)) continue;

            // I efteråret 2026 svarer APOD-API'et med en pladsholder ("NASA Science" + NASA-logoet)
            // i stedet for det rigtige billede. Den slags gemmes ikke.
            if (a.Title == "NASA Science" || (a.Url ?? "").Contains("nasa-logo")) continue;

            bool isVideo = a.MediaType == "video";
            string? thumb = isVideo ? a.ThumbnailUrl : a.Url;
            if (string.IsNullOrEmpty(thumb) || string.IsNullOrEmpty(a.Url)) continue; // Fx interaktive dage uden billede.

            db.NASAIotd.Add(new NASA
            {
                Date = a.Date,
                Title = a.Title ?? "",
                Tooltip = a.Explanation ?? "",
                Copyright = a.Copyright?.Trim().Replace("\n", " "),
                Thumbnail = thumb,
                Hdimg = isVideo ? a.Url : (a.HdUrl ?? a.Url),
                IsVideo = isVideo,
            });
            existing.Add(a.Date);
            added++;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("NASA: {Added} new of {Total} fetched", added, items.Count);
    }

    private async Task FetchBingAsync(VideoDbContext db, CancellationToken ct)
    {
        // Den fulde fil (en.json) er 5 MB med alle billeder siden 2010 og blev før hentet af hver ny besøgende.
        // Årsfilen er under 0,5 MB. I starten af januar tages sidste år med, så ugen bliver fuld.
        var client = _http.CreateClient();
        var now = DateTime.UtcNow;
        var items = new List<BingResponse>();
        if (now.DayOfYear <= DaysBack)
            items.AddRange(await client.GetFromJsonAsync<List<BingResponse>>($"https://bing.npanuhin.me/US/en.{now.Year - 1}.json", ct) ?? []);
        items.AddRange(await client.GetFromJsonAsync<List<BingResponse>>($"https://bing.npanuhin.me/US/en.{now.Year}.json", ct) ?? []);

        var latest = items.Where(b => !string.IsNullOrEmpty(b.Date)).OrderBy(b => b.Date).TakeLast(DaysBack);
        var existing = await db.BingIotd.Select(b => b.Date).ToHashSetAsync(ct);
        int added = 0;
        foreach (var b in latest)
        {
            var img = b.BingUrl ?? b.Url;
            if (existing.Contains(b.Date!) || string.IsNullOrEmpty(img)) continue;
            db.BingIotd.Add(new Bing
            {
                Date = b.Date!,
                Title = b.Title ?? "",
                Url = img,
                Copyright = b.Copyright,
            });
            added++;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Bing: {Added} new", added);
    }

    private record ApodResponse(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("explanation")] string? Explanation,
        [property: JsonPropertyName("copyright")] string? Copyright,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("hdurl")] string? HdUrl,
        [property: JsonPropertyName("media_type")] string? MediaType,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl);

    private record BingResponse(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("copyright")] string? Copyright,
        [property: JsonPropertyName("bing_url")] string? BingUrl,
        [property: JsonPropertyName("url")] string? Url);
}
