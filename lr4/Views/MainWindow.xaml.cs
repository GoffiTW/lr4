// MainWindow.xaml.cs
using System.Windows;
using System.Windows.Input;
using LumaChat.Models;

namespace LumaChat.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void SelectContact_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Contact contact)
            {
                if (DataContext is ViewModels.MainViewModel vm)
                    vm.SelectedContact = contact;
            }
        }

        private void OpenFile_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Message msg && !string.IsNullOrEmpty(msg.FilePath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(msg.FilePath) { UseShellExecute = true });
                }
                catch { /* ignore */ }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.DisconnectCommand.Execute(null);
            base.OnClosed(e);
        }
    }
}