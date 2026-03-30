using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JuriScraper.Scraping.Resources;

public class SniperSniffer
{
    public static async Task SniffAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions { 
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"
        });
        var page = await context.NewPageAsync();

        Console.WriteLine("\n--- Sniper 28.5: Iniciando Auditoria de Headers no Hub Central ---");

        // Intercepta todas as requisições para ver o tráfego do Angular para o MobileServices
        page.Request += (_, request) => {
            if (request.Url.Contains("mobileservices")) {
                Console.WriteLine($"\n[REQUISICAO] {request.Method} {request.Url}");
                foreach (var header in request.Headers) {
                    Console.WriteLine($"  {header.Key}: {header.Value}");
                }
            }
        };

        try {
            await page.GotoAsync("https://mob.trt4.jus.br/home", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            var html = await page.ContentAsync();
            Console.WriteLine("\n--- Sniper 28.11: DOM TRT 4 DETECTADO ---");
            Console.WriteLine(html.Length > 2000 ? html.Substring(0, 2000) : html);
            Console.WriteLine("\n--- FIM DO HTML ---");
            
            await Task.Delay(5000); 

            Console.WriteLine("\n--- Coletando Cookies de Sessão Ativos: ---");
            var cookies = await context.CookiesAsync();
            foreach (var cookie in cookies) {
                Console.WriteLine($"  {cookie.Name}={cookie.Value} (Domain: {cookie.Domain})");
            }
        } catch (Exception ex) {
            Console.WriteLine($"[ERRO] Falha na auditoria: {ex.Message}");
        }

        await browser.CloseAsync();
    }
}
