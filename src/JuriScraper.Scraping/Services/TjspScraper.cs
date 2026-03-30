using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using Microsoft.Playwright;

namespace JuriScraper.Scraping.Services;

public class TjspScraper : BaseScraper, IScraperService
{
    public Task<Processo?> ExtrairProcessoAsync(string numeroProcesso)
    {
        return ExecuteWithCaptchaFallbackAsync(
            "TJSP",
            numeroProcesso,
            (page, timeout, interactive) => ExtrairNoBrowserAsync(page, numeroProcesso, timeout, interactive));
    }

    private async Task<Processo?> ExtrairNoBrowserAsync(
        IPage page,
        string numeroProcesso,
        TimeSpan timeout,
        bool interactive)
    {
        await page.GotoAsync(
            BuildConsultaUrl(numeroProcesso),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var resultado = await WaitForResultadoAsync(page, numeroProcesso, timeout, interactive);
        if (resultado == TjspResultadoStatus.ProcessoNaoEncontrado)
        {
            return null;
        }

        return await MapearProcessoAsync(page, numeroProcesso);
    }

    private async Task<TjspResultadoStatus> WaitForResultadoAsync(
        IPage page,
        string numeroProcesso,
        TimeSpan timeout,
        bool interactive)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await page.Locator("#classeProcesso").CountAsync() > 0)
            {
                return TjspResultadoStatus.DetalhePronto;
            }

            var content = await page.ContentAsync();
            if (ContainsNotFoundMessage(content))
            {
                return TjspResultadoStatus.ProcessoNaoEncontrado;
            }

            if (!interactive && LooksLikeCaptchaPage(content))
            {
                throw new CaptchaChallengeException(
                    $"Portal TJSP exigiu validacao manual para o processo {numeroProcesso}.");
            }

            await Task.Delay(500);
        }

        if (interactive)
        {
            throw new Exception(
                $"CAPTCHA do portal TJSP nao foi resolvido dentro do tempo limite de {timeout.TotalMinutes:0} minutos para o processo {numeroProcesso}.");
        }

        throw new CaptchaChallengeException(
            $"Portal TJSP nao retornou a pagina de detalhe do processo {numeroProcesso} no modo headless.");
    }

    private async Task<Processo> MapearProcessoAsync(IPage page, string numeroProcesso)
    {
        var classe = await GetTextFromSelectorAsync(page, "#classeProcesso");
        var assunto = await GetTextFromSelectorAsync(page, "#assuntoProcesso");
        var foro = await GetTextFromSelectorAsync(page, "#foroProcesso");
        var dataDistribuicaoTexto = await GetTextFromSelectorAsync(page, "#dataHoraDistribuicaoProcesso");
        var partes = await ExtractPartesAsync(page);
        var ultimaMovimentacao = await ExtractUltimaMovimentacaoAsync(page);

        var processo = new Processo
        {
            NumeroProcesso = numeroProcesso,
            Classe = CleanText(classe) ?? string.Empty,
            Assunto = CleanText(assunto) ?? string.Empty,
            ForoComarca = CleanText(foro) ?? string.Empty,
            DataDistribuicao = ParseDateOnly(dataDistribuicaoTexto),
            Tribunal = "TJSP - e-SAJ",
            DataColeta = DateTime.Now
        };

        if (ultimaMovimentacao.Length >= 2)
        {
            processo.UltimoAndamento = CleanText(ultimaMovimentacao[1]) ?? string.Empty;
            processo.DataUltimoAndamento = ParseDateOnly(ultimaMovimentacao[0]);
        }

        foreach (var parte in partes)
        {
            var tokens = parte.Split("|||", StringSplitOptions.None);
            if (tokens.Length < 2)
            {
                continue;
            }

            processo.Partes.Add(new ParteProcesso
            {
                Nome = tokens[1],
                Tipo = tokens[0],
                Documento = string.Empty
            });
        }

        return processo;
    }

    private static string BuildConsultaUrl(string numeroProcesso)
    {
        var digits = new string(numeroProcesso.Where(char.IsDigit).ToArray());
        if (digits.Length != 20)
        {
            throw new ArgumentException("Numero CNJ invalido para consulta no TJSP.", nameof(numeroProcesso));
        }

        var numeroBase = numeroProcesso[..15];
        var foro = numeroProcesso[^4..];

        return "https://esaj.tjsp.jus.br/cpopg/search.do" +
            "?conversationId=&cbPesquisa=NUMPROC&dadosConsulta.tipoNuProcesso=UNIFICADO" +
            $"&numeroDigitoAnoUnificado={Uri.EscapeDataString(numeroBase)}" +
            $"&foroNumeroUnificado={Uri.EscapeDataString(foro)}" +
            $"&dadosConsulta.valorConsultaNuUnificado={Uri.EscapeDataString(digits)}" +
            "&dadosConsulta.valorConsulta=";
    }

    private static bool ContainsNotFoundMessage(string content)
    {
        return content.Contains("Nao foram encontrados processos", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Não foram encontrados processos", StringComparison.OrdinalIgnoreCase) ||
               content.Contains("Nenhum processo foi encontrado", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> GetTextFromSelectorAsync(IPage page, string selector)
    {
        try
        {
            var locator = page.Locator(selector);
            if (await locator.CountAsync() == 0)
            {
                return null;
            }

            return await locator.First.InnerTextAsync();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string[]> ExtractPartesAsync(IPage page)
    {
        return await page.EvaluateAsync<string[]>(
            @"() => Array.from(document.querySelectorAll('tr.fundoClaro'))
                .flatMap((row) => {
                    const tipo = row.querySelector('.tipoDeParticipacao')?.textContent?.replace(/\u00a0/g, ' ').trim();
                    const cell = row.querySelector('.nomeParteEAdvogado');

                    if (!tipo || !cell) {
                        return [];
                    }

                    const lines = (cell.innerText || '')
                        .split(/\r?\n/)
                        .map((line) => line.replace(/\u00a0/g, ' ').trim())
                        .filter(Boolean);

                    if (lines.length === 0) {
                        return [];
                    }

                    const resultado = [`${tipo}|||${lines[0]}`];

                    for (const line of lines.slice(1)) {
                        const matchAdvogado = line.match(/^Advogad[oa]:\s*(.+)$/i);
                        if (matchAdvogado) {
                            resultado.push(`Advogado|||${matchAdvogado[1].trim()}`);
                        }
                    }

                    return resultado;
                })");
    }

    private async Task<string[]> ExtractUltimaMovimentacaoAsync(IPage page)
    {
        return await page.EvaluateAsync<string[]>(
            @"() => {
                const row = document.querySelector('tr.containerMovimentacao');
                if (!row) {
                    return [];
                }

                return [
                    row.querySelector('.dataMovimentacao')?.textContent?.replace(/\u00a0/g, ' ').trim() || '',
                    row.querySelector('.descricaoMovimentacao')?.innerText?.replace(/\u00a0/g, ' ').trim() || ''
                ];
            }");
    }

    private static DateTime? ParseDateOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        var datePart = normalized.Split(" às ", StringSplitOptions.TrimEntries)[0]
                                 .Split(" as ", StringSplitOptions.TrimEntries)[0]
                                 .Split(" - ", StringSplitOptions.TrimEntries)[0];

        return DateTime.TryParseExact(
            datePart,
            "dd/MM/yyyy",
            CultureInfo.GetCultureInfo("pt-BR"),
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(" ", value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private enum TjspResultadoStatus
    {
        DetalhePronto,
        ProcessoNaoEncontrado
    }
}
