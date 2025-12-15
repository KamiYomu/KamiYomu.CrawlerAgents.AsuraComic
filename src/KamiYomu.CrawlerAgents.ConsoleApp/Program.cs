using KamiYomu.CrawlerAgents.AsuraComic;
using KamiYomu.CrawlerAgents.Core;
using KamiYomu.CrawlerAgents.Core.Catalog;
using Microsoft.Extensions.Logging;
using Spectre.Console;

#region startup
AnsiConsole.MarkupLine("[bold underline green]KamiYomu AgentCrawler Validator[/]\n");
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole(); 
});

ILogger logger = loggerFactory.CreateLogger<Program>();

var options = new Dictionary<string, object>()
{
    { CrawlerAgentSettings.DefaultInputs.KamiYomuILogger, logger }
};
ICrawlerAgent crawler = new AsuraComicCrawlerAgent(options);
var results = new List<(string Method, bool Success, string Message)>();

var mangaResult = await crawler.SearchAsync("dragon", new PaginationOptions(1, 30), CancellationToken.None);
#endregion

#region Test GetFaviconAsync
try
{
    AnsiConsole.MarkupLine($"[bold underline green] Start Testing the method {nameof(ICrawlerAgent.GetFaviconAsync)}... [/]\n");
    var favicon = await crawler.GetFaviconAsync(CancellationToken.None);
    results.Add((nameof(ICrawlerAgent.GetFaviconAsync), favicon != null && favicon.IsAbsoluteUri, favicon?.ToString() ?? "Returned null"));
}
catch (Exception ex)
{
    results.Add((nameof(ICrawlerAgent.GetFaviconAsync), false, ex.Message));
}
#endregion

#region Test SearchAsync
try
{
    AnsiConsole.MarkupLine($"[bold underline green] Start Testing the method {nameof(ICrawlerAgent.SearchAsync)}... [/]\n");
    Thread.Sleep(1000);
    var count = mangaResult.Data?.Count() ?? 0;
    results.Add((nameof(ICrawlerAgent.SearchAsync), count > 0, $"Returned {count} result(s)"));
    results.Add(($"{nameof(Manga.Id)} is not empty", !string.IsNullOrWhiteSpace(mangaResult.Data.ElementAt(0).Id), $"{mangaResult.Data.ElementAt(0).Id}"));
    results.Add(($"{nameof(Manga.Title)} is not empty", !string.IsNullOrWhiteSpace(mangaResult.Data.ElementAt(0).Title), $"{mangaResult.Data.ElementAt(0).Title}"));
    results.Add(($"{nameof(Manga.Description)} is not empty", !string.IsNullOrWhiteSpace(mangaResult.Data.ElementAt(0).Description), $"{mangaResult.Data.ElementAt(0).Description}"));
    results.Add(($"{nameof(Manga.WebSiteUrl)} is not empty", !string.IsNullOrWhiteSpace(mangaResult.Data.ElementAt(0).WebSiteUrl), $"{mangaResult.Data.ElementAt(0).WebSiteUrl}"));
    results.Add(($"{nameof(Manga.CoverFileName)} is not empty", !string.IsNullOrWhiteSpace(mangaResult.Data.ElementAt(0).CoverFileName), $"{mangaResult.Data.ElementAt(0).CoverFileName}"));
    results.Add(($"{nameof(Manga.IsFamilySafe)} is not empty", true, $"{mangaResult.Data.ElementAt(0).IsFamilySafe}"));

}
catch (Exception ex)
{
    results.Add((nameof(ICrawlerAgent.SearchAsync), false, ex.Message));
}
#endregion

#region Test GetByIdAsync
try
{
    AnsiConsole.MarkupLine($"[bold underline green] Start Testing the method {nameof(ICrawlerAgent.GetByIdAsync)}... [/]\n");
    Thread.Sleep(1000);
    var manga = await crawler.GetByIdAsync(mangaResult.Data.ElementAt(0)?.Id, CancellationToken.None);
    var any = mangaResult.Data?.Any() ?? false;
    results.Add((nameof(ICrawlerAgent.GetByIdAsync), any, $"Returning {manga.Title} and its cover {manga.CoverUrl}."));
    results.Add(($"{nameof(Manga.Id)} is not empty", !string.IsNullOrWhiteSpace(manga.Id), $"{manga.Id}"));
    results.Add(($"{nameof(Manga.Title)} is not empty", !string.IsNullOrWhiteSpace(manga.Title), $"{manga.Title}"));
    results.Add(($"{nameof(Manga.Description)} is not empty", !string.IsNullOrWhiteSpace(manga.Description), $"{manga.Description}"));
    results.Add(($"{nameof(Manga.WebSiteUrl)} is not empty", !string.IsNullOrWhiteSpace(manga.WebSiteUrl), $"{manga.WebSiteUrl}"));
    results.Add(($"{nameof(Manga.CoverFileName)} is not empty", !string.IsNullOrWhiteSpace(manga.CoverFileName), $"{manga.CoverFileName}"));
    results.Add(($"{nameof(Manga.IsFamilySafe)} is not empty", true, $"{manga.IsFamilySafe}"));
}
catch (Exception ex)
{
    results.Add((nameof(ICrawlerAgent.GetByIdAsync), false, ex.Message));
}
#endregion

