using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using LumaChat.Services;
using LumaChat.ViewModels;
using LumaChat.Views;

namespace LumaChat
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            var services = new ServiceCollection();
            services.AddSingleton<INetworkService, NetworkService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IStorageService, StorageService>();
            services.AddTransient<MainViewModel>();

            Services = services.BuildServiceProvider();

            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            mainWindow.Show();

            base.OnStartup(e);
        }
    }
}