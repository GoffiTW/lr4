using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Timer = System.Threading.Timer;
[assembly: InternalsVisibleTo("lr4.Tests")]

namespace LumaChat;

public partial class MainWindow : Window
{
    private readonly object networkLock = new();

    private TcpListener? listener;
    private TcpClient? client;
    private StreamReader? reader;
    private StreamWriter? writer;

    private UdpClient? discoveryResponder;
    private bool isDiscoveryResponderRunning;
    private bool isWaitingForClient;
    private bool isConnected;
    private bool useLumaProtocol;
    private bool sentHello;
    private string peerName = PeerDefault;

    private const int DiscoveryPort = 45545;
    private const int MaxAttachmentBytes = 5 * 1024 * 1024;
    private const int MaxHistoryEntries = 200;
    private const string DiscoveryProbe = "LUMA_TCP_CHAT_DISCOVER_V1";
    private const string DiscoveryReplyPrefix = "LUMA_TCP_CHAT_HOST_V1|";
    private const string PresencePrefix = "LUMA_PRESENCE|";
    private const string PeerDefault = "Собеседник";
    private const string DisconnectedText = "Не подключено";

    private ObservableCollection<ContactInfo> contacts;
    private ObservableCollection<ChatMessageViewModel> messages;
    private Timer? presenceTimer;
    private bool showNotifications = true;

    public MainWindow()
    {
        InitializeComponent();
        contacts = new ObservableCollection<ContactInfo>();
        ContactsPanel.ItemsSource = contacts;
        messages = new ObservableCollection<ChatMessageViewModel>();
        MessagesPanel.ItemsSource = messages;

        InitializeEvents();
        Loaded += MainWindow_Loaded;
        LoadHistory();
        InitializePresenceDiscovery();
        AddSystemMessage("На первом ПК нажмите \"Создать\". На втором ПК нажмите \"Обновить\" и выберите контакт, затем \"Войти\".");
    }

    private void InitializeEvents()
    {
        HostButton.Click += HostButton_Click;
        ConnectButton.Click += ConnectButton_Click;
        RefreshContactsButton.Click += (s, e) => _ = DiscoverHostsAsync();
        DisconnectButton.Click += DisconnectButton_Click;
        SendButton.Click += SendButton_Click;
        AttachButton.Click += AttachButton_Click;
        ClearHistoryButton.Click += ClearHistoryButton_Click;
        MessageTextBox.KeyDown += MessageTextBox_KeyDown;
        MessageTextBox.TextChanged += (s, e) => UpdateSendButton();

        Task.Run(() =>
        {
            Dispatcher.Invoke(() =>
            {
                LocalIpLabel.Text = $"Адрес этого ПК: {GetLocalIpText()}";
                PortTextBox.Text = "5000";
            });
        });
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateConnectionState(DisconnectedText, (SolidColorBrush)FindResource("AccentRed"), false, false);
    }

    #region Обнаружение контактов (Presence)
    private void InitializePresenceDiscovery()
    {
        Task.Run(() => ListenForPresenceAsync());
        presenceTimer = new Timer(_ => SendPresence(), null, 0, 5000);
    }

    private async Task ListenForPresenceAsync()
    {
        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        while (true)
        {
            try
            {
                var result = await udp.ReceiveAsync();
                string data = Encoding.UTF8.GetString(result.Buffer);
                if (data.StartsWith(PresencePrefix))
                {
                    var parts = data.Split('|');
                    if (parts.Length >= 4)
                    {
                        string name = DecodePayload(parts[1]);
                        string ip = result.RemoteEndPoint.Address.ToString();
                        int port = int.Parse(parts[2]);
                        Dispatcher.Invoke(() => UpdateContact(name, ip, port));
                    }
                }
            }
            catch { }
        }
    }

    private void SendPresence()
    {
        try
        {
            using var udp = new UdpClient();
            string presence = $"{PresencePrefix}{EncodePayload(GetOwnName())}|{PortTextBox.Text.Trim()}|{EncodePayload(Environment.MachineName)}";
            byte[] data = Encoding.UTF8.GetBytes(presence);
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        }
        catch { }
    }

