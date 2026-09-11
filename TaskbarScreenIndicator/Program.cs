using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Microsoft.Win32;

namespace TaskbarScreenIndicator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--inspect", StringComparer.OrdinalIgnoreCase))
        {
            TaskbarInspector.WriteReport();
            return;
        }

        Application.Run(new IndicatorApplicationContext());
    }
}

internal sealed class IndicatorApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly Dictionary<nint, IndicatorOverlay> overlays = [];
    private bool enabled = true;

    public IndicatorApplicationContext()
    {
        var menu = new ContextMenuStrip();
        var enabledItem = new ToolStripMenuItem("显示任务栏屏幕标记") { Checked = true, CheckOnClick = true };
        enabledItem.CheckedChanged += (_, _) =>
        {
            enabled = enabledItem.Checked;
            RefreshIndicators();
        };
        menu.Items.Add(enabledItem);
        menu.Items.Add("立即刷新", null, (_, _) => RefreshIndicators());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "任务栏屏幕标记",
            Visible = true,
            ContextMenuStrip = menu,
        };
        trayIcon.DoubleClick += (_, _) => RefreshIndicators();

        refreshTimer = new System.Windows.Forms.Timer { Interval = 900 };
        refreshTimer.Tick += (_, _) => RefreshIndicators();
        refreshTimer.Start();
        RefreshIndicators();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RefreshIndicators();

    private void RefreshIndicators()
    {
        if (!enabled)
        {
            foreach (var overlay in overlays.Values)
                overlay.Hide();
            return;
        }

        var taskbars = NativeMethods.FindTaskbars();
        var used = new HashSet<nint>();
        var windows = WindowCatalog.GetWindows();
        var screens = ScreenLayout.GetScreens();

        foreach (var taskbar in taskbars)
        {
            used.Add(taskbar);
            if (!NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
                continue;

            if (!overlays.TryGetValue(taskbar, out var overlay))
            {
                overlay = new IndicatorOverlay();
                overlays.Add(taskbar, overlay);
            }

            overlay.Update(taskbarRect.ToRectangle(), TaskbarInspector.GetIndicators(taskbar, windows, screens));
        }

        foreach (var pair in overlays.Where(pair => !used.Contains(pair.Key)).ToArray())
        {
            pair.Value.Dispose();
            overlays.Remove(pair.Key);
        }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        refreshTimer.Stop();
        refreshTimer.Dispose();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        foreach (var overlay in overlays.Values)
            overlay.Dispose();
        overlays.Clear();
        base.ExitThreadCore();
    }
}

internal sealed class IndicatorOverlay : Form
{
    private IReadOnlyList<Indicator> indicators = [];
    private Rectangle taskbarBounds;

    public IndicatorOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TRANSPARENT = 0x20;
            const int WS_EX_LAYERED = 0x80000;
            const int WS_EX_NOACTIVATE = 0x8000000;
            const int WS_EX_TOOLWINDOW = 0x80;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    public void Update(Rectangle taskbar, IReadOnlyList<Indicator> updatedIndicators)
    {
        taskbarBounds = taskbar;
        indicators = updatedIndicators;
        if (Bounds != taskbar)
            Bounds = taskbar;
        if (!Visible)
            Show();
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, taskbar.Left, taskbar.Top,
            taskbar.Width, taskbar.Height, NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        Invalidate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        const int height = 4;
        foreach (var indicator in indicators)
        {
            var bounds = indicator.Bounds;
            bounds.Offset(-taskbarBounds.Left, -taskbarBounds.Top);
            var usableWidth = Math.Max(8, bounds.Width - 12);
            var left = bounds.Left + Math.Max(4, (bounds.Width - usableWidth) / 2);
            var y = bounds.Bottom - height - 2;
            var colors = indicator.Colors;
            for (var i = 0; i < colors.Count; i++)
            {
                var segmentLeft = left + usableWidth * i / colors.Count;
                var segmentRight = left + usableWidth * (i + 1) / colors.Count;
                using var brush = new SolidBrush(colors[i]);
                e.Graphics.FillRectangle(brush, segmentLeft, y, Math.Max(2, segmentRight - segmentLeft), height);
            }
        }
    }
}

internal sealed record ScreenInfo(nint Handle, Rectangle Bounds, string Label, Color Color);
internal sealed record WindowInfo(nint Handle, string Title, string ProcessName, string ProductName, string AppId, ScreenInfo Screen);
internal sealed record Indicator(Rectangle Bounds, IReadOnlyList<Color> Colors);

internal static class ScreenLayout
{
    private static readonly Color[] Palette = [
        Color.FromArgb(0, 176, 255), Color.FromArgb(255, 156, 0), Color.FromArgb(117, 220, 72),
        Color.FromArgb(220, 70, 160), Color.FromArgb(240, 215, 65), Color.FromArgb(86, 104, 255),
    ];

    public static IReadOnlyList<ScreenInfo> GetScreens()
    {
        var primary = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        return Screen.AllScreens.Select((screen, index) =>
        {
            var label = screen.Primary ? "主屏幕" : GetLocationLabel(screen.Bounds, primary);
            return new ScreenInfo((nint)screen.GetHashCode(), screen.Bounds, label, Palette[index % Palette.Length]);
        }).ToArray();
    }

