using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using Microsoft.Playwright;
using Tesseract;

namespace JuriScraper.Scraping.Services;

public class PjeScraper : BaseScraper, IScraperService
{
    private static readonly CaptchaSolver CaptchaSolver = new();
    private static readonly HttpClientHandler Handler = new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.All,
        CookieContainer = new System.Net.CookieContainer(),
        UseCookies = true
    };

    private static readonly HttpClient Http = new(Handler);
    private readonly string _baseUrl;
    private readonly string _siglaTribunal;
    // Mantemos alinhado ao README: ate 5 sessoes humanizadas por CNJ.
    private const int MaxSessions = 2;
    private static readonly Random _rnd = new();

    public PjeScraper(string siglaTribunal, string baseUrl)
    {
        _siglaTribunal = siglaTribunal;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<Processo?> ExtrairProcessoAsync(string numeroProcesso)
    {
        // Vai direto para as sessoes Sniper humanizadas, furando o CloudFront.
        for (var sessao = 1; sessao <= MaxSessions; sessao++)
        {
            Console.WriteLine($"[PJE {_siglaTribunal}] Iniciando Sessao {sessao}/{MaxSessions} Sniper para {numeroProcesso}...");
            var result = await WithPageAsync(page => ExtrairViaNavegacaoAsync(page, numeroProcesso, AutomaticTimeout));
            if (result is not null)
            {
                if (!string.IsNullOrWhiteSpace(result.UltimoAndamento))
                    Console.WriteLine($"[PJE {_siglaTribunal}] Sucesso! Andamento capturado: {result.UltimoAndamento.Substring(0, Math.Min(30, result.UltimoAndamento.Length))}...");
                
                Console.WriteLine($"[PJE {_siglaTribunal}] Sucesso na sessao {sessao} para {numeroProcesso}.");
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
        }

        Console.Error.WriteLine($"[PJE {_siglaTribunal}] Falha critica: {numeroProcesso} apos {MaxSessions} sessoes Sniper.");
        return null;
    }


    private Processo MapearProcesso(string numeroProcesso, DadosBasicosPje dadosBasicos, JsonElement detalhe)
    {
        var processo = new Processo
        {
            NumeroProcesso = numeroProcesso,
            Classe = PrimeiroValorNaoVazio(
                GetString(detalhe, "classe"),
                GetString(detalhe, "classeProcessual"),
                dadosBasicos.Classe) ?? string.Empty,
            Assunto = ExtrairAssunto(detalhe) ?? string.Empty,
            ForoComarca = PrimeiroValorNaoVazio(
                GetString(detalhe, "orgaoJulgador"),
                GetString(detalhe, "descricaoOrgaoJulgador"),
                GetString(detalhe, "nomeOrgaoJulgador"),
                dadosBasicos.CodigoOrgaoJulgador) ?? string.Empty,
            DataDistribuicao = ExtrairData(
                GetString(detalhe, "autuadoEm"),
                GetString(detalhe, "dataDistribuicao"),
                GetString(detalhe, "dataAutuacao")),
            Tribunal = $"PJE - {_siglaTribunal}",
            DataColeta = DateTime.Now
        };

        foreach (var parte in ExtrairPartes(detalhe))
        {
            processo.Partes.Add(parte);
        }

        var ultimoAndamento = ExtrairUltimoAndamento(detalhe);
        if (ultimoAndamento is not null)
        {
            processo.UltimoAndamento = ultimoAndamento.Descricao ?? string.Empty;
            processo.DataUltimoAndamento = ultimoAndamento.Data;
        }

        return processo;
    }

    private IEnumerable<ParteProcesso> ExtrairPartes(JsonElement detalhe)
    {
        var partes = new List<ParteProcesso>();
        ExtrairPartesRecursivamente(detalhe, partes);
        return partes
            .Where(parte => !string.IsNullOrWhiteSpace(parte.Nome))
            .GroupBy(parte => $"{parte.Tipo}|{parte.Nome}", StringComparer.OrdinalIgnoreCase)
            .Select(grupo => grupo.First());
    }

    private void ExtrairPartesRecursivamente(JsonElement element, List<ParteProcesso> partes)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtrairPartesRecursivamente(item, partes);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var nome = PrimeiroValorNaoVazio(
            GetString(element, "nome"),
            GetString(element, "nomeParte"),
            GetString(element, "nomePessoa"));
        var tipo = PrimeiroValorNaoVazio(
            GetString(element, "tipo"),
            GetString(element, "tipoParte"),
            GetString(element, "polo"),
            GetString(element, "descricaoPolo"));

        if (!string.IsNullOrWhiteSpace(nome) && !string.IsNullOrWhiteSpace(tipo))
        {
            partes.Add(new ParteProcesso
            {
                Nome = nome,
                Tipo = tipo,
                Documento = string.Empty
            });
        }

        foreach (var propriedade in element.EnumerateObject())
        {
            ExtrairPartesRecursivamente(propriedade.Value, partes);
        }
    }

    private UltimoAndamentoPje? ExtrairUltimoAndamento(JsonElement detalhe)
    {
        var colecoes = new List<JsonElement>();
        ColetarColecoesPorNome(detalhe, colecoes, "movimentos", "eventos", "movimentacoes", "eventosProcessuais");

        foreach (var colecao in colecoes.Where(item => item.ValueKind == JsonValueKind.Array))
        {
            var candidatos = new List<UltimoAndamentoPje>();
            foreach (var item in colecao.EnumerateArray())
            {
                var descricao = PrimeiroValorNaoVazio(
                    GetString(item, "descricao"),
                    GetString(item, "descricaoEvento"),
                    GetString(item, "nomeEvento"),
                    GetString(item, "texto"));
                var data = ExtrairData(
                    GetString(item, "data"),
                    GetString(item, "dataHora"),
                    GetString(item, "dataHoraEvento"),
                    GetString(item, "atualizadoEm"));

                if (!string.IsNullOrWhiteSpace(descricao))
                {
                    candidatos.Add(new UltimoAndamentoPje(descricao, data));
                }
            }

            var ultimo = candidatos
                .OrderByDescending(item => item.Data ?? DateTime.MinValue)
                .FirstOrDefault();

            if (ultimo is not null)
            {
                return ultimo;
            }
        }

        return null;
    }

    private void ColetarColecoesPorNome(JsonElement element, List<JsonElement> colecoes, params string[] nomes)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    ColetarColecoesPorNome(item, colecoes, nomes);
                }
            }

            return;
        }

        foreach (var propriedade in element.EnumerateObject())
        {
            if (nomes.Any(nome => string.Equals(nome, propriedade.Name, StringComparison.OrdinalIgnoreCase)))
            {
                colecoes.Add(propriedade.Value);
            }

            ColetarColecoesPorNome(propriedade.Value, colecoes, nomes);
        }
    }

    private string? ExtrairAssunto(JsonElement detalhe)
    {
        var assuntoDireto = PrimeiroValorNaoVazio(
            GetString(detalhe, "assunto"),
            GetString(detalhe, "assuntoPrincipal"),
            GetString(detalhe, "descricaoAssuntoPrincipal"));
        if (!string.IsNullOrWhiteSpace(assuntoDireto))
        {
            return assuntoDireto;
        }

        var assuntos = new List<string>();
        ColetarStringsPorNome(detalhe, assuntos, "assuntos", "descricaoAssunto");
        return assuntos.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private void ColetarStringsPorNome(JsonElement element, List<string> resultados, params string[] nomes)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ColetarStringsPorNome(item, resultados, nomes);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var propriedade in element.EnumerateObject())
        {
            if (nomes.Any(nome => string.Equals(nome, propriedade.Name, StringComparison.OrdinalIgnoreCase)))
            {
                switch (propriedade.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        resultados.Add(propriedade.Value.GetString() ?? string.Empty);
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in propriedade.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                resultados.Add(item.GetString() ?? string.Empty);
                            }
                            else
                            {
                                resultados.AddRange(new[]
                                {
                                    GetString(item, "descricao"),
                                    GetString(item, "nome")
                                }.Where(value => !string.IsNullOrWhiteSpace(value))!);
                            }
                        }
                        break;
                }
            }

            ColetarStringsPorNome(propriedade.Value, resultados, nomes);
        }
    }

    private static DateTime? ExtrairData(params string?[] valores)
    {
        foreach (var valor in valores.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (DateTime.TryParse(valor, CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.AssumeLocal, out var data))
            {
                return data;
            }

            if (DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out data))
            {
                return data;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyRecursive(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = default;
        if (!TryGetPropertyRecursive(element, propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetPropertyRecursive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }

                if (TryGetPropertyRecursive(property.Value, propertyName, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryGetPropertyRecursive(item, propertyName, out value))
                {
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool ContemCaptcha(JsonElement element)
    {
        return TryGetPropertyRecursive(element, "tokenDesafio", out _) ||
               TryGetPropertyRecursive(element, "imagem", out _);
    }

    private static async Task<string?> HttpFetchAsync(
        string url,
        IDictionary<string, string> headers,
        TimeSpan timeout)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var response = await Http.SendAsync(request, cts.Token);
            var content = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(content))
            {
                if (url.Contains("/dadosbasicos", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("/processos/", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[PJE HTTP] {response.StatusCode} em {url}");
                }
                return null;
            }
            if (!response.IsSuccessStatusCode &&
                (url.Contains("/dadosbasicos", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("/processos/", StringComparison.OrdinalIgnoreCase)))
            {
                var preview = content.Length > 140 ? content[..140] + "..." : content;
                Console.Error.WriteLine($"[PJE HTTP] {response.StatusCode} em {url} corpo={preview}");
            }
            return content;
        }
        catch (Exception ex)
        {
            if (url.Contains("/dadosbasicos", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/processos/", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"[PJE HTTP] Erro {ex.Message} em {url}");
            }
            return null;
        }
    }

    private static async Task<byte[]?> HttpFetchBytesAsync(
        string url,
        IDictionary<string, string> headers,
        TimeSpan timeout)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var response = await Http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync(cts.Token);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Abre rapidamente as URLs publicas para carregar cookies do WAF/CloudFront antes das chamadas de API.
    /// </summary>
    private async Task AquecerSessaoAsync(string numeroProcesso, TimeSpan timeout)
    {
        var htmlHeaders = new Dictionary<string, string>
        {
            { "accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8" },
            { "accept-language", "pt-BR,pt;q=0.9,en-US;q=0.7,en;q=0.6" },
            { "user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0 Safari/537.36" }
        };

        var warmUrls = new[]
        {
            _baseUrl + "/",
            $"{_baseUrl}/captcha/detalhe-processo/{numeroProcesso}/1"
        };

        foreach (var url in warmUrls)
        {
            _ = await HttpFetchAsync(url, htmlHeaders, timeout);
        }
    }

    private static bool TentaExtrairCaptcha(string json, out string tokenDesafio, out string imagemBase64)
    {
        tokenDesafio = string.Empty;
        imagemBase64 = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return TentaExtrairCaptcha(doc.RootElement, out tokenDesafio, out imagemBase64);
        }
        catch
        {
            return false;
        }
    }

    private static bool TentaExtrairCaptcha(JsonElement element, out string tokenDesafio, out string imagemBase64)
    {
        tokenDesafio = string.Empty;
        imagemBase64 = string.Empty;

        if (!TryGetPropertyRecursive(element, "tokenDesafio", out var tokenElem) ||
            !TryGetPropertyRecursive(element, "imagem", out var imagemElem))
        {
            return false;
        }

        tokenDesafio = tokenElem.ValueKind == JsonValueKind.String ? tokenElem.GetString() ?? string.Empty : string.Empty;
        imagemBase64 = imagemElem.ValueKind == JsonValueKind.String ? imagemElem.GetString() ?? string.Empty : string.Empty;

        return !string.IsNullOrWhiteSpace(tokenDesafio) && !string.IsNullOrWhiteSpace(imagemBase64);
    }

    private static Task<string[]> SolveCaptchaAsync(string imagemBase64, TimeSpan timeout)
    {
        // 🧪 2. Converter base64 → imagem & 🔥 4. OCR com Tesseract (via solver)
        using var solver = new CaptchaSolver();
        var candidates = solver.Solve(imagemBase64);
        
        return Task.FromResult(candidates.ToArray());
    }

    private static List<string> PromptCaptchaManual(string numeroProcesso, string imagemBase64)
    {
        var list = new List<string>();
        try
        {
            var safeName = string.Concat(numeroProcesso.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            var fileName = Path.Combine(AppContext.BaseDirectory, $"captcha_{safeName}_{DateTime.Now:HHmmssfff}.png");
            var bytes = Convert.FromBase64String(imagemBase64);
            File.WriteAllBytes(fileName, bytes);
            Console.WriteLine($"[PJE] CAPTCHA salvo em: {fileName}");
            Console.Write($"[PJE] Digite o CAPTCHA para {numeroProcesso}: ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                list.Add(input.Trim());
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PJE] Falha ao salvar/ler CAPTCHA: {ex.Message}");
        }

        return list;
    }

    private static async Task CopiarCookiesParaHttpAsync(IPage page)
    {
        var cookies = await page.Context.CookiesAsync();
        foreach (var ck in cookies)
        {
            try
            {
                var domain = ck.Domain.StartsWith(".") ? ck.Domain : "." + ck.Domain;
                Handler.CookieContainer.Add(new System.Net.Cookie(ck.Name, ck.Value, ck.Path ?? "/", domain));
            }
            catch { /* ignora cookies invalidos */ }
        }
    }

    private static async Task<string?> TentarResolverCaptchaViaHttpAsync(
        string apiBase,
        int id,
        string grau,
        string tokenDesafio,
        string imagemBase64,
        string numeroProcesso,
        TimeSpan timeout)
    {
        var candidatos = new List<string>();
        using var solver = new CaptchaSolver();
        try { candidatos.AddRange(solver.Solve(imagemBase64)); }
        catch { Console.Error.WriteLine($"[PJE] Falha no OCR do CAPTCHA para {numeroProcesso}."); }

            // tenta captcha fresh via endpoint dedicado
            var fresh = await BuscarCaptchaDiretoAsync(apiBase, id, timeout);
            if (fresh is not null)
            {
                tokenDesafio = fresh.Value.Token;
                imagemBase64 = fresh.Value.Imagem;
                try { candidatos.AddRange(solver.Solve(imagemBase64)); }
                catch { Console.Error.WriteLine($"[PJE] Falha no OCR do captcha fresh para {numeroProcesso}."); }
            }

        // salva captcha e candidatos para dataset
        SalvarCaptchaParaDataset(numeroProcesso, imagemBase64, candidatos);

        foreach (var candidato in candidatos.Distinct().Where(c => c.Length >= 4 && c.Length <= 8))
        {
            var solvedUrl =
                $"{apiBase}/processos/{id}?grau={Uri.EscapeDataString(grau)}&tokenDesafio={Uri.EscapeDataString(tokenDesafio)}&resposta={Uri.EscapeDataString(candidato)}";

            var resposta = await HttpFetchAsync(solvedUrl, new Dictionary<string, string>
            {
                { "x-grau-instancia", "1" },
                { "accept", "application/json, text/plain, */*" },
                { "user-agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Mobile Safari/537.36" },
                { "referer", apiBase.Replace("/pje-consulta-api/api", string.Empty, StringComparison.OrdinalIgnoreCase) }
            }, timeout);

            if (string.IsNullOrWhiteSpace(resposta)) continue;
            if (TentaExtrairCaptcha(resposta, out _, out _)) continue;
            return resposta;
        }

        Console.Error.WriteLine($"[PJE] Falha ao resolver CAPTCHA via OCR para {numeroProcesso}.");
        return null;
    }

    private static async Task<(string Token, string Imagem)?> BuscarCaptchaDiretoAsync(string apiBase, int id, TimeSpan timeout)
    {
        var url = $"{apiBase}/captcha?idProcesso={id}";
        var headers = new Dictionary<string, string>
        {
            { "accept", "application/json, text/plain, */*" },
            { "accept-language", "pt-BR,pt;q=0.9,en-US;q=0.7,en;q=0.6" },
            { "user-agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Mobile Safari/537.36" },
            { "pragma", "no-cache" },
            { "cache-control", "no-cache" }
        };
        var json = await HttpFetchAsync(url, headers, timeout);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return TentaExtrairCaptcha(json, out var token, out var img) ? (token, img) : null;
    }

    private static async Task<string?> BuscarCaptchaAudioAsync(string apiBase, int id, TimeSpan timeout)
    {
        var url = $"{apiBase}/captcha/audio?idProcesso={id}";
        var headers = new Dictionary<string, string>
        {
            { "accept", "audio/wav,audio/*;q=0.9,application/json" },
            { "user-agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Mobile Safari/537.36" }
        };
        var bytes = await HttpFetchBytesAsync(url, headers, timeout);
        if (bytes is null || bytes.Length == 0) return null;
        return Convert.ToBase64String(bytes);
    }


    private static void SalvarCaptchaParaDataset(string numeroProcesso, string imagemBase64, IEnumerable<string> candidatos)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "captchas");
            Directory.CreateDirectory(root);
            var safe = string.Concat(numeroProcesso.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var imgPath = Path.Combine(root, $"{safe}_{ts}.png");
            File.WriteAllBytes(imgPath, Convert.FromBase64String(imagemBase64));

            var meta = new
            {
                NumeroProcesso = numeroProcesso,
                Image = Path.GetFileName(imgPath),
                Candidates = candidatos.Distinct().ToArray()
            };
            var metaPath = Path.Combine(root, $"{safe}_{ts}.json");
            File.WriteAllText(metaPath, System.Text.Json.JsonSerializer.Serialize(meta, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }

    private async Task<Processo?> ExtrairViaNavegacaoAsync(IPage page, string numeroProcesso, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        DadosBasicosPje? dadosBasicos = null;
        string? detalheJson = null;
        var apiBase = new Uri(new Uri(_baseUrl), "/pje-consulta-api/api/").ToString().TrimEnd('/');

        // 🎯 ESTRATÉGIA SNIPER 17.0 Elite: Cookie Warming & Bypass
        // 1. Navega para a home e "passeia" para ganhar confiança do WAF
        try
        {
            Console.WriteLine($"[PJE] Aquecendo Cookies Sniper para {numeroProcesso}...");
            await page.GotoAsync(_baseUrl + "/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
            await RandomDelayAsync(1000, 2000);
            await MoveMouseRandomAsync(page);
        }
        catch { }

        // 2. Intercepta a resposta da API de dados básicos
        EventHandler<IResponse>? handler = null;
        handler = async (_, response) =>
        {
            try
            {
                if (response.Url.Contains("/processos/dadosbasicos", StringComparison.OrdinalIgnoreCase))
                {
                    var texto = await response.TextAsync();
                    if (!string.IsNullOrWhiteSpace(texto))
                    {
                        using var doc = JsonDocument.Parse(texto);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        {
                            var primeiro = doc.RootElement.EnumerateArray().First();
                            if (TryGetInt32(primeiro, "id", out var id))
                            {
                                dadosBasicos = new DadosBasicosPje(id, GetString(primeiro, "numero") ?? numeroProcesso, GetString(primeiro, "classe") ?? string.Empty, GetString(primeiro, "codigoOrgaoJulgador"));
                            }
                        }
                    }
                }
                
                if (response.Url.Contains("/pje-consulta-api/api/processos/") && !response.Url.Contains("/dadosbasicos"))
                {
                    var texto = await response.TextAsync();
                    if (!string.IsNullOrWhiteSpace(texto) && !texto.Contains("captcha", StringComparison.OrdinalIgnoreCase))
                    {
                        detalheJson = texto;
                    }
                }
            }
            catch { }
        };

        page.Response += handler;

        try
        {
            // 3. Usa a URL de busca pública que costuma ser menos agressiva no CAPTCHA
            var searchUrl = $"{_baseUrl}/consulta-processual";
            await page.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = (float)timeout.TotalMilliseconds });
            
            // Tenta clicar no campo de busca e digitar humanamente
            await TryBuscarProcessoAsync(page, numeroProcesso, timeout);

            while (DateTime.UtcNow < deadline)
            {
                if (dadosBasicos != null && !string.IsNullOrWhiteSpace(detalheJson))
                {
                    using var detalhe = JsonDocument.Parse(detalheJson);
                    return MapearProcesso(numeroProcesso, dadosBasicos, detalhe.RootElement);
                }

                // Tenta detectar CAPTCHA e pedir ÁUDIO como fallback se a tela travar
                var hasCaptcha = await page.Locator("img[src^='data:image']").CountAsync() > 0;
                if (hasCaptcha)
                {
                     // Se o CAPTCHA de imagem aparecer, clicamos no botão de ÁUDIO (mais fácil de bypassar ou o WAF limpa se o cookie estiver quente)
                     var audioBtn = page.Locator("button:has(mat-icon:has-text('volume_up'))").Or(page.Locator(".captcha-audio"));
                     if (await audioBtn.CountAsync() > 0)
                     {
                         await audioBtn.First.ClickAsync();
                         await RandomDelayAsync(500, 1000);
                     }
                }

                await Task.Delay(500);
                if (dadosBasicos != null && string.IsNullOrWhiteSpace(detalheJson))
                {
                    // Se temos dados básicos mas não detalhes, tenta hitar a API de detalhes diretamente via Page Context (usa os cookies validos do navegador)
                    var detailUrl = $"{apiBase}/processos/{dadosBasicos.Id}?grau=1";
                    try 
                    {
                        var jsFetch = $"(async () => {{ try {{ const r = await fetch('{detailUrl}'); return await r.text(); }} catch {{ return null; }} }})()";
                        var result = await page.EvaluateAsync<string?>(jsFetch);
                        if (!string.IsNullOrWhiteSpace(result) && !result.Contains("tokenDesafio"))
                        {
                            detalheJson = result;
                        }
                    } catch { }
                }
            }

            return null;
        }
        finally
        {
            page.Response -= handler;
        }
    }

    private async Task<bool> TryBuscarProcessoAsync(IPage page, string numeroProcesso, TimeSpan timeout)
    {
        var inputSelectors = new[]
        {
            "input[placeholder*='Número do processo']",
            "input[formcontrolname='numeroProcesso']",
            "input[name='numeroProcesso']",
            "input[id*='numero']"
        };

        foreach (var selector in inputSelectors)
        {
            try
            {
                var handle = await page.QuerySelectorAsync(selector);
                if (handle is null) continue;

                await handle.ClickAsync();
                await RandomDelayAsync(150, 400);
                await TypeHumanAsync(handle, numeroProcesso, timeout);
                await handle.PressAsync("Enter");
                return true;
            }
            catch { }
        }

        return false;
    }

    private static async Task WarmupAsync(IPage page)
    {
        await page.EvaluateAsync("fetch('/favicon.ico').catch(()=>{})");
        await RandomDelayAsync(200, 400);
    }

    private static async Task MoveMouseRandomAsync(IPage page)
    {
        var x = _rnd.Next(100, 500);
        var y = _rnd.Next(100, 500);
        await page.Mouse.MoveAsync(x, y);
        await RandomDelayAsync(150, 300);
    }

    private static async Task ScrollRandomAsync(IPage page)
    {
        await page.Mouse.WheelAsync(0, _rnd.Next(200, 500));
    }

    private static async Task RandomDelayAsync(int minMs, int maxMs)
    {
        await Task.Delay(_rnd.Next(minMs, maxMs));
    }

    private static async Task TypeHumanAsync(IElementHandle input, string text, TimeSpan timeout)
    {
        var delay = _rnd.Next(80, 160);
        foreach (var ch in text)
        {
            await input.TypeAsync(ch.ToString(), new ElementHandleTypeOptions { Delay = delay });
        }
    }

    private static string? PrimeiroValorNaoVazio(params string?[] valores)
    {
        return valores.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private sealed record DadosBasicosPje(int Id, string Numero, string Classe, string? CodigoOrgaoJulgador);
    private sealed record UltimoAndamentoPje(string? Descricao, DateTime? Data);
}
