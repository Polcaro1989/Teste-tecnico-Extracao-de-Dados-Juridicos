using System;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using PuppeteerSharp;

namespace JuriScraper.Scraping.Services;

public class PjeScraper : BaseScraper, IScraperService
{
    private readonly string _baseUrl;
    private readonly string _siglaTribunal;

    public PjeScraper(string siglaTribunal, string baseUrl)
    {
        _siglaTribunal = siglaTribunal;
        _baseUrl = baseUrl;
    }

    public async Task<Processo?> ExtrairProcessoAsync(string numeroProcesso)
    {
        await InitializeBrowserAsync();
        try
        {
            using var page = await _browser!.NewPageAsync();
            await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            // Navega para a página de consulta Angular
            await page.GoToAsync(_baseUrl, WaitUntilNavigation.Networkidle2);
            
            // Aguarda o campo de busca (Padrão Angular PJE)
            await page.WaitForSelectorAsync("input[formcontrolname='numeroProcesso'], #numeroProcesso, input[name*='numero']", new WaitForSelectorOptions { Timeout = 15000 });
            
            // Digita com delay humano
            await page.TypeAsync("input[formcontrolname='numeroProcesso'], #numeroProcesso, input[name*='numero']", numeroProcesso, new TypeOptions { Delay = 70 });
            
            await Task.Delay(500);
            await page.ClickAsync("button#btnPesquisar, button[type='submit'], .mat-icon:has-text('search')");

            // Aguarda o carregamento do resultado
            try 
            {
                await page.WaitForSelectorAsync(".mat-row, .detalhe-processo, app-consulta-listagem", new WaitForSelectorOptions { Timeout = 15000 });
            }
            catch 
            {
                var content = await page.GetContentAsync();
                if (content.Contains("Não foram encontrados processos") || content.Contains("Nenhum registro encontrado"))
                    return null;
                
                throw new Exception("Timeout aguardando resultados do PJE (Possível bloqueio ou lentidão).");
            }

            // Seletores PJE Unificados (Angular)
            var classe = await GetTextFromSelector(page, ".classe-processual, .text-muted:contains('Classe') + span, app-processo-detalhe-identificacao .col-md-9");
            var assunto = await GetTextFromSelector(page, ".assunto-principal, .text-muted:contains('Assunto') + span");
            var foro = await GetTextFromSelector(page, ".orgao-julgador, .text-muted:contains('Órgão Julgador') + span");

            var processo = new Processo
            {
                NumeroProcesso = numeroProcesso,
                Classe = !string.IsNullOrEmpty(classe) ? classe.Trim() : "Consulte no Portal",
                Assunto = !string.IsNullOrEmpty(assunto) ? assunto.Trim() : "Consulte no Portal",
                ForoComarca = !string.IsNullOrEmpty(foro) ? foro.Trim() : "Vara do Trabalho",
                Tribunal = $"PJE - {_siglaTribunal}",
                DataColeta = DateTime.Now
            };

            // Tenta pegar o último andamento
            var ultimoAndamentoData = await GetTextFromSelector(page, ".historico-data, .data-movimentacao");
            var ultimoAndamentoDesc = await GetTextFromSelector(page, ".historico-descricao, .descricao-movimentacao");

            if (!string.IsNullOrEmpty(ultimoAndamentoData))
            {
                processo.UltimoAndamento = ultimoAndamentoDesc?.Trim() ?? "Movimentação recente";
                if (DateTime.TryParse(ultimoAndamentoData, out DateTime dtAn))
                    processo.DataUltimoAndamento = dtAn;
            }

            return processo;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PJE DEBUG] Erro em {_siglaTribunal}: {ex.Message}");
            throw;
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
