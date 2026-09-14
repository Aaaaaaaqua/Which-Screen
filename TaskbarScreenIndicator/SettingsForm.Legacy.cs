using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarScreenIndicator
{
    internal static class SettingsStore
    {
        private const string RegistryPath = @"Software\TaskbarScreenIndicator";

        public static bool Enabled
        {
            get { return ReadInt("Enabled", 1) != 0; }
            set { WriteInt("Enabled", value ? 1 : 0); }
        }

        public static int RefreshInterval
        {
            get { return Math.Max(250, Math.Min(5000, ReadInt("RefreshInterval", 900))); }
            set { WriteInt("RefreshInterval", Math.Max(250, Math.Min(5000, value))); }
        }

        public static Color GetScreenColor(string deviceName, Color fallback)
        {
            var value = ReadInt(ColorValueName(deviceName), fallback.ToArgb());
            return Color.FromArgb(value);
        }

        public static void SetScreenColor(string deviceName, Color color)
        {
            WriteInt(ColorValueName(deviceName), color.ToArgb());
        }

        private static string ColorValueName(string deviceName)
        {
            return "Color_" + deviceName.Replace("\\", "").Replace(".", "").Replace(":", "");
        }

        private static int ReadInt(string name, int fallback)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null)
                        return fallback;
                    var value = key.GetValue(name);
                    return value is int ? (int)value : fallback;
                }
            }
            catch
            {
                return fallback;
            }
        }

        private static void WriteInt(string name, int value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key != null)
                        key.SetValue(name, value, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Keep the app usable even when registry writes are restricted.
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        private static readonly Color Canvas = Color.FromArgb(246, 247, 249);
        private static readonly Color Ink = Color.FromArgb(27, 29, 32);
        private static readonly Color Muted = Color.FromArgb(103, 108, 116);
        private static readonly Color Hairline = Color.FromArgb(225, 228, 232);
        private static readonly Color Accent = Color.FromArgb(23, 125, 121);

        private readonly IList<ScreenInfo> screens;
        private readonly Dictionary<string, Color> screenColors = new Dictionary<string, Color>();
        private readonly Dictionary<string, Button> colorButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Label> colorLabels = new Dictionary<string, Label>();
        private readonly ToggleSwitch enabledToggle;
        private readonly Label enabledStatus;
        private readonly ComboBox refreshCombo;
        private readonly MonitorPreview preview;

        public SettingsForm(IList<ScreenInfo> connectedScreens, bool enabled, int refreshInterval)
        {
            screens = connectedScreens;
            foreach (var screen in screens)
                screenColors[screen.DeviceName] = screen.Color;

            Text = "屏幕标记设置";
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Canvas;
            ForeColor = Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, Math.Min(820, 550 + screens.Count * 66));
            MinimumSize = new Size(656, 620);
            Icon = SystemIcons.Application;

            var header = new Panel { Dock = DockStyle.Top, Size = new Size(ClientSize.Width, 118), BackColor = Ink };
            var title = new Label
            {
                AutoSize = true,
                Location = new Point(32, 24),
                Text = "屏幕标记",
                ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 20F, FontStyle.Bold)
            };
            var status = new Label
            {
                AutoSize = true,
                Location = new Point(34, 72),
                Text = string.Format("{0} 块屏幕已连接", screens.Count),
                ForeColor = Color.FromArgb(176, 181, 188),
                Font = new Font(Font.FontFamily, 9F)
            };
            preview = new MonitorPreview(screens, screenColors)
            {
                Location = new Point(370, 18),
                Size = new Size(238, 82),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            header.Controls.Add(title);
            header.Controls.Add(status);
            header.Controls.Add(preview);

            var footer = new Panel { Dock = DockStyle.Bottom, Size = new Size(ClientSize.Width, 72), BackColor = Color.White };
            footer.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (var pen = new Pen(Hairline))
                    args.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            var cancelButton = CreateButton("取消", Color.White, Ink, Hairline, 84);
            cancelButton.Location = new Point(424, 18);
            cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancelButton.DialogResult = DialogResult.Cancel;
            var saveButton = CreateButton("保存更改", Accent, Color.White, Accent, 104);
            saveButton.Location = new Point(520, 18);
            saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveButton.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            footer.Controls.Add(cancelButton);
            footer.Controls.Add(saveButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            var content = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(32, 18, 12, 18),
                BackColor = Canvas
            };

            var generalLabel = CreateSectionLabel("常规");
            content.Controls.Add(generalLabel);

            var enabledRow = CreateRow("显示屏幕标记", enabled ? "当前已开启" : "当前已关闭");
            enabledStatus = (Label)enabledRow.Controls[1];
            enabledToggle = new ToggleSwitch
            {
                Checked = enabled,
                Location = new Point(500, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AccessibleName = "显示屏幕标记"
            };
            enabledToggle.CheckedChanged += delegate
            {
                enabledStatus.Text = enabledToggle.Checked ? "当前已开启" : "当前已关闭";
            };
            enabledRow.Controls.Add(enabledToggle);
            content.Controls.Add(enabledRow);

            var screensLabel = CreateSectionLabel("屏幕颜色");
            screensLabel.Margin = new Padding(0, 18, 0, 8);
            content.Controls.Add(screensLabel);
            for (var index = 0; index < screens.Count; index++)
                content.Controls.Add(CreateScreenRow(screens[index], index + 1));

            var behaviorLabel = CreateSectionLabel("更新");
            behaviorLabel.Margin = new Padding(0, 18, 0, 8);
            content.Controls.Add(behaviorLabel);
            var refreshRow = CreateRow("刷新速度", "窗口移动后更新色带");
            refreshCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Font,
                Location = new Point(430, 17),
                Size = new Size(126, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            refreshCombo.Items.AddRange(new object[]
            {
                new RefreshOption("快速 · 0.5 秒", 500),
                new RefreshOption("标准 · 0.9 秒", 900),
                new RefreshOption("节能 · 1.5 秒", 1500),
                new RefreshOption("低频 · 3 秒", 3000)
            });
            SelectRefreshInterval(refreshInterval);
            refreshRow.Controls.Add(refreshCombo);
            content.Controls.Add(refreshRow);

            var resetButton = CreateButton("恢复默认颜色", Color.Transparent, Muted, Hairline, 122);
            resetButton.Margin = new Padding(0, 12, 0, 0);
            resetButton.Click += delegate { ResetColors(); };
            content.Controls.Add(resetButton);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
        }

        public bool EnabledValue { get { return enabledToggle.Checked; } }

        public int RefreshIntervalValue
        {
            get
            {
                var option = refreshCombo.SelectedItem as RefreshOption;
                return option == null ? 900 : option.Interval;
            }
        }

        public IDictionary<string, Color> ScreenColors { get { return screenColors; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var dark = 1;
                DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
            }
            catch { }
        }

        private Panel CreateScreenRow(ScreenInfo screen, int number)
        {
            var details = string.Format("屏幕 {0}  ·  {1} × {2}", number, screen.Bounds.Width, screen.Bounds.Height);
            var row = CreateRow(screen.Label, details);
            var hex = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(400, 18),
                Size = new Size(94, 28),
                ForeColor = Muted,
                Text = ToHex(screenColors[screen.DeviceName])
            };
            var swatch = new Button
            {
                BackColor = screenColors[screen.DeviceName],
                FlatStyle = FlatStyle.Flat,
                Location = new Point(506, 18),
                Size = new Size(50, 28),
                Cursor = Cursors.Hand,
                AccessibleName = screen.Label + "颜色",
                TabStop = true
            };
            swatch.FlatAppearance.BorderColor = Color.FromArgb(205, 209, 214);
            swatch.FlatAppearance.BorderSize = 1;
            swatch.Click += delegate
            {
                using (var dialog = new ColorDialog())
                {
                    dialog.Color = screenColors[screen.DeviceName];
                    dialog.FullOpen = true;
                    dialog.AnyColor = true;
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;
                    screenColors[screen.DeviceName] = dialog.Color;
                    swatch.BackColor = dialog.Color;
                    hex.Text = ToHex(dialog.Color);
                    preview.Invalidate();
                }
            };
            colorButtons[screen.DeviceName] = swatch;
            colorLabels[screen.DeviceName] = hex;
            row.Controls.Add(hex);
            row.Controls.Add(swatch);
            return row;
        }

        private static Panel CreateRow(string title, string subtitle)
        {
            var row = new Panel
            {
                Size = new Size(576, 64),
                Margin = new Padding(0, 0, 0, 1),
                BackColor = Color.White
            };
            var titleLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 10),
                Text = title,
                ForeColor = Ink,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            var subtitleLabel = new Label
            {
                AutoSize = true,
                Location = new Point(18, 36),
                Text = subtitle,
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 8.25F)
            };
            row.Controls.Add(titleLabel);
            row.Controls.Add(subtitleLabel);
            return row;
        }

        private static Label CreateSectionLabel(string text)
        {
            return new Label
            {
                AutoSize = false,
                Size = new Size(576, 26),
                Margin = new Padding(0, 0, 0, 8),
                Text = text,
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor, Color borderColor, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 36),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void SelectRefreshInterval(int interval)
        {
            RefreshOption closest = null;
            foreach (RefreshOption option in refreshCombo.Items)
            {
                if (closest == null || Math.Abs(option.Interval - interval) < Math.Abs(closest.Interval - interval))
                    closest = option;
            }
            refreshCombo.SelectedItem = closest;
        }

        private void ResetColors()
        {
            var defaults = new[]
            {
                Color.FromArgb(0, 176, 255), Color.FromArgb(255, 156, 0), Color.FromArgb(117, 220, 72),
                Color.FromArgb(220, 70, 160), Color.FromArgb(240, 215, 65), Color.FromArgb(86, 104, 255)
            };
            for (var index = 0; index < screens.Count; index++)
            {
                var key = screens[index].DeviceName;
                var color = defaults[index % defaults.Length];
                screenColors[key] = color;
                colorButtons[key].BackColor = color;
                colorLabels[key].Text = ToHex(color);
            }
            preview.Invalidate();
        }

        private static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private sealed class RefreshOption
        {
            public readonly string Label;
            public readonly int Interval;

            public RefreshOption(string label, int interval)
            {
                Label = label;
                Interval = interval;
            }

            public override string ToString() { return Label; }
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            Appearance = Appearance.Button;
            AutoSize = false;
            Size = new Size(52, 28);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(1, 2, Width - 2, Height - 4);
            using (var path = RoundedRectangle(track, track.Height / 2))
            using (var brush = new SolidBrush(Checked ? Color.FromArgb(23, 125, 121) : Color.FromArgb(183, 188, 194)))
                e.Graphics.FillPath(brush, path);
            var knobSize = Height - 10;
            var knobX = Checked ? Width - knobSize - 5 : 5;
            using (var brush = new SolidBrush(Color.White))
                e.Graphics.FillEllipse(brush, knobX, 5, knobSize, knobSize);
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class MonitorPreview : Control
    {
        private readonly IList<ScreenInfo> screens;
        private readonly IDictionary<string, Color> colors;

        public MonitorPreview(IList<ScreenInfo> connectedScreens, IDictionary<string, Color> screenColors)
        {
            screens = connectedScreens;
            colors = screenColors;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (screens.Count == 0)
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var union = screens.Select(screen => screen.Bounds).Aggregate(Rectangle.Union);
            var scale = Math.Min((Width - 18F) / union.Width, (Height - 18F) / union.Height);
            var drawWidth = union.Width * scale;
            var drawHeight = union.Height * scale;
            var originX = (Width - drawWidth) / 2F;
            var originY = (Height - drawHeight) / 2F;

            for (var index = 0; index < screens.Count; index++)
            {
                var screen = screens[index];
                var bounds = new RectangleF(
                    originX + (screen.Bounds.Left - union.Left) * scale,
                    originY + (screen.Bounds.Top - union.Top) * scale,
                    Math.Max(24F, screen.Bounds.Width * scale),
                    Math.Max(18F, screen.Bounds.Height * scale));
                using (var fill = new SolidBrush(Color.FromArgb(48, 51, 56)))
                using (var border = new Pen(Color.FromArgb(91, 96, 103), 1F))
                {
                    e.Graphics.FillRectangle(fill, bounds);
                    e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
                var color = colors.ContainsKey(screen.DeviceName) ? colors[screen.DeviceName] : screen.Color;
                using (var brush = new SolidBrush(color))
                    e.Graphics.FillRectangle(brush, bounds.X + 3, bounds.Bottom - 6, Math.Max(1, bounds.Width - 6), 3);
                TextRenderer.DrawText(e.Graphics, (index + 1).ToString(), Font, Rectangle.Round(bounds),
                    Color.FromArgb(218, 221, 225), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
