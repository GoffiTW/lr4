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

public sealed class ChatTextReceivedEventArgs : EventArgs
{
    public string Author { get; }
    public string Text { get; }
    public ChatTextReceivedEventArgs(string author, string text)
    {
        Author = author;
        Text = text;
    }
}

public sealed class ChatFileReceivedEventArgs : EventArgs
{
    public string Author { get; }
    public string FileName { get; }
    public string SavedPath { get; }
    public ChatFileReceivedEventArgs(string author, string fileName, string savedPath)
    {
        Author = author;
        FileName = fileName;
        SavedPath = savedPath;
    }
}