    private void UpdateContact(string name, string ip, int port)
    {
        var existing = contacts.FirstOrDefault(c => c.IpAddress == ip);
        if (existing == null)
        {
            contacts.Add(new ContactInfo { Name = name, IpAddress = ip, Port = port });
            ContactsPlaceholder.Visibility = contacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            existing.Name = name;
            existing.Port = port;
        }
    }

    private void Contact_Click(object sender, MouseButtonEventArgs e)
    {
        var contact = (sender as FrameworkElement)?.DataContext as ContactInfo;
        if (contact != null)
        {
            IpTextBox.Text = contact.IpAddress;
            PortTextBox.Text = contact.Port.ToString();
            useLumaProtocol = true;
            peerName = contact.Name;
            AddSystemMessage($"Выбран контакт {contact.Name} ({contact.IpAddress}:{contact.Port}). Нажмите «Войти».");
        }
    }

    private async Task DiscoverHostsAsync()
    {
        contacts.Clear();
        ContactsPlaceholder.Visibility = Visibility.Collapsed;
        RefreshContactsButton.IsEnabled = false;

        int found = 0;
        HashSet<string> seen = new();

        try
        {
            using UdpClient searcher = new();
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
                if (TryParseDiscoveryReply(reply, out string name, out string machine, out int port))
                {
                    string ip = result.RemoteEndPoint.Address.ToString();
                    string key = $"{ip}:{port}";
                    if (seen.Add(key))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            contacts.Add(new ContactInfo { Name = name, IpAddress = ip, Port = port });
                            found++;
                        });
                    }
                }
            }
        }
        catch { }

        Dispatcher.Invoke(() =>
        {
            RefreshContactsButton.IsEnabled = true;
            ContactsPlaceholder.Visibility = contacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (found == 0)
                AddSystemMessage("Контакты не найдены. Убедитесь, что на другом ПК нажали «Создать».");
            else
                AddSystemMessage($"Найдено {found} контактов. Нажмите на контакт и затем «Войти».");
        });
    }
    #endregion

    #region Уведомления через Popup
    private void ShowNotification(string author, string message)
    {
        if (!showNotifications) return;
        Dispatcher.Invoke(() =>
        {
            SystemSounds.Asterisk.Play();

            var popup = new Popup
            {
                Placement = PlacementMode.Mouse,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(240, 28, 151, 132)),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12, 6, 12, 6),
                    Child = new TextBlock
                    {
                        Text = $"{author}: {(message.Length > 60 ? message.Substring(0, 57) + "..." : message)}",
                        Foreground = Brushes.White,
                        FontSize = 12,
                        MaxWidth = 300,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            };
            popup.IsOpen = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, e) => { popup.IsOpen = false; timer.Stop(); };
            timer.Start();
        });
    }
    #endregion

    #region Сетевые методы (основные)
    private async void HostButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(out int port)) return;
        StopNetworking();
        useLumaProtocol = false;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            StartDiscoveryResponder(port);
            isWaitingForClient = true;
            isConnected = false;
            peerName = PeerDefault;
            UpdateConnectionState("Ждём клиента", (SolidColorBrush)FindResource("AccentOrange"), true, false);
            AddSystemMessage($"Комната открыта. Другой ПК найдёт вас автоматически через кнопку «Обновить».");
            TcpClient accepted = await listener.AcceptTcpClientAsync();
            ConfigureConnectedClient(accepted, true);
        }
        catch (Exception ex) { AddSystemMessage($"Ошибка: {ex.Message}"); StopNetworking(); UpdateConnectionState(DisconnectedText, (SolidColorBrush)FindResource("AccentRed"), false, false); }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadPort(out int port)) return;
        string host = IpTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(host)) { AddSystemMessage("Выберите контакт или введите IP."); return; }
        bool selectedProto = useLumaProtocol;
        StopNetworking();
        useLumaProtocol = selectedProto;
        UpdateConnectionState("Подключаемся", (SolidColorBrush)FindResource("AccentOrange"), true, false);
        try
        {
            TcpClient connecting = new();
            await ConnectWithTimeoutAsync(connecting, host, port, 6000);
            ConfigureConnectedClient(connecting, false);
            if (useLumaProtocol) _ = SendHelloAsync();
        }
        catch (Exception ex) { StopNetworking(); AddSystemMessage($"Подключение не удалось: {ex.Message}"); UpdateConnectionState(DisconnectedText, (SolidColorBrush)FindResource("AccentRed"), false, false); }
    }

    private void ConfigureConnectedClient(TcpClient connected, bool accepted)
    {
        lock (networkLock)
        {
            StopDiscoveryResponder();
            listener?.Stop(); listener = null;
            client = connected;
            client.NoDelay = true;
            var stream = client.GetStream();
            reader = new StreamReader(stream, new UTF8Encoding(false));
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            isWaitingForClient = false;
            isConnected = true;
        }
        AddSystemMessage(accepted ? "Собеседник подключился. Можно писать." : "Связь установлена.");
        UpdateConnectionState("Подключено", (SolidColorBrush)FindResource("AccentMint"), true, true);
        _ = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (isConnected && reader != null)
            {
                string? line = await reader.ReadLineAsync();
                if (line == null) break;
                HandleProtocolLine(line);
            }
            if (isConnected) HandleRemoteDisconnect("Собеседник отключился.");
        }
        catch (IOException) { if (isConnected) HandleRemoteDisconnect("Связь потеряна."); }
        catch { }
    }

    private void HandleProtocolLine(string line)
    {
        if (line.StartsWith("HELLO|"))
        {
            string name = DecodePayload(line.Substring(6)).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                useLumaProtocol = true;
                peerName = name;
                Dispatcher.Invoke(() => AddSystemMessage($"В чате: {peerName}."));
                if (!sentHello) _ = SendHelloAsync();
            }
            return;
        }
        if (line.StartsWith("MSG|"))
        {
            string msg = DecodePayload(line.Substring(4));
            Dispatcher.Invoke(() => AddMessage(peerName, msg, false));
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
                    Dispatcher.Invoke(() => AddFileMessage(peerName, fileName, savedPath, false));
                }
                catch { Dispatcher.Invoke(() => AddSystemMessage("Получен повреждённый файл.")); }
            }
            return;
        }
        Dispatcher.Invoke(() => AddMessage(peerName, line, false));
    }

    private async Task SendHelloAsync()
    {
        if (writer == null) return;
        try { await writer.WriteLineAsync($"HELLO|{EncodePayload(GetOwnName())}"); sentHello = true; }
        catch { HandleRemoteDisconnect("Не удалось отправить имя."); }
    }

    private void HandleRemoteDisconnect(string msg)
    {
        StopNetworking();
        Dispatcher.Invoke(() => { UpdateConnectionState(DisconnectedText, (SolidColorBrush)FindResource("AccentRed"), false, false); AddSystemMessage(msg); });
    }

    private void StartDiscoveryResponder(int port)
    {
        StopDiscoveryResponder();
        try
        {
            var responder = new UdpClient();
            responder.ExclusiveAddressUse = false;
            responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            responder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            discoveryResponder = responder;
            isDiscoveryResponderRunning = true;
            _ = DiscoveryResponderLoopAsync(responder, port, GetOwnName(), Environment.MachineName);
        }
        catch (Exception ex) { AddSystemMessage($"Авто-поиск недоступен: {ex.Message}"); }
    }

    private async Task DiscoveryResponderLoopAsync(UdpClient responder, int port, string name, string machine)
    {
        try
        {
            while (isDiscoveryResponderRunning && responder == discoveryResponder)
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
        isDiscoveryResponderRunning = false;
        discoveryResponder?.Close();
        discoveryResponder = null;
    }

    private void StopNetworking()
    {
        lock (networkLock)
        {
            StopDiscoveryResponder();
            isWaitingForClient = false;
            isConnected = false;
            useLumaProtocol = false;
            sentHello = false;
            listener?.Stop(); listener = null;
            reader?.Dispose(); reader = null;
            writer?.Dispose(); writer = null;
            client?.Close(); client = null;
        }
    }
    #endregion

    #region Отправка сообщений и файлов с прогрессом
    private async Task SendCurrentMessageAsync()
    {
        string text = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !isConnected || writer == null)
        {
            if (!isConnected) AddSystemMessage("Сначала подключитесь.");
            return;
        }
        try
        {
            if (useLumaProtocol) await writer.WriteLineAsync($"MSG|{EncodePayload(text)}");
            else await writer.WriteLineAsync(text.Replace("\r\n", " / ").Replace("\n", " / "));
            AddMessage(GetOwnName(), text, true);
            MessageTextBox.Clear();
        }
        catch { HandleRemoteDisconnect("Ошибка отправки."); }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendCurrentMessageAsync();

    private async void AttachButton_Click(object sender, RoutedEventArgs e) => await SendAttachmentWithProgressAsync();

    private async Task SendAttachmentWithProgressAsync()
    {
        if (!isConnected || writer == null) { AddSystemMessage("Подключитесь сначала."); return; }
        if (!useLumaProtocol) { AddSystemMessage("Вложения только в Luma-режиме."); return; }
        var dialog = new OpenFileDialog { Title = "Выберите файл", Filter = "Все файлы|*.*" };
        if (dialog.ShowDialog() != true) return;
        var fi = new FileInfo(dialog.FileName);
        if (fi.Length > MaxAttachmentBytes) { AddSystemMessage("Файл > 5 МБ."); return; }

        var progressMsg = new ChatMessageViewModel
        {
            Author = GetOwnName(),
            Message = $"Отправка: {fi.Name}",
            Time = DateTime.Now.ToString("HH:mm"),
            Own = true,
            IsFileTransfer = true,
            FileName = fi.Name,
            Progress = 0,
            ChipVisibility = Visibility.Collapsed,
            ProgressVisibility = Visibility.Visible
        };
        Dispatcher.Invoke(() => messages.Add(progressMsg));

        try
        {
            var progress = new Progress<float>(p => Dispatcher.Invoke(() => progressMsg.Progress = p));
            await SendFileWithProgressAsync(fi.FullName, progress);
            progressMsg.Message = $"Файл отправлен: {fi.Name}";
            progressMsg.IsFileTransfer = false;
            progressMsg.ProgressVisibility = Visibility.Collapsed;
            progressMsg.ChipVisibility = Visibility.Visible;
            progressMsg.ChipText = $"📎 Открыть: {fi.Name}";
            progressMsg.FilePath = fi.FullName;
        }
        catch (Exception ex)
        {
            progressMsg.Message = $"Ошибка: {ex.Message}";
            progressMsg.IsFileTransfer = false;
            progressMsg.ProgressVisibility = Visibility.Collapsed;
        }
    }

    private async Task SendFileWithProgressAsync(string path, IProgress<float> progress)
    {
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
            progress.Report((float)sent / total);
        }
        string base64 = Convert.ToBase64String(ms.ToArray());
        await writer!.WriteLineAsync($"FILE|{EncodePayload(Path.GetFileName(path))}|{base64}");
    }

    private void AddMessage(string author, string text, bool own, bool system = false)
    {
        Dispatcher.Invoke(() =>
        {
            var vm = new ChatMessageViewModel
            {
                Author = author,
                Message = text,
                Time = DateTime.Now.ToString("HH:mm"),
                Own = own,
                System = system,
                Background = GetBubbleBrush(own, system),
                StripColor = GetStripColor(own),
                Margin = new Thickness(own ? 60 : 12, 4, own ? 12 : 60, 8)
            };
            messages.Add(vm);
            if (!own && !system) ShowNotification(author, text);
            if (!system) AppendHistoryRecord("TEXT", author, text, own, "", "", false);
            ScrollToBottom();
        });
    }

    private void AddFileMessage(string author, string fileName, string filePath, bool own)
    {
        Dispatcher.Invoke(() =>
        {
            var vm = new ChatMessageViewModel
            {
                Author = author,
                Message = $"Файл: {fileName}",
                Time = DateTime.Now.ToString("HH:mm"),
                Own = own,
                IsFileTransfer = false,
                FileName = fileName,
                FilePath = filePath,
                ChipText = $"📎 Открыть: {fileName}",
                ChipVisibility = Visibility.Visible,
                ProgressVisibility = Visibility.Collapsed,
                Background = GetBubbleBrush(own, false),
                StripColor = GetStripColor(own),
                Margin = new Thickness(own ? 60 : 12, 4, own ? 12 : 60, 8)
            };
            messages.Add(vm);
            AppendHistoryRecord("FILE", author, $"Файл: {fileName}", own, fileName, filePath, IsImageFile(filePath));
            ScrollToBottom();
            if (!own) ShowNotification(author, $"Файл: {fileName}");
        });
    }

    private void AddSystemMessage(string msg) => AddMessage("Система", msg, false, true);

    private SolidColorBrush GetBubbleBrush(bool own, bool system)
    {
        if (system) return new SolidColorBrush(Color.FromRgb(38, 55, 78));
        return own ? new SolidColorBrush(Color.FromRgb(28, 151, 132)) : new SolidColorBrush(Color.FromRgb(25, 39, 64));
    }

    private SolidColorBrush GetStripColor(bool own) => own ? new SolidColorBrush(Color.FromRgb(143, 255, 226)) : new SolidColorBrush(Color.FromRgb(117, 165, 255));

    private void ScrollToBottom() => Dispatcher.BeginInvoke(() => MessagesScrollViewer.ScrollToBottom(), DispatcherPriority.Background);

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e) { messages.Clear(); DeleteHistory(); AddSystemMessage("История очищена."); }

    private void MessageTextBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { e.Handled = true; _ = SendCurrentMessageAsync(); } }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) { StopNetworking(); UpdateConnectionState(DisconnectedText, (SolidColorBrush)FindResource("AccentRed"), false, false); AddSystemMessage("Соединение завершено."); }
    #endregion

    #region Вспомогательные методы
    private bool TryReadPort(out int port) => int.TryParse(PortTextBox.Text.Trim(), out port) && port > 0 && port <= 65535;
    private string GetOwnName() => string.IsNullOrWhiteSpace(NameTextBox.Text) ? "Пользователь" : NameTextBox.Text.Trim();
    private string EncodePayload(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private string DecodePayload(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }
    private async Task ConnectWithTimeoutAsync(TcpClient tcp, string host, int port, int ms)
    {
        var task = tcp.ConnectAsync(host, port);
        if (await Task.WhenAny(task, Task.Delay(ms)) != task) { tcp.Close(); throw new TimeoutException(); }
        await task;
    }
    private bool TryParseDiscoveryReply(string payload, out string name, out string machine, out int port)
    {
        name = machine = ""; port = 0;
        if (!payload.StartsWith(DiscoveryReplyPrefix)) return false;
        var parts = payload.Substring(DiscoveryReplyPrefix.Length).Split('|');
        if (parts.Length < 3 || !int.TryParse(parts[1], out port)) return false;
        name = DecodePayload(parts[0]);
        machine = DecodePayload(parts[2]);
        return true;
    }
    private void UpdateConnectionState(string text, SolidColorBrush color, bool busy, bool conn)
    {
        StatusLabel.Text = text; StatusDot.Foreground = color;
        HeroStatusLabel.Text = conn ? (useLumaProtocol ? "Канал активен / Luma" : "Канал активен / TCP") : busy ? "Ожидание связи" : "Готов к старту";
        HostButton.IsEnabled = !busy || isWaitingForClient;
        ConnectButton.IsEnabled = !busy;
        RefreshContactsButton.IsEnabled = !busy;
        DisconnectButton.IsEnabled = busy || conn;
        MessageTextBox.IsEnabled = conn;
        AttachButton.IsEnabled = conn;
        SendButton.IsEnabled = conn && !string.IsNullOrWhiteSpace(MessageTextBox.Text);
    }
    private void UpdateSendButton() => SendButton.IsEnabled = isConnected && !string.IsNullOrWhiteSpace(MessageTextBox.Text);
    #endregion

    #region Локальное хранение
    private string SaveIncomingAttachment(string fileName, byte[] data)
    {
        string safe = SanitizeFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumaChatFiles");
        Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, safe);
        int idx = 1;
        string nameOnly = System.IO.Path.GetFileNameWithoutExtension(safe);
        string ext = System.IO.Path.GetExtension(safe);
        while (File.Exists(path)) path = System.IO.Path.Combine(folder, $"{nameOnly}_{idx++}{ext}");
        File.WriteAllBytes(path, data);
        return path;
    }
    private string SanitizeFileName(string name) => new string(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
    private bool IsImageFile(string path) => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" }.Contains(System.IO.Path.GetExtension(path).ToLower());
    private void LoadHistory()
    {
        string path = GetHistoryFilePath();
        if (!File.Exists(path)) return;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var parts = line.Split('|', 7);
            if (parts.Length < 7) continue;
            string kind = parts[0], author = DecodePayload(parts[1]), msg = DecodePayload(parts[2]);
            bool own = parts[3] == "1";
            string fname = DecodePayload(parts[4]), fpath = DecodePayload(parts[5]);
            if (kind == "FILE") AddFileMessage(author, fname, fpath, own);
            else AddMessage(author, msg, own, false);
        }
    }
    private void AppendHistoryRecord(string kind, string author, string msg, bool own, string fname, string fpath, bool isImg)
    {
        try
        {
            string path = GetHistoryFilePath();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            string line = string.Join("|", kind, EncodePayload(author), EncodePayload(msg), own ? "1" : "0", EncodePayload(fname), EncodePayload(fpath), isImg ? "1" : "0");
            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            TrimHistory(path);
        }
        catch { }
    }
    private void TrimHistory(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length > MaxHistoryEntries) File.WriteAllLines(path, lines.Skip(lines.Length - MaxHistoryEntries).ToArray(), Encoding.UTF8);
    }
    private void DeleteHistory() { try { if (File.Exists(GetHistoryFilePath())) File.Delete(GetHistoryFilePath()); } catch { } }
    private string GetHistoryFilePath() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LumaChat", "history.log");
    #endregion

    #region Сетевые помощники
    private string GetLocalIpText() => GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1";
    private string GetLocalIpHelpText() => string.Join(" или ", GetLocalIpAddresses());
    private string[] GetLocalIpAddresses()
    {
        try { return Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
    private string[] GetLocalSubnetTargets()
    {
        var set = new HashSet<string>();
        foreach (var ip in GetLocalIpAddresses().Where(IsLikelyLanIp))
        {
            var parts = ip.Split('.');
            if (parts.Length != 4) continue;
            string prefix = $"{parts[0]}.{parts[1]}.{parts[2]}.";
            for (int i = 1; i <= 254; i++) set.Add($"{prefix}{i}");
        }
        return set.Take(512).ToArray();
    }
    private static bool IsLikelyLanIp(string ip) => ip.StartsWith("10.") || ip.StartsWith("192.168.") || (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out int b) && b >= 16 && b <= 31);
    #endregion

    protected override void OnClosed(EventArgs e) { presenceTimer?.Dispose(); StopNetworking(); base.OnClosed(e); }
}

#region Модели данных
public class ContactInfo : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int Port { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "") { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
}

public class ChatMessageViewModel : INotifyPropertyChanged
{
    public string Author { get; set; } = "";
    public string Message { get; set; } = "";
    public string Time { get; set; } = "";
    public bool Own { get; set; }
    public bool System { get; set; }
    public bool IsFileTransfer { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    private double _progress;
    public double Progress { get => _progress; set { _progress = value; OnPropertyChanged(); } }
    public Visibility ProgressVisibility { get; set; } = Visibility.Collapsed;
    public Visibility ChipVisibility { get; set; } = Visibility.Collapsed;
    public string ChipText { get; set; } = "";
    public SolidColorBrush Background { get; set; } = Brushes.Transparent;
    public SolidColorBrush StripColor { get; set; } = Brushes.Transparent;
    public Thickness Margin { get; set; } = new Thickness(12, 4, 60, 8);
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
#endregion