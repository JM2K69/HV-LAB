# XAML patterns used in HV-LAB

Copy these patterns and adapt the names. They match the existing pages, so look at `VirtualMachinesPage.xaml`, `VirtualSwitchesPage.xaml` and `BaseVhdxPage.xaml` for live examples.

## Page skeleton (list page)

```xml
<Page
    x:Class="HVLab.Views.ThingsPage"
    x:Name="PageRoot"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:models="using:HVLab.Models"
    xmlns:helpers="using:HVLab.Helpers"
    Background="Transparent">

    <Grid Padding="36,24,36,20" RowSpacing="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- header -->
            <RowDefinition Height="Auto"/>  <!-- progress + error -->
            <RowDefinition Height="Auto"/>  <!-- column headers -->
            <RowDefinition Height="*"/>     <!-- list / empty state -->
            <RowDefinition Height="Auto"/>  <!-- status line -->
        </Grid.RowDefinitions>
        <!-- … -->
    </Grid>
</Page>
```

Code-behind:

```csharp
public sealed partial class ThingsPage : Page
{
    public ThingsViewModel ViewModel { get; } = App.GetService<ThingsViewModel>();
    public ThingsPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.RefreshAsync();
    }
}
```

Register the view model in `HVLab.Core/ServiceCollectionExtensions.cs` (`AddTransient<ThingsViewModel>()`).

## Header with Refresh (F5)

```xml
<Grid Grid.Row="0" Margin="0,0,0,8">
    <TextBlock Text="{Binding [Things_Title], Source={StaticResource Loc}}" FontSize="28" FontWeight="SemiBold"/>
    <Button HorizontalAlignment="Right" VerticalAlignment="Center"
            Command="{x:Bind ViewModel.RefreshCommand}" ToolTipService.ToolTip="F5">
        <Button.KeyboardAccelerators>
            <KeyboardAccelerator Key="F5"/>
        </Button.KeyboardAccelerators>
        <StackPanel Orientation="Horizontal" Spacing="8">
            <FontIcon Glyph="&#xE72C;" FontSize="14"/>
            <TextBlock Text="{Binding [Btn_Refresh], Source={StaticResource Loc}}"/>
        </StackPanel>
    </Button>
</Grid>
```

## Progress + error InfoBar

```xml
<StackPanel Grid.Row="1" Spacing="8">
    <ProgressBar IsIndeterminate="True"
                 Visibility="{x:Bind ViewModel.IsLoading, Mode=OneWay,
                               Converter={StaticResource BoolToVisibilityConverter}}"/>
    <InfoBar Severity="Error" IsClosable="False"
             IsOpen="{x:Bind ViewModel.HasError, Mode=OneWay}"
             Title="{Binding [Err_Title], Source={StaticResource Loc}}"
             Message="{x:Bind ViewModel.ErrorMessage, Mode=OneWay}"/>
</StackPanel>
```

## Shared column widths (header + rows)

```xml
<Page.Resources>
    <GridLength x:Key="ColName">200</GridLength>
    <GridLength x:Key="ColActions">80</GridLength>
</Page.Resources>

<!-- header -->
<Grid Grid.Row="2" Background="{ThemeResource SubtleFillColorSecondaryBrush}" Padding="12,6" CornerRadius="6">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="{StaticResource ColName}"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="{StaticResource ColActions}"/>
    </Grid.ColumnDefinitions>
    <!-- column titles: {Binding [Things_ColName], Source={StaticResource Loc}} FontWeight="SemiBold" -->
</Grid>
```

Use the same `ColumnDefinition` widths in the row `DataTemplate`.

## List with item actions, accessibility and the Delete key

```xml
<ListView x:Name="ThingList" Grid.Row="3"
          ItemsSource="{x:Bind ViewModel.Things, Mode=OneWay}" SelectionMode="Single">
    <ListView.KeyboardAccelerators>
        <KeyboardAccelerator Key="Delete" Invoked="DeleteSelected_Invoked"/>
    </ListView.KeyboardAccelerators>
    <ListView.ItemContainerStyle>
        <Style TargetType="ListViewItem">
            <Setter Property="Padding" Value="12,0"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="MinHeight" Value="44"/>
        </Style>
    </ListView.ItemContainerStyle>
    <ListView.ItemTemplate>
        <DataTemplate x:DataType="models:Thing">
            <Grid ColumnSpacing="8" VerticalAlignment="Center">
                <!-- same ColumnDefinitions as the header -->
                <TextBlock Text="{x:Bind Name}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <Button Grid.Column="2" Width="32" Height="32" Padding="0"
                        Command="{Binding ViewModel.RemoveThingCommand, ElementName=PageRoot}"
                        CommandParameter="{x:Bind}"
                        AutomationProperties.Name="{x:Bind helpers:A11y.Action('Btn_Delete', Name)}"
                        ToolTipService.ToolTip="{Binding [Btn_Delete], Source={StaticResource Loc}}">
                    <FontIcon Glyph="&#xE74D;" FontSize="12"/>
                </Button>
            </Grid>
        </DataTemplate>
    </ListView.ItemTemplate>
</ListView>
```

