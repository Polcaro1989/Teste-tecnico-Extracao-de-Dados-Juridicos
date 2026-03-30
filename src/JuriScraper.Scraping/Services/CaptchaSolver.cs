using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using Tesseract;

namespace JuriScraper.Scraping.Services;

/// <summary>
/// Sniper OCR 14.1 Platinum - Professional Image Restoration & Recognition
/// </summary>
public sealed class CaptchaSolver : IDisposable
{
    private readonly TesseractEngine _engine;

    public CaptchaSolver()
    {
        var tessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly);
        _engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789");
    }

    public List<string> Solve(string base64Image)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bytes = Convert.FromBase64String(base64Image);
            
            // Geramos variantes purificadas via Magick.NET
            foreach (var variantBytes in GenerateVariants(bytes))
            {
                using var pix = Pix.LoadFromMemory(variantBytes);
                
                // Usamos PSM 7 (Single Line), PSM 8 (Single Word) e PSM 6 (Sparse Text) como aposta Sniper
                foreach (var psm in new[] { PageSegMode.SingleLine, PageSegMode.SingleWord, PageSegMode.SparseText })
                {
                    using var page = _engine.Process(pix, psm);
                    var text = page.GetText() ?? string.Empty;
                    var clean = new string(text.Where(char.IsLetterOrDigit).ToArray());
                    
                    if (clean.Length >= 4 && clean.Length <= 8) candidates.Add(clean);
                }
            }

            return candidates.ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CAPTCHA] Sniper 16.5 Error: {ex.Message}");
            return new List<string>();
        }
    }

    private IEnumerable<byte[]> GenerateVariants(byte[] originalBytes)
    {
        var debugDir = Path.Combine(AppContext.BaseDirectory, "captchas", "debug");
        if (!Directory.Exists(debugDir)) Directory.CreateDirectory(debugDir);
        var timestamp = DateTime.Now.ToString("HHmmss_fff");

        byte[] ProcessAndSave(string name, SniperSettings s)
        {
            var bytes = ProcessInternal(originalBytes, s);
            try { File.WriteAllBytes(Path.Combine(debugDir, $"{timestamp}_{name}.png"), bytes); } catch { }
            return bytes;
        }

        // 🎯 VARIANTES SNIPER 16.5
        yield return ProcessAndSave("cleaned_otsu", new SniperSettings { RemoveNoise = true, UseOtsu = true });
        yield return ProcessAndSave("cleaned_contrast", new SniperSettings { RemoveNoise = true, Threshold = 55 });
        yield return ProcessAndSave("negate_otsu", new SniperSettings { RemoveNoise = true, UseOtsu = true, Negate = true });
        yield return ProcessAndSave("sharp_raw", new SniperSettings { RemoveNoise = false, Threshold = 60 });
    }

    private byte[] ProcessInternal(byte[] bytes, SniperSettings settings)
    {
        using var image = new MagickImage(bytes);
        
        // 1. Tratamento Inicial
        image.Grayscale();
        if (settings.Negate) image.Negate();
        
        image.Settings.AntiAlias = false;

        // 2. Sniper Scaling (Upscale 350% para mais densidade de pixel)
        image.Resize(new Percentage(350), new Percentage(350));

        // 3. Binarização Inteligente (Otsu vs Manual)
        if (settings.UseOtsu)
        {
            image.AutoThreshold(AutoThresholdMethod.OTSU);
        }
        else
        {
            image.Threshold(new Percentage(settings.Threshold > 0 ? settings.Threshold : 50));
        }

        if (settings.RemoveNoise)
        {
            // Sniper 16.5: Hybrid Median + Unsharp Mask
            image.MedianFilter(3); 
            image.UnsharpMask(0, 1, 1, 0); // Define bordas das letras
            
            // Filtro final de resíduos
            image.Negate(); 
            image.ConnectedComponents(new ConnectedComponentsSettings { AreaThreshold = new ImageMagick.Threshold(45) });
            image.Negate();
            
            if (settings.UseOtsu) image.AutoThreshold(AutoThresholdMethod.OTSU);
            else image.Threshold(new Percentage(50));
        }

        // 6. Padding Cirúrgico
        image.BackgroundColor = MagickColors.White;
        image.BorderColor = MagickColors.White;
        image.Border(40);
        
        image.GaussianBlur(0.15); // Suave para Tesseract não ver fragmentos

        return image.ToByteArray(MagickFormat.Png);
    }

    public void Dispose() => _engine.Dispose();

    private record SniperSettings { public bool RemoveNoise; public int Threshold; public bool UseOtsu; public bool Negate; }
}
