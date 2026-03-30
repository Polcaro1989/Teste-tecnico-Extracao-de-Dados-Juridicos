using System;

namespace JuriScraper.Scraping.Services;

internal sealed class CaptchaChallengeException : Exception
{
    public CaptchaChallengeException(string message)
        : base(message)
    {
    }
}
