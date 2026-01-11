using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SCDToolkit.Desktop.ViewModels;
using SCDToolkit.Desktop.Views;
using SCDToolkit.Desktop.Services;

namespace SCDToolkit.Desktop
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var window = new MainWindow();
                var picker = new Services.StorageProviderFolderPickerService(window);
                var config = ConfigLoader.Load();
                var vm = new ViewModels.MainViewModel(
                    new SCDToolkit.Core.Services.FileSystemLibraryService(),
                    new VgmstreamPlaybackService(),
                    picker);
                window.DataContext = vm;
                desktop.MainWindow = window;
                _ = vm.ApplyConfigAsync(config);
                vm.QueueAutoUpdateCheck();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
