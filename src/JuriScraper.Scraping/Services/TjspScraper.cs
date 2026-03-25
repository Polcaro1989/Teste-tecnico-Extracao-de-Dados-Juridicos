using System;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using PuppeteerSharp;

namespace JuriScraper.Scraping.Services;

public class TjspScraper : BaseScraper, IScraperService
{
    public async Task<Processo?> ExtrairProcessoAsync(string numeroProcesso)
    {
        await InitializeBrowserAsync();
        try
        {
            using var page = await _browser!.NewPageAsync();
            // User Agent realista para evitar bloqueio básico
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            await page.GoToAsync("https://esaj.tjsp.jus.br/cpopg/open.do", WaitUntilNavigation.Networkidle2);
            
            // Limpa e digita com delays (comportamento humano)
            await page.WaitForSelectorAsync("#numeroDigitoAnoUnificado");
            var numPart = numeroProcesso.Substring(0, 15);
            var foroPart = numeroProcesso.Substring(21, 4);

            await page.TypeAsync("#numeroDigitoAnoUnificado", numPart, new TypeOptions { Delay = 50 });
            await page.TypeAsync("#foroNumeroUnificado", foroPart, new TypeOptions { Delay = 50 });
            
            await Task.Delay(500);
            await page.ClickAsync("#pbConsultar");
            
            // Aguarda o resultado ou erro de captcha
            try 
            {
                await page.WaitForSelectorAsync("#classeProcesso", new WaitForSelectorOptions { Timeout = 15000 });
            }
            catch 
            {
                // Se não achou a classe, pode ser um captcha ou processo não encontrado
                var content = await page.GetContentAsync();
                if (content.Contains("Não foram encontrados processos") || content.Contains("Nenhum processo foi encontrado"))
                    return null;
                
                throw new Exception("Possível bloqueio de Captcha ou lentidão extrema no portal e-SAJ.");
            }
            
            var classe = await GetTextFromSelector(page, "#classeProcesso");
            var assunto = await GetTextFromSelector(page, "#assuntoProcesso");
            var foro = await GetTextFromSelector(page, "#foroProcesso");
            var dtDistribuicaoStr = await GetTextFromSelector(page, "#dataHoraDistribuicaoProcesso");
            
            var processo = new Processo
            {
                NumeroProcesso = numeroProcesso,
                Classe = classe?.Trim() ?? "Não Informada",
                Assunto = assunto?.Trim() ?? "Não Informado",
                ForoComarca = foro?.Trim() ?? "Não Informado",
                Tribunal = "TJSP - e-SAJ",
                DataColeta = DateTime.Now
            };

            // Extração de Andamentos (Pega o mais recente)
            var ultimoAndamentoData = await GetTextFromSelector(page, ".dataAndamento");
            var ultimoAndamentoDesc = await GetTextFromSelector(page, ".descricaoAndamento");
            
            if (!string.IsNullOrEmpty(ultimoAndamentoData))
            {
                processo.UltimoAndamento = ultimoAndamentoDesc?.Trim() ?? "";
                if (DateTime.TryParse(ultimoAndamentoData.Trim(), out DateTime dtAndamento))
                    processo.DataUltimoAndamento = dtAndamento;
            }

            return processo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TJSP DEBUG] Erro: {ex.Message}");
            throw; // Repassa para o Program.cs mostrar no Swagger
        }
        finally
        {
            await DisposeBrowserAsync();
        }
    }

    private async Task<string?> GetTextFromSelector(IPage page, string selector)
    {
        try {
            var element = await page.QuerySelectorAsync(selector);
            return element != null ? await element.EvaluateFunctionAsync<string>("e => e.innerText") : null;
        } catch {
            return null;
        }
    }
}
