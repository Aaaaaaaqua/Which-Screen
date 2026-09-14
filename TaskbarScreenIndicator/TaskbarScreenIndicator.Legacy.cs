using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarScreenIndicator
{
    internal static class LegacyProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            NativeMethods.EnablePerMonitorDpi();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Any(arg => string.Equals(arg, "--inspect", StringComparison.OrdinalIgnoreCase)))
            {
                TaskbarInspector.WriteReport();
                return;
            }
            if (args.Any(arg => string.Equals(arg, "--verify", StringComparison.OrdinalIgnoreCase)))
            {
                TaskbarInspector.WriteIndicatorReport();
                return;
            }
            var openSettings = args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase));
            Application.Run(new IndicatorApplicationContext(openSettings));
        }
    }

    internal sealed class IndicatorApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly Timer refreshTimer;
        private readonly Dictionary<IntPtr, IndicatorOverlay> overlays = new Dictionary<IntPtr, IndicatorOverlay>();
        private bool enabled = SettingsStore.Enabled;

        public IndicatorApplicationContext(bool openSettings)
        {
            var menu = new ContextMenuStrip();
            var openSettingsItem = new ToolStripMenuItem("打开设置");
            openSettingsItem.Font = new Font(openSettingsItem.Font, FontStyle.Bold);
            var enabledItem = new ToolStripMenuItem("显示任务栏屏幕标记");
            enabledItem.Checked = enabled;
            enabledItem.CheckOnClick = true;
            enabledItem.CheckedChanged += delegate
            {
                enabled = enabledItem.Checked;
                SettingsStore.Enabled = enabled;
                RefreshIndicators();
            };
            openSettingsItem.Click += delegate { ShowSettings(enabledItem); };
            menu.Items.Add(openSettingsItem);
            menu.Items.Add(enabledItem);
            menu.Items.Add("立即刷新", null, delegate { RefreshIndicators(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Information;
            trayIcon.Text = "任务栏屏幕标记";
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { ShowSettings(enabledItem); };

            refreshTimer = new Timer();
            refreshTimer.Interval = SettingsStore.RefreshInterval;
            refreshTimer.Tick += delegate { RefreshIndicators(); };
            refreshTimer.Start();
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            if (openSettings)
                ShowSettings(enabledItem);
            RefreshIndicators();
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs args)
        {
            RefreshIndicators();
        }

        private void RefreshIndicators()
        {
            if (!enabled)
            {
                foreach (var overlay in overlays.Values)
                    overlay.Hide();
                return;
            }

            var taskbars = NativeMethods.FindTaskbarInfos();
            var used = new HashSet<IntPtr>();
            var windows = WindowCatalog.GetWindows();
            var screens = ScreenLayout.GetScreens();
            foreach (var taskbar in taskbars)
            {
                used.Add(taskbar.Key);

                IndicatorOverlay overlay;
                if (!overlays.TryGetValue(taskbar.Key, out overlay))
                {
                    overlay = new IndicatorOverlay();
                    overlays.Add(taskbar.Key, overlay);
                }
                overlay.Update(taskbar.Bounds, TaskbarInspector.GetIndicators(taskbar, windows, screens));
            }

            foreach (var pair in overlays.Where(pair => !used.Contains(pair.Key)).ToArray())
            {
                pair.Value.Dispose();
                overlays.Remove(pair.Key);
            }
        }

        private void ShowSettings(ToolStripMenuItem enabledItem)
        {
            using (var form = new SettingsForm(ScreenLayout.GetScreens(), enabledItem.Checked, refreshTimer.Interval))
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return;
                SettingsStore.Enabled = form.EnabledValue;
                SettingsStore.RefreshInterval = form.RefreshIntervalValue;
                foreach (var pair in form.ScreenColors)
                    SettingsStore.SetScreenColor(pair.Key, pair.Value);
                enabledItem.Checked = form.EnabledValue;
                refreshTimer.Interval = form.RefreshIntervalValue;
                RefreshIndicators();
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
        private IList<Indicator> indicators = new List<Indicator>();
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

        public void Update(Rectangle taskbar, IList<Indicator> updatedIndicators)
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

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message message)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTTRANSPARENT = -1;
            if (message.Msg == WM_NCHITTEST)
            {
                message.Result = new IntPtr(HTTRANSPARENT);
                return;
            }
            base.WndProc(ref message);
        }

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
                for (var index = 0; index < indicator.Colors.Count; index++)
                {
                    var segmentLeft = left + usableWidth * index / indicator.Colors.Count;
                    var segmentRight = left + usableWidth * (index + 1) / indicator.Colors.Count;
                    using (var brush = new SolidBrush(indicator.Colors[index]))
                        e.Graphics.FillRectangle(brush, segmentLeft, y, Math.Max(2, segmentRight - segmentLeft), height);
                }
            }
        }
    }

    internal sealed class ScreenInfo
    {
        public IntPtr Handle;
        public string DeviceName;
        public Rectangle Bounds;
        public string Label;
        public Color Color;
    }

    internal sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string ProcessName;
        public string ProductName;
        public string AppId;
        public ScreenInfo Screen;
    }

    internal sealed class Indicator
    {
        public Rectangle Bounds;
        public IList<Color> Colors;
    }

    internal sealed class TaskbarInfo
    {
        public IntPtr Key;
        public Rectangle Bounds;
        public AutomationElement Root;
    }

    internal static class ScreenLayout
    {
        private static readonly Color[] Palette =
        {
            Color.FromArgb(0, 176, 255), Color.FromArgb(255, 156, 0), Color.FromArgb(117, 220, 72),
            Color.FromArgb(220, 70, 160), Color.FromArgb(240, 215, 65), Color.FromArgb(86, 104, 255)
        };

        public static IList<ScreenInfo> GetScreens()
        {
            var primary = Screen.PrimaryScreen == null ? Rectangle.Empty : Screen.PrimaryScreen.Bounds;
            var allScreens = Screen.AllScreens.OrderBy(screen => screen.Primary ? 0 : 1)
                .ThenBy(screen => screen.Bounds.Top).ThenBy(screen => screen.Bounds.Left).ToArray();
            var result = new List<ScreenInfo>();
            for (var index = 0; index < allScreens.Length; index++)
            {
                var screen = allScreens[index];
                result.Add(new ScreenInfo
                {
                    Handle = new IntPtr(screen.DeviceName.GetHashCode()),
                    DeviceName = screen.DeviceName,
                    Bounds = screen.Bounds,
                    Label = screen.Primary ? "主屏幕" : GetLocationLabel(screen.Bounds, primary),
                    Color = SettingsStore.GetScreenColor(screen.DeviceName, Palette[index % Palette.Length])
                });
            }
            return result;
        }

        public static ScreenInfo ScreenForWindow(IntPtr hwnd, IList<ScreenInfo> screens)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
                return null;

            // Minimized windows are reported at (-32000, -32000). Use their
            // saved normal position so their taskbar marker keeps the right
            // monitor assignment.
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.WINDOWPLACEMENT placement = new NativeMethods.WINDOWPLACEMENT();
                placement.Length = (uint)Marshal.SizeOf(typeof(NativeMethods.WINDOWPLACEMENT));
                if (NativeMethods.GetWindowPlacement(hwnd, ref placement) &&
                    placement.NormalPosition.Right > placement.NormalPosition.Left &&
                    placement.NormalPosition.Bottom > placement.NormalPosition.Top)
                    rect = placement.NormalPosition;
            }
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
            var dx = Math.Max(rectangle.Left - point.X, Math.Max(0, point.X - rectangle.Right));
            var dy = Math.Max(rectangle.Top - point.Y, Math.Max(0, point.Y - rectangle.Bottom));
            return (long)dx * dx + (long)dy * dy;
        }
    }

    internal static class WindowCatalog
    {
        public static IList<WindowInfo> GetWindows()
        {
            var screens = ScreenLayout.GetScreens();
            var windows = new List<WindowInfo>();
            NativeMethods.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                // Include minimized windows because they retain their display association, but
                // exclude fully hidden helper windows such as DDE Server Window.
                if (NativeMethods.IsToolWindow(hwnd) ||
                    (!NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd)))
                    return true;
                var title = NativeMethods.GetWindowText(hwnd);
                if (string.IsNullOrWhiteSpace(title))
                    return true;
                uint processId;
                NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
                var processName = "";
                var productName = "";
                var appId = "";
                try
                {
                    using (var process = Process.GetProcessById((int)processId))
                    {
                        processName = process.ProcessName ?? "";
                        appId = NativeMethods.GetAppUserModelId(process.Handle);
                        if (process.MainModule != null && !string.IsNullOrEmpty(process.MainModule.FileName))
                        {
                            var info = FileVersionInfo.GetVersionInfo(process.MainModule.FileName);
                            productName = info.ProductName ?? info.FileDescription ?? "";
                        }
                    }
                }
                catch (ArgumentException) { }
                catch (System.ComponentModel.Win32Exception) { }

                var screen = ScreenLayout.ScreenForWindow(hwnd, screens);
                if (screen != null)
                {
                    windows.Add(new WindowInfo
                    {
                        Handle = hwnd,
                        Title = title,
                        ProcessName = processName,
                        ProductName = productName,
                        AppId = appId,
                        Screen = screen
                    });
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }
    }

    internal static class TaskbarInspector
    {
        public static IList<Indicator> GetIndicators(TaskbarInfo taskbar, IList<WindowInfo> windows, IList<ScreenInfo> screens)
        {
            try
            {
                var root = taskbar.Root ?? AutomationElement.FromHandle(taskbar.Key);
                var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
                var buttons = root.FindAll(TreeScope.Descendants, condition);
                var indicators = new List<Indicator>();
                foreach (AutomationElement button in buttons)
                {
                    var indicator = GetIndicator(button, windows);
                    if (indicator != null)
                        indicators.Add(indicator);
                }
                return indicators;
            }
            catch (ElementNotAvailableException)
            {
                return new List<Indicator>();
            }
        }

        private static Indicator GetIndicator(AutomationElement button, IList<WindowInfo> windows)
        {
            try
            {
                if (button.Current.ClassName != "Taskbar.TaskListButtonAutomationPeer" ||
                    !button.Current.AutomationId.StartsWith("Appid:", StringComparison.OrdinalIgnoreCase))
                    return null;
                var bounds = button.Current.BoundingRectangle;
                var name = button.Current.Name;
                if (bounds.Width < 20 || bounds.Height < 20 || string.IsNullOrWhiteSpace(name))
                    return null;
                var matches = MatchWindows(name, button.Current.AutomationId, windows);
                if (matches.Count == 0)
                    return null;
                return new Indicator
                {
                    Bounds = Rectangle.FromLTRB(
                        (int)Math.Round(bounds.Left),
                        (int)Math.Round(bounds.Top),
                        (int)Math.Round(bounds.Right),
                        (int)Math.Round(bounds.Bottom)),
                    Colors = matches.Select(window => window.Screen.Color).Distinct().ToList()
                };
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
        }

        private static IList<WindowInfo> MatchWindows(string taskbarName, string automationId, IList<WindowInfo> windows)
        {
            var key = Normalize(taskbarName);
            var appKey = Normalize(automationId.Replace("Appid:", ""));
            var exactAppMatches = windows.Where(window =>
                !string.IsNullOrEmpty(window.AppId) && Normalize(window.AppId) == appKey).ToList();
            if (exactAppMatches.Count > 0)
                return exactAppMatches.GroupBy(match => match.Handle).Select(group => group.First()).ToList();

            var scored = windows.Select(window => new { Window = window, Score = Score(key, appKey, window) })
                .Where(scoreItem => scoreItem.Score > 0).ToArray();
            if (scored.Length == 0)
                return new List<WindowInfo>();
            var hasStrongAppMatch = scored.Any(scoreItem => scoreItem.Score >= 100);
            var selected = hasStrongAppMatch
                ? scored.Where(scoreItem => scoreItem.Score >= 100)
                : scored.Where(scoreItem => scoreItem.Score >= Math.Max(45, scored.Max(maxItem => maxItem.Score) - 15));
            return selected.Select(scoreItem => scoreItem.Window).GroupBy(windowItem => windowItem.Handle).Select(group => group.First()).ToList();
        }

        private static int Score(string key, string appKey, WindowInfo window)
        {
            var title = Normalize(window.Title);
            var process = Normalize(window.ProcessName);
            var product = Normalize(window.ProductName);
            var appId = Normalize(window.AppId);
            if (appId.Length >= 4 && appKey == appId) return 120;
            if (appId.Length >= 4 && (appKey.Contains(appId) || appId.Contains(appKey))) return 110;
            if (process.Length >= 2 && (appKey == process || appKey.EndsWith(process + "exe") || appKey.Contains(process))) return 105;
            if (key == title) return 100;
            if (title.Length >= 4 && (key.Contains(title) || title.Contains(key))) return 85;
            if (product.Length >= 4 && (key.Contains(product) || product.Contains(key))) return 70;
            if (process.Length >= 4 && (key.Contains(process) || process.Contains(key))) return 55;
            return 0;
        }

        private static string Normalize(string value)
        {
            return Regex.Replace(value.ToLowerInvariant(), "[^\\p{L}\\p{N}]", "");
        }

        public static void WriteReport()
        {
            var windows = WindowCatalog.GetWindows();
            Console.WriteLine("OPEN WINDOWS");
            foreach (var window in windows)
                Console.WriteLine("{0} | {1} | {2} | {3} | {4}", window.Screen.Label, window.ProcessName, window.ProductName, window.AppId, window.Title);
            Console.WriteLine("TASKBAR BUTTONS");
            foreach (var taskbar in NativeMethods.FindTaskbarInfos())
            {
                Console.WriteLine("TASKBAR {0} {1}", taskbar.Key, taskbar.Bounds);
                try
                {
                    var root = taskbar.Root ?? AutomationElement.FromHandle(taskbar.Key);
                    var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
                    foreach (AutomationElement button in buttons)
                        Console.WriteLine("{0} | {1} | {2} | {3}", button.Current.ClassName, button.Current.AutomationId, button.Current.Name, button.Current.BoundingRectangle);
                }
                catch (ElementNotAvailableException) { }
            }
        }

        public static void WriteIndicatorReport()
        {
            var windows = WindowCatalog.GetWindows();
            foreach (var taskbar in NativeMethods.FindTaskbarInfos())
            {
                var indicators = GetIndicators(taskbar, windows, ScreenLayout.GetScreens());
                Console.WriteLine("INDICATORS {0}: {1}", taskbar.Bounds, indicators.Count);
                foreach (var indicator in indicators)
                    Console.WriteLine("  {0} | {1}", indicator.Bounds, string.Join(",", indicator.Colors.Select(color => color.ToArgb().ToString("X8")).ToArray()));
            }
        }
    }

    internal static class NativeMethods
    {
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x00000080L;

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder appId);

        public static string GetWindowText(IntPtr hwnd)
        {
            var buffer = new StringBuilder(512);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            return buffer.ToString();
        }

        public static bool IsToolWindow(IntPtr hwnd)
        {
            return (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;
        }

        public static void EnablePerMonitorDpi()
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
        }

        public static string GetAppUserModelId(IntPtr process)
        {
            const int ERROR_INSUFFICIENT_BUFFER = 122;
            uint length = 0;
            var result = GetApplicationUserModelId(process, ref length, new StringBuilder());
            if (result != ERROR_INSUFFICIENT_BUFFER || length == 0)
                return "";
            var appId = new StringBuilder((int)length);
            return GetApplicationUserModelId(process, ref length, appId) == 0 ? appId.ToString() : "";
        }

        public static IList<TaskbarInfo> FindTaskbarInfos()
        {
            var taskbars = new List<TaskbarInfo>();
            var primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero)
            {
                RECT rect;
                if (GetWindowRect(primary, out rect))
                    taskbars.Add(new TaskbarInfo { Key = primary, Bounds = rect.ToRectangle(), Root = AutomationElement.FromHandle(primary) });
            }
            var current = IntPtr.Zero;
            while ((current = FindWindowEx(IntPtr.Zero, current, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            {
                RECT rect;
                if (GetWindowRect(current, out rect))
                    taskbars.Add(new TaskbarInfo { Key = current, Bounds = rect.ToRectangle(), Root = AutomationElement.FromHandle(current) });
            }

            // Windows 11 may expose secondary taskbars only through UI Automation.
            try
            {
                var condition = new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskbarFrame");
                var frames = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
                for (var index = 0; index < frames.Count; index++)
                {
                    var frame = frames[index];
                    var bounds = frame.Current.BoundingRectangle;
                    var rectangle = Rectangle.FromLTRB((int)Math.Round(bounds.Left), (int)Math.Round(bounds.Top),
                        (int)Math.Round(bounds.Right), (int)Math.Round(bounds.Bottom));
                    if (rectangle.Width < 200 || rectangle.Height < 30 || taskbars.Any(item => item.Bounds == rectangle))
                        continue;
                    taskbars.Add(new TaskbarInfo { Key = new IntPtr(-1000 - index), Bounds = rectangle, Root = frame });
                }
            }
            catch (ElementNotAvailableException) { }
            return taskbars;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public uint Length;
            public uint Flags;
            public uint ShowCommand;
            public POINT MinPosition;
            public POINT MaxPosition;
            public RECT NormalPosition;
        }
    }
}
