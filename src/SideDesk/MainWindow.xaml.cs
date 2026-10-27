using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SideDesk.Interop;
using SideDesk.Models;
using SideDesk.Services;

namespace SideDesk;

public partial class MainWindow : Window
{
    private const double PanelWidth = 392;
    private const double ArrowWidth = 36;
    private const double ArrowHeight = 58;
    private const double CollapsedOffset = PanelWidth;

    private readonly AppSettings _settings;
    private readonly TimeSpan? _autoExitAfter;
    private readonly DesktopCatalog _catalog;
    private readonly ObservableCollection<DesktopItem> _items = new();
    private readonly ICollectionView _view;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _guardTimer;
    private readonly DispatcherTimer _arrowTimer;
    private readonly DispatcherTimer _statusResetTimer;
    private DispatcherTimer? _autoExitTimer;

    private TrayIconController? _tray;
    private GlobalHotKey? _hotkey;
    private AppearanceWindow? _appearanceWindow;
    private DesktopItem? _contextItem;
    private bool _panelOpen = true;
    private bool _appearanceOpen;
    private bool _arrowShown;
    private bool _fullscreenActive;
    private DateTime _arrowHideAt = DateTime.MaxValue;
    private bool _exiting;
    private bool _iconsHidden;
    private bool _wantIconsHidden;
    private bool _loading;
    private bool _reloadRequested;
    private bool _startupEnabled;
    private bool _welcomeShown;
    private bool _syncingPin;
    private DateTime _statusHoldUntil;

