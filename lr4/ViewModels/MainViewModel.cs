using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LumaChat.Models;
using LumaChat.Services;

namespace LumaChat.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IChatService _chat;
    private readonly IPresenceService _presence;
    private readonly IHistoryService _history;
    private readonly IDialogService _dialog;
    private readonly ITranslationService? _translation;
    private readonly Action<Action> _dispatch;

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<ContactInfo> Contacts { get; } = new();

    [ObservableProperty]
    private string ipAddress = "127.0.0.1";

    [ObservableProperty]
    private string port = "5000";

    [ObservableProperty]
    private string userName = "Пользователь";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(HeroStatusText))]
    [NotifyCanExecuteChangedFor(nameof(HostCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(AttachCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshContactsCommand))]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string statusText = "Не подключено";

    [ObservableProperty]
    private string localIpText = "Адрес этого ПК: получение...";

    [ObservableProperty]
    private bool contactsPlaceholderVisible = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string composedMessage = string.Empty;

    public bool IsBusy => ConnectionState is ConnectionState.WaitingForClient or ConnectionState.Connecting;
    public bool IsConnected => ConnectionState == ConnectionState.Connected;

    public Brush StatusBrush => ConnectionState switch
    {
        ConnectionState.Connected => new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB0)),
        ConnectionState.WaitingForClient or ConnectionState.Connecting => new SolidColorBrush(Color.FromRgb(0xFF, 0xBE, 0x58)),
        _ => new SolidColorBrush(Color.FromRgb(0xFF, 0x68, 0x79))
    };

    public string HeroStatusText => ConnectionState switch
    {
        ConnectionState.Connected => _chat.UseLumaProtocol ? "Канал активен / Luma" : "Канал активен / TCP",
        ConnectionState.WaitingForClient or ConnectionState.Connecting => "Ожидание связи",
        _ => "Готов к старту"
    };

    public MainViewModel(
        IChatService chat,
        IPresenceService presence,
        IHistoryService history,
        IDialogService dialog,
        Action<Action>? dispatcher = null,
        ITranslationService? translation = null)
    {
        _chat = chat;
        _presence = presence;
        _history = history;
        _dialog = dialog;
        _translation = translation;
        _dispatch = dispatcher ?? (a => a());

        _chat.StateChanged += (_, state) => _dispatch(() =>
        {
            ConnectionState = state;
            StatusText = state switch
            {
                ConnectionState.Connected => "Подключено",
                ConnectionState.WaitingForClient => "Ждём клиента",
                ConnectionState.Connecting => "Подключаемся",
                _ => "Не подключено"
            };
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(HeroStatusText));
            OnPropertyChanged(nameof(StatusBrush));
            SendCommand.NotifyCanExecuteChanged();
            AttachCommand.NotifyCanExecuteChanged();
            HostCommand.NotifyCanExecuteChanged();
            ConnectCommand.NotifyCanExecuteChanged();
            DisconnectCommand.NotifyCanExecuteChanged();
            RefreshContactsCommand.NotifyCanExecuteChanged();
        });
        _chat.SystemMessage += (_, msg) => _dispatch(() => AddSystemMessage(msg));
        _chat.TextReceived += (_, args) => _dispatch(() => AddMessage(args.Author, args.Text, false));
        _chat.FileReceived += (_, args) => _dispatch(() => AddFileMessage(args.Author, args.FileName, args.SavedPath, false));

        _presence.ContactDiscovered += (_, contact) => _dispatch(() => UpsertContact(contact));
    }

    public void Initialize()
    {
        LocalIpText = $"Адрес этого ПК: {ChatUtils.GetLocalIpAddresses().FirstOrDefault() ?? "127.0.0.1"}";
        LoadHistory();
        AddSystemMessage("На первом ПК нажмите «Создать». На втором — введите IP вручную или выберите контакт и нажмите «Войти».");

        _presence.Start(
            getPort: () => int.TryParse(Port, out int p) ? p : 5000,
            getName: () => string.IsNullOrWhiteSpace(UserName) ? "Пользователь" : UserName.Trim());
    }

    public void Shutdown()
    {
        _presence.Stop();
        _chat.Disconnect();
    }

    private bool CanHost() => ConnectionState == ConnectionState.Disconnected;
    private bool CanConnect() => ConnectionState == ConnectionState.Disconnected && !string.IsNullOrWhiteSpace(IpAddress);
    private bool CanDisconnect() => ConnectionState != ConnectionState.Disconnected;
    private bool CanSend() => IsConnected && !string.IsNullOrWhiteSpace(ComposedMessage);
    private bool CanAttach() => IsConnected;
    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanHost))]
    private async Task HostAsync()
    {
        if (!ChatUtils.TryReadPort(Port, out int port))
        {
            AddSystemMessage("Некорректный порт.");
            return;
        }
        string name = GetOwnName();
        _presence.StartHostResponder(port, name, Environment.MachineName);
        await _chat.HostAsync(port, name);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (!ChatUtils.TryReadPort(Port, out int port))
        {
            AddSystemMessage("Некорректный порт.");
            return;
        }
        string host = (IpAddress ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            AddSystemMessage("Введите IP другого ПК или выберите контакт.");
            return;
        }
        await _chat.ConnectAsync(host, port, GetOwnName());
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        _presence.StopHostResponder();
        _chat.Disconnect();
        AddSystemMessage("Соединение завершено.");
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string text = (ComposedMessage ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!IsConnected)
        {
            AddSystemMessage("Сначала подключитесь.");
            return;
        }
        try
        {
            await _chat.SendTextAsync(text);
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Ошибка отправки: {ex.Message}");
            return;
        }
        if (_chat.State != ConnectionState.Connected)
        {
            AddSystemMessage("Соединение прервано — сообщение не доставлено.");
            return;
        }
        AddMessage(GetOwnName(), text, true);
        ComposedMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanAttach))]
    private async Task AttachAsync()
    {
        string? path = _dialog.PickFileToSend();
        if (string.IsNullOrWhiteSpace(path)) return;
        var fi = new FileInfo(path);

        var progressMsg = new ChatMessage
        {
            Author = GetOwnName(),
            Message = $"Отправка: {fi.Name}",
            Time = DateTime.Now.ToString("HH:mm"),
            Own = true,
            IsFileTransfer = true,
            FileName = fi.Name,
            ProgressVisibility = Visibility.Visible,
            ChipVisibility = Visibility.Collapsed,
            Background = GetBubbleBrush(true, false),
            StripColor = GetStripColor(true),
            Margin = new Thickness(60, 4, 12, 8)
        };
        Messages.Add(progressMsg);

        try
        {
            var progress = new Progress<float>(p => _dispatch(() => progressMsg.Progress = p));
            await _chat.SendFileAsync(path, progress);
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

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshContactsAsync()
    {
        Contacts.Clear();
        ContactsPlaceholderVisible = false;
        int found = await _presence.ProbeOnceAsync();
        ContactsPlaceholderVisible = Contacts.Count == 0;
        AddSystemMessage(found == 0
            ? "Контакты не найдены. Введите IP вручную или попросите собеседника нажать «Создать»."
            : $"Найдено контактов: {found}. Нажмите на контакт, затем «Войти».");
    }

    [RelayCommand]
    private void ClearHistory()
    {
        Messages.Clear();
        _history.Clear();
        AddSystemMessage("История очищена.");
    }

    [RelayCommand]
    private async Task ToggleTranslateAsync(ChatMessage? msg)
    {
        if (msg == null || msg.System) return;
        if (msg.IsTranslated)
        {
            msg.Message = msg.OriginalMessage;
            msg.IsTranslated = false;
            return;
        }
        if (_translation == null)
        {
            AddSystemMessage("Сервис перевода недоступен.");
            return;
        }
        if (msg.IsTranslating) return;
        msg.IsTranslating = true;
        try
        {
            string translated = await _translation.TranslateAsync(msg.OriginalMessage);
            if (!string.IsNullOrEmpty(translated) && translated != msg.OriginalMessage)
            {
                msg.Message = translated;
                msg.IsTranslated = true;
            }
            else
            {
                AddSystemMessage("Перевод не получен (возможно, текст уже на русском или нет сети).");
            }
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Ошибка перевода: {ex.Message}");
        }
        finally
        {
            msg.IsTranslating = false;
        }
    }

    [RelayCommand]
    private void OpenFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            AddSystemMessage("Файл не найден.");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Не удалось открыть файл: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SelectContact(ContactInfo? contact)
    {
        if (contact == null) return;
        IpAddress = contact.IpAddress;
        Port = contact.Port.ToString();
        AddSystemMessage($"Выбран контакт {contact.Name} ({contact.IpAddress}:{contact.Port}). Нажмите «Войти».");
    }

    private void UpsertContact(ContactInfo contact)
    {
        var existing = Contacts.FirstOrDefault(c => c.IpAddress == contact.IpAddress);
        if (existing == null)
        {
            Contacts.Add(contact);
        }
        else
        {
            existing.Name = contact.Name;
            existing.Port = contact.Port;
        }
        ContactsPlaceholderVisible = Contacts.Count == 0;
    }

    private string GetOwnName() => string.IsNullOrWhiteSpace(UserName) ? "Пользователь" : UserName.Trim();

    private void AddSystemMessage(string text) => AddMessage("Система", text, false, true);

    private void AddMessage(string author, string text, bool own, bool system = false)
    {
        var msg = new ChatMessage
        {
            Author = author,
            Message = text,
            OriginalMessage = text,
            CanTranslate = !system && _translation != null,
            Time = DateTime.Now.ToString("HH:mm"),
            Own = own,
            System = system,
            Background = GetBubbleBrush(own, system),
            StripColor = GetStripColor(own),
            Margin = new Thickness(own ? 60 : 12, 4, own ? 12 : 60, 8)
        };
        Messages.Add(msg);
        if (!system)
        {
            _history.Append(new HistoryEntry
            {
                Kind = "TEXT",
                Author = author,
                Message = text,
                Own = own
            });
        }
        OnMessageAppended?.Invoke();
    }

    private void AddFileMessage(string author, string fileName, string filePath, bool own)
    {
        var msg = new ChatMessage
        {
            Author = author,
            Message = $"Файл: {fileName}",
            Time = DateTime.Now.ToString("HH:mm"),
            Own = own,
            FileName = fileName,
            FilePath = filePath,
            ChipText = $"📎 Открыть: {fileName}",
            ChipVisibility = Visibility.Visible,
            ProgressVisibility = Visibility.Collapsed,
            Background = GetBubbleBrush(own, false),
            StripColor = GetStripColor(own),
            Margin = new Thickness(own ? 60 : 12, 4, own ? 12 : 60, 8)
        };
        Messages.Add(msg);
        _history.Append(new HistoryEntry
        {
            Kind = "FILE",
            Author = author,
            Message = $"Файл: {fileName}",
            Own = own,
            FileName = fileName,
            FilePath = filePath,
            IsImage = IsImageFile(filePath)
        });
        OnMessageAppended?.Invoke();
    }

    public event Action? OnMessageAppended;

    private void LoadHistory()
    {
        foreach (var entry in _history.Load())
        {
            if (entry.Kind == "FILE")
                AddFileMessage(entry.Author, entry.FileName, entry.FilePath, entry.Own);
            else
                AddMessage(entry.Author, entry.Message, entry.Own);
        }
    }

    private static SolidColorBrush GetBubbleBrush(bool own, bool system)
    {
        if (system) return new SolidColorBrush(Color.FromRgb(38, 55, 78));
        return own
            ? new SolidColorBrush(Color.FromRgb(28, 151, 132))
            : new SolidColorBrush(Color.FromRgb(25, 39, 64));
    }

    private static SolidColorBrush GetStripColor(bool own) => own
        ? new SolidColorBrush(Color.FromRgb(143, 255, 226))
        : new SolidColorBrush(Color.FromRgb(117, 165, 255));

    private static bool IsImageFile(string path) => new[]
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    }.Contains(Path.GetExtension(path).ToLower());
}
