using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LumaChat.Services;
using LumaChat.ViewModels;

[assembly: InternalsVisibleTo("lr4.Tests")]

namespace LumaChat;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var history = new HistoryService();
        var chat = new ChatService(history);
        var presence = new PresenceService();
        var dialog = new DialogService();
        var translation = new TranslationService();

        _viewModel = new MainViewModel(
            chat, presence, history, dialog,
            dispatcher: action => Dispatcher.Invoke(action),
            translation: translation);

        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.Initialize();
        Closed += (_, _) => _viewModel.Shutdown();

        _viewModel.OnMessageAppended += () =>
            Dispatcher.BeginInvoke(() => MessagesScrollViewer.ScrollToBottom(),
                DispatcherPriority.Background);
    }

    private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
                _viewModel.SendCommand.Execute(null);
        }
    }

    private void ComposerBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        MessageTextBox.Focus();
        Keyboard.Focus(MessageTextBox);
    }
}