```csharp
private void DeleteSelected_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
{
    if (ThingList.SelectedItem is Thing t && ViewModel.RemoveThingCommand.CanExecute(t))
    {
        ViewModel.RemoveThingCommand.Execute(t);
        args.Handled = true;
    }
}
```

## Empty state (same row as the list)

```xml
<StackPanel Grid.Row="3" Spacing="8" MaxWidth="380"
            HorizontalAlignment="Center" VerticalAlignment="Center"
            Visibility="{x:Bind ViewModel.IsThingListEmpty, Mode=OneWay,
                         Converter={StaticResource BoolToVisibilityConverter}}">
    <FontIcon Glyph="&#xE7F8;" FontSize="40" Foreground="{ThemeResource TextFillColorTertiaryBrush}"/>
    <TextBlock Text="{Binding [Things_EmptyTitle], Source={StaticResource Loc}}"
               FontSize="16" FontWeight="SemiBold" HorizontalAlignment="Center"/>
    <TextBlock Text="{Binding [Things_EmptyText], Source={StaticResource Loc}}"
               TextAlignment="Center" TextWrapping="Wrap"
               Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
</StackPanel>
```

## View model (HVLab.Core)

```csharp
public partial class ThingsViewModel(IThingService things, IDialogService dialogs) : PageViewModelBase
{
    [ObservableProperty] private ObservableCollection<Thing> thingList = [];
    [ObservableProperty] private bool   isLoading;
    [ObservableProperty] private string status = T("St_Ready");

    public bool IsThingListEmpty => IsEmptyAfterLoad(ThingList.Count);
    protected override void OnEmptyStateChanged() => OnPropertyChanged(nameof(IsThingListEmpty));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var items = await things.GetAllAsync();
            ThingList.Clear();
            foreach (var i in items) ThingList.Add(i);
            Status = T("Things_Count", items.Count);
            MarkLoaded();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task RemoveThingAsync(Thing t)
    {
        if (!await dialogs.ConfirmAsync(T("Things_DeleteTitle"), $"{T("Things_DeleteConfirm")} « {t.Name} » ?", T("Btn_Delete")))
            return;
        try   { await things.RemoveAsync(t.Name); await RefreshAsync(); }
        catch (Exception ex) { ShowError(ex); }
    }
}
```

## Long operation with Cancel

```xml
<InfoBar IsOpen="{x:Bind ViewModel.IsWorking, Mode=OneWay}" Severity="Informational" IsClosable="False"
         Title="{Binding [Things_Working], Source={StaticResource Loc}}"
         Message="{x:Bind ViewModel.ProgressText, Mode=OneWay}">
    <InfoBar.ActionButton>
        <Button Content="{Binding [Btn_Cancel], Source={StaticResource Loc}}" Command="{x:Bind ViewModel.CancelCommand}"/>
    </InfoBar.ActionButton>
</InfoBar>
```

In the view model:
- keep a `CancellationTokenSource`;
- pass `new Progress<string>(l => ProgressText = TranslateProgress(l))` and the token to the service;
- catch `OperationCanceledException` and show `T("Op_Cancelled")`.

## Adaptive card grid (wide: 1 row; narrow: 2×2)

```xml
<VisualStateManager.VisualStateGroups>
    <VisualStateGroup>
        <VisualState x:Name="Wide">
            <VisualState.StateTriggers><AdaptiveTrigger MinWindowWidth="1200"/></VisualState.StateTriggers>
        </VisualState>
        <VisualState x:Name="Narrow">
            <VisualState.StateTriggers><AdaptiveTrigger MinWindowWidth="0"/></VisualState.StateTriggers>
            <VisualState.Setters>
                <Setter Target="CardB.(Grid.Column)" Value="2"/>
                <Setter Target="CardC.(Grid.Row)"    Value="1"/>
                <!-- … see DashboardPage.xaml -->
            </VisualState.Setters>
        </VisualState>
    </VisualStateGroup>
</VisualStateManager.VisualStateGroups>
```

Put the `VisualStateGroups` on the first child of the page's root element. `x:Name` must be unique in the page.

## Localization keys

Add each key to `Strings_fr` and `Strings_en` in `HVLab.Core/Services/LocalizationService.cs`:

```csharp
["Things_EmptyTitle"] = "Aucun élément",          // FR
["Things_EmptyTitle"] = "No items",               // EN
```

Prefix keys by page (`VM_`, `SW_`, `NAT_`, `BV_`, `CVM_`, `SET_`, `Dash_`) or by kind:
- `Btn_` for buttons;
- `St_` for statuses;
- `Err_` for errors;
- `Val_` for validation messages.

PowerShell scripts report known errors as `throw "##Err_Key|arg"`, and `PowerShellRunner` translates them.

## Useful Segoe Fluent Icons glyphs

| Glyph | Use |
|---|---|
| `&#xE72C;` | Refresh |
| `&#xE710;` | Add |
| `&#xE74D;` | Delete |
| `&#xE768;` | Play / start |
| `&#xE71A;` | Stop |
| `&#xE8A7;` | Open / console |
| `&#xE711;` | Cancel |
| `&#xE713;` | Settings |
| `&#xE7F8;` | Device / VM |
| `&#xEDA3;` | Network switch |
| `&#xEDA2;` | Disk |
| `&#xE945;` | Lightning (Quick VM) |
| `&#xE946;` | Info |