#region Test GetChaptersAsync
try
{
    AnsiConsole.MarkupLine($"[bold underline green] Start Testing the method {nameof(ICrawlerAgent.GetChaptersAsync)}... [/]\n");
    Thread.Sleep(1000);
    var chaptersResult = await crawler.GetChaptersAsync(mangaResult.Data.First(), new PaginationOptions(0, 300, 300), CancellationToken.None);
    var count = chaptersResult?.Data.Count() ?? 0;
    var allNumbers = chaptersResult.Data?
    .Select(c => c.Number)
    .OrderByDescending(p => p)
    .ToList();

    var numbersString = allNumbers != null ? string.Join(";", allNumbers) : string.Empty;
    results.Add((nameof(ICrawlerAgent.GetChaptersAsync), !string.IsNullOrWhiteSpace(chaptersResult?.Data.FirstOrDefault()?.Id), $"Returned {chaptersResult?.Data.FirstOrDefault()?.Id} result(s)"));
    results.Add(($"{nameof(Chapter.Id)} is not empty", !string.IsNullOrWhiteSpace(chaptersResult.Data?.FirstOrDefault().Id), $"{chaptersResult.Data?.FirstOrDefault().Id}"));
    results.Add(($"{nameof(Chapter.Title)} is not empty", !string.IsNullOrWhiteSpace(chaptersResult?.Data?.FirstOrDefault().Title), $"{chaptersResult?.Data?.FirstOrDefault().Title}"));
    results.Add(($"{nameof(Chapter.Uri)} is not empty", !string.IsNullOrWhiteSpace(chaptersResult.Data?.FirstOrDefault().Uri.ToString()), $"{chaptersResult.Data?.FirstOrDefault().Uri}"));
    results.Add(($"{nameof(Chapter.Number)} is not empty", allNumbers.Count > 0, numbersString));
    results.Add(($"{nameof(allNumbers.Count)} is not empty", allNumbers.Count > 0, allNumbers.Count.ToString()));
    results.Add(($"{nameof(Chapter.ParentManga)} is not empty", chaptersResult.Data?.FirstOrDefault() != null, $"{chaptersResult.Data?.FirstOrDefault().ParentManga.Title}"));
}
catch (Exception ex)
{
    results.Add((nameof(ICrawlerAgent.GetChaptersAsync), false, ex.Message));
}
#endregion

#region Test GetByteArrayAsync
try
{
    AnsiConsole.MarkupLine($"[bold underline green] Start Testing the method {nameof(HttpClient.GetByteArrayAsync)}... [/]\n");
    Thread.Sleep(1000);
    var chaptersResult = await crawler.GetChaptersAsync(mangaResult.Data.First(), new PaginationOptions(0, 10), CancellationToken.None);
    Thread.Sleep(1000);
    var chapterImages = await crawler.GetChapterPagesAsync(chaptersResult.Data.Last(), CancellationToken.None);

    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(CrawlerAgentSettings.HttpUserAgent);
    var imageBytes = await httpClient.GetByteArrayAsync(chapterImages.ElementAt(0).ImageUrl);
    results.Add(($"{nameof(Chapter.Uri)} is not empty", !string.IsNullOrWhiteSpace(chaptersResult.Data?.FirstOrDefault().Uri.ToString()), $"{chaptersResult.Data?.FirstOrDefault().Uri}"));
    results.Add(($"Count is not empty", chapterImages.Any(), $"{chapterImages.Count()}"));
    results.Add((nameof(HttpClient.GetByteArrayAsync), chapterImages.Any(), $"Returned {imageBytes.Count()} bytes as result(s)"));
}
catch (Exception ex)
{
    results.Add((nameof(HttpClient.GetByteArrayAsync), false, ex.Message));
}
#endregion

#region Display results
var table = new Table()
    .Title("[yellow]Test Results[/]")
    .Border(TableBorder.Rounded)
    .AddColumn("Method")
    .AddColumn("Status")
    .AddColumn("Message");

foreach (var (method, success, message) in results)
{
    table.AddRow(
        $"[bold]{method}[/]",
        success ? "[green]✔ Passed[/]" : "[red]✘ Failed[/]",
         Markup.Escape(message)
    );
}

AnsiConsole.Write(table);
#endregion