using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace JuriScraper.Scraping.Services;

public abstract class BaseScraper
{
    protected static readonly TimeSpan AutomaticTimeout = TimeSpan.FromSeconds(60);

    /// Maximo de tentativas headless (CAPTCHA pode ser intermitente)
    private const int MaxRetries = 3;

    /// Delay entre tentativas para evitar rate-limit e contornar CAPTCHA
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(8);

    // Rotacao de User-Agents para reduzir deteccao como bot
    private static readonly string[] UserAgents = new[]
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36 Edg/144.0.0.0"
    };

    private static readonly Random _random = new();
    private static readonly string[] LanguagesHeader = { "pt-BR,pt;q=0.9,en-US;q=0.7,en;q=0.6" };

    protected async Task<T> WithPageAsync<T>(Func<IPage, Task<T>> action, string? userAgent = null)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--disable-blink-features=AutomationControlled",
                    "--no-sandbox",
                    "--disable-dev-shm-usage"
                }
            });

            await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = userAgent ?? UserAgents[_random.Next(UserAgents.Length)],
                Locale = "pt-BR",
                TimezoneId = "America/Sao_Paulo",
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize
                {
                    Width = 1440,
                    Height = 1080
                },
                ExtraHTTPHeaders = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "Accept-Language", LanguagesHeader[0] },
                    { "DNT", "1" },
                    { "Upgrade-Insecure-Requests", "1" }
                }
            });

            // 🕵️ Stealth Sniper 13.0: Evasão total de detecção
            await context.AddInitScriptAsync(@"
                // 1. Apaga flag de automação
                Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                
                // 2. Mimetiza Chrome real
                window.chrome = {
                  runtime: {},
                  loadTimes: function() {},
                  csi: function() {},
                  app: {}
                };
                
                // 3. Mocks de hardware e plugins
                Object.defineProperty(navigator, 'languages', { get: () => ['pt-BR', 'pt', 'en-US'] });
                Object.defineProperty(navigator, 'platform', { get: () => 'Win32' });
                Object.defineProperty(navigator, 'deviceMemory', { get: () => 8 });
                Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 8 });
                Object.defineProperty(navigator, 'plugins', { get: () => [
                    { name: 'Chrome PDF Viewer', filename: 'internal-pdf-viewer' },
                    { name: 'Microsoft Edge PDF Viewer', filename: 'internal-pdf-viewer' }
                ] });

                // 4. Evasão de WebGL Fingerprinting
                const getParameter = WebGLRenderingContext.prototype.getParameter;
                WebGLRenderingContext.prototype.getParameter = function(parameter) {
                  if (parameter === 37445) return 'Intel Open Source Technology Center';
                  if (parameter === 37446) return 'Mesa DRI Intel(R) UHD Graphics 630';
                  return getParameter(parameter);
                };

                // 5. Mock de Permissions
                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                  parameters.name === 'notifications' ?
                    Promise.resolve({ state: Notification.permission }) :
                    originalQuery(parameters)
                );
            ");

            var page = await context.NewPageAsync();
            return await action(page);
        }
        catch (PlaywrightException ex) when (LooksLikeBrowserInstallIssue(ex))
        {
            throw new InvalidOperationException(
                "Playwright browsers nao estao instalados. Execute: pwsh playwright.ps1 install chromium",
                ex);
        }
    }

    /// <summary>
    /// Estrategia de tratamento de CAPTCHA conforme enunciado:
    ///   1. Tenta scraping headless (maioria dos portais nao exibe CAPTCHA na primeira tentativa)
    ///   2. Se CAPTCHA for detectado, aguarda e tenta novamente com User-Agent diferente
    ///   3. Retries com delays crescentes contornam CAPTCHAs intermitentes
    ///   4. Tudo 100% headless e automatizado (sem interface)
    /// </summary>
    protected async Task<T?> ExecuteWithCaptchaFallbackAsync<T>(
        string portalDisplayName,
        string numeroProcesso,
        Func<IPage, TimeSpan, bool, Task<T?>> scrapeAsync)
        where T : class
    {
        for (int tentativa = 1; tentativa <= MaxRetries; tentativa++)
        {
            try
            {
                // Usa User-Agent diferente em cada tentativa para contornar bloqueio
                var ua = UserAgents[(tentativa - 1) % UserAgents.Length];

                Console.WriteLine($"[{portalDisplayName}] Tentativa {tentativa}/{MaxRetries} para {numeroProcesso}...");

                var result = await WithPageAsync(
                    page => scrapeAsync(page, AutomaticTimeout, false),
                    ua);

                if (result != null)
                {
                    Console.WriteLine($"[{portalDisplayName}] Sucesso na tentativa {tentativa} para {numeroProcesso}.");
                    return result;
                }
            }
            catch (CaptchaChallengeException)
            {
                Console.WriteLine($"[{portalDisplayName}] CAPTCHA detectado na tentativa {tentativa}/{MaxRetries} para {numeroProcesso}.");
            }

            // Delay crescente entre tentativas para contornar CAPTCHA
            if (tentativa < MaxRetries)
            {
                var delay = RetryDelay * tentativa;
                Console.WriteLine($"[{portalDisplayName}] Aguardando {delay.TotalSeconds}s antes da proxima tentativa...");
                await Task.Delay(delay);
            }
        }

        Console.Error.WriteLine($"[{portalDisplayName}] Nao foi possivel coletar {numeroProcesso} apos {MaxRetries} tentativas.");
        return null;
    }

    protected static bool LooksLikeCaptchaPage(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("captcha", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("g-recaptcha", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("tokenDesafio", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("sou humano", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("verificacao de seguranca", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBrowserInstallIssue(PlaywrightException ex)
    {
        return ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("playwright.ps1 install", StringComparison.OrdinalIgnoreCase);
    }
}
