using System.IO;
using System.Text;

namespace LumaChat;

public static class ChatUtils
{
    public static string EncodePayload(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    public static string DecodePayload(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
        catch { return ""; }
    }

    public static bool TryReadPort(string portText, out int port)
    {
        return int.TryParse(portText, out port) && port > 0 && port <= 65535;
    }

    public static string SanitizeFileName(string name) =>
        new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    public static string[] GetLocalIpAddresses()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
                .AddressList
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a))
                .Select(a => a.ToString())
                .ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    public static bool IsLikelyLanIp(string ip)
    {
        if (ip.StartsWith("10.")) return true;
        if (ip.StartsWith("192.168.")) return true;
        if (ip.StartsWith("172."))
        {
            var parts = ip.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out int b))
                return b >= 16 && b <= 31;
        }
        return false;
    }

    public static bool TryParseDiscoveryReply(string payload, out string name, out string machine, out int port)
    {
        name = machine = "";
        port = 0;
        const string prefix = "LUMA_TCP_CHAT_HOST_V1|";
        if (!payload.StartsWith(prefix)) return false;
        var parts = payload.Substring(prefix.Length).Split('|');
        if (parts.Length < 3 || !int.TryParse(parts[1], out port)) return false;
        name = DecodePayload(parts[0]);
        machine = DecodePayload(parts[2]);
        return true;
    }
}