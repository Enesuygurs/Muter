using System.Configuration;

namespace Muter.Properties
{
    // The settings are kept in one fixed file instead of the .NET default (see SettingsFileProvider).
    [SettingsProvider(typeof(SettingsFileProvider))]
    internal sealed partial class Settings
    {
    }
}
