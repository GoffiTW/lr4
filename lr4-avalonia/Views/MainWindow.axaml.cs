using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using LumaChat.Models;
using LumaChat.ViewModels;

namespace LumaChat.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var history = new Services.HistoryService();
        var chat = new Services.ChatService(history);
        var presence = new Services.PresenceService();
        var dialog = new Services.DialogService();
        var translation = new Services.TranslationService();

        _viewModel = new MainViewModel(
            chat, presence, history, dialog,
            dispatcher: action => Dispatcher.UIThread.Post(action),
            translation: translation);

        DataContext = _viewModel;

        Opened += (_, _) => _viewModel.Initialize();
        Closed += (_, _) => _viewModel.Shutdown();

        _viewModel.OnMessageAppended += () =>
            Dispatcher.UIThread.Post(() => MessagesScrollViewer.ScrollToEnd(),
                priority: Avalonia.Threading.DispatcherPriority.Background);
    }

    private void MessageTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
                _viewModel.SendCommand.Execute(null);
        }
    }

    private void OnContactPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is ContactInfo contact)
        {
            _viewModel.SelectContactCommand.Execute(contact);
        }
    }

    private void OnFileChipPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string filePath)
        {
            _viewModel.OpenFileCommand.Execute(filePath);
        }
    }
}
