using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SR2TerrainMapper {
    public class App : Application {
        public override void Initialize() {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted() {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
                desktop.MainWindow = new MainWindow();
            } else if (ApplicationLifetime is ISingleViewApplicationLifetime) {
                throw new NotSupportedException("Wait even is this SingleViewApplication thing.");
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
