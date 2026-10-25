using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SideDesk.Services;

namespace SideDesk;

public partial class AppearanceWindow : Window
{
    private static readonly (string Hex, string Name)[] Palette =
    {
        ("#8B5CF6", "星云紫"),
        ("#3B82F6", "深海蓝"),
        ("#06B6D4", "冰川青"),
        ("#10B981", "翡翠绿"),
        ("#F59E0B", "琥珀金"),
        ("#EF4444", "珊瑚红"),
        ("#EC4899", "玫瑰粉"),
        ("#64748B", "石墨灰")
    };

    private readonly AppSettings _settings;
    private readonly MainWindow _owner;
    private bool _loading = true;

    public AppearanceWindow(AppSettings settings, MainWindow owner)
    {
        _settings = settings;
        _owner = owner;
        InitializeComponent();
        Height = Math.Min(660, Math.Max(520, SystemParameters.WorkArea.Height - 32));

        SidebarOpacitySlider.Value = Math.Clamp(_settings.SidebarOpacity, 0.35, 1.0) * 100;
        ArrowOpacitySlider.Value = Math.Clamp(_settings.ArrowOpacity, 0.15, 1.0) * 100;
        SidebarHexBox.Text = _settings.SidebarColor;
        BuildSwatches(SidebarSwatches, _settings.SidebarColor);
        _loading = false;

        UpdateSidebarLabels();
        UpdateArrowLabel();
        UpdatePreview();
    }

    private void BuildSwatches(WrapPanel panel, string selectedHex)
    {
        foreach (var (hex, name) in Palette)
        {
            var button = new Button
            {
                Style = (Style)FindResource("ColorSwatch"),
                Background = ColorUtil.Brush(ColorUtil.Parse(hex, AppearanceTheme.DefaultAccent)),
                Tag = hex,
                ToolTip = name
            };
            AutomationProperties.SetName(button, "侧边栏颜色 " + name);
            button.Click += SidebarSwatch_Click;
            panel.Children.Add(button);
        }

        UpdateSwatchSelection(panel, selectedHex);
    }

    private static void UpdateSwatchSelection(WrapPanel panel, string selectedHex)
    {
        var selected = ColorUtil.NormalizeHex(selectedHex, "#000000");
        foreach (var child in panel.Children)
        {
            if (child is Button button)
            {
                var hex = button.Tag as string;
                button.BorderBrush = string.Equals(
                    ColorUtil.NormalizeHex(hex, "#000000"),
                    selected,
                    StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent;
            }
        }
    }

    private void SidebarSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not Button button || button.Tag is not string hex)
        {
            return;
        }

        SetSidebarColor(hex);
    }

    private void SetSidebarColor(string hex)
    {
        var normalized = ColorUtil.NormalizeHex(hex, _settings.SidebarColor);
        _settings.SidebarColor = normalized;
        SidebarHexBox.Text = normalized;
        SidebarHexBox.ClearValue(Control.BorderBrushProperty);
        UpdateSwatchSelection(SidebarSwatches, normalized);
        _owner.ApplySidebarAppearance();
        UpdatePreview();
    }

    private void SidebarOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.SidebarOpacity = Math.Clamp(e.NewValue / 100.0, 0.35, 1.0);
        UpdateSidebarLabels();
        _owner.ApplySidebarAppearance();
        UpdatePreview();
    }

    private void ArrowOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading)
        {
            return;
        }

        _settings.ArrowOpacity = Math.Clamp(e.NewValue / 100.0, 0.15, 1.0);
        UpdateArrowLabel();
        _owner.ApplyArrowOpacity();
    }

    private void UpdateSidebarLabels()
    {
        SidebarOpacityValue.Text = $"{Math.Round(_settings.SidebarOpacity * 100)}%";
    }

    private void UpdateArrowLabel()
    {
        ArrowOpacityValue.Text = $"{Math.Round(_settings.ArrowOpacity * 100)}%";
    }

    private void UpdatePreview()
    {
        var accent = ColorUtil.Parse(_settings.SidebarColor, AppearanceTheme.DefaultAccent);
        SidebarPreview.Background = AppearanceTheme.CreatePanelGradient(accent, _settings.SidebarOpacity);
        SidebarPreview.BorderBrush = ColorUtil.Brush(ColorUtil.WithAlpha(ColorUtil.Lighten(accent, 0.5), 0.35));
        DialogLogo.Background = AppearanceTheme.CreateAccentGradient(accent);
    }

    private void ApplySidebarHex_Click(object sender, RoutedEventArgs e)
    {
        if (!ColorUtil.TryNormalizeHex(SidebarHexBox.Text, out var normalized))
        {
            SidebarHexBox.BorderBrush = (Brush)FindResource("DangerBrush");
            return;
        }

        SetSidebarColor(normalized);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        _settings.SidebarColor = "#8B5CF6";
        _settings.SidebarOpacity = 0.90;
        _settings.ArrowOpacity = 0.50;

        _loading = true;
        SidebarHexBox.Text = _settings.SidebarColor;
        SidebarOpacitySlider.Value = _settings.SidebarOpacity * 100;
        ArrowOpacitySlider.Value = _settings.ArrowOpacity * 100;
        _loading = false;

        UpdateSwatchSelection(SidebarSwatches, _settings.SidebarColor);
        UpdateSidebarLabels();
        UpdateArrowLabel();
        _owner.ApplySidebarAppearance();
        UpdatePreview();
        SettingsStore.Save(_settings);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Dragging can fail if the mouse is released too quickly.
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SettingsStore.Save(_settings);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
