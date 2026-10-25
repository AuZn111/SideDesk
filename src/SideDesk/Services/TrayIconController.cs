using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SideDesk.Services;

public sealed class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _showItem;
    private readonly ToolStripMenuItem _pinItem;
    private readonly ToolStripMenuItem _iconsItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _icon;

    private bool _panelOpen = true;
    private bool _pinned;
    private bool _iconsHidden;
    private bool _startup;
    private bool _disposed;

    public TrayIconController(
        Action reveal,
        Action togglePin,
        Action toggleIcons,
        Action toggleStartup,
        Action appearance,
        Action exit)
    {
        _icon = TrayIconFactory.CreateIcon();
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = Color.FromArgb(25, 28, 38),
            ForeColor = Color.FromArgb(238, 242, 249),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(5),
            Font = new Font("Microsoft YaHei UI", 9f),
            DropShadowEnabled = true
        };

        _showItem = CreateItem("收起侧栏", reveal);
        _pinItem = CreateCheckItem("保持展开", togglePin);
        _iconsItem = CreateItem("恢复桌面图标", toggleIcons);
        _startupItem = CreateCheckItem("开机自动启动", toggleStartup);

        _menu.Items.Add(_showItem);
        _menu.Items.Add(_pinItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_iconsItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("外观设置…", appearance));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(CreateItem("退出 SideDesk", exit));
        _menu.Opening += (_, _) => SyncChecks();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "SideDesk 桌面侧边栏",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == MouseButtons.Left)
            {
                reveal();
            }
        };
        _notifyIcon.DoubleClick += (_, _) => reveal();
    }

    public void UpdateState(
        bool panelOpen,
        bool pinned,
        bool iconsHidden,
        bool startupEnabled)
    {
        _panelOpen = panelOpen;
        _pinned = pinned;
        _iconsHidden = iconsHidden;
        _startup = startupEnabled;
        SyncChecks();
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            _notifyIcon.ShowBalloonTip(4500);
        }
        catch (Exception ex)
        {
            AppLog.Write("托盘气泡提示失败。", ex);
        }
    }

    private ToolStripMenuItem CreateItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.FromArgb(238, 242, 249),
            BackColor = Color.Transparent,
            Padding = new Padding(2, 3, 2, 3)
        };
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStripMenuItem CreateCheckItem(string text, Action action)
    {
        var item = CreateItem(text, action);
        item.CheckOnClick = false;
        return item;
    }

    private void SyncChecks()
    {
        if (_disposed)
        {
            return;
        }

        _showItem.Text = _panelOpen ? "收起侧栏" : "显示侧栏";
        _pinItem.Checked = _pinned;
        _iconsItem.Text = _iconsHidden ? "恢复桌面图标" : "隐藏桌面图标";
        _startupItem.Checked = _startup;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _menu.Dispose();
            _icon.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write("释放托盘图标失败。", ex);
        }
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Background = Color.FromArgb(25, 28, 38);
        private static readonly Color Hover = Color.FromArgb(44, 47, 62);
        private static readonly Color Border = Color.FromArgb(60, 63, 78);

        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.FromArgb(139, 92, 246);
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color CheckBackground => Color.FromArgb(70, 139, 92, 246);
        public override Color CheckSelectedBackground => Color.FromArgb(90, 139, 92, 246);
        public override Color CheckPressedBackground => Color.FromArgb(110, 139, 92, 246);
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = e.ImageRectangle;
            using var pen = new Pen(Color.FromArgb(196, 181, 253), 2f);
            var start = new PointF(bounds.Left + 4, bounds.Top + bounds.Height * 0.52f);
            var middle = new PointF(bounds.Left + bounds.Width * 0.44f, bounds.Bottom - 4);
            var end = new PointF(bounds.Right - 2, bounds.Top + 4);
            graphics.DrawLines(pen, new[] { start, middle, end });
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(55, 58, 72));
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.DrawRectangle(pen, bounds);
        }
    }
}
