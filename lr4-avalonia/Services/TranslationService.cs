using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LumaChat.Services;

public sealed class TranslationService : ITranslationService
{
    private static readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<string> TranslateAsync(string text, string sourceLang = "en", string targetLang = "ru")
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        string url = "https://translate.googleapis.com/translate_a/single?client=gtx" +
                     $"&sl={Uri.EscapeDataString(sourceLang)}" +
                     $"&tl={Uri.EscapeDataString(targetLang)}" +
                     "&dt=t&q=" + Uri.EscapeDataString(text);

        try
        {
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync();
            return ParseGoogleTranslateResponse(body) ?? text;
        }
        catch
        {
            return await TryMyMemoryAsync(text, sourceLang, targetLang) ?? text;
        }
    }

    private static async Task<string?> TryMyMemoryAsync(string text, string sourceLang, string targetLang)
    {
        try
        {
            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) +
                         $"&langpair={Uri.EscapeDataString(sourceLang)}|{Uri.EscapeDataString(targetLang)}";
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            string body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("responseData", out var rd) &&
                rd.TryGetProperty("translatedText", out var tt))
            {
                return tt.GetString();
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public static string? ParseGoogleTranslateResponse(string body)
    {
        // Response looks like:
        // [[["Привет","hello",null,null,1]],null,"en",...]
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return null;
            var sentences = root[0];
            if (sentences.ValueKind != JsonValueKind.Array) return null;
            var sb = new StringBuilder();
            foreach (var s in sentences.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0)
                {
                    var translated = s[0];
                    if (translated.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(translated.GetString());
                    }
                }
            }
            string result = sb.ToString();
            return string.IsNullOrEmpty(result) ? null : result;
        }
        catch
        {
            return null;
        }
    }
}
