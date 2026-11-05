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
            string? full = isVideo ? a.Url : (a.HdUrl ?? a.Url);
            // Kun rigtige https-adresser gemmes, da de ender i <a href> og <img src>. Springer fx interaktive dage uden billede over.
            if (!IsHttpsUrl(thumb) || !IsHttpsUrl(full)) continue;

            db.NASAIotd.Add(new NASA
            {
                Date = a.Date,
                Title = a.Title ?? "",
                Tooltip = a.Explanation ?? "",
                Copyright = a.Copyright?.Trim().Replace("\n", " "),
                Thumbnail = thumb!,
                Hdimg = full!,
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
        // Før brugtes bing.npanuhin.me, fordi Bings eget endpoint blokerer JavaScript i browseren (CORS).
        // Nu henter serveren selv, og CORS gælder kun for browsere, så Bing kan kaldes direkte.
        // Endpointet er udokumenteret, men har været uændret i over ti år. Det giver højst de seneste 8 dage.
        var client = _http.CreateClient();
        var archive = await client.GetFromJsonAsync<BingArchive>(
            $"https://www.bing.com/HPImageArchive.aspx?format=js&idx=0&n={DaysBack + 1}&mkt=en-US", ct);

        var existing = await db.BingIotd.Select(b => b.Date).ToHashSetAsync(ct);
        int added = 0;
        foreach (var b in archive?.Images ?? [])
        {
            // startdate er "20261105". Gemmes som "2026-11-05" ligesom NASA og de gamle rækker.
            if (b.StartDate is not { Length: 8 } d || !d.All(char.IsAsciiDigit)) continue;
            var date = $"{d[..4]}-{d[4..6]}-{d[6..]}";
            if (existing.Contains(date)) continue;

            // urlbase skal være en sti på bing.com ("/th?id=..."), ikke en hel adresse et andet sted hen.
            var url = $"https://www.bing.com{b.UrlBase}_UHD.jpg";
            if (b.UrlBase is null || !b.UrlBase.StartsWith("/th?id=", StringComparison.Ordinal)
                || !IsHttpsUrl(url) || new Uri(url).Host != "www.bing.com") continue;

            // copyright er "Sted, Land (© Fotograf/Bureau)". Stedet bliver titlen og resten copyright.
            var text = b.Copyright ?? "";
            var split = text.LastIndexOf(" (©", StringComparison.Ordinal);
            var title = split > 0 ? text[..split] : (b.Title ?? text);
            var copyright = split > 0 ? text[(split + 2)..].TrimEnd(')') : null;

            db.BingIotd.Add(new Bing
            {
                Date = date,
                Title = title,
                Url = url, // Fuld opløsning. Siden viser en 1280px udgave.
                Copyright = copyright,
            });
            existing.Add(date);
            added++;
        }
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Bing: {Added} new", added);
    }

    private static bool IsHttpsUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && url!.Length <= 2048;

    private record ApodResponse(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("explanation")] string? Explanation,
        [property: JsonPropertyName("copyright")] string? Copyright,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("hdurl")] string? HdUrl,
        [property: JsonPropertyName("media_type")] string? MediaType,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl);

    private record BingArchive(
        [property: JsonPropertyName("images")] List<BingImage>? Images);

    private record BingImage(
        [property: JsonPropertyName("startdate")] string? StartDate,
        [property: JsonPropertyName("urlbase")] string? UrlBase,
        [property: JsonPropertyName("copyright")] string? Copyright,
        [property: JsonPropertyName("title")] string? Title);
}
