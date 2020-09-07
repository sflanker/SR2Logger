using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;

namespace SR2TerrainMapper {
    public class MainWindow : Window {
        private readonly Int32?[,] data = new Int32?[7200, 3600];
        private UdpClient udpClient;
        private Thread udpListenerThread;
        private Thread heightMapRendererThread;
        private readonly ManualResetEventSlim hasUpdates = new ManualResetEventSlim();
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();

        private WriteableBitmap bitmap1;
        private WriteableBitmap bitmap2;
        private ImageBrush background;

        // public static readonly DirectProperty<MainWindow, Bitmap> ImageProperty =
        //     AvaloniaProperty.RegisterDirect<MainWindow, Bitmap>(nameof(Image), o => o.Image);

        // private Bitmap image;
        // private RenderTargetBitmap bitmap1;
        // private RenderTargetBitmap bitmap2;
        // private Visual visual;

        // public Bitmap Image {
        //     get => image;
        //     private set => SetAndRaise(ImageProperty, ref image, value);
        // }

        public MainWindow() {
            this.InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif

            this.DataContext = this;
        }

        private void InitializeComponent() {
            AvaloniaXamlLoader.Load(this);

            this.udpClient =
                new UdpClient(new IPEndPoint(new IPAddress(new Byte[] { 127, 0, 0, 1 }), 2837));

            // this.visual = new Visual();
            this.bitmap1 = InitializeBitmap();
            this.bitmap2 = InitializeBitmap();

            this.Background = this.background = new ImageBrush(this.bitmap2);
        }

        private static WriteableBitmap InitializeBitmap() {

            var bitmap = new WriteableBitmap(
                new PixelSize(7200, 3600),
                new Vector(96, 96),
                PixelFormat.Rgba8888);

            using var buffer = bitmap.Lock();

            unsafe {
                var ptr = buffer.Address;

                for (var y = 0; y < 3600; y++) {
                    var row = ptr + y * buffer.RowBytes;
                    for (var x = 0; x < 7200; x++) {
                        var pixel = row + x * 4;
                        // fill with black
                        *((uint*)pixel) = 0xff000000;
                    }
                }
            }

            return bitmap;
        }

        // private  RenderTargetBitmap InitializeBitmap() {
        //     var bitmap = new RenderTargetBitmap(new PixelSize(7200, 3600));
        //     using var drawingContext = new DrawingContext(bitmap.CreateDrawingContext(new ImmediateRenderer(visual)));
        //     drawingContext.FillRectangle(new SolidColorBrush(Colors.Black), new Rect(0, 0, 7200, 3600));
        //     return bitmap;
        // }

        protected override void OnOpened(EventArgs e) {
            base.OnOpened(e);

            this.udpListenerThread = new Thread(this.UdpListener);
            this.udpListenerThread.Start();

            this.heightMapRendererThread = new Thread(this.HeightMapRenderer);
            this.heightMapRendererThread.Start();
        }

        protected override void OnClosing(CancelEventArgs e) {
            this.cancellationSource.Cancel();

            this.heightMapRendererThread.Join();
            this.udpListenerThread.Join();

            base.OnClosing(e);
        }

        private void UdpListener() {
            var cancellationToken = this.cancellationSource.Token;

            try {
                using (var dataReceivedSignal = new ManualResetEventSlim()) {
                    IPEndPoint remoteEP = null;
                    Byte[] receivedData = null;
                    while (!cancellationToken.IsCancellationRequested) {
                        this.udpClient.BeginReceive(
                            result => {
                                try {
                                    receivedData = this.udpClient.EndReceive(result, ref remoteEP);
                                    dataReceivedSignal.Set();
                                } catch (ObjectDisposedException) {
                                }
                            },
                            null
                        );

                        if (dataReceivedSignal.Wait(Timeout.Infinite, cancellationToken)) {
                            dataReceivedSignal.Reset();
                            // Parse Message
                            var msg = SR2LoggerPlusMessage.Deserialize(receivedData);

                            if (msg is SR2LoggerPlusLogMessage logMessage) {
                                Console.Error.WriteLine(logMessage.LogMessage);
                            } else if (msg is SR2LoggerPlusVariableMessage variableMessage) {
                                var yCenter = (Int32)((variableMessage.Variables["lat"].NumberValue + 90) / 0.05);
                                var x = (Int32)((variableMessage.Variables["lon"].NumberValue + 180) / 0.05);
                                var elevations = variableMessage.Variables["elevations"].ListValue;
                                for (var i = 0; i <= 100 && i < elevations.Count; i++) {
                                    var y = yCenter + (i - 50);
                                    if (elevations[i] != null && y > 0 && y < 3600) {
                                        data[x, y] = (Int32)Double.Parse(elevations[i]);
                                    }
                                }

                                this.hasUpdates.Set();
                            }
                        } else {
                            break;
                        }
                    }
                }
            } catch (OperationCanceledException) {
            }
        }

        private void HeightMapRenderer() {
            var cancellationToken = this.cancellationSource.Token;

            var toggle = true;
            try {
                while (!cancellationToken.IsCancellationRequested) {
                    if (this.hasUpdates.Wait(Timeout.Infinite, cancellationToken)) {
                        this.hasUpdates.Reset();

                        // Draw a bitmap with the latest data
                        Int32? minAltitude = null;
                        Int32? maxAltitude = null;

                        foreach (var altitude in this.data) {
                            if (altitude.HasValue) {
                                if (!minAltitude.HasValue || altitude < minAltitude) {
                                    minAltitude = altitude;
                                }

                                if (!maxAltitude.HasValue || altitude > maxAltitude) {
                                    maxAltitude = altitude;
                                }
                            }
                        }

                        if (minAltitude.HasValue) {
                            var bitmap = toggle ? this.bitmap1 : this.bitmap2;
                            toggle = !toggle;
                            // Shade per meter
                            var spm = 128.0 / (maxAltitude.Value - minAltitude.Value);
                            var min = minAltitude.Value;
                            // using var drawingContext = new DrawingContext(bitmap.CreateDrawingContext(new ImmediateRenderer(this.visual)));
                            // var brush = new SolidColorBrush();

                            using (var buffer = bitmap.Lock()) {
                                var ptr = buffer.Address;
                                unsafe {
                                    for (var y = 0; y < 3600; y++) {
                                        var row = ptr + y * buffer.RowBytes;
                                        for (var x = 0; x < 7200; x++) {
                                            var alt = data[x, y];
                                            if (alt.HasValue) {
                                                var val = (UInt32)((alt.Value - min) * spm + 64);
                                                // brush.Color = Color.FromRgb(val, val, val);
                                                // drawingContext.FillRectangle(brush, new Rect(x, y, 1, 1));
                                                var pixel = row + x * 4;
                                                *((uint*)pixel) = 0xFF000000 | (val << 16) | (val << 8) | val;
                                            }
                                        }
                                    }
                                }
                            }

                            Dispatcher.UIThread.Post(() => { this.background.Source = bitmap; });
                        }
                    }
                }
            } catch (OperationCanceledException) {
            }
        }
    }
}
