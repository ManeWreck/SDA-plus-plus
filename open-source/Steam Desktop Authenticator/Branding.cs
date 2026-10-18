using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class Branding
    {
        private static Icon cachedAppIcon;
        private static UiPalette palette = UiPalette.Create(UiThemePreset.Graphite, Color.FromArgb(108, 146, 255));

        public const string AppName = "SDA++";
        public const string FullAppName = "SDA++ by Manewreck";
        public const string GithubUser = "Manewreck";
        public const string GithubUrl = "https://github.com/ManeWreck/SDA-plus-plus";
        public const string GithubReleasesUrl = "https://github.com/ManeWreck/SDA-plus-plus/releases";
        public const string GithubLatestReleaseApiUrl = "https://api.github.com/repos/ManeWreck/SDA-plus-plus/releases/latest";
        public const string KofiUrl = "https://ko-fi.com/manewreck";
        public const string FooterText = "Кастомный форк Manewreck";
        public const string WelcomeSubtitle = "";

        public static Color WindowBackground => palette.WindowBackground;
        public static Color CardBackground => palette.CardBackground;
        public static Color Accent => palette.Accent;
        public static Color AccentSoft => palette.AccentSoft;
        public static Color AccentDark => palette.AccentDark;
        public static Color Outline => palette.Outline;
        public static Color Success => palette.Success;
        public static Color Warning => palette.Warning;
        public static Color Danger => palette.Danger;
        public static Color MutedText => palette.MutedText;
        public static Color HeadingText => palette.HeadingText;
        public static UiDensity Density { get; private set; } = UiDensity.Compact;
        public static int ControlRadius => Density == UiDensity.Compact ? 8 : 11;
        public static int CardRadius => Density == UiDensity.Compact ? 14 : 18;

        public static void ApplyAppearance(Manifest manifest)
        {
            if (manifest == null)
            {
                return;
            }

            Color accent = ParseAccent(manifest.UiAccentColor, Color.FromArgb(108, 146, 255));
            palette = UiPalette.Create(manifest.UiThemePreset, accent);
            Density = manifest.UiDensity;
        }

        public static string NormalizeAccent(string value)
        {
            return ToHex(ParseAccent(value, Color.FromArgb(108, 146, 255)));
        }

        public static Color ParseAccent(string value, Color fallback)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    Color parsed = ColorTranslator.FromHtml(value.Trim());
                    if (parsed.A > 0)
                    {
                        return Color.FromArgb(parsed.R, parsed.G, parsed.B);
                    }
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        public static string ToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private sealed class UiPalette
        {
            public Color WindowBackground { get; private set; }
            public Color CardBackground { get; private set; }
            public Color Accent { get; private set; }
            public Color AccentSoft { get; private set; }
            public Color AccentDark { get; private set; }
            public Color Outline { get; private set; }
            public Color Warning { get; private set; }
            public Color Success { get; private set; }
            public Color Danger { get; private set; }
            public Color MutedText { get; private set; }
            public Color HeadingText { get; private set; }

            public static UiPalette Create(UiThemePreset preset, Color accent)
            {
                Color window;
                Color card;
                Color field;
                Color outline;

                switch (preset)
                {
                    case UiThemePreset.Obsidian:
                        window = Color.FromArgb(8, 10, 14);
                        card = Color.FromArgb(15, 18, 24);
                        field = Color.FromArgb(11, 14, 20);
                        outline = Color.FromArgb(52, 61, 76);
                        break;
                    case UiThemePreset.Slate:
                        window = Color.FromArgb(18, 23, 31);
                        card = Color.FromArgb(28, 35, 46);
                        field = Color.FromArgb(21, 27, 37);
                        outline = Color.FromArgb(78, 91, 111);
                        break;
                    default:
                        window = Color.FromArgb(16, 19, 25);
                        card = Color.FromArgb(24, 28, 36);
                        field = Color.FromArgb(20, 24, 32);
                        outline = Color.FromArgb(70, 80, 98);
                        break;
                }

                return new UiPalette
                {
                    WindowBackground = window,
                    CardBackground = card,
                    Accent = accent,
                    AccentSoft = Blend(card, accent, 0.18f),
                    AccentDark = field,
                    Outline = outline,
                    Success = preset == UiThemePreset.Obsidian
                        ? Color.FromArgb(68, 186, 137)
                        : Color.FromArgb(76, 194, 145),
                    Warning = Blend(outline, Color.FromArgb(214, 170, 82), 0.28f),
                    Danger = Color.FromArgb(191, 92, 100),
                    MutedText = preset == UiThemePreset.Obsidian ? Color.FromArgb(154, 164, 182) : Color.FromArgb(166, 176, 194),
                    HeadingText = Color.FromArgb(240, 244, 252)
                };
            }

            private static Color Blend(Color first, Color second, float amount)
            {
                float inverse = 1f - amount;
                return Color.FromArgb(
                    (int)((first.R * inverse) + (second.R * amount)),
                    (int)((first.G * inverse) + (second.G * amount)),
                    (int)((first.B * inverse) + (second.B * amount)));
            }
        }

        public static Icon LoadAppIcon()
        {
            if (cachedAppIcon == null)
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    cachedAppIcon = new Icon(iconPath);
                }
                else
                {
                    cachedAppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
                }
            }

            return (Icon)cachedAppIcon.Clone();
        }
    }
}
