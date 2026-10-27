---
name: winui-fluent
description: Design, build or review HV-LAB WinUI 3 screens following Fluent Design and the project's conventions (theme resources, typography, spacing, InfoBar/empty states, localization keys, accessibility, commands, adaptive layout). Use when adding or changing a page, control, dialog or XAML style in HVLab/, when asked to "improve the UI", "make it Fluent", review a screen or screenshot of the app, or check XAML for hard-coded colours, strings or missing accessibility.
---

# WinUI 3 Fluent design for HV-LAB

HV-LAB is a WinUI 3 app (Windows App SDK 1.8, .NET 10, unpackaged), with view models in `HVLab.Core` (no WinUI reference) and views in `HVLab/Views`. Answer the user in their language (usually French).

Read `reference.md` (next to this file) for the copy-paste XAML patterns before writing XAML.

## Workflow

1. **Locate the screen**: `HVLab/Views/<Page>.xaml(.cs)` plus its view model in `HVLab.Core/ViewModels/`. Read both before changing anything.
2. **Design or review against the rules below**, starting with the checklist at the end of this file.
3. **Implement**: XAML in `HVLab/`, logic and state in the `HVLab.Core` view model. Code-behind is only for view concerns: pickers, navigation, timers, keyboard accelerators.
4. **Strings**: add every new key to **both** `Strings_fr` and `Strings_en` in `HVLab.Core/Services/LocalizationService.cs`.
5. **Verify**:
   - `dotnet test HVLab.Core.Tests`. `LocalizationTests` fails on a missing key or on a mismatched `{0}` between FR and EN.
   - `dotnet build HVLab\HVLab.csproj -c Debug -p:Platform=x64` must report 0 errors and 0 warnings. A XAML error shows as `WMC…`.
   - Launch the app elevated and look at the screen in light theme, dark theme and at a narrow width (about 1000 px). The app requires administrator rights.

## Fluent rules

### Colour: theme resources only
- Use `{ThemeResource …}` brushes, never hex values or `White` / `Black`. Hard-coded colours break dark mode and high contrast.
  - Text: `TextFillColorPrimaryBrush`, `TextFillColorSecondaryBrush`, `TextFillColorTertiaryBrush`, `TextFillColorDisabledBrush`.
  - Surfaces: `CardBackgroundFillColorDefaultBrush` with `CardStrokeColorDefaultBrush`. Use `SubtleFillColorSecondaryBrush` for table headers and `LayerFillColorDefaultBrush` for layers.
  - Accent: `AccentFillColorDefaultBrush`, `AccentTextFillColorPrimaryBrush`. Text on an accent fill: `TextOnAccentFillColorPrimaryBrush`.
  - Status: `SystemFillColorSuccessBrush`, `SystemFillColorCautionBrush`, `SystemFillColorCriticalBrush`, `SystemFillColorNeutralBrush`, plus their `…BackgroundBrush` variants for tinted areas.
- Pages keep `Background="Transparent"` so the Mica backdrop of `MainWindow` shows through.
- Exception: `StateToColorConverter` (VM state badge) uses dark status colours with white text on purpose, so it stays readable in both themes. Do not copy that pattern elsewhere.
- Known debt to fix when touching these files: the hex KPI icon circles and `Foreground="White"` in `DashboardPage.xaml`, and `Foreground="White"` badges in `QuickVmPage.xaml` and `BaseVhdxPage.xaml`.

### Typography: the Fluent type ramp
- Use the built-in styles rather than ad-hoc `FontSize`:
  - page title: `TitleTextBlockStyle`, or the existing 28 SemiBold;
  - section: `SubtitleTextBlockStyle` / `BodyStrongTextBlockStyle`;
  - body: `BodyTextBlockStyle`;
  - small or secondary: `CaptionTextBlockStyle`.
- Use Segoe UI Variable, which is the default: do not set `FontFamily`. The exceptions are paths, keys and the XML preview, which use `Consolas`.
- Secondary information always uses `TextFillColorSecondaryBrush`, not a smaller grey hex.

### Layout and spacing
- Work on a 4 px grid: spacing 4, 8, 12, 16, 24 or 32.
  - Page padding: `36,24,36,20`, as the existing pages use.
  - Between cards or sections: 12 to 16.
  - Inside a card: `Padding="20,16"`.
- Corners: `CornerRadius="{StaticResource ControlCornerRadius}"` (4) on controls, `OverlayCornerRadius` (8) on cards and flyouts. Do not invent other radii.
- Lists:
  - a `ListView` sits in a `*` row with **no outer `ScrollViewer`**, which keeps virtualization and a fixed header;
  - a page that stacks several lists inside a `ScrollViewer` gives each list a `MaxHeight`;
  - column widths are shared between the header grid and the row template through `Page.Resources` (`GridLength` / `x:Double`).
- Adaptive layout:
  - the window minimum is 1000×640 DIP (`WindowPlacement`) and `NavigationView` uses `PaneDisplayMode="Auto"`;
  - hide secondary columns below about 900 px (the page `SizeChanged` handler sets a DependencyProperty, as in `VirtualMachinesPage`);
  - reflow cards with `VisualStateManager` + `AdaptiveTrigger`, as in the 2×2 layout of `DashboardPage`.

