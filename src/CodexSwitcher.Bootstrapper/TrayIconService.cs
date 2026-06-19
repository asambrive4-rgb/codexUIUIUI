using System.Drawing;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace CodexSwitcher.Bootstrapper;

internal sealed class TrayIconService : IDisposable
{
    private const string ApplicationName =
        "Codex Account Switcher";
    private const int MaximumTooltipLength = 63;

    private readonly Icon _icon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly NotifyIcon _notifyIcon;

    public TrayIconService(
        Action openApplication,
        Action exitApplication)
    {
        _icon = LoadIcon();

        var openItem = new ToolStripMenuItem("열기");
        openItem.Click += (_, _) => openApplication();

        var exitItem = new ToolStripMenuItem("앱 종료");
        exitItem.Click += (_, _) => exitApplication();

        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false
        };
        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = _contextMenu,
            Icon = _icon,
            Text = ApplicationName,
            Visible = true
        };
        _notifyIcon.DoubleClick +=
            (_, _) => openApplication();
    }

    public void UpdateStatus(string status)
    {
        var tooltip = string.IsNullOrWhiteSpace(status)
            ? ApplicationName
            : $"{ApplicationName} · {status}";
        _notifyIcon.Text = tooltip.Length <= MaximumTooltipLength
            ? tooltip
            : tooltip[..MaximumTooltipLength];
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _icon.Dispose();
    }

    private static Icon LoadIcon()
    {
        var resource = WpfApplication.GetResourceStream(
            new Uri(
                "pack://application:,,,/Assets/codexUIUIUI.ico"));
        if (resource is null)
        {
            throw new InvalidOperationException(
                "트레이 아이콘 리소스를 찾을 수 없습니다.");
        }

        using (resource.Stream)
        using (var sourceIcon = new Icon(resource.Stream))
        {
            return (Icon)sourceIcon.Clone();
        }
    }
}
