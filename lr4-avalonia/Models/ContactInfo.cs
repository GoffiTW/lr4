using CommunityToolkit.Mvvm.ComponentModel;

namespace LumaChat.Models;

public partial class ContactInfo : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string ipAddress = string.Empty;

    [ObservableProperty]
    private int port;
}
