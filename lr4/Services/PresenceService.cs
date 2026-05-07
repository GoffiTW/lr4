using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LumaChat.Models;

namespace LumaChat.Services;

public sealed class PresenceService : IPresenceService
{
    private const int DiscoveryPort = 45545;
    private const string DiscoveryProbe = "LUMA_TCP_CHAT_DISCOVER_V1";
    private const string DiscoveryReplyPrefix = "LUMA_TCP_CHAT_HOST_V1|";
    private const string PresencePrefix = "LUMA_PRESENCE|";

    private UdpClient? hostResponder;
    private bool isHostResponderRunning;
    private Timer? presenceTimer;
    private Func<int>? getPort;
    private Func<string>? getName;
    private CancellationTokenSource? listenerCts;

    public event EventHandler<ContactInfo>? ContactDiscovered;

    public void Start(Func<int> getPort, Func<string> getName)
    {
        this.getPort = getPort;
        this.getName = getName;
        listenerCts = new CancellationTokenSource();
        _ = Task.Run(() => ListenForPresenceAsync(listenerCts.Token));
        presenceTimer = new Timer(_ => SendPresence(), null, 0, 5000);
    }

    public void Stop()
    {
        presenceTimer?.Dispose();
        presenceTimer = null;
        listenerCts?.Cancel();
        listenerCts = null;
        StopHostResponder();
    }

    private async Task ListenForPresenceAsync(CancellationToken ct)
    {
        UdpClient? udp = null;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        }
        catch { return; }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                    string data = Encoding.UTF8.GetString(result.Buffer);
                    if (data.StartsWith(PresencePrefix))
                    {
                        var parts = data.Split('|');
                        if (parts.Length >= 4 && int.TryParse(parts[2], out int port))
                        {
                            string name = ChatUtils.DecodePayload(parts[1]);
                            string ip = result.RemoteEndPoint.Address.ToString();
                            ContactDiscovered?.Invoke(this, new ContactInfo
                            {
                                Name = string.IsNullOrWhiteSpace(name) ? "Пользователь" : name,
                                IpAddress = ip,
                                Port = port
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }
        finally { udp?.Dispose(); }
    }

    private void SendPresence()
    {
        if (getPort == null || getName == null) return;
        try
        {
            using var udp = new UdpClient();
            string presence = $"{PresencePrefix}{ChatUtils.EncodePayload(getName())}|{getPort()}|{ChatUtils.EncodePayload(Environment.MachineName)}";
            byte[] data = Encoding.UTF8.GetBytes(presence);
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch { }
    }

    public void StartHostResponder(int port, string name, string machine)
    {
        StopHostResponder();
        try
        {
            var responder = new UdpClient();
            responder.ExclusiveAddressUse = false;
            responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            responder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            hostResponder = responder;
            isHostResponderRunning = true;
            _ = Task.Run(() => HostResponderLoopAsync(responder, port, name, machine));
        }
        catch { }
    }

    public void StopHostResponder()
    {
        isHostResponderRunning = false;
        hostResponder?.Close();
        hostResponder = null;
    }

    private async Task HostResponderLoopAsync(UdpClient responder, int port, string name, string machine)
    {
        try
        {
            while (isHostResponderRunning && responder == hostResponder)
            {
                var result = await responder.ReceiveAsync();
                string request = Encoding.UTF8.GetString(result.Buffer).Trim();
                if (request == DiscoveryProbe)
                {
                    string reply = $"{DiscoveryReplyPrefix}{ChatUtils.EncodePayload(name)}|{port}|{ChatUtils.EncodePayload(machine)}";
                    byte[] data = Encoding.UTF8.GetBytes(reply);
                    await responder.SendAsync(data, data.Length, result.RemoteEndPoint);
                }
            }
        }
        catch { }
    }

    public async Task<int> ProbeOnceAsync(int timeoutMs = 1800)
    {
        int found = 0;
        try
        {
            using UdpClient searcher = new();
            searcher.EnableBroadcast = true;
            byte[] probe = Encoding.UTF8.GetBytes(DiscoveryProbe);
            await searcher.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (remaining <= 0) break;
                var receiveTask = searcher.ReceiveAsync();
                var timeoutTask = Task.Delay(remaining);
                var completed = await Task.WhenAny(receiveTask, timeoutTask);
                if (completed != receiveTask) break;

                var result = receiveTask.Result;
                string reply = Encoding.UTF8.GetString(result.Buffer);
                if (ChatUtils.TryParseDiscoveryReply(reply, out string name, out string machine, out int port))
                {
                    string ip = result.RemoteEndPoint.Address.ToString();
                    ContactDiscovered?.Invoke(this, new ContactInfo
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? machine : name,
                        IpAddress = ip,
                        Port = port
                    });
                    found++;
                }
            }
        }
        catch { }
        return found;
    }
}
