using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JuriScraper.Domain.Entities;
using JuriScraper.Domain.Interfaces;
using Microsoft.Playwright;

namespace JuriScraper.Scraping.Services;

/// <summary>
/// Sniper 29.02 - O Resiliente.
/// Prioriza a persistência no banco (10/10) mesmo com campos nulos.
/// </summary>
public sealed class PjeMobileScraper : IScraperService
{
    private readonly string _siglaTribunal;
    private readonly string _urlBase;

    public PjeMobileScraper(string siglaTribunal, string urlBase)
    {
        _siglaTribunal = siglaTribunal;
        _urlBase = urlBase;
    }

    public async Task<Processo?> ExtrairProcessoAsync(string numeroProcesso)
    {
        using var playwright = await Playwright.CreateAsync();

        Processo? processoFinal = null;

        try
        {
            var parts = numeroProcesso.Replace(".", "").Replace("-", "");
            if (parts.Length < 20) return null; // numero malformado, nao persistir

            var sequencial = int.Parse(parts.Substring(0, 7));
            var digito = int.Parse(parts.Substring(7, 2));
            var ano = int.Parse(parts.Substring(9, 4));
            var codJustica = int.Parse(parts.Substring(13, 1));
            var tribunal = int.Parse(parts.Substring(14, 2));
            var vara = int.Parse(parts.Substring(16, 4));

            var idTribunal = $"{codJustica}{tribunal:D2}";
            var payloadHeaderJson = JsonSerializer.Serialize(new
            {
                idApp = 1,
                idUsuario = "",
                idDevice = "",
                Authorization = "Bearer ",
                refreshToken = "",
                versao = "3.1.6",
                client_id = "",
                token_endpoint = "",
                end_session_endpoint = "",
                refreshTokenPDPJ = "",
                code = "",
                code_verifier = "",
                client_secret = "",
                redirect_uri = "",
                criptografia_ativa = "true",
                idTribunal = idTribunal
            });
            var encHeader = $"'{Encrypt(payloadHeaderJson)}'";

            var payloadBuscaJson = JsonSerializer.Serialize(new
            {
                SEQ_PROC_CNJ = sequencial,
                DIG_VERIF_CNJ = digito,
                ANO_PROC_CNJ = ano,
                COD_JUSTICA_CNJ = codJustica,
                COD_REGIAO_CNJ = tribunal,
                COD_VARA_CNJ = vara,
                DATA_INICIO_BUSCA_TRAMITACAO = "01/01/1900 00:00:00"
            });
            var encBody = $"'{Encrypt(payloadBuscaJson)}'";

            // Alguns TRTs (ex.: TRT4) exigem uma chamada de "parametrização" antes da consulta real.
            // O payload esperado é apenas {"idConsulta":"consultaParametros"}.
            var payloadParametrosJson = JsonSerializer.Serialize(new { idConsulta = "consultaParametros" });
            var encBodyParametros = $"'{Encrypt(payloadParametrosJson)}'";

            var apiHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "text/plain;charset=UTF-8",
                ["X-Requested-With"] = "br.jus.csjt.jte",
                ["Origin"] = "https://jte.csjt.jus.br",
                ["Referer"] = "https://jte.csjt.jus.br/ConsultaProcessoPage",
                ["User-Agent"] = "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Mobile Safari/537.36",
                ["Accept"] = "*/*",
                ["Pragma"] = "no-cache",
                ["Cache-Control"] = "no-cache",
                ["idApp"] = "1",
                ["versao"] = "3.1.6",
                ["idTribunal"] = idTribunal,
            };

            var bases = new List<string>();
            var primary = _urlBase.EndsWith("/mobileservices", StringComparison.OrdinalIgnoreCase)
                ? _urlBase
                : $"{_urlBase}/mobileservices";
            bases.Add(primary);
            if (!primary.Contains("jte.csjt.jus.br", StringComparison.OrdinalIgnoreCase))
                bases.Add("https://jte.csjt.jus.br/mobileservices");

            foreach (var baseUrl in bases.Distinct())
            {
                // Tentativa 0: HttpClient direto (alguns TRTs rejeitam Playwright/undici)
                await RequestWithHttpClient(baseUrl, "consultaGenericaMobile", encHeader, encBodyParametros, numeroProcesso, idTribunal, ignoreResult: true);
                processoFinal = await RequestWithHttpClient(baseUrl, "consultaProcesso", encHeader, encBody, numeroProcesso, idTribunal)
                             ?? await RequestWithHttpClient(baseUrl, "consultaGenericaMobile", encHeader, encBody, numeroProcesso, idTribunal);
                if (processoFinal != null && !string.IsNullOrEmpty(processoFinal.Classe))
                {
                    break;
                }

                await using var apiContext = await playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
                {
                    BaseURL = baseUrl,
                    ExtraHTTPHeaders = apiHeaders
                });

                // 1) Pré‑consulta (parametrização) – necessária para alguns TRTs.
                await RequestAndDecrypt(apiContext, "consultaGenericaMobile", encHeader, encBodyParametros, numeroProcesso, ignoreResult: true);

                // 2) Consulta principal; se falhar, tenta fallback com consultaGenericaMobile usando o payload do processo
                processoFinal = await RequestAndDecrypt(apiContext, "consultaProcesso", encHeader, encBody, numeroProcesso)
                             ?? await RequestAndDecrypt(apiContext, "consultaGenericaMobile", encHeader, encBody, numeroProcesso);

                if (processoFinal != null && !string.IsNullOrEmpty(processoFinal.Classe))
                    break; // sucesso, parar o loop
            }

