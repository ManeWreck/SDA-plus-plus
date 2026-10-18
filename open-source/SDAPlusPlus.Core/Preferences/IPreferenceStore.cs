namespace SDAPlusPlus.Core.Preferences;

public interface IPreferenceStore
{
    AppPreferences Load();
    void Save(AppPreferences preferences);
}
