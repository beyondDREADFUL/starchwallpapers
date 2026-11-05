using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MvcMovie.Models;
using MVCVideoGuide.Data;
using MVCVideoGuide.Data.Entities;

namespace MvcMovie.Controllers;

public class HomeController : Controller
{
    // Kontrollerer projektets views/undersider. Heriblandt også hentning og gemning af data til og fra databasen.
    // Index2/Index3/Index4 (GET-links der skrev til databasen) er fjernet. Hentningen sker nu i Services/ImageOfTheDayFetcher.cs.
    private readonly ILogger<HomeController> _logger;
    private readonly VideoDbContext _context;
    private readonly IConfiguration _config;

    public HomeController(ILogger<HomeController> logger, VideoDbContext context, IConfiguration config)
    {
        _logger = logger;
        _context = context;
        _config = config;
    }

    public IActionResult Index() // Returnerer blot View. Dvs. blot indholdet i den specifikke ViewComponent. Dette View'et specificeres af metodens navn.
    {
        return View();
    }

    public IActionResult NASAIotd() // Henter og viser NASA billeder i rækkefølgen nyeste først.
    {
        List<NASA> result = _context.NASAIotd.OrderByDescending(p => p.Date).ToList();
        return View(result);
    }

    public IActionResult BingIotd() // Henter og viser Bing billeder i rækkefølgen nyeste først.
    {
        List<Bing> result = _context.BingIotd.OrderByDescending(p => p.Date).ToList();
        return View(result);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult ShowProducts() // Henter og viser brugernes wallpapers i rækkefølgen nyeste først.
    {
        List<Product> result = _context.Products.OrderByDescending(p => p.Id).ToList();
        return View(result);
    }

    public IActionResult About()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")] // Se Program.cs: højst 5 forsøg i minuttet.
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        // Login tjekkes nu på serveren. Brugernavn og kodeord står i appsettings.Development.json (kun til udvikling).
        var expectedUser = _config["Admin:Username"];
        var expectedPassword = _config["Admin:Password"];

        if (string.IsNullOrEmpty(expectedUser) || string.IsNullOrEmpty(expectedPassword)
            || !SecureEquals(username, expectedUser) || !SecureEquals(password, expectedPassword))
        {
            ViewData["ReturnUrl"] = returnUrl;
            ViewData["Error"] = "Wrong username or password.";
            return View();
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("AddProduct");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index");
    }

    [HttpGet]
    [Authorize]
    public IActionResult AddProduct()
    {
        return View(new Product());
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public IActionResult AddProduct([Bind("Item,Cost,Img,Tag")] Product product) // Id kan ikke sendes med (overposting).
    {
        // "29,95" ville ellers blive læst som 2995, fordi komma er tusindtalsseparator med punktum som decimaltegn.
        if (Request.Form["Cost"].ToString().Contains(','))
        {
            ModelState.AddModelError(nameof(Product.Cost), "Use a dot for decimals, e.g. 29.95.");
        }

        // Billeder vises direkte fra brugerens link, og browsere blokerer http-billeder på en https-side.
        if (!string.IsNullOrEmpty(product.Img) && !product.Img.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Product.Img), "Use an https:// link.");
        }

        if (!ModelState.IsValid) // Før blev et tomt felt gemt som null og fik databasen til at crashe.
        {
            return View(product);
        }

        product.Tag = product.Tag.Trim().TrimStart('#').Trim().ToLowerInvariant(); // "#Nature " -> "nature"
        product.Item = product.Item.Trim();

        _context.Products.Add(product); // Tilføjer objekt til tabel.
        _context.SaveChanges(); // Gemmer ændringer til databasen.
        return RedirectToAction("ShowProducts"); // Redirecter til browse efter tilføjelse af baggrund til databasen.
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private static bool SecureEquals(string? a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b));
}
