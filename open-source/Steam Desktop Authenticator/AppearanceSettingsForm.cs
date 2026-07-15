using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal sealed class AppearanceSettingsForm : Form
    {
        private readonly Manifest manifest;
        private readonly ComboBox cmbTheme = new ComboBox();
        private readonly ComboBox cmbDensity = new ComboBox();
        private readonly Button btnAccent = new Button();
        private readonly Button btnSave = new Button();
        private readonly Button btnCancel = new Button();
        private readonly GroupBox preview = new GroupBox();
        private readonly Label lblPreviewTitle = new Label();
        private readonly Label lblPreviewBody = new Label();
        private Color selectedAccent;
        private bool saved;

        public AppearanceSettingsForm(Manifest manifest)
        {
            this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            selectedAccent = Branding.ParseAccent(manifest.UiAccentColor, Color.FromArgb(108, 146, 255));

            Text = Localizer.Choose("Appearance", "Внешний вид");
            ClientSize = new Size(430, 350);
            MinimumSize = new Size(430, 350);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;
            Icon = Branding.LoadAppIcon();
            Paint += ModernUi.PaintGlassBackground;

            BuildLayout();
            ModernUi.AttachWindowChrome(this, false, false);
            ApplyPreview();
        }

        private void BuildLayout()
        {
            Label intro = CreateLabel(
                Localizer.Choose(
                    "Choose a base surface, accent color, and control density.",
                    "Выберите основу темы, акцентный цвет и плотность элементов."),
                20,
                54,
                390,
                38);

            Label themeLabel = CreateLabel(Localizer.Choose("Theme", "Тема"), 20, 98, 120, 20);
            cmbTheme.SetBounds(20, 120, 186, 28);
            cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTheme.Items.AddRange(new object[] { "Graphite", "Obsidian", "Slate" });
            cmbTheme.SelectedIndex = (int)manifest.UiThemePreset;

            Label densityLabel = CreateLabel(Localizer.Choose("Density", "Плотность"), 224, 98, 120, 20);
            cmbDensity.SetBounds(224, 120, 186, 28);
            cmbDensity.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDensity.Items.Add(Localizer.Choose("Compact", "Компактная"));
            cmbDensity.Items.Add(Localizer.Choose("Comfortable", "Свободная"));
            cmbDensity.SelectedIndex = (int)manifest.UiDensity;

            btnAccent.SetBounds(20, 162, 390, 32);
            btnAccent.Text = Localizer.Choose("Choose accent color", "Выбрать акцентный цвет");
            btnAccent.Click += btnAccent_Click;

            preview.SetBounds(20, 210, 390, 78);
            lblPreviewTitle.SetBounds(18, 24, 210, 22);
            lblPreviewTitle.Text = Localizer.Choose("SDA++ appearance preview", "Предпросмотр SDA++");
            lblPreviewTitle.Font = new Font("Segoe UI Semibold", 10F);
            lblPreviewBody.SetBounds(18, 48, 330, 18);
            lblPreviewBody.Text = Localizer.Choose("One visual system across every window", "Единый стиль во всех окнах");
            preview.Controls.Add(lblPreviewTitle);
            preview.Controls.Add(lblPreviewBody);

            btnCancel.SetBounds(218, 304, 92, 30);
            btnCancel.Text = Localizer.Choose("Cancel", "Отмена");
            btnCancel.Click += (_, _) => Close();
            btnSave.SetBounds(318, 304, 92, 30);
            btnSave.Text = Localizer.Choose("Apply", "Применить");
            btnSave.Click += btnSave_Click;

            Controls.AddRange(new Control[]
            {
                intro, themeLabel, cmbTheme, densityLabel, cmbDensity,
                btnAccent, preview, btnCancel, btnSave
            });

            cmbTheme.SelectedIndexChanged += (_, _) => ApplyPreview();
            cmbDensity.SelectedIndexChanged += (_, _) => ApplyPreview();
            FormClosed += (_, _) =>
            {
                if (!saved)
                {
                    Branding.ApplyAppearance(manifest);
                }
            };
        }

        private Label CreateLabel(string text, int x, int y, int width, int height)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                ForeColor = Branding.MutedText
            };
        }

        private void btnAccent_Click(object sender, EventArgs e)
        {
            using ColorDialog dialog = new ColorDialog
            {
                Color = selectedAccent,
                FullOpen = true,
                AnyColor = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                selectedAccent = dialog.Color;
                ApplyPreview();
            }
        }

        private void ApplyPreview()
        {
            Manifest draft = new Manifest
            {
                UiThemePreset = (UiThemePreset)Math.Max(0, cmbTheme.SelectedIndex),
                UiDensity = (UiDensity)Math.Max(0, cmbDensity.SelectedIndex),
                UiAccentColor = Branding.ToHex(selectedAccent)
            };

            Branding.ApplyAppearance(draft);
            BackColor = Branding.WindowBackground;
            ForeColor = Branding.HeadingText;
            ModernUi.RoundComboBox(cmbTheme);
            ModernUi.RoundComboBox(cmbDensity);
            ModernUi.ApplyGlassCard(preview, Localizer.Choose("Preview", "Предпросмотр"));
            lblPreviewTitle.ForeColor = Branding.HeadingText;
            lblPreviewBody.ForeColor = Branding.MutedText;
            ModernUi.RoundButton(btnAccent, false);
            ModernUi.RoundButton(btnCancel, false);
            ModernUi.RoundButton(btnSave, true);
            btnAccent.ForeColor = Branding.HeadingText;
            ModernUi.RefreshTheme(this);
            Invalidate(true);
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            manifest.UiThemePreset = (UiThemePreset)cmbTheme.SelectedIndex;
            manifest.UiDensity = (UiDensity)cmbDensity.SelectedIndex;
            manifest.UiAccentColor = Branding.ToHex(selectedAccent);
            manifest.Save();
            Branding.ApplyAppearance(manifest);
            saved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
