using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using HtmlAgilityPack;

using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;
using KamiYomu.CrawlerAgents.Core.Inputs;

using Microsoft.Extensions.Logging;

using PuppeteerSharp;

using Page = KamiYomu.CrawlerAgents.Core.Catalog.Page;

namespace KamiYomu.CrawlerAgents.AsuraComic;

[DisplayName("KamiYomu Crawler Agent – asuracomic.net")]
[CrawlerSelect("Mirror", "AsuraComic offers multiple mirror sites that may be online and useful.",
    true, 0, [
        "https://asuracomic.net",
    ])]
public class AsuraComicCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent
{
    private readonly Uri _baseUri;
    private readonly Lazy<Task<IBrowser>> _browser;
    private bool _disposed = false;
    private readonly string _timezone;

    public AsuraComicCrawlerAgent(IDictionary<string, object> options) : base(options)
    {
        _browser = new Lazy<Task<IBrowser>>(CreateBrowserAsync, true);
        _timezone = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Eastern Standard Time" : "America/Toronto";
        string mirrorUrl = Options.TryGetValue("Mirror", out object? mirror) && mirror is string mirrorValue ? mirrorValue : "https://asuracomic.net";
        _baseUri = new Uri(mirrorUrl);
    }

    protected virtual async Task DisposeAsync(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                if (_browser.IsValueCreated)
                {
                    await _browser.Value.Result.CloseAsync();
                    _browser.Value.Result.Dispose();
                }
            }

