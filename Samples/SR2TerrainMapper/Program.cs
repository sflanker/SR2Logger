using System;
using Avalonia;

namespace SR2TerrainMapper {
    public static class Program {
        public static AppBuilder BuildAvaloniaApp() {
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }

        public static Int32 Main(String[] args) {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }
}
