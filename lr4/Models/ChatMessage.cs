using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LumaChat.Models;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string author = string.Empty;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private string time = string.Empty;

    [ObservableProperty]
    private bool own;

    [ObservableProperty]
    private bool system;

    [ObservableProperty]
    private bool isFileTransfer;

    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private Visibility progressVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility chipVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string chipText = string.Empty;

    [ObservableProperty]
    private SolidColorBrush background = new(Colors.Transparent);

    [ObservableProperty]
    private SolidColorBrush stripColor = new(Colors.Transparent);

    [ObservableProperty]
    private Thickness margin = new(12, 4, 60, 8);
}
