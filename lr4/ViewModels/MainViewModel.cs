// ViewModels/MainViewModel.cs
using lr4;
using LumaChat.Commands;
using LumaChat.Models;
using LumaChat.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.IO;
using LumaChat.Services;

namespace LumaChat.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly INetworkService _networkService;
        private readonly IDialogService _dialogService;
        private readonly IStorageService _storageService;

        public MainViewModel(INetworkService networkService, IDialogService dialogService, IStorageService storageService)
        {
            _networkService = networkService;
            _dialogService = dialogService;
            _storageService = storageService;

            // Подписка на события
            _networkService.MessageReceived += OnMessageReceived;
            _networkService.ConnectionStatusChanged += OnConnectionStatusChanged;
            _networkService.ContactDiscovered += OnContactDiscovered;

            // Команды
            HostCommand = new RelayCommand(_ => HostAsync(), _ => !IsBusy);
            ConnectCommand = new RelayCommand(_ => ConnectAsync(), _ => !IsBusy && SelectedContact != null);
            SendMessageCommand = new RelayCommand(_ => SendMessageAsync(), _ => IsConnected && !string.IsNullOrWhiteSpace(NewMessage));
            AttachFileCommand = new RelayCommand(_ => AttachFileAsync(), _ => IsConnected);
            RefreshContactsCommand = new RelayCommand(_ => RefreshContactsAsync(), _ => !IsBusy);
            DisconnectCommand = new RelayCommand(_ => DisconnectAsync(), _ => IsConnected);
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());

            // Загрузка сохранённой истории
            LoadHistory();

            // Получение локального IP
            LocalIp = _networkService.LocalIp ?? "неизвестно";
        }

        // Свойства
        private string _userName = "Пользователь";
        public string UserName
        {
            get => _userName;
            set { _userName = value; OnPropertyChanged(); }
        }

        private string _localIp;
        public string LocalIp
        {
            get => _localIp;
            set { _localIp = value; OnPropertyChanged(); }
        }

        private int _port = 5000;
        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        private string _statusText = "Не подключено";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        private string _newMessage = "";
        public string NewMessage
        {
            get => _newMessage;
            set { _newMessage = value; OnPropertyChanged(); }
        }

        private Contact? _selectedContact;
        public Contact? SelectedContact
        {
            get => _selectedContact;
            set { _selectedContact = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Contact> Contacts { get; } = new();
        public ObservableCollection<Message> Messages { get; } = new();

        // Команды
        public ICommand HostCommand { get; }
        public ICommand ConnectCommand { get; }
        public ICommand SendMessageCommand { get; }
        public ICommand AttachFileCommand { get; }
        public ICommand RefreshContactsCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ClearHistoryCommand { get; }

        private async void HostAsync()
        {
            IsBusy = true;
            try
            {
                await _networkService.StartHostAsync(Port, UserName);
                StatusText = "Ожидание клиента...";
            }
            catch (Exception ex)
            {
                _dialogService.ShowNotification("Ошибка", ex.Message);
                StatusText = "Не подключено";
            }
            finally { IsBusy = false; }
        }

        private async void ConnectAsync()
        {
            if (SelectedContact == null) return;
            IsBusy = true;
            try
            {
                await _networkService.ConnectToHostAsync(SelectedContact.IpAddress, SelectedContact.Port, UserName);
            }
            catch (Exception ex)
            {
                _dialogService.ShowNotification("Ошибка", ex.Message);
            }
            finally { IsBusy = false; }
        }

        private async void SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMessage)) return;
            await _networkService.SendMessageAsync(NewMessage);
            NewMessage = string.Empty;
        }

        private async void AttachFileAsync()
        {
            var filePath = await _dialogService.ShowOpenFileDialogAsync();
            if (filePath == null) return;

            // Создаём временное сообщение для отображения прогресса
            var progressMsg = new Message
            {
                Author = UserName,
                Text = $"Отправка: {Path.GetFileName(filePath)}",
                IsOwn = true,
                TransferProgress = 0
            };
            Messages.Add(progressMsg);

            var progress = new Progress<double>(p =>
            {
                progressMsg.TransferProgress = p;
                if (p >= 1.0)
                {
                    progressMsg.Text = $"Файл отправлен: {Path.GetFileName(filePath)}";
                    progressMsg.FileName = Path.GetFileName(filePath);
                    progressMsg.FilePath = filePath;
                }
            });

            try
            {
                await _networkService.SendFileAsync(filePath, progress);
            }
            catch (Exception ex)
            {
                _dialogService.ShowNotification("Ошибка", ex.Message);
                Messages.Remove(progressMsg);
            }
        }

        private async void RefreshContactsAsync()
        {
            IsBusy = true;
            Contacts.Clear();
            await _networkService.DiscoverHostsAsync();
            IsBusy = false;
        }

        private async void DisconnectAsync()
        {
            await _networkService.StopAsync();
        }

        private void ClearHistory()
        {
            Messages.Clear();
            _storageService.ClearHistory();
        }

        private void OnMessageReceived(Message msg)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                // Если сообщение от нас, но пришло из сервиса, скорректируем автора
                if (msg.IsOwn) msg.Author = UserName;
                Messages.Add(msg);
                if (!msg.IsOwn && !msg.IsSystem)
                    _dialogService.ShowNotification(msg.Author, msg.Text);
                _storageService.AppendMessage(msg);
            });
        }

        private void OnConnectionStatusChanged(bool connected)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = connected;
                StatusText = connected ? "Подключено" : "Не подключено";
                IsBusy = false;
                if (!connected)
                    _networkService.DiscoverHostsAsync(); // обновим контакты
            });
        }

        private void OnContactDiscovered(Contact contact)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                if (!Contacts.Any(c => c.IpAddress == contact.IpAddress))
                    Contacts.Add(contact);
            });
        }

        private void LoadHistory()
        {
            var saved = _storageService.LoadMessages();
            foreach (var msg in saved)
                Messages.Add(msg);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}