            _disposed = true;
        }
    }

    ~AsuraComicCrawlerAgent()
    {
        _ = DisposeAsync(disposing: false);
    }

    // <inheritdoc/>
    public void Dispose()
    {
        _ = DisposeAsync(disposing: true);
        GC.SuppressFinalize(this);
    }

    public Task<IBrowser> GetBrowserAsync()
    {
        return _browser.Value;
    }

    private async Task<IBrowser> CreateBrowserAsync()
    {
        LaunchOptions launchOptions = new()
        {
            Headless = true,
            Timeout = TimeoutMilliseconds,
            Args = [
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-dev-shm-usage"
            ]
        };

        return await Puppeteer.LaunchAsync(launchOptions);
    }

    private async Task PreparePageForNavigationAsync(IPage page)
    {
        page.Console += (sender, e) =>
        {
            // e.Message contains the console message
            Logger?.LogDebug($"[Browser Console] {e.Message.Type}: {e.Message.Text}");

            // You can also inspect arguments
            if (e.Message.Args != null)
            {
                foreach (IJSHandle? arg in e.Message.Args)
                {
                    Logger?.LogDebug($"   Arg: {arg.RemoteObject.Value}");
                }
            }
        };



        _ = await page.EvaluateExpressionOnNewDocumentAsync(@"
            // Neutralize devtools detection
            const originalLog = console.log;
            console.log = function(...args) {
                if (args.length === 1 && args[0] === '[object HTMLDivElement]') {
                    return; // skip detection trick
                }
                return originalLog.apply(console, args);
            };

            // Override reload to do nothing
            window.location.reload = () => console.log('Reload prevented');
        ");

        await page.EmulateTimezoneAsync(_timezone);

        DateTime fixedDate = DateTime.Now;

        string fixedDateIso = fixedDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        _ = await page.EvaluateExpressionOnNewDocumentAsync($@"
            // Freeze time to a specific date
            const fixedDate = new Date('{fixedDateIso}');
            Date = class extends Date {{
                constructor(...args) {{
                    if (args.length === 0) {{
                        return fixedDate;
                    }}
                    return super(...args);
                }}
                static now() {{
                    return fixedDate.getTime();
                }}
            }};
        ");

    }

    private string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!url.StartsWith("/") && Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute))
        {
            return absolute.ToString();
        }

        Uri resolved = new(_baseUri, url);
        return resolved.ToString();
    }


    private static bool IsGenreNotFamilySafe(string p)
    {
        return !string.IsNullOrWhiteSpace(p) && (p.Contains("adult", StringComparison.OrdinalIgnoreCase)
            || p.Contains("harem", StringComparison.OrdinalIgnoreCase)
            || p.Contains("hentai", StringComparison.OrdinalIgnoreCase)
            || p.Contains("ecchi", StringComparison.OrdinalIgnoreCase)
            || p.Contains("violence", StringComparison.OrdinalIgnoreCase)
            || p.Contains("smut", StringComparison.OrdinalIgnoreCase)
            || p.Contains("shota", StringComparison.OrdinalIgnoreCase)
            || p.Contains("sexual", StringComparison.OrdinalIgnoreCase));
    }


    public Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Uri("https://asuracomic.net/images/logo.webp"));
    }

    public async Task<PagedResult<Manga>> SearchAsync(string titleName, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        IBrowser browser = await GetBrowserAsync();
        using IPage page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);
        int pageNumber = string.IsNullOrWhiteSpace(paginationOptions?.ContinuationToken)
                        ? 1
                        : int.Parse(paginationOptions.ContinuationToken);

        Uri targetUri = new(new Uri(_baseUri.ToString()), $"series?page={pageNumber}&name={titleName}");
        _ = await page.GoToAsync(targetUri.ToString(), new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (CookieParam? cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        string content = await page.GetContentAsync();

        HtmlDocument document = new();
        document.LoadHtml(content);

        List<Manga> mangas = [];
        if (pageNumber > 0)
        {
            HtmlNodeCollection? nodes = document.DocumentNode.SelectNodes("//a[starts-with(@href, 'series/')]");
            if (nodes != null)
            {
                foreach (HtmlNode divNode in nodes)
                {
                    Manga manga = ConvertToMangaFromList(divNode);
                    mangas.Add(manga);
                }
            }
        }

        return PagedResultBuilder<Manga>.Create()
            .WithData(mangas)
            .WithPaginationOptions(new PaginationOptions((pageNumber + 1).ToString()))
            .Build();
    }

    private Manga ConvertToMangaFromList(HtmlNode divNode)
    {
        // The outer <a> contains the href for the series
        HtmlNode anchorNode = divNode.SelectSingleNode("./ancestor-or-self::a");
        string websiteUrl = anchorNode?.GetAttributeValue("href", string.Empty) ?? string.Empty;

        // Cover image
        HtmlNode imgNode = divNode.SelectSingleNode(".//img");
        string coverUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        string coverFileName = Path.GetFileName(new Uri(coverUrl).AbsolutePath);

        // Title
        HtmlNode titleNode = divNode.SelectSingleNode(".//span[contains(@class,'block') and contains(@class,'font-bold')]");
        string title = titleNode?.InnerText.Trim() ?? "Unknown Title";

        // Chapter
        HtmlNode chapterNode = divNode.SelectSingleNode(".//span[contains(text(),'Chapter')]");
        string? chapterText = chapterNode?.InnerText.Replace("Chapter", "").Trim();
        string chapter = chapterText ?? "0";

        // Volume (not present in snippet, default to 0)
        string volume = "0";

        // Genres (e.g., MANHWA tag)
        HtmlNode genreNode = divNode.SelectSingleNode(".//div[contains(@class,'bg-[#a12e24]')]/span");
        List<string> genres = [];
        if (genreNode != null)
        {
            genres.Add(genreNode.InnerText.Trim());
        }

        // Alternative titles (none in snippet, leave empty)
        List<string> altTitles = [];

        // Author (not present in snippet, placeholder)
        string author = "Unknown";

        // Id (derive from href slug)
        string id = Path.GetFileName(websiteUrl);


        string _language = "en";

        Manga manga = MangaBuilder.Create()
            .WithId(id)
            .WithTitle(title)
            .WithAuthors([author])
            .WithDescription("No Description Available")
            .WithCoverUrl(new Uri(coverUrl))
            .WithCoverFileName(coverFileName)
            .WithWebsiteUrl(websiteUrl)
            .WithAlternativeTitles(
                altTitles.Select((p, i) => new { i = i.ToString(), p })
                         .ToDictionary(x => x.i, x => x.p))
            .WithLatestChapterAvailable(decimal.TryParse(chapter, out decimal chapterResult) ? chapterResult : 0)
            .WithLastVolumeAvailable(decimal.TryParse(volume, out decimal volumeResult) ? volumeResult : 0)
            .WithTags([.. genres])
            .WithOriginalLanguage(_language)
            .WithIsFamilySafe(!genres.Any(IsGenreNotFamilySafe))
            .Build();

        return manga;
    }

    public async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        IBrowser browser = await GetBrowserAsync();
        using IPage page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        string finalUrl = new Uri(_baseUri, $"series/{id}").ToString();
        IResponse response = await page.GoToAsync(finalUrl, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (CookieParam? cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        string content = await page.GetContentAsync();
        HtmlDocument document = new();
        document.LoadHtml(content);
        HtmlNode rootNode = document.DocumentNode.SelectSingleNode("//body");
        Manga manga = ConvertToMangaFromSingleBook(rootNode, id);

        return manga;
    }

    private Manga ConvertToMangaFromSingleBook(HtmlNode rootNode, string id)
    {
        // --- Cover image ---
        HtmlNode imgNode = rootNode.SelectSingleNode(".//img[contains(@class,'rounded')]");
        string coverUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        string coverFileName = Path.GetFileName(new Uri(coverUrl).AbsolutePath);

        // --- Title ---
        HtmlNode titleNode = rootNode.SelectSingleNode(".//div[contains(@class,'col-span-12') and contains(@class,'sm:col-span-9')]//span[contains(@class,'text-xl') and contains(@class,'font-bold')]");
        string title = titleNode?.InnerText.Trim() ?? "Unknown Title";

        // --- Synopsis / Description ---
        HtmlNode synopsisNode = rootNode.SelectSingleNode(".//h3[contains(@class,'text-[#D9D9D9]') and contains(text(),'Synopsis')]");
        HtmlNode? descriptionNode = synopsisNode?.NextSibling;
        string description = descriptionNode?.InnerText.Trim() ?? "No Description Available";

        // --- Authors ---
        List<string> authors = [];
        HtmlNode authorNode = rootNode.SelectSingleNode(".//h3[contains(@class,'text-[#A2A2A2]') and contains(text(),'VOKE')]");
        if (authorNode != null)
        {
            authors.AddRange(authorNode.InnerText.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(a => a.Trim()));
        }

        // --- Genres ---
        HtmlNodeCollection genreNodes = rootNode.SelectNodes(".//button[contains(@class,'bg-[#343434]')]");
        List<string> genres = genreNodes?.Select(b => b.InnerText.Trim()).ToList() ?? [];

        // --- Release Status ---
        HtmlNode statusNode = rootNode.SelectSingleNode(".//div[contains(@class,'bg-[#343434]')]/h3[contains(@class,'capitalize')]");
        string releaseStatus = statusNode?.InnerText.Trim() ?? "Ongoing";

        // --- Alternative Titles (none in snippet, leave empty) ---
        List<string> altTitles = [];

        string href = $"series/{id}";

        Manga manga = MangaBuilder.Create()
            .WithId(id)
            .WithTitle(title)
            .WithAlternativeTitles(
                altTitles.Select((p, i) => new { i = i.ToString(), p })
                         .ToDictionary(x => x.i, x => x.p))
            .WithDescription(description)
            .WithAuthors([.. authors])
            .WithTags([.. genres])
            .WithCoverUrl(new Uri(coverUrl))
            .WithCoverFileName(coverFileName)
            .WithWebsiteUrl(NormalizeUrl(href))
            .WithIsFamilySafe(!genres.Any(IsGenreNotFamilySafe))
            .WithReleaseStatus(releaseStatus.ToLowerInvariant() switch
            {
                "completed" => ReleaseStatus.Completed,
                "hiatus" => ReleaseStatus.OnHiatus,
                "cancelled" => ReleaseStatus.Cancelled,
                _ => ReleaseStatus.Continuing,
            })
            .Build();

        return manga;
    }

    public async Task<PagedResult<Chapter>> GetChaptersAsync(Manga manga, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        IBrowser browser = await GetBrowserAsync();
        using IPage page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        string finalUrl = new Uri(_baseUri, $"series/{manga.Id}").ToString();
        IResponse response = await page.GoToAsync(finalUrl, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (CookieParam? cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        string content = await page.GetContentAsync();

        HtmlDocument document = new();
        document.LoadHtml(content);
        HtmlNodeCollection? nodes = document.DocumentNode.SelectNodes("//a[contains(@href, 'chapter/')]");
        IEnumerable<Chapter> chapters = ConvertChaptersFromSingleBook(manga, nodes);

        return PagedResultBuilder<Chapter>.Create()
                                          .WithPaginationOptions(new PaginationOptions(chapters.Count(), chapters.Count(), chapters.Count()))
                                          .WithData(chapters)
                                          .Build();
    }

    private IEnumerable<Chapter> ConvertChaptersFromSingleBook(Manga manga, HtmlNodeCollection nodes)
    {
        List<Chapter> chapters = [];

        foreach (HtmlNode chapterDiv in nodes)
        {
            HtmlNode skipNode = chapterDiv.SelectSingleNode(".//div[contains(@class,'bg-themecolor')]");
            if (skipNode != null)
            {
                continue;
            }

            // --- Anchor node with href ---
            HtmlNode anchorNode = chapterDiv.SelectSingleNode("./self::a");
            string href = anchorNode?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            string uri = NormalizeUrl($"series/{href}");

            // --- Chapter Id (derive from href) ---
            string chapterId = href;

            // --- Title and Number ---
            HtmlNode titleNode = chapterDiv.SelectSingleNode(".//h3[contains(text(),'Chapter')]");
            string titleText = titleNode?.InnerText.Trim() ?? "Unknown Chapter";

            // Extract number from "Chapter 124"
            decimal number = 0m;
            string title = titleText;
            Match match = Regex.Match(titleText, @"Chapter\s+(\d+)");
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out decimal parsedNumber))
            {
                number = parsedNumber;
                title = $"Chapter {parsedNumber}";
            }

            // --- Volume (not present in snippet, default to 0) ---
            decimal volume = 0m;

            // --- Build Chapter object ---
            Chapter chapter = ChapterBuilder.Create()
                .WithId(chapterId)
                .WithTitle(title)
                .WithParentManga(manga)
                .WithVolume(volume > 0 ? volume : 0)
                .WithNumber(number > 0 ? number : 0)
                .WithUri(new Uri(NormalizeUrl(uri)))
                .WithTranslatedLanguage("en")
                .Build();

            chapters.Add(chapter);
        }

        return chapters;
    }

    public async Task<IEnumerable<Page>> GetChapterPagesAsync(Chapter chapter, CancellationToken cancellationToken)
    {
        IBrowser browser = await GetBrowserAsync();
        using IPage page = await browser.NewPageAsync();

        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        _ = await page.GoToAsync(chapter.Uri.ToString(), new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        _ = await page.EvaluateFunctionAsync(@"async () => {
            await new Promise(resolve => {
                let totalHeight = 0;
                const distance = 500;
                const timer = setInterval(() => {
                    window.scrollBy(0, distance);
                    totalHeight += distance;

                    if (totalHeight >= document.body.scrollHeight) {
                        clearInterval(timer);
                        resolve();
                    }
                }, 200);
            });
        }");

        await Task.Delay(1000, cancellationToken);

        foreach (CookieParam? cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        string content = await page.GetContentAsync();
        HtmlDocument document = new();
        document.LoadHtml(content);

        HtmlNodeCollection pageNodes = document.DocumentNode.SelectNodes("//img[contains(@src,'storage/media/')]");
        return ConvertToChapterPages(chapter, pageNodes);
    }

    private IEnumerable<Page> ConvertToChapterPages(Chapter chapter, HtmlNodeCollection pageNodes)
    {
        if (pageNodes == null)
        {
            return [];
        }

        List<Page> pages = [];
        int index = 1;

        foreach (HtmlNode node in pageNodes)
        {
            // --- Image URL ---
            string imageUrl = node.GetAttributeValue("src", string.Empty);

            // --- Alt text (e.g., "chapter page 6") ---
            string altText = node.GetAttributeValue("alt", string.Empty);

            // --- Page number ---
            int pageNumber = 0;
            Match match = Regex.Match(altText, @"page\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parsedNumber))
            {
                pageNumber = parsedNumber;
            }
            else
            {
                // fallback: use loop index if alt not present
                pageNumber = index;
            }

            // --- Page Id (unique per image) ---
            string idAttr = $"{chapter.Id}-page-{pageNumber}";

            // --- Build Page object ---
            Page page = PageBuilder.Create()
                .WithChapterId(chapter.Id)
                .WithId(idAttr)
                .WithPageNumber(pageNumber > 0 ? pageNumber : 0)
                .WithImageUrl(new Uri(imageUrl))
                .WithParentChapter(chapter)
                .Build();

            pages.Add(page);
            index++;
        }

        return pages;
    }
}
