// Services/DialogService.cs
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace LumaChat.Services
{
    public class DialogService : IDialogService
    {
        public Task<string?> ShowOpenFileDialogAsync(string filter = "Все файлы|*.*")
        {
            var dialog = new OpenFileDialog { Filter = filter };
            bool? result = dialog.ShowDialog();
            return Task.FromResult(result == true ? dialog.FileName : null);
        }

        public void ShowNotification(string title, string message)
        {
            // В WPF можно использовать всплывающее уведомление, но для простоты используем MessageBox
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}