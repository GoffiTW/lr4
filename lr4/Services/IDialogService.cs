// Services/IDialogService.cs
using System.Threading.Tasks;

namespace LumaChat.Services
{
    public interface IDialogService
    {
        Task<string?> ShowOpenFileDialogAsync(string filter = "Все файлы|*.*");
        void ShowNotification(string title, string message);
    }
}