using System;
using System.Threading.Tasks;

namespace LumaChat.Services;

public interface IChatService
{
    ConnectionState State { get; }
    string PeerName { get; }
    bool UseLumaProtocol { get; }

    event EventHandler<ConnectionState>? StateChanged;
    event EventHandler<string>? SystemMessage;
    event EventHandler<ChatTextReceivedEventArgs>? TextReceived;
    event EventHandler<ChatFileReceivedEventArgs>? FileReceived;

    Task HostAsync(int port, string ownName);
    Task ConnectAsync(string host, int port, string ownName, int timeoutMs = 6000);
    Task SendTextAsync(string text);
    Task SendFileAsync(string path, IProgress<float>? progress = null);
    void Disconnect();
}
