using HtmlAgilityPack;
using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using KamiYomu.CrawlerAgents.Core.Catalog.Builders;
using KamiYomu.CrawlerAgents.Core.Catalog.Definitions;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;
using Page = KamiYomu.CrawlerAgents.Core.Catalog.Page;

namespace KamiYomu.CrawlerAgents.AsuraComic;

[DisplayName("KamiYomu Crawler Agent – asuracomic.net")]
public class AsuraComicCrawlerAgent : AbstractCrawlerAgent, ICrawlerAgent, IDisposable
{
    private readonly Uri _baseUri;
    private Lazy<Task<IBrowser>> _browser;
    private bool _disposed = false;
    private string _timezone;

    public AsuraComicCrawlerAgent(IDictionary<string, object> options) : base(options)
    {
        _baseUri = new Uri("https://asuracomic.net");
        _browser = new Lazy<Task<IBrowser>>(CreateBrowserAsync, true);
        _timezone = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Eastern Standard Time" : "America/Toronto";
    }

    public Task<IBrowser> GetBrowserAsync() => _browser.Value;

    private async Task<IBrowser> CreateBrowserAsync()
    {
        var launchOptions = new LaunchOptions
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
                foreach (var arg in e.Message.Args)
                {
                    Logger?.LogDebug($"   Arg: {arg.RemoteObject.Value}");
                }
            }
        };



        await page.EvaluateExpressionOnNewDocumentAsync(@"
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

        var fixedDate = DateTime.Now;

        var fixedDateIso = fixedDate.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await page.EvaluateExpressionOnNewDocumentAsync($@"
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
            return string.Empty;

        if (!url.StartsWith("/") && Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        var resolved = new Uri(_baseUri, url);
        return resolved.ToString();
    }


    private static bool IsGenreNotFamilySafe(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return false;
        return p.Contains("adult", StringComparison.OrdinalIgnoreCase)
            || p.Contains("harem", StringComparison.OrdinalIgnoreCase)
            || p.Contains("hentai", StringComparison.OrdinalIgnoreCase)
            || p.Contains("ecchi", StringComparison.OrdinalIgnoreCase)
            || p.Contains("violence", StringComparison.OrdinalIgnoreCase)
            || p.Contains("smut", StringComparison.OrdinalIgnoreCase)
            || p.Contains("shota", StringComparison.OrdinalIgnoreCase)
            || p.Contains("sexual", StringComparison.OrdinalIgnoreCase);
    }


    public Task<Uri> GetFaviconAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new Uri("https://asuracomic.net/images/logo.webp"));
    }

    public async Task<PagedResult<Manga>> SearchAsync(string titleName, PaginationOptions paginationOptions, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync();
        using var page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);
        var pageNumber = string.IsNullOrWhiteSpace(paginationOptions?.ContinuationToken)
                        ? 1
                        : int.Parse(paginationOptions.ContinuationToken);

        var targetUri = new Uri(new Uri(_baseUri.ToString()), $"series?page={pageNumber}&name={titleName}");
        await page.GoToAsync(targetUri.ToString(), new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (var cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        var content = await page.GetContentAsync();

        var document = new HtmlDocument();
        document.LoadHtml(content);

        List<Manga> mangas = [];
        if (pageNumber > 0)
        {
            HtmlNodeCollection? nodes = document.DocumentNode.SelectNodes("//a[starts-with(@href, 'series/')]"); 
            if (nodes != null)
            {
                foreach (var divNode in nodes)
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
        var anchorNode = divNode.SelectSingleNode("./ancestor-or-self::a");
        var websiteUrl = anchorNode?.GetAttributeValue("href", string.Empty) ?? string.Empty;

        // Cover image
        var imgNode = divNode.SelectSingleNode(".//img");
        var coverUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        var coverFileName = Path.GetFileName(new Uri(coverUrl).AbsolutePath);

        // Title
        var titleNode = divNode.SelectSingleNode(".//span[contains(@class,'font-bold')]");
        var title = titleNode?.InnerText.Trim() ?? "Unknown Title";

        // Chapter
        var chapterNode = divNode.SelectSingleNode(".//span[contains(text(),'Chapter')]");
        var chapterText = chapterNode?.InnerText.Replace("Chapter", "").Trim();
        var chapter = chapterText ?? "0";

        // Volume (not present in snippet, default to 0)
        var volume = "0";

        // Genres (e.g., MANHWA tag)
        var genreNode = divNode.SelectSingleNode(".//div[contains(@class,'bg-[#a12e24]')]/span");
        var genres = new List<string>();
        if (genreNode != null)
            genres.Add(genreNode.InnerText.Trim());

        // Alternative titles (none in snippet, leave empty)
        var altTitles = new List<string>();

        // Author (not present in snippet, placeholder)
        var author = "Unknown";

        // Id (derive from href slug)
        var id = Path.GetFileName(websiteUrl);

        // Language (guess from genre, e.g., MANHWA → Korean)
        var _language = genres.Contains("MANHWA") ? "Korean" : "Unknown";

        var manga = MangaBuilder.Create()
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
            .WithLatestChapterAvailable(decimal.TryParse(chapter, out var chapterResult) ? chapterResult : 0)
            .WithLastVolumeAvailable(decimal.TryParse(volume, out var volumeResult) ? volumeResult : 0)
            .WithTags([.. genres])
            .WithOriginalLanguage(_language)
            .WithIsFamilySafe(!genres.Any(IsGenreNotFamilySafe))
            .Build();

        return manga;
    }

    public async Task<Manga> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync();
        using var page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        var finalUrl = new Uri(_baseUri, $"series/{id}").ToString();
        var response = await page.GoToAsync(finalUrl, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (var cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        var content = await page.GetContentAsync();
        var document = new HtmlDocument();
        document.LoadHtml(content);
        var rootNode = document.DocumentNode.SelectSingleNode("//body");
        Manga manga = ConvertToMangaFromSingleBook(rootNode, id);

        return manga;
    }

    private Manga ConvertToMangaFromSingleBook(HtmlNode rootNode, string id)
    {
        // --- Cover image ---
        var imgNode = rootNode.SelectSingleNode(".//img[contains(@class,'rounded')]");
        var coverUrl = imgNode?.GetAttributeValue("src", string.Empty) ?? string.Empty;
        var coverFileName = Path.GetFileName(new Uri(coverUrl).AbsolutePath);

        // --- Title ---
        var titleNode = rootNode.SelectSingleNode(".//span[contains(@class,'text-xl') and contains(@class,'font-bold')]");
        var title = titleNode?.InnerText.Trim() ?? "Unknown Title";

        // --- Synopsis / Description ---
        var synopsisNode = rootNode.SelectSingleNode(".//h3[contains(@class,'text-[#D9D9D9]') and contains(text(),'Synopsis')]");
        var descriptionNode = synopsisNode?.NextSibling;
        var description = descriptionNode?.InnerText.Trim() ?? "No Description Available";

        // --- Authors ---
        var authors = new List<string>();
        var authorNode = rootNode.SelectSingleNode(".//h3[contains(@class,'text-[#A2A2A2]') and contains(text(),'VOKE')]");
        if (authorNode != null)
        {
            authors.AddRange(authorNode.InnerText.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(a => a.Trim()));
        }

        // --- Genres ---
        var genreNodes = rootNode.SelectNodes(".//button[contains(@class,'bg-[#343434]')]");
        var genres = genreNodes?.Select(b => b.InnerText.Trim()).ToList() ?? new List<string>();

        // --- Release Status ---
        var statusNode = rootNode.SelectSingleNode(".//div[contains(@class,'bg-[#343434]')]/h3[contains(@class,'capitalize')]");
        var releaseStatus = statusNode?.InnerText.Trim() ?? "Ongoing";

        // --- Alternative Titles (none in snippet, leave empty) ---
        var altTitles = new List<string>();

        var href = $"series/{id}";

        var manga = MangaBuilder.Create()
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
            .WithIsFamilySafe(!genres.Any(g => IsGenreNotFamilySafe(g)))
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
        var browser = await GetBrowserAsync();
        using var page = await browser.NewPageAsync();
        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        var finalUrl = new Uri(_baseUri, $"series/{manga.Id}").ToString();
        var response = await page.GoToAsync(finalUrl, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        foreach (var cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        var content = await page.GetContentAsync();

        var document = new HtmlDocument();
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
        var chapters = new List<Chapter>();

        foreach (var chapterDiv in nodes)
        {
            var skipNode = chapterDiv.SelectSingleNode(".//div[contains(@class,'bg-themecolor')]");
            if (skipNode != null)
                continue;

            // --- Anchor node with href ---
            var anchorNode = chapterDiv.SelectSingleNode("./self::a");
            var href = anchorNode?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var uri = NormalizeUrl($"series/{href}");

            // --- Chapter Id (derive from href) ---
            var chapterId = href;

            // --- Title and Number ---
            var titleNode = chapterDiv.SelectSingleNode(".//h3[contains(text(),'Chapter')]");
            var titleText = titleNode?.InnerText.Trim() ?? "Unknown Chapter";

            // Extract number from "Chapter 124"
            var number = 0m;
            var title = titleText;
            var match = System.Text.RegularExpressions.Regex.Match(titleText, @"Chapter\s+(\d+)");
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var parsedNumber))
            {
                number = parsedNumber;
                title = $"Chapter {parsedNumber}";
            }

            // --- Volume (not present in snippet, default to 0) ---
            var volume = 0m;

            // --- Build Chapter object ---
            var chapter = ChapterBuilder.Create()
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

    public async Task<IEnumerable<Core.Catalog.Page>> GetChapterPagesAsync(Chapter chapter, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync();
        using var page = await browser.NewPageAsync();

        await PreparePageForNavigationAsync(page);
        await page.SetUserAgentAsync(HttpClientDefaultUserAgent);

        await page.GoToAsync(chapter.Uri.ToString(), new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = TimeoutMilliseconds
        });

        await page.EvaluateFunctionAsync(@"async () => {
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

        foreach (var cookie in await page.GetCookiesAsync())
        {
            Logger?.LogDebug("{name}={value}; Domain={domain}; Path={path}", cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
        }

        var content = await page.GetContentAsync();
        var document = new HtmlDocument();
        document.LoadHtml(content);

        var pageNodes = document.DocumentNode.SelectNodes("//img[contains(@src,'storage/media/')]");
        return ConvertToChapterPages(chapter, pageNodes);
    }

    private IEnumerable<Core.Catalog.Page> ConvertToChapterPages(Chapter chapter, HtmlNodeCollection pageNodes)
    {
        if (pageNodes == null)
            return Enumerable.Empty<Page>();

        var pages = new List<Page>();
        int index = 1;

        foreach (var node in pageNodes)
        {
            // --- Image URL ---
            var imageUrl = node.GetAttributeValue("src", string.Empty);

            // --- Alt text (e.g., "chapter page 6") ---
            var altText = node.GetAttributeValue("alt", string.Empty);

            // --- Page number ---
            int pageNumber = 0;
            var match = System.Text.RegularExpressions.Regex.Match(altText, @"page\s+(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsedNumber))
            {
                pageNumber = parsedNumber;
            }
            else
            {
                // fallback: use loop index if alt not present
                pageNumber = index;
            }

            // --- Page Id (unique per image) ---
            var idAttr = $"{chapter.Id}-page-{pageNumber}";

            // --- Build Page object ---
            var page = PageBuilder.Create()
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

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~AsuraComicCrawlerAgent()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
