// Services/INetworkService.cs
using System;
using System.Threading.Tasks;
using LumaChat.Models;

namespace LumaChat.Services
{
    public interface INetworkService
    {
        event Action<Message>? MessageReceived;
        event Action<bool>? ConnectionStatusChanged;
        event Action<Contact>? ContactDiscovered;

        Task StartHostAsync(int port, string userName);
        Task ConnectToHostAsync(string ip, int port, string userName);
        Task SendMessageAsync(string text);
        Task SendFileAsync(string filePath, IProgress<double> progress);
        Task StopAsync();
        Task DiscoverHostsAsync();
        bool IsConnected { get; }
        string? LocalIp { get; }
    }
}