// Services/NetworkService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LumaChat.Models;

namespace LumaChat.Services
{
    public class NetworkService : INetworkService
    {
        public event Action<Message>? MessageReceived;
        public event Action<bool>? ConnectionStatusChanged;
        public event Action<Contact>? ContactDiscovered;

        private TcpListener? _listener;
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private UdpClient? _discoveryResponder;
        private bool _isDiscoveryResponderRunning;
        private bool _isConnected;
        private bool _useLumaProtocol;
        private bool _sentHello;
        private string _peerName = "Собеседник";
        private readonly object _networkLock = new object();

        private const int DiscoveryPort = 45545;
        private const int MaxAttachmentBytes = 5 * 1024 * 1024;
        private const string DiscoveryProbe = "LUMA_TCP_CHAT_DISCOVER_V1";
        private const string DiscoveryReplyPrefix = "LUMA_TCP_CHAT_HOST_V1|";
        private const string PresencePrefix = "LUMA_PRESENCE|";

        public bool IsConnected => _isConnected;
        public string? LocalIp => GetLocalIpAddresses().FirstOrDefault();

        public async Task StartHostAsync(int port, string userName)
        {
            await StopAsync();
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                StartDiscoveryResponder(port, userName, Environment.MachineName);
                ConnectionStatusChanged?.Invoke(false); // ожидание клиента

                _client = await _listener.AcceptTcpClientAsync();
                ConfigureConnectedClient(_client, userName);
                ConnectionStatusChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                await StopAsync();
                throw new Exception($"Ошибка при создании хоста: {ex.Message}");
            }
        }

        public async Task ConnectToHostAsync(string ip, int port, string userName)
        {
            await StopAsync();
            try
            {
                var client = new TcpClient();
                await ConnectWithTimeoutAsync(client, ip, port, 6000);
                ConfigureConnectedClient(client, userName);
                ConnectionStatusChanged?.Invoke(true);
                if (_useLumaProtocol) await SendHelloAsync(userName);
            }
            catch (Exception ex)
            {
                await StopAsync();
                throw new Exception($"Не удалось подключиться: {ex.Message}");
            }
        }

