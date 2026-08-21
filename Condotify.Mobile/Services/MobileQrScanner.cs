namespace Condotify.Mobile.Services;

public sealed record MobileQrScannerResult(bool Success, string? Code, string Message);

/// <summary>
/// Opens a native camera surface for QR reading. The WebView camera APIs are
/// intentionally not used here because Android WebView does not consistently
/// expose BarcodeDetector/getUserMedia to hybrid applications.
/// </summary>
public sealed class MobileQrScanner
{
    public async Task<MobileQrScannerResult> ScanAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID || IOS
        if (!ZXing.Net.Maui.BarcodeScanning.IsSupported)
            return new(false, null, "Este aparelho não disponibilizou uma câmera compatível.");

        cancellationToken.ThrowIfCancellationRequested();
        var scannerPage = new NativeQrScannerPage();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var hostPage = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (hostPage is null)
                    throw new InvalidOperationException("Não foi possível abrir a câmera neste momento.");

                await hostPage.Navigation.PushModalAsync(scannerPage, false);
            });

            using var registration = cancellationToken.Register(scannerPage.Cancel);
            var code = await scannerPage.Completion.WaitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(code)
                ? new(false, null, "Leitura cancelada.")
                : new(true, code, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(false, null, "Leitura cancelada.");
        }
        catch (Exception ex)
        {
            return new(false, null, string.IsNullOrWhiteSpace(ex.Message)
                ? "Não foi possível abrir a câmera neste momento."
                : ex.Message);
        }
#else
        await Task.CompletedTask;
        return new(false, null, "O leitor de QR Code não está disponível nesta plataforma.");
#endif
    }
}

#if ANDROID || IOS
internal sealed class NativeQrScannerPage : ContentPage
{
    private readonly ZXing.Net.Maui.Controls.CameraBarcodeReaderView _camera;
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;

    public NativeQrScannerPage()
    {
        Title = "Ler convite";
        BackgroundColor = Color.FromArgb("#050B18");

        _camera = new ZXing.Net.Maui.Controls.CameraBarcodeReaderView
        {
            CameraLocation = ZXing.Net.Maui.CameraLocation.Rear,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsDetecting = true,
            Options = new ZXing.Net.Maui.BarcodeReaderOptions
            {
                Formats = ZXing.Net.Maui.BarcodeFormats.TwoDimensional,
                AutoRotate = true,
                Multiple = false,
                TryInverted = true
            }
        };
        _camera.BarcodesDetected += OnBarcodesDetected;

        var title = new Label
        {
            Text = "Aponte para o QR Code do visitante",
            TextColor = Colors.White,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };
        var close = new Button
        {
            Text = "Fechar",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#70000000"),
            CornerRadius = 18,
            Padding = new Thickness(16, 8),
            HorizontalOptions = LayoutOptions.End
        };
        close.Clicked += (_, _) => Cancel();

        var topBar = new Grid
        {
            Padding = new Thickness(20, 18),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        topBar.Children.Add(title);
        topBar.Children.Add(close);
        Grid.SetColumn(close, 1);

        var scanFrame = new Border
        {
            WidthRequest = 250,
            HeightRequest = 250,
            Stroke = Color.FromArgb("#61E4C5"),
            StrokeThickness = 4,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var hint = new Border
        {
            Margin = new Thickness(20, 0, 20, 28),
            Padding = new Thickness(18, 12),
            BackgroundColor = Color.FromArgb("#B0050B18"),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Content = new Label
            {
                Text = "Mantenha o código dentro do quadro",
                TextColor = Colors.White,
                FontSize = 14,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        layout.Children.Add(_camera);
        Grid.SetRowSpan(_camera, 3);
        layout.Children.Add(scanFrame);
        Grid.SetRow(scanFrame, 1);
        layout.Children.Add(topBar);
        Grid.SetRow(topBar, 0);
        layout.Children.Add(hint);
        Grid.SetRow(hint, 2);
        Content = layout;
    }

    public Task<string?> Completion => _completion.Task;

    public void Cancel() => MainThread.BeginInvokeOnMainThread(async () => await CompleteAsync(null));

    private void OnBarcodesDetected(object? sender, ZXing.Net.Maui.BarcodeDetectionEventArgs args)
    {
        var value = args.Results.FirstOrDefault(result => !string.IsNullOrWhiteSpace(result.Value))?.Value;
        if (string.IsNullOrWhiteSpace(value)) return;
        MainThread.BeginInvokeOnMainThread(async () => await CompleteAsync(value));
    }

    private async Task CompleteAsync(string? value)
    {
        if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0) return;
        _camera.IsDetecting = false;
        _camera.BarcodesDetected -= OnBarcodesDetected;
        _completion.TrySetResult(value);
        try
        {
            if (Navigation.ModalStack.LastOrDefault() == this)
                await Navigation.PopModalAsync(false);
        }
        catch
        {
            // The host window may already be closing; the completion result is
            // still delivered to the Blazor dialog.
        }
    }

    protected override void OnDisappearing()
    {
        _camera.IsDetecting = false;
        _camera.BarcodesDetected -= OnBarcodesDetected;
        if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
            _completion.TrySetResult(null);
        base.OnDisappearing();
    }
}
#endif
