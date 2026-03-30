using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JuriScraper.Scraping.Resources;

public class UiScanner
{
    public static async Task ScanHomeAsync(string url)
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try {
            Console.WriteLine($"\n--- Sniper 28.19: Escaneando UI em {url} ---");
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            
            var screenshotPath = @"C:\Users\abraa\.gemini\antigravity\brain\397e3328-7b8a-4c24-aff6-d0443556e88c\trt4_home.png";
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
            Console.WriteLine($"[SCREENSHOT] {screenshotPath}");

            var html = await page.ContentAsync();
            Console.WriteLine("\n--- HTML BRUTO (TRT 4) ---");
            Console.WriteLine(html.Length > 3000 ? html.Substring(0, 3000) : html);
            Console.WriteLine("\n--- FIM DO HTML ---");
        } catch (Exception ex) {
            Console.WriteLine($"[ERRO] Falha no scan: {ex.Message}");
        }

        await browser.CloseAsync();
    }
}
