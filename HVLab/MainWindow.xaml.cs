using HVLab.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace HVLab;

public sealed partial class MainWindow : Window
{
    // Keep a reference to the handler so we can detach it when navigating away
    private Page? _currentPage;
    private readonly PointerEventHandler _wheelHandler;

    public MainWindow()
    {
        InitializeComponent();

        // ── Mica backdrop ────────────────────────────────────────────────────
        SystemBackdrop = new MicaBackdrop();

        // ── Custom title bar ─────────────────────────────────────────────────
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // ── Window size ──────────────────────────────────────────────────────
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));

        // ── Mouse-wheel fix ──────────────────────────────────────────────────
        // In WinUI 3, PointerWheelChanged does NOT bubble across Frame
        // boundaries, so AddHandler on the Frame itself never fires.
        // Instead we hook ContentFrame.Navigated and attach the handler
        // directly on each page after it is loaded.
        _wheelHandler = new PointerEventHandler(Page_PointerWheelChanged);
        ContentFrame.Navigated += ContentFrame_Navigated;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void NavView_Loaded(object sender, RoutedEventArgs e)
        => NavView.SelectedItem = NavView.MenuItems[0];

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var pageType = item.Tag?.ToString() switch
        {
            "dashboard" => typeof(DashboardPage),
            "vms"       => typeof(VirtualMachinesPage),
            "switches"  => typeof(VirtualSwitchesPage),
            "basevhdx"  => typeof(BaseVhdxPage),
            "createvm"  => typeof(CreateVmPage),
            "settings"  => typeof(SettingsPage),
            _           => (Type?)null
        };
        if (pageType is not null)
            ContentFrame.Navigate(pageType);
    }

    // ── Wheel fix ────────────────────────────────────────────────────────────

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        // Detach from the previous page to avoid leaking handlers
        if (_currentPage is not null)
            _currentPage.RemoveHandler(UIElement.PointerWheelChangedEvent, _wheelHandler);

        _currentPage = ContentFrame.Content as Page;

        // Attach on the new page with handledEventsToo=true so we receive the
        // event even when an inner ListView / ComboBox has already handled it.
        if (_currentPage is not null)
            _currentPage.AddHandler(
                UIElement.PointerWheelChangedEvent,
                _wheelHandler,
                handledEventsToo: true);
    }

    /// <summary>
    /// Scrolls the topmost scrollable <see cref="ScrollViewer"/> inside the
    /// current page when the user rotates the mouse wheel.
    /// </summary>
    private void Page_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as UIElement).Properties;
        if (props.IsHorizontalMouseWheel) return;

        var sv = FindTopmostScrollViewer(sender as DependencyObject);
        if (sv is null) return;

        sv.ChangeView(
            horizontalOffset: null,
            verticalOffset:   sv.VerticalOffset - props.MouseWheelDelta,
            zoomFactor:       null,
            disableAnimation: false);
        e.Handled = true;
    }

    /// <summary>
    /// BFS walk of the visual tree to find the outermost
    /// <see cref="ScrollViewer"/> that actually has content to scroll.
    /// </summary>
    private static ScrollViewer? FindTopmostScrollViewer(DependencyObject? root)
    {
        if (root is null) return null;

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer sv && sv.ScrollableHeight > 0)
                return sv;

            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }
}

