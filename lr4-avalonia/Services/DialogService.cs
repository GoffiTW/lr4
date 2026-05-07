using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using LumaChat.Views;

namespace LumaChat.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFileToSend()
    {
        var mainWindow = App.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow as Window
            : null;

        if (mainWindow == null) return null;

        var storageProvider = mainWindow.StorageProvider;

        var options = new FilePickerOpenOptions
        {
            Title = "Выберите файл",
            FileTypeFilter = new[] { new FilePickerFileType("Все файлы") { Patterns = new[] { "*.*" } } },
            AllowMultiple = false
        };

        var files = storageProvider.OpenFilePickerAsync(options).GetAwaiter().GetResult();
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
}
