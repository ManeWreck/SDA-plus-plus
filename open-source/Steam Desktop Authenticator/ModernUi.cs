using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class ModernUi
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private static readonly Dictionary<Button, bool> ButtonAccentMap = new Dictionary<Button, bool>();
        private static readonly HashSet<Button> HoveredButtons = new HashSet<Button>();
        private static readonly HashSet<Button> PressedButtons = new HashSet<Button>();
        private static readonly HashSet<Button> NavigationButtons = new HashSet<Button>();
        private static readonly HashSet<Button> SelectedNavigationButtons = new HashSet<Button>();
        private static readonly HashSet<CheckBox> ToggleRows = new HashSet<CheckBox>();
        private static readonly Dictionary<ComboBox, ModernComboHost> ComboHosts = new Dictionary<ComboBox, ModernComboHost>();
        private static readonly Dictionary<NumericUpDown, ModernNumericHost> NumericHosts = new Dictionary<NumericUpDown, ModernNumericHost>();
        private static readonly Dictionary<CheckBox, float> ToggleProgress = new Dictionary<CheckBox, float>();
        private static readonly Dictionary<CheckBox, float> ToggleTargets = new Dictionary<CheckBox, float>();
        private static readonly Dictionary<Panel, float> FieldFocusProgress = new Dictionary<Panel, float>();
        private static readonly Dictionary<Panel, float> FieldFocusTargets = new Dictionary<Panel, float>();
        private static readonly Timer UiAnimationTimer = CreateUiAnimationTimer();

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public static int HeaderHeight => Branding.Density == UiDensity.Compact ? 38 : 42;

        public static void AttachWindowChrome(Form form, bool showMinimize, bool showMaximize)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            if (form.Controls.OfType<Panel>().Any(panel => Equals(panel.Tag, "modern-titlebar")))
            {
                return;
            }

            form.FormBorderStyle = FormBorderStyle.None;
            form.Padding = new Padding(1);
            form.BackColor = Branding.WindowBackground;

            Panel titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = HeaderHeight,
                BackColor = Branding.AccentDark,
                Tag = "modern-titlebar"
            };

            Label titleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = form.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Branding.HeadingText,
                Font = new Font("Segoe UI Variable Text", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Padding = new Padding(14, 0, 0, 0)
            };

            titleBar.Controls.Add(CreateWindowButton(form, "X", DockStyle.Right, 46, () => form.Close()));
            if (showMaximize)
            {
                titleBar.Controls.Add(CreateWindowButton(form, "[]", DockStyle.Right, 42, () =>
                {
                    form.WindowState = form.WindowState == FormWindowState.Maximized
                        ? FormWindowState.Normal
                        : FormWindowState.Maximized;
                }));
            }

            if (showMinimize)
            {
                titleBar.Controls.Add(CreateWindowButton(form, "-", DockStyle.Right, 42, () =>
                {
                    form.WindowState = FormWindowState.Minimized;
                }));
            }

            titleBar.Controls.Add(titleLabel);
            titleBar.MouseDown += (_, e) => BeginWindowDrag(form, e);
            titleLabel.MouseDown += (_, e) => BeginWindowDrag(form, e);
            form.TextChanged += (_, _) => titleLabel.Text = form.Text;

            form.Controls.Add(titleBar);
            titleBar.BringToFront();

            form.Paint -= FormPaint;
            form.Paint += FormPaint;
        }

        public static void ShiftControlsDown(Form form, int deltaY, params Control[] exclusions)
        {
            HashSet<Control> excluded = new HashSet<Control>(exclusions ?? Array.Empty<Control>());
            foreach (Control control in form.Controls.Cast<Control>().ToArray())
            {
                if (excluded.Contains(control) || Equals(control.Tag, "modern-titlebar"))
                {
                    continue;
                }

                control.Location = new Point(control.Location.X, control.Location.Y + deltaY);
            }

            form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height + deltaY);
        }

        public static void OffsetChildren(Control parent, int deltaY)
        {
            foreach (Control control in parent.Controls)
            {
                control.Location = new Point(control.Location.X, control.Location.Y + deltaY);
            }
        }

        public static void ApplyGlassCard(GroupBox group, string title)
        {
            if (group == null)
            {
                return;
            }

            group.Tag = title;
            group.Text = string.Empty;
            group.BackColor = Color.Transparent;
            group.ForeColor = Branding.HeadingText;
            group.Padding = new Padding(14, 28, 14, 14);
            group.Paint -= GroupPaint;
            group.Paint += GroupPaint;
        }

        public static void RoundButton(Button button, bool accent)
        {
            if (button == null)
            {
                return;
            }

            ButtonAccentMap[button] = accent;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
              button.FlatAppearance.MouseOverBackColor = Color.Transparent;
              button.FlatAppearance.MouseDownBackColor = Color.Transparent;
              button.BackColor = accent ? Branding.Accent : Color.FromArgb(164, Branding.CardBackground);
              button.ForeColor = Branding.HeadingText;
              button.UseVisualStyleBackColor = false;
              button.Padding = Padding.Empty;
              button.Paint -= PaintRoundedButton;
              button.Paint += PaintRoundedButton;
            button.MouseEnter -= RoundedButtonMouseEnter;
            button.MouseEnter += RoundedButtonMouseEnter;
            button.MouseLeave -= RoundedButtonMouseLeave;
            button.MouseLeave += RoundedButtonMouseLeave;
            button.MouseDown -= RoundedButtonMouseDown;
            button.MouseDown += RoundedButtonMouseDown;
            button.MouseUp -= RoundedButtonMouseUp;
            button.MouseUp += RoundedButtonMouseUp;
            button.Resize -= ButtonResize;
            button.Resize += ButtonResize;
            ApplyRoundedRegion(button, Branding.ControlRadius);
            button.Invalidate();
        }

        public static void StyleNavigationButton(Button button, bool selected)
        {
            if (button == null) return;
            NavigationButtons.Add(button);
            if (selected) SelectedNavigationButtons.Add(button);
            else SelectedNavigationButtons.Remove(button);
            RoundButton(button, false);
            button.Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            button.Padding = new Padding(18, 0, 8, 0);
            button.Invalidate();
        }

        public static void StyleToggleRow(CheckBox checkBox)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.Appearance = Appearance.Button;
            checkBox.AutoSize = false;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderSize = 0;
            checkBox.FlatAppearance.MouseOverBackColor = Color.Transparent;
            checkBox.FlatAppearance.MouseDownBackColor = Color.Transparent;
            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = Branding.HeadingText;
            checkBox.UseVisualStyleBackColor = false;
            checkBox.Padding = Padding.Empty;
            checkBox.Paint -= PaintToggleRow;
            checkBox.Paint += PaintToggleRow;
            checkBox.CheckedChanged -= ToggleRowChanged;
            checkBox.CheckedChanged += ToggleRowChanged;
            ToggleRows.Add(checkBox);
            if (!ToggleProgress.ContainsKey(checkBox))
            {
                ToggleProgress[checkBox] = checkBox.Checked ? 1F : 0F;
                ToggleTargets[checkBox] = ToggleProgress[checkBox];
            }
            checkBox.Invalidate();
        }

        public static Panel WrapTextBox(TextBox textBox, int horizontalPadding = 10, int verticalPadding = 6)
        {
            if (textBox == null || textBox.Parent == null)
            {
                return null;
            }

            if (textBox.Parent is Panel existingPanel && Equals(existingPanel.Tag, "glass-shell"))
            {
                return existingPanel;
            }

            Control parent = textBox.Parent;
            int childIndex = parent.Controls.GetChildIndex(textBox);
            Point location = textBox.Location;
            Size size = textBox.Size;
            AnchorStyles anchor = textBox.Anchor;
            Padding margin = textBox.Margin;

            Panel shell = new Panel
            {
                Location = location,
                Size = size,
                Anchor = anchor,
                Margin = margin,
                BackColor = Color.Transparent,
                Tag = "glass-shell"
            };

            parent.Controls.Add(shell);
            parent.Controls.SetChildIndex(shell, childIndex);
            parent.Controls.Remove(textBox);
            shell.Controls.Add(textBox);

            textBox.BorderStyle = BorderStyle.None;
            textBox.BackColor = Branding.AccentDark;
            textBox.ForeColor = Branding.HeadingText;
            textBox.Location = new Point(horizontalPadding, verticalPadding);
            textBox.Width = Math.Max(12, shell.Width - (horizontalPadding * 2));

            if (!textBox.Multiline)
            {
                textBox.Top = Math.Max(verticalPadding, (shell.Height - textBox.PreferredHeight) / 2);
            }
            else
            {
                textBox.Height = Math.Max(18, shell.Height - (verticalPadding * 2));
            }

            shell.Resize += (_, _) =>
            {
                textBox.Width = Math.Max(12, shell.Width - (horizontalPadding * 2));
                if (textBox.Multiline)
                {
                    textBox.Height = Math.Max(18, shell.Height - (verticalPadding * 2));
                    textBox.Location = new Point(horizontalPadding, verticalPadding);
                }
                else
                {
                    textBox.Top = Math.Max(verticalPadding, (shell.Height - textBox.PreferredHeight) / 2);
                }
            };

            shell.Click += (_, _) => textBox.Focus();
            FieldFocusProgress[shell] = textBox.Focused ? 1F : 0F;
            FieldFocusTargets[shell] = FieldFocusProgress[shell];
            shell.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (shell.Width <= 1 || shell.Height <= 1)
                {
                    return;
                }

                Rectangle rect = new Rectangle(0, 0, shell.Width - 1, shell.Height - 1);
                float focus = FieldFocusProgress.TryGetValue(shell, out float progress) ? progress : 0F;

                using GraphicsPath path = CreateRoundedPath(rect, Branding.ControlRadius + 2);
                using SolidBrush fillBrush = new SolidBrush(Color.FromArgb(196, Branding.AccentDark));
                using Pen borderPen = new Pen(
                    Blend(Color.FromArgb(150, Branding.Outline), Branding.Accent, focus),
                    1F + (focus * 0.6F));
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            };

            textBox.Enter += (_, _) => StartFieldFocusAnimation(shell, true);
            textBox.Leave += (_, _) => StartFieldFocusAnimation(shell, false);
            shell.Disposed += (_, _) =>
            {
                FieldFocusProgress.Remove(shell);
                FieldFocusTargets.Remove(shell);
            };
            return shell;
        }

        public static Panel WrapListBox(ListBox listBox, int padding = 8)
        {
            if (listBox == null || listBox.Parent == null)
            {
                return null;
            }

            if (listBox.Parent is Panel existingPanel && Equals(existingPanel.Tag, "glass-list-shell"))
            {
                return existingPanel;
            }

            Control parent = listBox.Parent;
            int childIndex = parent.Controls.GetChildIndex(listBox);
            Point location = listBox.Location;
            Size size = listBox.Size;
            AnchorStyles anchor = listBox.Anchor;
            Padding margin = listBox.Margin;

            Panel shell = new Panel
            {
                Location = location,
                Size = size,
                Anchor = anchor,
                Margin = margin,
                BackColor = Color.Transparent,
                Tag = "glass-list-shell"
            };

            parent.Controls.Add(shell);
            parent.Controls.SetChildIndex(shell, childIndex);
            parent.Controls.Remove(listBox);
            shell.Controls.Add(listBox);

            listBox.BorderStyle = BorderStyle.None;
            listBox.BackColor = Branding.AccentDark;
            listBox.ForeColor = Branding.HeadingText;
            listBox.Location = new Point(padding, padding);
            listBox.Size = new Size(Math.Max(12, shell.Width - (padding * 2)), Math.Max(12, shell.Height - (padding * 2)));

            shell.Resize += (_, _) =>
            {
                listBox.Location = new Point(padding, padding);
                listBox.Size = new Size(Math.Max(12, shell.Width - (padding * 2)), Math.Max(12, shell.Height - (padding * 2)));
            };

            shell.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (shell.Width <= 1 || shell.Height <= 1)
                {
                    return;
                }

                Rectangle rect = new Rectangle(0, 0, shell.Width - 1, shell.Height - 1);
                using GraphicsPath path = CreateRoundedPath(rect, Branding.CardRadius);
                using SolidBrush fillBrush = new SolidBrush(Color.FromArgb(196, Branding.AccentDark));
                using Pen borderPen = new Pen(Color.FromArgb(150, Branding.Outline));
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            };

            return shell;
        }

        public static void RoundComboBox(ComboBox comboBox)
        {
            if (comboBox == null)
            {
                return;
            }

            if (ComboHosts.TryGetValue(comboBox, out ModernComboHost existingHost))
            {
                existingHost.Invalidate();
                return;
            }

            if (comboBox.Parent == null)
            {
                return;
            }

            Control parent = comboBox.Parent;
            int childIndex = parent.Controls.GetChildIndex(comboBox);
            ModernComboHost host = new ModernComboHost(comboBox)
            {
                Location = comboBox.Location,
                Size = new Size(comboBox.Width, Math.Max(32, comboBox.Height)),
                Anchor = comboBox.Anchor,
                Font = comboBox.Font,
                Enabled = comboBox.Enabled,
                Tag = "modern-combo-host"
            };
            parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, childIndex);
            parent.Controls.Remove(comboBox);
            host.Controls.Add(comboBox);
            comboBox.Visible = false;
            comboBox.Location = Point.Empty;
            ComboHosts[comboBox] = host;
        }

        public static void RoundNumericUpDown(NumericUpDown numeric)
        {
            if (numeric == null)
            {
                return;
            }

            if (NumericHosts.TryGetValue(numeric, out ModernNumericHost existingHost))
            {
                existingHost.Invalidate();
                return;
            }

            if (numeric.Parent == null)
            {
                return;
            }

            Control parent = numeric.Parent;
            int childIndex = parent.Controls.GetChildIndex(numeric);
            ModernNumericHost host = new ModernNumericHost(numeric)
            {
                Location = numeric.Location,
                Size = new Size(Math.Max(100, numeric.Width), Math.Max(32, numeric.Height)),
                Anchor = numeric.Anchor,
                Font = numeric.Font,
                Enabled = numeric.Enabled,
                Tag = "modern-numeric-host"
            };
            parent.Controls.Add(host);
            parent.Controls.SetChildIndex(host, childIndex);
            parent.Controls.Remove(numeric);
            host.Controls.Add(numeric);
            numeric.Visible = false;
            numeric.Location = Point.Empty;
            NumericHosts[numeric] = host;
        }

        public static void RefreshTheme(Control root)
        {
            if (root == null || root.IsDisposed)
            {
                return;
            }

            if (root is Form form)
            {
                form.BackColor = Branding.WindowBackground;
                form.ForeColor = Branding.HeadingText;
            }
            else if (root is Panel panel && Equals(panel.Tag, "modern-titlebar"))
            {
                panel.BackColor = Branding.AccentDark;
                foreach (Button windowButton in panel.Controls.OfType<Button>())
                {
                    windowButton.BackColor = Branding.AccentDark;
                    windowButton.ForeColor = Branding.MutedText;
                    windowButton.FlatAppearance.MouseOverBackColor = windowButton.Text == "X"
                        ? Branding.Danger
                        : Branding.AccentSoft;
                }
            }
            else if (root is Panel workspaceSurface && Equals(workspaceSurface.Tag, "workspace-surface"))
            {
                workspaceSurface.BackColor = Branding.WindowBackground;
            }
            else if (root is TextBox textBox)
            {
                textBox.BackColor = Branding.AccentDark;
                textBox.ForeColor = Branding.HeadingText;
            }
            else if (root is ComboBox comboBox)
            {
                RoundComboBox(comboBox);
            }
            else if (root is ModernComboHost or ModernNumericHost)
            {
                root.Invalidate();
            }
            else if (root is ListBox listBox)
            {
                listBox.BackColor = Branding.AccentDark;
                listBox.ForeColor = Branding.HeadingText;
            }
            else if (root is CheckBox checkBox && ToggleRows.Contains(checkBox))
            {
                StyleToggleRow(checkBox);
            }
            else if (root is GroupBox groupBox)
            {
                groupBox.ForeColor = Branding.HeadingText;
            }
            else if (root is Button button && ButtonAccentMap.TryGetValue(button, out bool accent))
            {
                RoundButton(button, accent);
            }

            foreach (Control child in root.Controls)
            {
                RefreshTheme(child);
            }

            root.Invalidate();
        }

        public static void PaintGlassBackground(object sender, PaintEventArgs e)
        {
            if (sender is not Form form)
            {
                return;
            }

            Rectangle bounds = form.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Branding.CardBackground, Branding.WindowBackground, 108f))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(18, Branding.Accent)))
            {
                e.Graphics.FillEllipse(glowBrush, new Rectangle(bounds.Width - 250, -118, 300, 220));
                e.Graphics.FillEllipse(glowBrush, new Rectangle(-180, bounds.Height - 90, 260, 180));
            }

            using (Pen borderPen = new Pen(Color.FromArgb(126, Branding.Outline)))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }
        }

        private static Button CreateWindowButton(Form form, string text, DockStyle dock, int width, Action onClick)
        {
            Button button = new Button
            {
                Dock = dock,
                Width = width,
                FlatStyle = FlatStyle.Flat,
                BackColor = Branding.AccentDark,
                ForeColor = Branding.MutedText,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
                Text = text,
                TabStop = false,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = text == "X" ? Branding.Danger : Branding.AccentSoft;
            button.FlatAppearance.MouseDownBackColor = text == "X" ? Branding.Danger : Color.FromArgb(110, Branding.AccentSoft);
            button.MouseLeave += (_, _) => button.BackColor = Branding.AccentDark;
            button.Click += (_, _) => onClick();
            return button;
        }

        private static void BeginWindowDrag(Form form, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(form.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        private static void FormPaint(object sender, PaintEventArgs e)
        {
            PaintGlassBackground(sender, e);
        }

        private static void GroupPaint(object sender, PaintEventArgs e)
        {
            if (sender is not GroupBox group)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color surface = ResolveSurfaceColor(group.Parent);
            e.Graphics.Clear(surface);
            if (group.Width <= 3 || group.Height <= 10)
            {
                return;
            }

            Rectangle rect = new Rectangle(1, 1, group.Width - 3, group.Height - 3);
            using (GraphicsPath path = CreateRoundedPath(rect, Branding.CardRadius))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                rect,
                Color.FromArgb(232, Branding.CardBackground),
                Color.FromArgb(205, Branding.AccentDark),
                92F))
            using (Pen borderPen = new Pen(Color.FromArgb(138, Branding.Outline)))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);

                string title = group.Tag as string ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    using Font titleFont = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold, GraphicsUnit.Point);
                    TextRenderer.DrawText(
                        e.Graphics,
                        title.ToUpperInvariant(),
                        titleFont,
                        new Rectangle(16, 9, group.Width - 32, 18),
                        Branding.MutedText,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
        }

        private static void ButtonResize(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                ApplyRoundedRegion(button, Branding.ControlRadius);
                button.Invalidate();
            }
        }

        private static void RoundedButtonMouseEnter(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                HoveredButtons.Add(button);
                button.Invalidate();
            }
        }

        private static void RoundedButtonMouseLeave(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                HoveredButtons.Remove(button);
                PressedButtons.Remove(button);
                button.Invalidate();
            }
        }

        private static void RoundedButtonMouseDown(object sender, MouseEventArgs e)
        {
            if (sender is Button button && e.Button == MouseButtons.Left)
            {
                PressedButtons.Add(button);
                button.Invalidate();
            }
        }

        private static void RoundedButtonMouseUp(object sender, MouseEventArgs e)
        {
            if (sender is Button button)
            {
                PressedButtons.Remove(button);
                button.Invalidate();
            }
        }

        private static void PaintRoundedButton(object sender, PaintEventArgs e)
        {
            if (sender is not Button button)
            {
                return;
            }

            bool accent = ButtonAccentMap.TryGetValue(button, out bool isAccent) && isAccent;
            bool hovered = HoveredButtons.Contains(button);
            bool pressed = PressedButtons.Contains(button);
            bool navigation = NavigationButtons.Contains(button);
            bool navigationSelected = SelectedNavigationButtons.Contains(button);

            Color fill = accent ? Branding.Accent : Color.FromArgb(142, Branding.CardBackground);
            Color border = accent ? Color.FromArgb(190, Branding.Accent) : Color.FromArgb(115, Branding.Outline);
            if (navigation)
            {
                fill = navigationSelected ? Color.FromArgb(76, Branding.AccentSoft) : Color.FromArgb(34, Branding.CardBackground);
                border = navigationSelected ? Color.FromArgb(150, Branding.Accent) : Color.FromArgb(58, Branding.Outline);
            }

            if (pressed)
            {
                fill = accent
                    ? Blend(Branding.Accent, Branding.WindowBackground, 0.18f)
                    : Blend(Branding.CardBackground, Branding.Accent, 0.22f);
            }
            else if (hovered)
            {
                fill = accent ? Blend(Branding.Accent, Color.White, 0.12f) : Branding.AccentSoft;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(button.Parent?.BackColor ?? Branding.WindowBackground);
            if (button.Width <= 1 || button.Height <= 1)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            using (GraphicsPath path = CreateRoundedPath(rect, Branding.ControlRadius))
            using (Pen borderPen = new Pen(border))
            {
                if (accent)
                {
                    using LinearGradientBrush gradient = new LinearGradientBrush(
                        rect,
                        Blend(fill, Color.White, 0.10f),
                        Blend(fill, Branding.WindowBackground, 0.10f),
                        0F);
                    e.Graphics.FillPath(gradient, path);
                }
                else
                {
                    using SolidBrush fillBrush = new SolidBrush(fill);
                    e.Graphics.FillPath(fillBrush, path);
                }
                e.Graphics.DrawPath(borderPen, path);
            }

            if (navigationSelected)
            {
                using SolidBrush indicator = new SolidBrush(Branding.Accent);
                e.Graphics.FillRectangle(indicator, 1, 10, 3, Math.Max(8, button.Height - 20));
            }

              Rectangle textRect = navigation
                  ? new Rectangle(18, 0, Math.Max(1, button.Width - 26), button.Height)
                  : new Rectangle(0, 0, button.Width, button.Height);
              TextRenderer.DrawText(
                  e.Graphics,
                  button.Text,
                  button.Font,
                  textRect,
                  button.ForeColor,
                  (navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter)
                  | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
          }

        private static void ToggleRowChanged(object sender, EventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                ToggleTargets[checkBox] = checkBox.Checked ? 1F : 0F;
                UiAnimationTimer.Start();
                checkBox.Invalidate();
            }
        }

        private static void PaintToggleRow(object sender, PaintEventArgs e)
        {
            if (sender is not CheckBox checkBox || checkBox.Width < 4 || checkBox.Height < 4)
            {
                return;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Branding.CardBackground);

            Rectangle row = new Rectangle(0, 0, checkBox.Width - 1, checkBox.Height - 1);
            using (GraphicsPath rowPath = CreateRoundedPath(row, 10))
            using (SolidBrush rowFill = new SolidBrush(Color.FromArgb(72, Branding.AccentDark)))
            using (Pen rowBorder = new Pen(Color.FromArgb(64, Branding.Outline)))
            {
                e.Graphics.FillPath(rowFill, rowPath);
                e.Graphics.DrawPath(rowBorder, rowPath);
            }

            int switchWidth = 38;
            int switchHeight = 20;
            Rectangle switchRect = new Rectangle(
                checkBox.Width - switchWidth - 10,
                (checkBox.Height - switchHeight) / 2,
                switchWidth,
                switchHeight);
            float progress = ToggleProgress.TryGetValue(checkBox, out float currentProgress)
                ? currentProgress
                : checkBox.Checked ? 1F : 0F;
            Color switchFill = Blend(Color.FromArgb(170, Branding.AccentDark), Branding.Accent, progress);
            using (GraphicsPath switchPath = CreateRoundedPath(switchRect, 10))
            using (SolidBrush switchBrush = new SolidBrush(switchFill))
            using (Pen switchBorder = new Pen(Blend(Branding.Outline, Branding.Accent, progress)))
            {
                e.Graphics.FillPath(switchBrush, switchPath);
                e.Graphics.DrawPath(switchBorder, switchPath);
            }

            int knobSize = 14;
            int knobStart = switchRect.Left + 3;
            int knobEnd = switchRect.Right - knobSize - 3;
            int knobX = knobStart + (int)Math.Round((knobEnd - knobStart) * progress);
            Rectangle knob = new Rectangle(knobX, switchRect.Top + 3, knobSize, knobSize);
            using (SolidBrush knobBrush = new SolidBrush(Blend(Branding.MutedText, Color.White, progress)))
            {
                e.Graphics.FillEllipse(knobBrush, knob);
            }

            Rectangle textRect = new Rectangle(12, 2, Math.Max(1, switchRect.Left - 22), checkBox.Height - 4);
            TextRenderer.DrawText(
                e.Graphics,
                checkBox.Text,
                checkBox.Font,
                textRect,
                checkBox.Enabled ? Branding.HeadingText : Branding.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        private static Timer CreateUiAnimationTimer()
        {
            Timer timer = new Timer { Interval = 15 };
            timer.Tick += UiAnimationTick;
            return timer;
        }

        private static void StartFieldFocusAnimation(Panel shell, bool focused)
        {
            FieldFocusTargets[shell] = focused ? 1F : 0F;
            UiAnimationTimer.Start();
            shell.Invalidate();
        }

        private static void UiAnimationTick(object sender, EventArgs e)
        {
            bool active = false;
            foreach (CheckBox checkBox in ToggleProgress.Keys.ToArray())
            {
                if (checkBox.IsDisposed)
                {
                    ToggleProgress.Remove(checkBox);
                    ToggleTargets.Remove(checkBox);
                    continue;
                }

                float target = ToggleTargets.TryGetValue(checkBox, out float requested)
                    ? requested
                    : checkBox.Checked ? 1F : 0F;
                float next = AnimateValue(ToggleProgress[checkBox], target);
                ToggleProgress[checkBox] = next;
                checkBox.Invalidate();
                active |= Math.Abs(next - target) > 0.001F;
            }

            foreach (Panel shell in FieldFocusProgress.Keys.ToArray())
            {
                if (shell.IsDisposed)
                {
                    FieldFocusProgress.Remove(shell);
                    FieldFocusTargets.Remove(shell);
                    continue;
                }

                float target = FieldFocusTargets.TryGetValue(shell, out float requested) ? requested : 0F;
                float next = AnimateValue(FieldFocusProgress[shell], target);
                FieldFocusProgress[shell] = next;
                shell.Invalidate();
                active |= Math.Abs(next - target) > 0.001F;
            }

            if (!active)
            {
                UiAnimationTimer.Stop();
            }
        }

        private static float AnimateValue(float current, float target)
        {
            float next = current + ((target - current) * 0.24F);
            return Math.Abs(next - target) < 0.01F ? target : next;
        }

        private static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control.Width <= 0 || control.Height <= 0)
            {
                control.Region = null;
                return;
            }

            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                control.Region = new Region(path);
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color first, Color second, float amount)
        {
            float inverse = 1f - amount;
            return Color.FromArgb(
                (int)((first.R * inverse) + (second.R * amount)),
                (int)((first.G * inverse) + (second.G * amount)),
                (int)((first.B * inverse) + (second.B * amount)));
        }

        private static Color ResolveSurfaceColor(Control control)
        {
            Control current = control;
            while (current != null)
            {
                if (current.BackColor.A == byte.MaxValue)
                {
                    return current.BackColor;
                }

                current = current.Parent;
            }

            return Branding.WindowBackground;
        }
    }
}