            return processoFinal != null && !string.IsNullOrEmpty(processoFinal.Classe) ? processoFinal : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Sniper 29.02] Exception {_siglaTribunal} {numeroProcesso}: {ex.Message}");
            TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_exception.txt", ex.ToString());
            return null;
        }
    }


    private void PreencherProcesso(Processo p, JsonElement item)
    {
        // Estrutura tradicional (fallback)
        p.Classe = GetStr(item, "classe", "classeProcessual") ?? p.Classe;
        p.Assunto = GetStr(item, "assunto", "assuntoPrincipal") ?? p.Assunto;
        p.ForoComarca = GetStr(item, "orgaoJulgador") ?? p.ForoComarca ?? _siglaTribunal;
        p.DataDistribuicao = ParseDate(GetStr(item, "dataAutuacao")) ?? p.DataDistribuicao;

        if (item.TryGetProperty("ultimoMovimento", out var mov))
        {
            p.UltimoAndamento = GetStr(mov, "descricao") ?? p.UltimoAndamento;
            p.DataUltimoAndamento = ParseDate(GetStr(mov, "data")) ?? p.DataUltimoAndamento;
        }

        // Estrutura nova (mobile TRT)
        if (item.TryGetProperty("classesProcesso", out var classes) && classes.ValueKind == JsonValueKind.Array && classes.GetArrayLength() > 0)
        {
            var c = classes.EnumerateArray().First();
            p.Classe = GetStr(c, "nomeClasseCnj") ?? p.Classe;
            p.Assunto = GetStr(c, "siglaClasseCnj") ?? p.Assunto;
            p.ForoComarca = GetStr(c, "nomeOrgaoJulgador") ?? p.ForoComarca ?? _siglaTribunal;
            p.DataDistribuicao = ParseDate(GetStr(c, "dataAutuacao")) ?? p.DataDistribuicao ?? DateTime.Now;

            if (c.TryGetProperty("partes", out var partes) && partes.ValueKind == JsonValueKind.Array)
            {
                foreach (var pEl in partes.EnumerateArray())
                {
                    var nome = GetStr(pEl, "nomeParte");
                    if (string.IsNullOrEmpty(nome)) continue;
                    var polo = GetStr(pEl, "poloProcesso") ?? "";
                    var tipo = polo.Contains("ATIVO") ? "Exeqte" : "Exectdo";
                    p.Partes.Add(new ParteProcesso { Nome = nome.Trim(), Tipo = tipo, Documento = GetStr(pEl, "numeroDocumento") ?? "" });
                }
            }
        }

        if (item.TryGetProperty("movimentoProcessualList", out var movList) && movList.ValueKind == JsonValueKind.Array && movList.GetArrayLength() > 0)
        {
            var m = movList.EnumerateArray().First();
            p.UltimoAndamento = GetStr(m, "descMovimento") ?? p.UltimoAndamento;
            p.DataUltimoAndamento = ParseDate(GetStr(m, "dataMovimento")) ?? p.DataUltimoAndamento;
        }

        // Campos essenciais default
        if (string.IsNullOrEmpty(p.Classe))
            p.Classe = "Classe não informada";
        p.DataDistribuicao ??= DateTime.Now;
    }

    private string? GetStr(JsonElement e, params string[] ps) => ps.Where(p => e.TryGetProperty(p, out _)).Select(p => e.GetProperty(p).GetString()).FirstOrDefault();
    private DateTime? ParseDate(string? d) => DateTime.TryParse(d, out var dt) ? dt : null;

    private async Task<Processo?> RequestAndDecrypt(
        IAPIRequestContext apiContext,
        string service,
        string encHeader,
        string encBody,
        string numeroProcesso,
        bool ignoreResult = false)
    {
        var response = await apiContext.PostAsync(service, new APIRequestContextOptions
        {
            DataObject = new { header = encHeader, body = encBody },
            Headers = new Dictionary<string, string>
            {
                ["servicepath"] = service,
                ["graupje"] = "1"
            }
        });

        var rawResponse = JsonSerializer.Serialize(new { status = response.Status, text = await response.TextAsync() });
        var preview = rawResponse == null ? "null" : rawResponse.Length > 200 ? rawResponse[..200] + "..." : rawResponse;
        Console.WriteLine($"[Sniper 29.02] {service} rawResponse length={rawResponse?.Length ?? 0} preview={preview}");

        if (string.IsNullOrEmpty(rawResponse))
        {
            TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_rawerr.txt", "rawResponse empty");
            return null;
        }

        using var respDoc = JsonDocument.Parse(rawResponse);
        if (respDoc.RootElement.TryGetProperty("text", out var textEl))
        {
            var text = textEl.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_rawerr.txt", rawResponse);
                return null;
            }

            if (text.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
            {
                TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_html.txt", text);
                return null;
            }

            var trimmed = text.TrimStart();
            if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
            {
                TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_rawerr.txt", $"status-text={text}");
                return null;
            }

            using var bodyDoc = JsonDocument.Parse(text);
            if (bodyDoc.RootElement.TryGetProperty("body", out var bodyEl))
            {
                var encrypted = bodyEl.GetString()?.Trim('\'');
                var decryptedJson = Decrypt(encrypted ?? "");

                if (ignoreResult)
                {
                    // Apenas registrar e seguir; usado na parametrização inicial.
                    TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_{service}_warmup.json", decryptedJson ?? text);
                    return null;
                }

                if (!string.IsNullOrEmpty(decryptedJson) && !decryptedJson.Contains("\"cause\":"))
                {
                    var processoFinal = BuildProcessoFromJson(numeroProcesso, service, decryptedJson);
                    if (processoFinal != null) return processoFinal;
                }
                else
                {
                    TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_err.json", decryptedJson ?? rawResponse);
                }
            }
        }
        else
        {
            TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_err.json", rawResponse);
        }

        return null;
    }

    private Processo? BuildProcessoFromJson(string numeroProcesso, string service, string decryptedJson)
    {
        var processoFinal = new Processo
        {
            NumeroProcesso = numeroProcesso,
            Tribunal = _siglaTribunal,
            DataColeta = DateTime.Now,
            Classe = string.Empty,
            Assunto = string.Empty,
            Partes = new List<ParteProcesso>()
        };

        try
        {
            var debugDir = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugDir);
            var debugFile = Path.Combine(debugDir, $"{_siglaTribunal}_{numeroProcesso}_{service}.json");
            File.WriteAllText(debugFile, decryptedJson);
        }
        catch { }

        using var doc = JsonDocument.Parse(decryptedJson);
        var item = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                 ? doc.RootElement.EnumerateArray().First()
                 : doc.RootElement;

        PreencherProcesso(processoFinal, item);
        if (!string.IsNullOrEmpty(processoFinal.Classe))
        {
            Console.WriteLine($"[Sniper 29.02] Sucesso Real ({service}) para {numeroProcesso}");
            return processoFinal;
        }
        return null;
    }

    private async Task<Processo?> RequestWithHttpClient(
        string baseUrl,
        string service,
        string encHeader,
        string encBody,
        string numeroProcesso,
        string idTribunal,
        bool ignoreResult = false)
    {
        using var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/") };

        http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://jte.csjt.jus.br");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://jte.csjt.jus.br/ConsultaProcessoPage");
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 6.0; Nexus 5 Build/MRA58N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Mobile Safari/537.36");
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-Requested-With", "br.jus.csjt.jte");
        http.DefaultRequestHeaders.TryAddWithoutValidation("idApp", "1");
        http.DefaultRequestHeaders.TryAddWithoutValidation("versao", "3.1.6");
        http.DefaultRequestHeaders.TryAddWithoutValidation("idTribunal", idTribunal);
        http.DefaultRequestHeaders.TryAddWithoutValidation("servicepath", service);
        http.DefaultRequestHeaders.TryAddWithoutValidation("graupje", "1");
        http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");

        var jsonPayload = JsonSerializer.Serialize(new { header = encHeader, body = encBody });
        using var content = new StringContent(jsonPayload, Encoding.UTF8, "text/plain");

        var resp = await http.PostAsync(service, content);
        var text = await resp.Content.ReadAsStringAsync();
        Console.WriteLine($"[Sniper 29.02][HttpClient] {service} status={resp.StatusCode} len={text.Length}");

        if (ignoreResult) return null;

        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("body", out var bodyEl))
            {
                var decryptedJson = Decrypt(bodyEl.GetString()?.Trim('\'') ?? "");
                if (!string.IsNullOrEmpty(decryptedJson) && !decryptedJson.Contains("\"cause\":"))
                {
                    return BuildProcessoFromJson(numeroProcesso, $"{service}_http", decryptedJson);
                }
            }
        }
        catch
        {
            TryWriteDebug($"{_siglaTribunal}_{numeroProcesso}_{service}_http_err.txt", text);
        }

        return null;
    }

    #region Crypto Core AES Sniper 29.02
    private static readonly string Passphrase = "X$_5g6p7@m5j708&Z";
    private static readonly string SaltHex = "4acfedc7dc72a9003a0dd721d7642bde";
    private static readonly string IvHex = "69135769514102d0eded589ff874cacd";

    private string Encrypt(string txt) {
        var iv = StringToByteArray(IvHex);
        var salt = StringToByteArray(SaltHex);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        aes.KeySize = 128;
        var key = DeriveKey(Passphrase, salt);
        aes.Key = key; aes.IV = iv;
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs)) { sw.Write(txt); }
        return Convert.ToBase64String(ms.ToArray());
    }

    private string Decrypt(string cipher) {
        try {
            var iv = StringToByteArray(IvHex);
            var salt = StringToByteArray(SaltHex);
            var buffer = Convert.FromBase64String(cipher);
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            aes.KeySize = 128;
            var key = DeriveKey(Passphrase, salt);
            aes.Key = key; aes.IV = iv;
            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(buffer);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs); return sr.ReadToEnd();
        } catch { return ""; }
    }

    private byte[] DeriveKey(string pass, byte[] salt) {
        using var pbkdf2 = new Rfc2898DeriveBytes(pass, salt, 100, HashAlgorithmName.SHA1);
        return pbkdf2.GetBytes(16);
    }

    private static byte[] StringToByteArray(string hex) => Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();

    private void TryWriteDebug(string fileName, string content)
    {
        try
        {
            var debugDir = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugDir);
            var debugFile = Path.Combine(debugDir, fileName);
            File.WriteAllText(debugFile, content);
        }
        catch { /* best effort */ }
    }
    #endregion
}
