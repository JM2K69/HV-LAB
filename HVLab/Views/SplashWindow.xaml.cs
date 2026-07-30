using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace HVLab.Views;

/// <summary>
/// Splash screen sans chrome (ni titre, ni boutons), construit entièrement en code.
/// </summary>
public sealed class SplashWindow : Window
{
    private readonly TextBlock _statusText;

    public SplashWindow()
    {
        // ── Supprimer tout le chrome (titre + bordure + boutons) ──────────────
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsMaximizable  = false;
            presenter.IsMinimizable  = false;
            presenter.IsResizable    = false;
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        }

        // Étendre le contenu dans toute la fenêtre (supprime le fond blanc WinUI)
        ExtendsContentIntoTitleBar = true;

        // Taille et centrage
        AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 360));
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Images", "AppIcon.ico"));
        CenterOnScreen();

        // ── Couche racine avec fond noir (évite le flash blanc) ───────────────
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 5, 10, 25))
        };

        Content = rootGrid;

        // ── Image de fond (SplashScreen.png) ──────────────────────────────────
        var bgImage = new Image
        {
            Source  = new BitmapImage(new Uri("ms-appx:///Images/SplashScreen.png")),
            Stretch = Stretch.UniformToFill
        };

        // ── Overlay semi-transparent (gradient du bas) ────────────────────────
        var overlay = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5, 0),
                EndPoint   = new Windows.Foundation.Point(0.5, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(140, 5, 10, 25),  Offset = 0.0 },
                    new GradientStop { Color = Color.FromArgb(220, 5, 10, 25),  Offset = 0.5 },
                    new GradientStop { Color = Color.FromArgb(255, 5, 10, 25),  Offset = 1.0 }
                }
            }
        };

        // ── Contenu central ───────────────────────────────────────────────────
        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Spacing             = 16
        };

        // Logo
        var logo = new Image
        {
            Source              = new BitmapImage(new Uri("ms-appx:///Images/Square150x150Logo.png")),
            Width               = 96,
            Height              = 96,
            Stretch             = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Titre
        var title = new TextBlock
        {
            Text                = "HyperV Lab Manager",
            FontSize            = 26,
            FontWeight          = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground          = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Version
        var version = new TextBlock
        {
            Text                = "v1.0.0",
            FontSize            = 13,
            Foreground          = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Séparateur visuel
        var separator = new Border
        {
            Height              = 1,
            Width               = 200,
            Margin              = new Thickness(0, 4, 0, 4),
            Background          = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Spinner + statut
        var spinnerRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            Spacing             = 12,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var spinner = new ProgressRing
        {
            IsActive   = true,
            Width      = 20,
            Height     = 20,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 80, 160, 255))
        };
        _statusText = new TextBlock
        {
            Text              = "Initialisation…",
            FontSize          = 13,
            Foreground        = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        spinnerRow.Children.Add(spinner);
        spinnerRow.Children.Add(_statusText);

        center.Children.Add(logo);
        center.Children.Add(title);
        center.Children.Add(version);
        center.Children.Add(separator);
        center.Children.Add(spinnerRow);

        // ── Copyright bas de page ─────────────────────────────────────────────
        var copyright = new TextBlock
        {
            Text                = "© 2026 JM2K69",
            FontSize            = 11,
            Foreground          = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Margin              = new Thickness(0, 0, 0, 16)
        };

        rootGrid.Children.Add(bgImage);
        rootGrid.Children.Add(overlay);
        rootGrid.Children.Add(center);
        rootGrid.Children.Add(copyright);
    }

    /// <summary>Met à jour le message de statut affiché sous le spinner.</summary>
    public void SetStatus(string message)
    {
        DispatcherQueue.TryEnqueue(() => _statusText.Text = message);
    }

    private void CenterOnScreen()
    {
        var display  = DisplayArea.Primary;
        var workArea = display.WorkArea;
        var size     = AppWindow.Size;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            workArea.X + (workArea.Width  - size.Width)  / 2,
            workArea.Y + (workArea.Height - size.Height) / 2));
    }
}