    public MainWindow(AppSettings settings, TimeSpan? autoExitAfter)
    {
        _settings = settings;
        _autoExitAfter = autoExitAfter;
        _wantIconsHidden = settings.HideDesktopIconsOnStart;
        _startupEnabled = StartupManager.IsEnabled();
        _settings.RunAtStartup = _startupEnabled;

        InitializeComponent();

        Icon = TrayIconFactory.CreateImageSource();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        ItemsList.ItemsSource = _view;
        ApplySidebarAppearance();

        _catalog = new DesktopCatalog();
        _catalog.Changed += OnCatalogChanged;

        _collapseTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _collapseTimer.Tick += CollapseTimer_Tick;

        _guardTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _guardTimer.Tick += GuardTimer_Tick;

        _arrowTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _arrowTimer.Tick += ArrowTimer_Tick;

        _statusResetTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3.4)
        };
        _statusResetTimer.Tick += (_, _) =>
        {
            _statusResetTimer.Stop();
            UpdateIconStatus();
        };

        _syncingPin = true;
        PinButton.IsChecked = _settings.Pinned;
        HideOnStartMenu.IsChecked = _settings.HideDesktopIconsOnStart;
        _syncingPin = false;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;

        DockToScreen();
    }

    public void RevealFromExternal()
    {
        if (_exiting)
        {
            return;
        }

        ExpandPanel(focusSearch: true);
        try
        {
            Activate();
        }
        catch
        {
            // Activation is best effort.
        }
    }

    public void AllowSystemShutdown()
    {
        _exiting = true;
        _collapseTimer.Stop();
        _guardTimer.Stop();
        _arrowTimer.Stop();
        _autoExitTimer?.Stop();
        _statusResetTimer.Stop();
    }

    public void RequestExit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _collapseTimer.Stop();
        _guardTimer.Stop();
        _arrowTimer.Stop();
        _autoExitTimer?.Stop();
        _statusResetTimer.Stop();
        SettingsStore.Save(_settings);
        DesktopIcons.RestoreCapturedState();
        try
        {
            _appearanceWindow?.Close();
        }
        catch
        {
            // The appearance window may already be closing.
        }

        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(
                handle,
                NativeMethods.GwlExStyle,
                new IntPtr(style | NativeMethods.WsExToolWindow));
        }
        catch (Exception ex)
        {
            AppLog.Write("设置工具窗口样式失败。", ex);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DockToScreen();
        SetSlide(0, animate: false);
        SetArrowVisible(false);
        UpdateWindowRegion(expanded: true);
        ApplyInitialDesktopIconState();
        InitializeTray();
        InitializeHotKey();
        UpdatePanelState();
        _guardTimer.Start();
        _arrowTimer.Start();

        if (_autoExitAfter is { } delay)
        {
            _autoExitTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = delay
            };
            _autoExitTimer.Tick += (_, _) => RequestExit();
            _autoExitTimer.Start();
        }
        else if (!_settings.Pinned)
        {
            ScheduleCollapse(4200);
        }

        await LoadItemsAsync();

        if (!_settings.FirstRunTipShown && _autoExitAfter is null)
        {
            _ = Dispatcher.BeginInvoke(ShowFirstRunTip, DispatcherPriority.ApplicationIdle);
        }
    }

    private void InitializeTray()
    {
        if (_tray is not null)
        {
            return;
        }

        _tray = new TrayIconController(
            reveal: RevealFromExternal,
            togglePin: TogglePinFromTray,
            toggleIcons: ToggleIconsFromTray,
            toggleStartup: ToggleStartupFromTray,
            appearance: OpenAppearanceSettings,
            exit: RequestExit);
        UpdateTrayState();
    }

    private void InitializeHotKey()
    {
        if (_hotkey is not null)
        {
            return;
        }

        _hotkey = new GlobalHotKey(
            this,
            ModifierKeys.Control | ModifierKeys.Alt,
            Key.D,
            "Ctrl+Alt+D");

        if (_hotkey.IsRegistered)
        {
            _hotkey.Pressed += (_, _) => Dispatcher.BeginInvoke(TogglePanelFromHotKey);
        }
        else
        {
            HintText.Text = "双击打开 · 通过托盘图标唤出";
        }
    }

    private void TogglePanelFromHotKey()
    {
        if (_exiting)
        {
            return;
        }

        if (_panelOpen)
        {
            if (_settings.Pinned)
            {
                SetPinned(false, persist: true);
            }

            CollapsePanel(ignorePin: true);
        }
        else
        {
            ExpandPanel(focusSearch: true);
        }
    }

    private void ApplyInitialDesktopIconState()
    {
        DesktopIcons.CaptureInitialState();

        if (_settings.HideDesktopIconsOnStart)
        {
            SetIconsHidden(true);
            return;
        }

        _wantIconsHidden = false;
        if (DesktopIcons.TryGetVisible(out var visible))
        {
            _iconsHidden = !visible;
        }

        UpdateIconStatus();
    }

    private void GuardTimer_Tick(object? sender, EventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (!_wantIconsHidden)
        {
            return;
        }

        DesktopIcons.CaptureInitialState();
        if (!DesktopIcons.EnsureHidden())
        {
            return;
        }

        if (!_iconsHidden)
        {
            _iconsHidden = true;
            UpdateIconStatus();
        }
    }

    private void SetIconsHidden(bool hide)
    {
        _wantIconsHidden = hide;
        if (hide)
        {
            DesktopIcons.CaptureInitialState();
        }

        var changed = DesktopIcons.SetVisible(!hide);
        if (DesktopIcons.TryGetVisible(out var visible))
        {
            _iconsHidden = !visible;
        }
        else if (!changed)
        {
            _iconsHidden = false;
        }

        if (!hide)
        {
            _wantIconsHidden = false;
        }

        UpdateIconStatus();
    }

    private void UpdateIconStatus()
    {
        ToggleIconsMenu.Header = _iconsHidden ? "恢复桌面图标" : "隐藏桌面图标";
        UpdateTrayState();

        if (DateTime.UtcNow < _statusHoldUntil)
        {
            return;
        }

        if (_iconsHidden)
        {
            StatusDot.Fill = (Brush)FindResource("SuccessBrush");
            StatusText.Text = "桌面图标已隐藏";
            ToggleIconsButton.Content = "显示图标";
        }
        else if (_wantIconsHidden)
        {
            StatusDot.Fill = (Brush)FindResource("WarningBrush");
            StatusText.Text = "正在等待桌面图标…";
            ToggleIconsButton.Content = "重试";
        }
        else
        {
            StatusDot.Fill = (Brush)FindResource("TextMuted");
            StatusText.Text = "桌面图标已显示";
            ToggleIconsButton.Content = "隐藏图标";
        }
    }

    private void ShowTransientStatus(string text, bool error = false)
    {
        _statusHoldUntil = DateTime.UtcNow.AddSeconds(3.2);
        StatusDot.Fill = (Brush)FindResource(error ? "DangerBrush" : "AccentSoftBrush");
        StatusText.Text = text;
        _statusResetTimer.Stop();
        _statusResetTimer.Start();
    }

    private void ToggleIcons()
    {
        SetIconsHidden(!_iconsHidden);
    }

    private void ToggleIconsFromTray()
    {
        Dispatcher.BeginInvoke(ToggleIcons);
    }

    private void TogglePinFromTray()
    {
        Dispatcher.BeginInvoke(() => SetPinned(!_settings.Pinned, persist: true));
    }

    private void SetPinned(bool pinned, bool persist)
    {
        _settings.Pinned = pinned;
        _syncingPin = true;
        PinButton.IsChecked = pinned;
        PinMenu.IsChecked = pinned;
        _syncingPin = false;

        if (pinned)
        {
            _collapseTimer.Stop();
            ExpandPanel();
        }
        else
        {
            ScheduleCollapse(500);
        }

        if (persist)
        {
            SettingsStore.Save(_settings);
        }

        UpdateTrayState();
    }

    private void ToggleStartupFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var target = !_startupEnabled;
            if (StartupManager.SetEnabled(target))
            {
                _startupEnabled = target;
                _settings.RunAtStartup = target;
                SettingsStore.Save(_settings);
                UpdateTrayState();
            }
            else
            {
                ShowTransientStatus("修改开机启动失败", error: true);
            }
        });
    }

    private void UpdateTrayState()
    {
        _tray?.UpdateState(_panelOpen, _settings.Pinned, _iconsHidden, _startupEnabled);
    }

    private void UpdatePanelState()
    {
        PinButton.IsChecked = _settings.Pinned;
        PinMenu.IsChecked = _settings.Pinned;
        UpdateTrayState();
    }

    private void ExpandPanel(bool focusSearch = false)
    {
        if (_exiting)
        {
            return;
        }

        _collapseTimer.Stop();
        _panelOpen = true;
        SetArrowVisible(false);
        UpdateWindowRegion(expanded: true);
        SetSlide(0, animate: true);
        ForceWindowRedraw();
        UpdateTrayState();

        if (focusSearch)
        {
            try
            {
                Activate();
            }
            catch
            {
                // Best effort.
            }

            Dispatcher.BeginInvoke(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void CollapsePanel(bool ignorePin = false, bool animate = true)
    {
        if (_exiting || (!ignorePin && _settings.Pinned))
        {
            return;
        }

        _collapseTimer.Stop();
        _panelOpen = false;
        SetSlide(CollapsedOffset, animate);
        if (!animate)
        {
            FinishCollapse();
        }

        UpdateTrayState();

        if (SearchBox.IsKeyboardFocusWithin)
        {
            Keyboard.ClearFocus();
        }
    }

    private void ScheduleCollapse(int delayMilliseconds)
    {
        if (_settings.Pinned
            || !_panelOpen
            || _exiting
            || _appearanceOpen
            || _autoExitAfter is not null)
        {
            return;
        }

        _collapseTimer.Stop();
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(delayMilliseconds);
        _collapseTimer.Start();
    }

    private void CollapseTimer_Tick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (_exiting || !_panelOpen || _settings.Pinned || _appearanceOpen)
        {
            return;
        }

        if (IsMouseOver
            || (SearchBox.IsKeyboardFocusWithin && IsActive)
            || SettingsMenu.IsOpen
            || ItemsList.ContextMenu?.IsOpen == true)
        {
            _collapseTimer.Interval = TimeSpan.FromMilliseconds(700);
            _collapseTimer.Start();
            return;
        }

        CollapsePanel();
    }

    private void SetSlide(double offset, bool animate)
    {
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
        if (!animate)
        {
            SlideTransform.X = offset;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = offset,
            Duration = TimeSpan.FromMilliseconds(offset < 1 ? 260 : 300),
            EasingFunction = new CubicEase
            {
                EasingMode = offset < 1 ? EasingMode.EaseOut : EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };

        if (offset > 0)
        {
            animation.Completed += (_, _) =>
            {
                if (!_panelOpen)
                {
                    FinishCollapse();
                }
            };
        }
        else
        {
            animation.Completed += (_, _) => ForceWindowRedraw();
        }

        SlideTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void FinishCollapse()
    {
        if (_panelOpen)
        {
            return;
        }

        UpdateLayout();
        RefreshArrowForCursor();
        UpdateWindowRegion(expanded: false);
    }

    private void SetArrowVisible(bool visible)
    {
        _arrowShown = visible;
        if (visible)
        {
            ArrowTab.Opacity = Math.Clamp(_settings.ArrowOpacity, 0.15, 1.0);
            ArrowTab.Visibility = Visibility.Visible;
        }
        else
        {
            ArrowTab.Visibility = Visibility.Collapsed;
            ArrowTab.Opacity = 0;
        }

        UpdateLayout();
    }

    private void ArrowTimer_Tick(object? sender, EventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        if (IsFullscreenAppActive())
        {
            SuppressForFullscreen();
            return;
        }

        if (_fullscreenActive)
        {
            _fullscreenActive = false;
            AppLog.Write("全屏应用已退出，恢复侧边栏自动弹出。");
        }

        if (_panelOpen)
        {
            return;
        }

        if (IsCursorNearRightEdge())
        {
            _arrowHideAt = DateTime.MaxValue;
            if (!_arrowShown)
            {
                ShowArrowAtEdge();
            }

            return;
        }

        if (_arrowShown)
        {
            if (_arrowHideAt == DateTime.MaxValue)
            {
                _arrowHideAt = DateTime.UtcNow.AddMilliseconds(1100);
            }
            else if (DateTime.UtcNow >= _arrowHideAt)
            {
                HideArrow();
            }
        }
    }

    private void RefreshArrowForCursor()
    {
        if (IsFullscreenAppActive() || _panelOpen || !IsCursorNearRightEdge())
        {
            HideArrow();
            return;
        }

        ShowArrowAtEdge();
    }

    private void SuppressForFullscreen()
    {
        if (!_fullscreenActive)
        {
            _fullscreenActive = true;
            AppLog.Write("检测到全屏应用，暂停侧边栏自动弹出。");
        }

        _arrowHideAt = DateTime.MaxValue;
        HideArrow();

        if (_panelOpen)
        {
            CollapsePanel(ignorePin: true);
        }
    }

    private static bool IsFullscreenNotificationState()
    {
        return NativeMethods.SHQueryUserNotificationState(out var state) == 0
            && (state is NativeMethods.QunsBusy
                or NativeMethods.QunsRunningD3DFullScreen
                or NativeMethods.QunsPresentationMode);
    }

    private bool IsFullscreenAppActive()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return IsFullscreenNotificationState();
            }

            NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            if (processId == (uint)Environment.ProcessId)
            {
                return false;
            }

            var className = new StringBuilder(64);
            NativeMethods.GetClassName(foreground, className, className.Capacity);
            var name = className.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                return false;
            }

            if (!NativeMethods.IsWindowVisible(foreground))
            {
                return false;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var ownMonitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MonitorDefaultToNearest);
            var foregroundMonitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
            if (ownMonitor == IntPtr.Zero || foregroundMonitor != ownMonitor)
            {
                return false;
            }

            if (IsFullscreenNotificationState())
            {
                return true;
            }

            if (NativeMethods.IsZoomed(foreground))
            {
                return false;
            }

            if (!NativeMethods.GetWindowRect(foreground, out var windowRect))
            {
                return false;
            }

            var monitorInfo = new NativeMethods.MonitorInfo
            {
                Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
            };
            if (!NativeMethods.GetMonitorInfo(foregroundMonitor, ref monitorInfo))
            {
                return false;
            }

            const int tolerance = 2;
            return windowRect.Left <= monitorInfo.Monitor.Left + tolerance
                && windowRect.Top <= monitorInfo.Monitor.Top + tolerance
                && windowRect.Right >= monitorInfo.Monitor.Right - tolerance
                && windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
        }
        catch (Exception ex)
        {
            AppLog.Write("检测全屏应用失败。", ex);
            return false;
        }
    }

    private void ShowArrowAtEdge()
    {
        _arrowHideAt = DateTime.MaxValue;
        if (_arrowShown)
        {
            return;
        }

        SetArrowVisible(true);
        UpdateWindowRegion(expanded: false);
    }

    private void HideArrow()
    {
        _arrowHideAt = DateTime.MaxValue;
        if (!_arrowShown)
        {
            return;
        }

        SetArrowVisible(false);
        UpdateWindowRegion(expanded: false);
    }

    private bool IsCursorNearRightEdge()
    {
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        var screenWidth = NativeMethods.GetSystemMetrics(0);
        var dpi = VisualTreeHelper.GetDpi(this);
        var threshold = Math.Max(6, (int)Math.Ceiling(9 * dpi.DpiScaleX));
        return point.X >= screenWidth - threshold;
    }

    private void ForceWindowRedraw()
    {
        Root.InvalidateVisual();
        UpdateLayout();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            NativeMethods.RedrawWindow(
                handle,
                IntPtr.Zero,
                IntPtr.Zero,
                NativeMethods.RdwInvalidate
                | NativeMethods.RdwErase
                | NativeMethods.RdwAllChildren
                | NativeMethods.RdwUpdateNow
                | NativeMethods.RdwFrame);
        }
    }

    private void DockToScreen()
    {
        var area = SystemParameters.WorkArea;
        Width = PanelWidth;
        Height = Math.Max(MinHeight, area.Height);
        Left = Math.Round(area.Right - PanelWidth);
        Top = Math.Round(area.Top);
        PanelRoot.Width = PanelWidth;

        if (IsLoaded)
        {
            UpdateWindowRegion(_panelOpen);
        }
    }

    private void UpdateWindowRegion(bool expanded)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            var left = 0;
            var top = 0;
            var right = width;
            var bottom = height;

            if (!expanded && !_arrowShown)
            {
                left = Math.Max(0, width - 1);
                top = 0;
                right = width;
                bottom = 1;
            }
            else if (!expanded)
            {
                var arrowWidth = ArrowTab.ActualWidth > 0 ? ArrowTab.ActualWidth : ArrowWidth;
                var arrowHeight = ArrowTab.ActualHeight > 0 ? ArrowTab.ActualHeight : ArrowHeight;
                var padding = (int)Math.Ceiling(5 * dpi.DpiScaleX);
                left = Math.Max(0,
                    (int)Math.Floor((ActualWidth - arrowWidth) * dpi.DpiScaleX) - padding);
                top = Math.Max(0,
                    (int)Math.Floor((ActualHeight - arrowHeight) / 2 * dpi.DpiScaleY) - padding);
                right = Math.Min(width,
                    (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX) + padding);
                bottom = Math.Min(height,
                    (int)Math.Ceiling(((ActualHeight - arrowHeight) / 2 + arrowHeight) * dpi.DpiScaleY)
                    + padding);
            }

            var region = NativeMethods.CreateRectRgn(left, top, right, bottom);
            if (region == IntPtr.Zero)
            {
                return;
            }

            if (NativeMethods.SetWindowRgn(handle, region, true) == 0)
            {
                NativeMethods.DeleteObject(region);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("设置窗口可点击区域失败。", ex);
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            Dispatcher.BeginInvoke(DockToScreen, DispatcherPriority.Background);
        }
    }

    private async Task LoadItemsAsync()
    {
        if (_loading)
        {
            _reloadRequested = true;
            return;
        }

        _loading = true;
        UpdateViewState();

        try
        {
            var loaded = await _catalog.LoadAsync(CancellationToken.None);
            _items.Clear();
            foreach (var item in loaded)
            {
                _items.Add(item);
            }

            _view.Refresh();
        }
        catch (Exception ex)
        {
            AppLog.Write("读取桌面图标失败。", ex);
            ShowTransientStatus("读取桌面图标失败", error: true);
        }
        finally
        {
            _loading = false;
            UpdateViewState();

            if (_reloadRequested && !_exiting)
            {
                _reloadRequested = false;
                _ = LoadItemsAsync();
            }
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_exiting)
            {
                _ = LoadItemsAsync();
            }
        }, DispatcherPriority.Background);
    }

    private bool FilterItem(object item)
    {
        if (item is not DesktopItem desktopItem)
        {
            return false;
        }

        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            return true;
        }

        return desktopItem.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
               || desktopItem.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateViewState()
    {
        var total = _items.Count;
        var visible = _view.Cast<object>().Count();
        var filtering = !string.IsNullOrWhiteSpace(SearchBox.Text);

        if (_loading && total == 0)
        {
            CountText.Text = "读取中…";
            EmptyState.Visibility = Visibility.Visible;
            EmptyGlyph.Text = "\uE8B7";
            EmptyTitle.Text = "正在读取桌面…";
            EmptySubtitle.Text = "桌面上的图标会出现在这里";
            return;
        }

        if (_loading)
        {
            CountText.Text = "刷新中…";
        }
        else
        {
            CountText.Text = filtering ? $"{visible} / {total} 个" : $"{total} 个";
        }

        if (visible > 0)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Visible;
        if (total == 0)
        {
            EmptyGlyph.Text = "\uE8B7";
            EmptyTitle.Text = "桌面还没有图标";
            EmptySubtitle.Text = "把文件或快捷方式放到桌面即可";
        }
        else
        {
            EmptyGlyph.Text = "\uE721";
            EmptyTitle.Text = "没有找到匹配项";
            EmptySubtitle.Text = "换个关键词试试看";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var empty = string.IsNullOrEmpty(SearchBox.Text);
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        _view.Refresh();
        UpdateViewState();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        var container = FindAncestor<ListBoxItem>(element);
        if (container?.DataContext is DesktopItem item)
        {
            OpenItem(item);
        }
    }

    private void ItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var item = ItemsList.SelectedItem as DesktopItem
                       ?? _view.Cast<DesktopItem>().FirstOrDefault();
            if (item is not null)
            {
                OpenItem(item);
                e.Handled = true;
            }
        }
    }

    private void ItemsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container is not null)
        {
            container.IsSelected = true;
        }
    }

    private void ItemsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        _contextItem = ItemsList.SelectedItem as DesktopItem;
        if (_contextItem is null)
        {
            e.Handled = true;
        }
    }

    private void OpenItem(DesktopItem? item)
    {
        if (item is null || _exiting)
        {
            return;
        }

        try
        {
            var workingDirectory = item.IsFolder
                ? Directory.GetParent(item.Path)?.FullName
                : Path.GetDirectoryName(item.Path);
            Process.Start(new ProcessStartInfo(item.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = workingDirectory ?? AppContext.BaseDirectory
            });

            Keyboard.ClearFocus();
            if (!_settings.Pinned)
            {
                ScheduleCollapse(1100);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"打开桌面项目失败: {item.Path}", ex);
            ShowTransientStatus("无法打开这个图标", error: true);
        }
    }

    private void OpenLocation(DesktopItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var argument = item.IsFolder
                ? $"\"{item.Path}\""
                : $"/select,\"{item.Path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", argument)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("打开文件位置失败。", ex);
            ShowTransientStatus("无法打开所在位置", error: true);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void ShowFirstRunTip()
    {
        if (_welcomeShown || _exiting)
        {
            return;
        }

        _welcomeShown = true;
        var detail = _iconsHidden
            ? "桌面图标已收进右侧边栏。悬停屏幕边缘的箭头即可展开，Ctrl+Alt+D 也能打开。"
            : "侧边栏已就位。悬停屏幕边缘的箭头即可展开，Ctrl+Alt+D 也能打开。";
        _tray?.ShowBalloon("SideDesk 已就绪", detail);
        _settings.FirstRunTipShown = true;
        SettingsStore.Save(_settings);
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (IsFullscreenAppActive())
        {
            SuppressForFullscreen();
            return;
        }

        if (!_panelOpen)
        {
            ExpandPanel();
        }
        else
        {
            _collapseTimer.Stop();
        }
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        ScheduleCollapse(720);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_settings.Pinned)
        {
            ScheduleCollapse(450);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsMenu.IsOpen)
            {
                SettingsMenu.IsOpen = false;
            }
            else if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Clear();
            }
            else if (_settings.Pinned)
            {
                SetPinned(false, persist: true);
                CollapsePanel(ignorePin: true);
            }
            else
            {
                CollapsePanel();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            _ = LoadItemsAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_panelOpen)
            {
                ExpandPanel(focusSearch: true);
            }
            else
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin)
        {
            var first = _view.Cast<DesktopItem>().FirstOrDefault();
            if (first is not null)
            {
                OpenItem(first);
                e.Handled = true;
            }
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            CollapsePanel(ignorePin: true);
            return;
        }

        _guardTimer.Stop();
        _collapseTimer.Stop();
        _arrowTimer.Stop();
        _statusResetTimer.Stop();
        _autoExitTimer?.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        _catalog.Changed -= OnCatalogChanged;
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;
        _catalog.Dispose();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void ArrowTab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsFullscreenAppActive())
        {
            SuppressForFullscreen();
            e.Handled = true;
            return;
        }

        ExpandPanel();
        e.Handled = true;
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Pinned)
        {
            SetPinned(false, persist: true);
        }

        CollapsePanel(ignorePin: true);
    }

    public void ApplySidebarAppearance()
    {
        AppearanceTheme.ApplySidebar(_settings, PanelRoot, LogoBadge, ArrowTab, AccentDot);
        ApplyArrowOpacity();
    }

    public void ApplyArrowOpacity()
    {
        if (_arrowShown)
        {
            ArrowTab.Opacity = Math.Clamp(_settings.ArrowOpacity, 0.15, 1.0);
        }
    }

    private void AppearanceMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenAppearanceSettings();
    }

    public void OpenAppearanceSettings()
    {
        if (_exiting)
        {
            return;
        }

        if (_appearanceWindow is not null)
        {
            _appearanceWindow.Activate();
            return;
        }

        _appearanceOpen = true;
        ExpandPanel();
        _collapseTimer.Stop();

        var window = new AppearanceWindow(_settings, this)
        {
            Owner = this
        };
        _appearanceWindow = window;
        window.Closed += (_, _) =>
        {
            _appearanceWindow = null;
            _appearanceOpen = false;
            SettingsStore.Save(_settings);
            UpdateTrayState();
            if (!_settings.Pinned)
            {
                ScheduleCollapse(600);
            }
        };

        try
        {
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            AppLog.Write("打开外观设置窗口失败。", ex);
        }
        finally
        {
            _appearanceWindow = null;
            _appearanceOpen = false;
            SettingsStore.Save(_settings);
            UpdateTrayState();
            if (!_settings.Pinned && !_exiting)
            {
                ScheduleCollapse(600);
            }
        }
    }

    private void PinButton_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingPin || _exiting)
        {
            return;
        }

        SetPinned(PinButton.IsChecked == true, persist: true);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        SyncSettingsMenu();
        SettingsMenu.PlacementTarget = MoreButton;
        SettingsMenu.Placement = PlacementMode.Bottom;
        SettingsMenu.IsOpen = true;
    }

    private void SettingsMenu_Opened(object sender, RoutedEventArgs e)
    {
        SyncSettingsMenu();
    }

    private void SyncSettingsMenu()
    {
        PinMenu.IsChecked = _settings.Pinned;
        StartupMenu.IsChecked = _startupEnabled;
        HideOnStartMenu.IsChecked = _settings.HideDesktopIconsOnStart;
        ToggleIconsMenu.Header = _iconsHidden ? "恢复桌面图标" : "隐藏桌面图标";
    }

    private void PinMenu_Click(object sender, RoutedEventArgs e)
    {
        SetPinned(PinMenu.IsChecked, persist: true);
    }

    private void StartupMenu_Click(object sender, RoutedEventArgs e)
    {
        var target = StartupMenu.IsChecked;
        if (StartupManager.SetEnabled(target))
        {
            _startupEnabled = target;
            _settings.RunAtStartup = target;
            SettingsStore.Save(_settings);
            UpdateTrayState();
        }
        else
        {
            StartupMenu.IsChecked = _startupEnabled;
            ShowTransientStatus("修改开机启动失败", error: true);
        }
    }

    private void HideOnStartMenu_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideDesktopIconsOnStart = HideOnStartMenu.IsChecked;
        SettingsStore.Save(_settings);

        if (HideOnStartMenu.IsChecked && !_iconsHidden)
        {
            SetIconsHidden(true);
        }
    }

    private void ToggleIconsMenu_Click(object sender, RoutedEventArgs e)
    {
        ToggleIcons();
    }

    private void OpenDesktopMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenDesktopFolder();
    }

    private void RefreshMenu_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadItemsAsync();
    }

    private void ExitMenu_Click(object sender, RoutedEventArgs e)
    {
        RequestExit();
    }

    private void OpenContextMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenItem(_contextItem);
    }

    private void OpenLocationContextMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenLocation(_contextItem);
    }

    private void CopyPathContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_contextItem is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(_contextItem.Path);
            ShowTransientStatus("路径已复制");
        }
        catch (Exception ex)
        {
            AppLog.Write("复制路径失败。", ex);
            ShowTransientStatus("复制路径失败", error: true);
        }
    }

    private void ToggleIconsButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleIcons();
    }

    private void OpenDesktopButton_Click(object sender, RoutedEventArgs e)
    {
        OpenDesktopFolder();
    }

    private void OpenDesktopFolder()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{desktop}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("打开桌面文件夹失败。", ex);
            ShowTransientStatus("无法打开桌面文件夹", error: true);
        }
    }
}