    public static ScreenInfo? ScreenForWindow(nint hwnd, IReadOnlyList<ScreenInfo> screens)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return null;
        var center = new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        return screens.OrderBy(screen => DistanceToRectangle(center, screen.Bounds)).FirstOrDefault();
    }

    private static string GetLocationLabel(Rectangle bounds, Rectangle primary)
    {
        if (bounds.Left >= primary.Right) return "右侧屏幕";
        if (bounds.Right <= primary.Left) return "左侧屏幕";
        if (bounds.Top >= primary.Bottom) return "下方屏幕";
        return "上方屏幕";
    }

    private static long DistanceToRectangle(Point point, Rectangle rectangle)
    {
        var dx = Math.Max(rectangle.Left - point.X, 0, point.X - rectangle.Right);
        var dy = Math.Max(rectangle.Top - point.Y, 0, point.Y - rectangle.Bottom);
        return (long)dx * dx + (long)dy * dy;
    }
}

internal static class WindowCatalog
{
    public static IReadOnlyList<WindowInfo> GetWindows()
    {
        var screens = ScreenLayout.GetScreens();
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsToolWindow(hwnd))
                return true;
            var title = NativeMethods.GetWindowText(hwnd);
            if (string.IsNullOrWhiteSpace(title))
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            var processName = "";
            var productName = "";
            var appId = "";
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
                if (!string.IsNullOrEmpty(process.MainModule?.FileName))
                {
                    var info = FileVersionInfo.GetVersionInfo(process.MainModule.FileName);
                    productName = info.ProductName ?? info.FileDescription ?? "";
                }
                appId = NativeMethods.GetAppUserModelId(process.Handle);
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }

            var screen = ScreenLayout.ScreenForWindow(hwnd, screens);
            if (screen is not null)
                windows.Add(new WindowInfo(hwnd, title, processName, productName, appId, screen));
            return true;
        }, nint.Zero);
        return windows;
    }
}

internal static class TaskbarInspector
{
    public static IReadOnlyList<Indicator> GetIndicators(nint taskbar, IReadOnlyList<WindowInfo> windows, IReadOnlyList<ScreenInfo> screens)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, condition).Cast<AutomationElement>();
            return buttons.Select(button => GetIndicator(button, windows, screens))
                .Where(indicator => indicator is not null)
                .Cast<Indicator>()
                .ToArray();
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    private static Indicator? GetIndicator(AutomationElement button, IReadOnlyList<WindowInfo> windows, IReadOnlyList<ScreenInfo> screens)
    {
        try
        {
            var bounds = button.Current.BoundingRectangle;
            var name = button.Current.Name;
            if (bounds.Width < 20 || bounds.Height < 20 || string.IsNullOrWhiteSpace(name))
                return null;

            var matched = MatchWindows(name, windows);
            if (matched.Count == 0)
                return null;
            var colors = matched.Select(window => window.Screen.Color).Distinct().ToArray();
            return new Indicator(Rectangle.Round(bounds), colors);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static IReadOnlyList<WindowInfo> MatchWindows(string taskbarName, IReadOnlyList<WindowInfo> windows)
    {
        var key = Normalize(taskbarName);
        var scored = windows.Select(window => (Window: window, Score: Score(key, window))).Where(item => item.Score > 0).ToArray();
        if (scored.Length == 0)
            return [];
        var best = scored.Max(item => item.Score);
        return scored.Where(item => item.Score >= Math.Max(45, best - 15)).Select(item => item.Window).ToArray();
    }

    private static int Score(string key, WindowInfo window)
    {
        var title = Normalize(window.Title);
        var process = Normalize(window.ProcessName);
        var product = Normalize(window.ProductName);
        if (key == title) return 100;
        if (title.Length >= 4 && (key.Contains(title) || title.Contains(key))) return 85;
        if (product.Length >= 4 && (key.Contains(product) || product.Contains(key))) return 70;
        if (process.Length >= 4 && (key.Contains(process) || process.Contains(key))) return 55;
        return 0;
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[^\\p{L}\\p{N}]", "");

    public static void WriteReport()
    {
        var windows = WindowCatalog.GetWindows();
        Console.WriteLine("OPEN WINDOWS");
        foreach (var window in windows)
            Console.WriteLine($"{window.Screen.Label} | {window.ProcessName} | {window.ProductName} | {window.Title}");
        Console.WriteLine("TASKBAR BUTTONS");
        foreach (var taskbar in NativeMethods.FindTaskbars())
        {
            Console.WriteLine($"TASKBAR {taskbar}");
            try
            {
                var root = AutomationElement.FromHandle(taskbar);
                var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                foreach (AutomationElement button in buttons)
                    Console.WriteLine($"{button.Current.ClassName} | {button.Current.AutomationId} | {button.Current.Name} | {button.Current.BoundingRectangle}");
            }
            catch (ElementNotAvailableException) { }
        }
    }
}

internal static class NativeMethods
{
    public static readonly nint HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(nint hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hWnd, int index);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder appId);

    public static string GetWindowText(nint hwnd)
    {
        var buffer = new StringBuilder(512);
        GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static bool IsToolWindow(nint hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    public static string GetAppUserModelId(nint process)
    {
        uint length = 0;
        var result = GetApplicationUserModelId(process, ref length, new StringBuilder());
        if (result != ERROR_INSUFFICIENT_BUFFER || length == 0)
            return "";
        var appId = new StringBuilder((int)length);
        return GetApplicationUserModelId(process, ref length, appId) == 0 ? appId.ToString() : "";
    }

    public static IReadOnlyList<nint> FindTaskbars()
    {
        var taskbars = new List<nint>();
        var primary = FindWindow("Shell_TrayWnd", null);
        if (primary != nint.Zero) taskbars.Add(primary);
        var current = nint.Zero;
        while ((current = FindWindowEx(nint.Zero, current, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
            taskbars.Add(current);
        return taskbars;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }
}
