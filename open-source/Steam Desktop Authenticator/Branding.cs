using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Steam_Desktop_Authenticator
{
    internal static class Branding
    {
        private static Icon cachedAppIcon;

        public const string AppName = "SDA++";
        public const string FullAppName = "SDA++ by Manewreck";
        public const string GithubUser = "Manewreck";
        public const string GithubUrl = "https://github.com/Manewreck";
        public const string KofiUrl = "https://ko-fi.com/manewreck";
        public const string FooterText = "Кастомный форк Manewreck";
        public const string WelcomeSubtitle = "";

        public static readonly Color WindowBackground = Color.FromArgb(16, 19, 25);
        public static readonly Color CardBackground = Color.FromArgb(24, 28, 36);
        public static readonly Color Accent = Color.FromArgb(108, 146, 255);
        public static readonly Color AccentSoft = Color.FromArgb(33, 40, 54);
        public static readonly Color AccentDark = Color.FromArgb(20, 24, 32);
        public static readonly Color Outline = Color.FromArgb(70, 80, 98);
        public static readonly Color Success = Color.FromArgb(108, 146, 255);
        public static readonly Color Warning = Color.FromArgb(72, 86, 110);
        public static readonly Color Danger = Color.FromArgb(191, 92, 100);
        public static readonly Color MutedText = Color.FromArgb(166, 176, 194);
        public static readonly Color HeadingText = Color.FromArgb(240, 244, 252);

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