        private void ConfigureConnectedClient(TcpClient connected, string userName)
        {
            lock (_networkLock)
            {
                StopDiscoveryResponder();
                _listener?.Stop();
                _listener = null;
                _client = connected;
                _client.NoDelay = true;
                var stream = _client.GetStream();
                _reader = new StreamReader(stream, new UTF8Encoding(false));
                _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                _isConnected = true;
                _useLumaProtocol = true; // используем расширенный протокол
            }
            _ = ReceiveLoopAsync();
        }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_isConnected && _reader != null)
                {
                    string? line = await _reader.ReadLineAsync();
                    if (line == null) break;
                    HandleProtocolLine(line);
                }
            }
            catch (IOException)
            {
                // соединение разорвано
            }
            finally
            {
                if (_isConnected) await StopAsync();
            }
        }

        private void HandleProtocolLine(string line)
        {
            if (line.StartsWith("HELLO|"))
            {
                string name = DecodePayload(line.Substring(6)).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _peerName = name;
                    MessageReceived?.Invoke(new Message
                    {
                        Author = "Система",
                        Text = $"В чате: {_peerName}",
                        IsSystem = true
                    });
                }
                return;
            }
            if (line.StartsWith("MSG|"))
            {
                string text = DecodePayload(line.Substring(4));
                MessageReceived?.Invoke(new Message
                {
                    Author = _peerName,
                    Text = text,
                    IsOwn = false
                });
                return;
            }
            if (line.StartsWith("FILE|"))
            {
                string[] parts = line.Split('|', 3);
                if (parts.Length == 3)
                {
                    string fileName = DecodePayload(parts[1]);
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(parts[2]);
                        string savedPath = SaveIncomingAttachment(fileName, bytes);
                        MessageReceived?.Invoke(new Message
                        {
                            Author = _peerName,
                            Text = $"Файл: {fileName}",
                            FileName = fileName,
                            FilePath = savedPath,
                            IsOwn = false
                        });
                    }
                    catch
                    {
                        MessageReceived?.Invoke(new Message
                        {
                            Author = "Система",
                            Text = "Получен повреждённый файл",
                            IsSystem = true
                        });
                    }
                }
                return;
            }
            // fallback: просто текст
            MessageReceived?.Invoke(new Message
            {
                Author = _peerName,
                Text = line,
                IsOwn = false
            });
        }

        private async Task SendHelloAsync(string userName)
        {
            if (_writer == null) return;
            try
            {
                await _writer.WriteLineAsync($"HELLO|{EncodePayload(userName)}");
                _sentHello = true;
            }
            catch
            {
                await StopAsync();
            }
        }

        public async Task SendMessageAsync(string text)
        {
            if (!_isConnected || _writer == null) return;
            try
            {
                if (_useLumaProtocol)
                    await _writer.WriteLineAsync($"MSG|{EncodePayload(text)}");
                else
                    await _writer.WriteLineAsync(text.Replace("\r\n", " / ").Replace("\n", " / "));

                MessageReceived?.Invoke(new Message
                {
                    Author = GetOwnNameFromSettings(), // будет установлено из ViewModel
                    Text = text,
                    IsOwn = true
                });
            }
            catch
            {
                await StopAsync();
            }
        }

        public async Task SendFileAsync(string filePath, IProgress<double> progress)
        {
            if (!_isConnected || _writer == null || !_useLumaProtocol)
                throw new InvalidOperationException("Файлы можно отправлять только в Luma-режиме");

            var fi = new FileInfo(filePath);
            if (fi.Length > MaxAttachmentBytes)
                throw new InvalidOperationException("Файл превышает 5 МБ");

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long total = fs.Length;
            byte[] buffer = new byte[8192];
            using var ms = new MemoryStream();
            long sent = 0;
            int read;
            while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await ms.WriteAsync(buffer, 0, read);
                sent += read;
                progress.Report((double)sent / total);
            }
            string base64 = Convert.ToBase64String(ms.ToArray());
            await _writer.WriteLineAsync($"FILE|{EncodePayload(Path.GetFileName(filePath))}|{base64}");
        }

        public async Task StopAsync()
        {
            lock (_networkLock)
            {
                StopDiscoveryResponder();
                _isConnected = false;
                _useLumaProtocol = false;
                _sentHello = false;
                _listener?.Stop();
                _listener = null;
                _reader?.Dispose();
                _reader = null;
                _writer?.Dispose();
                _writer = null;
                _client?.Close();
                _client = null;
            }
            await Task.CompletedTask;
            ConnectionStatusChanged?.Invoke(false);
        }

        public async Task DiscoverHostsAsync()
        {
            var foundContacts = new List<Contact>();
            try
            {
                using var searcher = new UdpClient();
                searcher.EnableBroadcast = true;
                byte[] probe = Encoding.UTF8.GetBytes(DiscoveryProbe);
                await searcher.SendAsync(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(1800);
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
                    if (TryParseDiscoveryReply(reply, out string name, out int port))
                    {
                        string ip = result.RemoteEndPoint.Address.ToString();
                        foundContacts.Add(new Contact { Name = name, IpAddress = ip, Port = port, IsOnline = true });
                    }
                }
            }
            catch { /* ignore */ }

            foreach (var contact in foundContacts)
                ContactDiscovered?.Invoke(contact);
        }

        #region Вспомогательные методы
        private void StartDiscoveryResponder(int port, string name, string machine)
        {
            StopDiscoveryResponder();
            try
            {
                var responder = new UdpClient();
                responder.ExclusiveAddressUse = false;
                responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                responder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                _discoveryResponder = responder;
                _isDiscoveryResponderRunning = true;
                _ = DiscoveryResponderLoopAsync(responder, port, name, machine);
            }
            catch { }
        }

        private async Task DiscoveryResponderLoopAsync(UdpClient responder, int port, string name, string machine)
        {
            try
            {
                while (_isDiscoveryResponderRunning && responder == _discoveryResponder)
                {
                    var result = await responder.ReceiveAsync();
                    string request = Encoding.UTF8.GetString(result.Buffer).Trim();
                    if (request == DiscoveryProbe)
                    {
                        string reply = $"{DiscoveryReplyPrefix}{EncodePayload(name)}|{port}|{EncodePayload(machine)}";
                        byte[] data = Encoding.UTF8.GetBytes(reply);
                        await responder.SendAsync(data, data.Length, result.RemoteEndPoint);
                    }
                }
            }
            catch { }
        }

        private void StopDiscoveryResponder()
        {
            _isDiscoveryResponderRunning = false;
            _discoveryResponder?.Close();
            _discoveryResponder = null;
        }

        private static async Task ConnectWithTimeoutAsync(TcpClient tcp, string host, int port, int ms)
        {
            var task = tcp.ConnectAsync(host, port);
            if (await Task.WhenAny(task, Task.Delay(ms)) != task)
            {
                tcp.Close();
                throw new TimeoutException();
            }
            await task;
        }

        private static string EncodePayload(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        private static string DecodePayload(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch { return string.Empty; }
        }

        private static bool TryParseDiscoveryReply(string payload, out string name, out int port)
        {
            name = string.Empty;
            port = 0;
            if (!payload.StartsWith(DiscoveryReplyPrefix)) return false;
            var parts = payload.Substring(DiscoveryReplyPrefix.Length).Split('|');
            if (parts.Length < 3 || !int.TryParse(parts[1], out port)) return false;
            name = DecodePayload(parts[0]);
            return true;
        }

        private static string SaveIncomingAttachment(string fileName, byte[] data)
        {
            string safe = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumaChatFiles");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, safe);
            int idx = 1;
            string nameOnly = Path.GetFileNameWithoutExtension(safe);
            string ext = Path.GetExtension(safe);
            while (File.Exists(path))
                path = Path.Combine(folder, $"{nameOnly}_{idx++}{ext}");
            File.WriteAllBytes(path, data);
            return path;
        }

        private static string SanitizeFileName(string name) =>
            new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

        private static string[] GetLocalIpAddresses()
        {
            try
            {
                return Dns.GetHostEntry(Dns.GetHostName())
                    .AddressList
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    .Select(a => a.ToString())
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private static string GetOwnNameFromSettings() => "Пользователь"; // будет переопределено из ViewModel
        #endregion
    }
}