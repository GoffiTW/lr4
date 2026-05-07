using Microsoft.Win32;

namespace LumaChat.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFileToSend()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл",
            Filter = "Все файлы|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