### Controls: pick the Fluent control
- Errors: `InfoBar Severity="Error"` bound to `HasError` / `ErrorMessage`. Never put errors in the status line.
- Long operations: a determinate `ProgressBar`, or an `InfoBar Severity="Informational"` with a **Cancel** action button. Use an indeterminate `ProgressBar` while loading.
- Confirmations: through `IDialogService.ConfirmAsync(title, content, primary)`, which is a `ContentDialog` whose default button is Cancel. Always use it for destructive actions and name the object: `« SRV-01 »`.
- Settings: `ctk:SettingsCard` / `SettingsExpander` from CommunityToolkit.
- Choices:
  - `ToggleSwitch` for on/off settings;
  - `RadioButtons` or selectable cards for 2–4 exclusive options;
  - `ComboBox` for longer lists;
  - `NumberBox` with `Minimum` / `Maximum` for numbers.
- Primary action: one `AccentButtonStyle` button per area. Secondary actions use the default style.
- Icons: `FontIcon` with Segoe Fluent Icons glyphs, about 16 for buttons and 14 for inline use. Icon-only buttons are 32×32.

### Empty, loading and error states
- Every list has all three states:
  - loading: `ProgressBar`;
  - error: `InfoBar`;
  - empty: an icon, a title and one explanatory sentence, plus an action button when one makes sense.
- The empty state appears only after a successful load: `PageViewModelBase.IsEmptyAfterLoad(count)`, exposed as `IsXxxEmpty`.

## HV-LAB conventions

- **Bindings**:
  - `x:Bind` (compiled) to view model properties, with an explicit `Mode=OneWay` or `TwoWay`.
  - Localized text uses `{Binding [Key], Source={StaticResource Loc}}`, because it refreshes live when the language changes. Never hard-code UI text in XAML or C#.
  - In C#, use `LocalizationService.T("Key", args)` (`using static HVLab.Services.LocalizationService;`).
- **Commands**:
  - actions are `[RelayCommand]` in the view model;
  - inside a `DataTemplate`, bind through the page: `Command="{Binding ViewModel.XxxCommand, ElementName=PageRoot}" CommandParameter="{x:Bind}"`, with `x:Name="PageRoot"` on the `<Page>`;
  - no `Click` handler for business actions.
- **View model state**:
  - list pages derive from `PageViewModelBase`: `ErrorMessage`, `HasError`, `HasLoaded`, `MarkLoaded()`, `ShowError(ex)`;
  - VM creation pages derive from `VmCreationViewModelBase`;
  - per-item busy flags (`IsBusy`, `CanStart`…) live on the model, so double actions are impossible.
- **Accessibility**:
  - every icon-only button gets `AutomationProperties.Name="{x:Bind helpers:A11y.Action('Key', Name)}"`, which reads as "Delete — SRV-01", plus a localized `ToolTipService.ToolTip`;
  - F5 refreshes: `KeyboardAccelerator Key="F5"` on the Refresh button;
  - Delete removes the selected item, through an accelerator on the `ListView` that calls the command;
  - keep `AccessKey` on navigation items;
  - check keyboard order (Tab) and visible focus.
- **Pages**:
  - form pages and long-running pages use `NavigationCacheMode="Required"`, and the view model preserves its selections when it reloads;
  - list pages refresh in `OnNavigatedTo`;
  - pages resolve their view model with `App.GetService<T>()`.
- **Title bar**:
  - the custom 48 px row in `MainWindow.xaml` is the drag region;
  - the caption buttons follow the app theme through `ThemeService.ApplyTitleBar` (`PreferredTheme`);
  - do not put interactive controls in the title bar without `InputNonClientPointerSource`.
- **PowerShell-facing input**: validate it in the view model (`InputValidator`, `NatService.NormalizeGateway`) before calling a service, and show the localized `Val_*` message.

## Review checklist

Report findings as `file:line`, most important first, and give the concrete fix.

- [ ] There are no hex colours, `White` / `Black` or hard-coded brushes outside the documented exceptions.
- [ ] There is no hard-coded user-facing string, and every new key exists in FR and EN.
- [ ] Text uses type ramp styles, with secondary text in `TextFillColorSecondaryBrush`.
- [ ] Spacing is on the 4 px grid and corner radii come from the standard resources.
- [ ] Each list has its loading, error (InfoBar) and empty states.
- [ ] There is a single accent button per area, and destructive actions are confirmed through `IDialogService`.
- [ ] Icon-only buttons have an accessible name and a tooltip, and the F5 / Delete accelerators are present where relevant.
- [ ] Lists are virtualized (no outer ScrollViewer around a single list), and header and row columns share their widths.
- [ ] The screen is readable at 1000 px wide, in light, dark and high-contrast themes.
- [ ] Logic lives in the `HVLab.Core` view model (testable), and code-behind contains view concerns only.
