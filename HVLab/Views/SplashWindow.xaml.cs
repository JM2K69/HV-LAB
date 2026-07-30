using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HVLab.Views;

/// <summary>
/// Fenêtre splash affichée pendant le pre-load Hyper-V au démarrage.
/// Entièrement construite en code (pas de XAML) pour éviter les problèmes
/// de découverte XAML d'une seconde fenêtre WinUI.
/// </summary>
public sealed class SplashWindow : Window
{
    private readonly TextBlock _statusText;

    public SplashWindow()
    {
        // Icône
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.ico"));

        // Taille et centrage
        AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 380));
        CenterOnScreen();

        // Mica
        SystemBackdrop = new MicaBackdrop();

        // ── Racine ──────────────────────────────────────────────────────────
        var root = new Grid();

        // ── Contenu central ─────────────────────────────────────────────────
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing             = 24
        };

        // Logo
        var logo = new Image
        {
            Source              = new BitmapImage(new Uri("ms-appx:///Images/Square150x150Logo.png")),
            Width               = 120,
            Height              = 120,
            Stretch             = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Nom
        var title = new TextBlock
        {
            Text                = "HyperV Lab Manager",
            FontSize            = 28,
            FontWeight          = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Version
        var version = new TextBlock
        {
            Text                = "v1.0.0",
            FontSize            = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        version.SetValue(FrameworkElement.StyleProperty,
            Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var s) ? s : null);

        // Spinner + statut
        var spinnerPanel = new StackPanel
        {
            Spacing             = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var spinner = new ProgressRing { IsActive = true, Width = 32, Height = 32 };
        _statusText = new TextBlock
        {
            Text                = "Initialisation…",
            FontSize            = 13,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        spinnerPanel.Children.Add(spinner);
        spinnerPanel.Children.Add(_statusText);

        center.Children.Add(logo);
        center.Children.Add(title);
        center.Children.Add(version);
        center.Children.Add(spinnerPanel);
        root.Children.Add(center);

        // ── Copyright bas de page ────────────────────────────────────────────
        var copyright = new TextBlock
        {
            Text                = "© 2026 JM2K69",
            FontSize            = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Margin              = new Thickness(0, 0, 0, 20)
        };
        root.Children.Add(copyright);

        Content = root;

        // Appliquer le thème courant
        if (Content is FrameworkElement fe)
            Services.ThemeService.ApplyCurrent(fe);
    }

    /// <summary>Met à jour le message de statut affiché sous le spinner.</summary>
    public void SetStatus(string message)
    {
        DispatcherQueue.TryEnqueue(() => _statusText.Text = message);
    }

    private void CenterOnScreen()
    {
        var display  = Microsoft.UI.Windowing.DisplayArea.Primary;
        var workArea = display.WorkArea;
        var size     = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            workArea.X + (workArea.Width  - size.Width)  / 2,
            workArea.Y + (workArea.Height - size.Height) / 2));
    }
}

