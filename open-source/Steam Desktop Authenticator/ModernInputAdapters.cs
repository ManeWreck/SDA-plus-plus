using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class ModernComboHost : Control
    {
        private readonly ComboBox source;
        private bool hovered;

        public ModernComboHost(ComboBox source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ComboBox;
            AccessibleName = source.AccessibleName;
            source.SelectedIndexChanged += (_, _) => Invalidate();
            source.TextChanged += (_, _) => Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            ShowMenu();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F4)
            {
                ShowMenu();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down && source.SelectedIndex < source.Items.Count - 1)
            {
                source.SelectedIndex++;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && source.SelectedIndex > 0)
            {
                source.SelectedIndex--;
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Branding.CardBackground);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = Focused || hovered ? Branding.Accent : Color.FromArgb(150, Branding.Outline);
            Color fill = hovered ? Branding.AccentSoft : Branding.AccentDark;
            using GraphicsPath path = CreateRoundedPath(bounds, Branding.ControlRadius);
            using SolidBrush fillBrush = new SolidBrush(Color.FromArgb(220, fill));
            using Pen borderPen = new Pen(border);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            string text = source.SelectedItem?.ToString() ?? source.Text ?? string.Empty;
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(12, 0, Math.Max(1, Width - 48), Height),
                Enabled ? Branding.HeadingText : Branding.MutedText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding);

            int centerX = Width - 20;
            int centerY = Height / 2;
            Point[] arrow =
            {
                new Point(centerX - 4, centerY - 2),
                new Point(centerX + 4, centerY - 2),
                new Point(centerX, centerY + 3)
            };
            using SolidBrush arrowBrush = new SolidBrush(Branding.MutedText);
            e.Graphics.FillPolygon(arrowBrush, arrow);
        }

        private void ShowMenu()
        {
            if (!Enabled || source.Items.Count == 0)
            {
                return;
            }

            ContextMenuStrip menu = new ContextMenuStrip
            {
                BackColor = Branding.CardBackground,
                ForeColor = Branding.HeadingText,
                Font = Font,
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Renderer = new ToolStripProfessionalRenderer(new ModernMenuColors()),
                AutoSize = false,
                Opacity = 0D
            };

            for (int index = 0; index < source.Items.Count; index++)
            {
                int selectedIndex = index;
                ToolStripMenuItem item = new ToolStripMenuItem(source.Items[index]?.ToString() ?? string.Empty)
                {
                    Checked = index == source.SelectedIndex,
                    CheckOnClick = false,
                    AutoSize = false,
                    Width = Math.Max(Width, 180),
                    Height = 34,
                    BackColor = Branding.CardBackground,
                    ForeColor = Branding.HeadingText
                };
                item.Click += (_, _) => source.SelectedIndex = selectedIndex;
                menu.Items.Add(item);
            }

            int targetWidth = Math.Max(Width, 180);
            int targetHeight = Math.Max(42, (source.Items.Count * 34) + 8);
            menu.Size = new Size(targetWidth, 8);
            Timer animation = new Timer { Interval = 15 };
            int frame = 0;
            animation.Tick += (_, _) =>
            {
                frame++;
                float time = Math.Min(1F, frame / 10F);
                float eased = 1F - (float)Math.Pow(1F - time, 3D);
                menu.Opacity = Math.Min(1D, eased);
                menu.Size = new Size(targetWidth, Math.Max(8, (int)Math.Round(targetHeight * eased)));
                if (time >= 1F)
                {
                    animation.Stop();
                }
            };
            menu.Closed += (_, _) =>
            {
                animation.Stop();
                animation.Dispose();
                Invalidate();
                menu.Dispose();
            };
            menu.Show(this, new Point(0, Height + 4));
            menu.Size = new Size(targetWidth, 8);
            animation.Start();
        }

        private sealed class ModernMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Branding.CardBackground;
            public override Color MenuItemSelected => Branding.AccentSoft;
            public override Color MenuItemBorder => Branding.Accent;
            public override Color ImageMarginGradientBegin => Branding.CardBackground;
            public override Color ImageMarginGradientMiddle => Branding.CardBackground;
            public override Color ImageMarginGradientEnd => Branding.CardBackground;
            public override Color SeparatorDark => Branding.Outline;
            public override Color SeparatorLight => Branding.CardBackground;
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernNumericHost : Control
    {
        private readonly NumericUpDown source;
        private int hoveredAction;

        public ModernNumericHost(NumericUpDown source)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.SpinButton;
            AccessibleName = source.AccessibleName;
            source.ValueChanged += (_, _) => Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int next = e.X >= Width - 28 ? 1 : e.X >= Width - 56 ? -1 : 0;
            if (next != hoveredAction)
            {
                hoveredAction = next;
                Cursor = next == 0 ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoveredAction = 0;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                if (e.X >= Width - 28)
                {
                    ChangeValue(source.Increment);
                }
                else if (e.X >= Width - 56)
                {
                    ChangeValue(-source.Increment);
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ChangeValue(e.Delta > 0 ? source.Increment : -source.Increment);
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Add)
            {
                ChangeValue(source.Increment);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Subtract)
            {
                ChangeValue(-source.Increment);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Branding.CardBackground);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using GraphicsPath path = CreateRoundedPath(bounds, Branding.ControlRadius);
            using SolidBrush fill = new SolidBrush(Color.FromArgb(220, Branding.AccentDark));
            using Pen border = new Pen(Focused ? Branding.Accent : Color.FromArgb(150, Branding.Outline));
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            string format = source.DecimalPlaces > 0 ? $"F{source.DecimalPlaces}" : "0";
            string valueText = source.Value.ToString(format, CultureInfo.CurrentCulture);
            TextRenderer.DrawText(
                e.Graphics,
                valueText,
                Font,
                new Rectangle(12, 0, Math.Max(1, Width - 68), Height),
                source.ReadOnly || !Enabled ? Branding.MutedText : Branding.HeadingText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawAction(e.Graphics, new Rectangle(Width - 56, 3, 27, Height - 6), "-", hoveredAction == -1);
            DrawAction(e.Graphics, new Rectangle(Width - 28, 3, 25, Height - 6), "+", hoveredAction == 1);
        }

        private void DrawAction(Graphics graphics, Rectangle bounds, string glyph, bool hovered)
        {
            if (hovered && !source.ReadOnly && Enabled)
            {
                using GraphicsPath path = CreateRoundedPath(bounds, 7);
                using SolidBrush fill = new SolidBrush(Branding.AccentSoft);
                graphics.FillPath(fill, path);
            }
            TextRenderer.DrawText(
                graphics,
                glyph,
                Font,
                bounds,
                source.ReadOnly || !Enabled ? Branding.MutedText : Branding.HeadingText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void ChangeValue(decimal delta)
        {
            if (source.ReadOnly || !Enabled)
            {
                return;
            }
            source.Value = Math.Max(source.Minimum, Math.Min(source.Maximum, source.Value + delta));
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
