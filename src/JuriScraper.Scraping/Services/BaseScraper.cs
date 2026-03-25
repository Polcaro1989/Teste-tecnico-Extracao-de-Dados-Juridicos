using System;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using PuppeteerSharp;

namespace JuriScraper.Scraping.Services;

public abstract class BaseScraper
{
    protected IBrowser? _browser;

    protected async Task InitializeBrowserAsync()
    {
        var fetcher = new BrowserFetcher();
        await fetcher.DownloadAsync(); // Baixa o Chrome Headless automaticamente na primeira vez
        
        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
        });
    }

    public async Task DisposeBrowserAsync()
    {
        if (_browser != null)
        {
            await _browser.CloseAsync();
            await _browser.DisposeAsync();
        }
    }
}
