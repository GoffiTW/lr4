using System.Threading.Tasks;

namespace LumaChat.Services;

public interface ITranslationService
{
    Task<string> TranslateAsync(string text, string sourceLang = "en", string targetLang = "ru");
}
