using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace LumaChat.Services;

public sealed class ChatService : IChatService
{
    private const string PeerDefault = "Собеседник";
    private const int MaxAttachmentBytes = 5 * 1024 * 1024;

    private readonly object networkLock = new();
    private readonly IHistoryService? _history;

    private TcpListener? listener;
    private TcpClient? client;
    private StreamReader? reader;
    private StreamWriter? writer;

    private string ownName = "";
    private bool sentHello;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string PeerName { get; private set; } = PeerDefault;
    public bool UseLumaProtocol { get; private set; }

    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<string>? SystemMessage;
    public event EventHandler<ChatTextReceivedEventArgs>? TextReceived;
    public event EventHandler<ChatFileReceivedEventArgs>? FileReceived;

    public ChatService(IHistoryService? history = null)
    {
        _history = history;
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public async Task HostAsync(int port, string ownName)
    {
        Disconnect();
        this.ownName = ownName;
        UseLumaProtocol = true;
        PeerName = PeerDefault;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            SetState(ConnectionState.WaitingForClient);
            SystemMessage?.Invoke(this, "Комната открыта. Ждём собеседника.");
            TcpClient accepted = await listener.AcceptTcpClientAsync();
            ConfigureConnectedClient(accepted, true);
        }
        catch (Exception ex)
        {
            SystemMessage?.Invoke(this, $"Ошибка: {ex.Message}");
            Disconnect();
        }
    }

    public async Task ConnectAsync(string host, int port, string ownName, int timeoutMs = 6000)
    {
        Disconnect();
        this.ownName = ownName;
        UseLumaProtocol = true;
        PeerName = PeerDefault;
        SetState(ConnectionState.Connecting);
        try
        {
            TcpClient connecting = new();
            var task = connecting.ConnectAsync(host, port);
            if (await Task.WhenAny(task, Task.Delay(timeoutMs)) != task)
            {
                connecting.Close();
                throw new TimeoutException($"Не удалось подключиться к {host}:{port}.");
            }
            await task;
            ConfigureConnectedClient(connecting, false);
            _ = SendHelloAsync();
        }
        catch (Exception ex)
        {
            Disconnect();
            SystemMessage?.Invoke(this, $"Подключение не удалось: {ex.Message}");
        }
    }

    private void ConfigureConnectedClient(TcpClient connected, bool accepted)
    {
        lock (networkLock)
        {
            listener?.Stop(); listener = null;
            client = connected;
            client.NoDelay = true;
            var stream = client.GetStream();
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        SetState(ConnectionState.Connected);
        SystemMessage?.Invoke(this, accepted ? "Собеседник подключился. Можно писать." : "Связь установлена.");
        _ = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (State == ConnectionState.Connected && reader != null)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;
                HandleProtocolLine(line);
            }
            if (State == ConnectionState.Connected) HandleRemoteDisconnect("Собеседник отключился.");
        }
        catch (IOException) { if (State == ConnectionState.Connected) HandleRemoteDisconnect("Связь потеряна."); }
        catch { }
    }

    private void HandleProtocolLine(string line)
    {
        if (line.StartsWith("HELLO|"))
        {
            string name = ChatUtils.DecodePayload(line.Substring(6)).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                UseLumaProtocol = true;
                PeerName = name;
                SystemMessage?.Invoke(this, $"В чате: {PeerName}.");
                if (!sentHello) _ = SendHelloAsync();
            }
            return;
        }
        if (line.StartsWith("MSG|"))
        {
            string msg = ChatUtils.DecodePayload(line.Substring(4));
            TextReceived?.Invoke(this, new ChatTextReceivedEventArgs(PeerName, msg));
            return;
        }
        if (line.StartsWith("FILE|"))
        {
            string[] parts = line.Split('|', 3);
            if (parts.Length == 3)
            {
                string fileName = ChatUtils.DecodePayload(parts[1]);
                try
                {
                    byte[] bytes = Convert.FromBase64String(parts[2]);
                    string savedPath = SaveIncomingAttachment(fileName, bytes);
                    FileReceived?.Invoke(this, new ChatFileReceivedEventArgs(PeerName, fileName, savedPath));
                }
                catch
                {
                    SystemMessage?.Invoke(this, "Получен повреждённый файл.");
                }
            }
            return;
        }
        TextReceived?.Invoke(this, new ChatTextReceivedEventArgs(PeerName, line));
    }

    private async Task SendHelloAsync()
    {
        if (writer == null) return;
        try { await writer.WriteLineAsync($"HELLO|{ChatUtils.EncodePayload(ownName)}"); sentHello = true; }
        catch { HandleRemoteDisconnect("Не удалось отправить имя."); }
    }

    private void HandleRemoteDisconnect(string msg)
    {
        Disconnect();
        SystemMessage?.Invoke(this, msg);
    }

    public async Task SendTextAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || State != ConnectionState.Connected || writer == null)
        {
            if (State != ConnectionState.Connected)
                SystemMessage?.Invoke(this, "Сначала подключитесь.");
            return;
        }
        try
        {
            if (UseLumaProtocol) await writer.WriteLineAsync($"MSG|{ChatUtils.EncodePayload(text)}");
            else await writer.WriteLineAsync(text.Replace("\r\n", " / ").Replace("\n", " / "));
        }
        catch { HandleRemoteDisconnect("Ошибка отправки."); }
    }

    public async Task SendFileAsync(string path, IProgress<float>? progress = null)
    {
        if (State != ConnectionState.Connected || writer == null)
        {
            SystemMessage?.Invoke(this, "Подключитесь сначала.");
            return;
        }
        if (!UseLumaProtocol)
        {
            SystemMessage?.Invoke(this, "Вложения только в Luma-режиме.");
            return;
        }
        var fi = new FileInfo(path);
        if (fi.Length > MaxAttachmentBytes)
        {
            SystemMessage?.Invoke(this, "Файл > 5 МБ.");
            return;
        }
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        long total = fs.Length;
        byte[] buffer = new byte[8192];
        using var ms = new MemoryStream();
        long sent = 0;
        int read;
        while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await ms.WriteAsync(buffer, 0, read);
            sent += read;
            progress?.Report(total == 0 ? 1f : (float)sent / total);
        }
        string base64 = Convert.ToBase64String(ms.ToArray());
        try
        {
            await writer.WriteLineAsync($"FILE|{ChatUtils.EncodePayload(Path.GetFileName(path))}|{base64}");
        }
        catch { HandleRemoteDisconnect("Ошибка отправки файла."); }
    }

    public void Disconnect()
    {
        lock (networkLock)
        {
            sentHello = false;
            UseLumaProtocol = false;
            listener?.Stop(); listener = null;
            reader?.Dispose(); reader = null;
            writer?.Dispose(); writer = null;
            client?.Close(); client = null;
        }
        SetState(ConnectionState.Disconnected);
    }

    private string SaveIncomingAttachment(string fileName, byte[] data)
    {
        string safe = ChatUtils.SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumaChatFiles");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, safe);
        int idx = 1;
        string nameOnly = Path.GetFileNameWithoutExtension(safe);
        string ext = Path.GetExtension(safe);
        while (File.Exists(path)) path = Path.Combine(folder, $"{nameOnly}_{idx++}{ext}");
        File.WriteAllBytes(path, data);
        return path;
    }
}
