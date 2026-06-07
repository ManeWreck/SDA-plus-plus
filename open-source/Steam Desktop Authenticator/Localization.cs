namespace Steam_Desktop_Authenticator
{
    public enum AppLanguage
    {
        English = 0,
        Russian = 1
    }

    internal static class Localizer
    {
        public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.English;

        public static bool IsRussian => CurrentLanguage == AppLanguage.Russian;

        public static void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
        }

        public static string LanguageDisplayName(AppLanguage language)
        {
            return language == AppLanguage.Russian ? "Русский" : "English";
        }

        public static string Choose(string english, string russian)
        {
            return IsRussian ? russian : english;
        }
    }
}
