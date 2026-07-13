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

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public static int HeaderHeight => 38;

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
                Font = new Font("Segoe UI Semibold", 9.75F, FontStyle.Regular, GraphicsUnit.Point),
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
            ApplyRoundedRegion(button, 8);
            button.Invalidate();
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
            shell.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (shell.Width <= 1 || shell.Height <= 1)
                {
                    return;
                }

                Rectangle rect = new Rectangle(0, 0, shell.Width - 1, shell.Height - 1);
                bool focused = textBox.Focused;

                using GraphicsPath path = CreateRoundedPath(rect, 10);
                using SolidBrush fillBrush = new SolidBrush(Color.FromArgb(196, Branding.AccentDark));
                using Pen borderPen = new Pen(focused ? Branding.Accent : Color.FromArgb(150, Branding.Outline));
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            };

            textBox.Enter += (_, _) => shell.Invalidate();
            textBox.Leave += (_, _) => shell.Invalidate();
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
                using GraphicsPath path = CreateRoundedPath(rect, 12);
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

            comboBox.FlatStyle = FlatStyle.Flat;
            comboBox.BackColor = Branding.AccentDark;
            comboBox.ForeColor = Branding.HeadingText;
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

            using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.FromArgb(18, 22, 30), Color.FromArgb(11, 13, 18), 90f))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }

            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(28, Branding.Accent)))
            {
                e.Graphics.FillEllipse(glowBrush, new Rectangle(bounds.Width - 220, -84, 240, 180));
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
            e.Graphics.Clear(group.Parent?.BackColor ?? Branding.WindowBackground);
            if (group.Width <= 3 || group.Height <= 10)
            {
                return;
            }

            Rectangle rect = new Rectangle(1, 8, group.Width - 3, group.Height - 10);
            using (GraphicsPath path = CreateRoundedPath(rect, 16))
            using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(214, Branding.CardBackground)))
            using (Pen borderPen = new Pen(Color.FromArgb(126, Branding.Outline)))
            using (SolidBrush titleBackground = new SolidBrush(group.Parent?.BackColor ?? Branding.WindowBackground))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);

                string title = group.Tag as string ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(title))
                {
                    Size textSize = TextRenderer.MeasureText(title, group.Font);
                    Rectangle textRect = new Rectangle(16, 0, textSize.Width + 8, 20);
                    e.Graphics.FillRectangle(titleBackground, textRect);
                    TextRenderer.DrawText(e.Graphics, title, group.Font, new Point(20, 1), Branding.MutedText);
                }
            }
        }

        private static void ButtonResize(object sender, EventArgs e)
        {
            if (sender is Button button)
            {
                ApplyRoundedRegion(button, 8);
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

            Color fill = accent ? Branding.Accent : Color.FromArgb(164, Branding.CardBackground);
            Color border = accent ? Color.FromArgb(185, Branding.Accent) : Color.FromArgb(170, Branding.Outline);

            if (pressed)
            {
                fill = accent ? Color.FromArgb(86, 126, 238) : Color.FromArgb(92, 100, 118);
            }
            else if (hovered)
            {
                fill = accent ? Color.FromArgb(122, 160, 255) : Branding.AccentSoft;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(button.Parent?.BackColor ?? Branding.WindowBackground);
            if (button.Width <= 1 || button.Height <= 1)
            {
                return;
            }

            Rectangle rect = new Rectangle(0, 0, button.Width - 1, button.Height - 1);
            using (GraphicsPath path = CreateRoundedPath(rect, 8))
            using (SolidBrush fillBrush = new SolidBrush(fill))
            using (Pen borderPen = new Pen(border))
            {
                e.Graphics.FillPath(fillBrush, path);
                e.Graphics.DrawPath(borderPen, path);
            }

              Rectangle textRect = new Rectangle(0, 0, button.Width, button.Height);
              TextRenderer.DrawText(
                  e.Graphics,
                  button.Text,
                  button.Font,
                  textRect,
                  button.ForeColor,
                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
    }
}
