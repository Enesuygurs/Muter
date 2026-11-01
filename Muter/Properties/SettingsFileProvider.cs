using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml;

namespace Muter.Properties
{
    // Keeps the settings in one fixed file, %LOCALAPPDATA%\Muter\settings.xml, whatever folder Muter.exe is run from and
    // whatever its version. (The .NET default keeps a separate user.config per exe folder and per version, so moving
    // the exe or unzipping a new version elsewhere started from scratch.)
    //
    // * Writes go to a temporary file that then replaces settings.xml in one step, keeping the previous file as
    //   settings.xml.bak: an interrupted save never leaves a broken file, and a damaged file falls back to the backup.
    // * The first time (no settings.xml yet) the values are taken over from the newest user.config of earlier versions,
    //   preferring the one of this exe's folder. Those old files are left as they were.
    // * Values are stored as the same text as in user.config; settings left at their default are not stored, so a
    //   changed default applies to them.
    public sealed class SettingsFileProvider : SettingsProvider, IApplicationSettingsProvider
    {
        public const string FileName = "settings.xml";

        private static readonly string DefaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Muter");
        private static string directory;
        private static string legacyDirectory;

        // Where settings.xml is. Tests point it elsewhere.
        internal static string Directory
        {
            get => directory ?? DefaultDirectory;
            set => directory = value;
        }

        // Where the user.config files of versions up to 1.3 are (one folder per exe location and version). Tests point it
        // elsewhere.
        internal static string LegacyDirectory
        {
            get => legacyDirectory ?? DefaultDirectory;
            set => legacyDirectory = value;
        }

        internal static string FilePath => Path.Combine(Directory, FileName);

        public override string ApplicationName
        {
            get => "Muter";
            set { }
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(string.IsNullOrEmpty(name) ? nameof(SettingsFileProvider) : name, config ?? new NameValueCollection());
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
        {
            Dictionary<string, string> stored = LoadOrImport();
            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty property in properties)
            {
                var value = new SettingsPropertyValue(property) { IsDirty = false };
                if (stored.TryGetValue(property.Name, out string text))
                    value.SerializedValue = text;
                values.Add(value);
            }
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values)
        {
            // Entries this version does not know (e.g. written by a newer version) are kept.
            Dictionary<string, string> stored = Load(FilePath) ?? new Dictionary<string, string>();
            foreach (SettingsPropertyValue value in values)
            {
                if (value.UsingDefaultValue && !value.IsDirty)
                    continue;
                if (value.SerializedValue is string text)
                    stored[value.Name] = text;
                else
                    stored.Remove(value.Name);
            }
            Write(stored);
            foreach (SettingsPropertyValue value in values)
                value.IsDirty = false;
        }

        // Back to the defaults (an empty file, so the old settings are not taken over again).
        public void Reset(SettingsContext context) => Write(new Dictionary<string, string>());

        // Nothing to do: there is one file for every version.
        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        {
        }

        // What an earlier version saved (null if nothing), for converting settings of version 1.0.
        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            var value = new SettingsPropertyValue(property);
            string legacy = FindLegacyFile();
            Dictionary<string, string> old = legacy != null ? ReadUserConfig(legacy) : null;
            if (old != null && old.TryGetValue(property.Name, out string text))
                value.SerializedValue = text;
            else
                value.PropertyValue = null;
            value.IsDirty = false;
            return value;
        }

        private static Dictionary<string, string> LoadOrImport()
        {
            string path = FilePath;
            Dictionary<string, string> stored = Load(path);
            if (stored != null)
                return stored;

            stored = Load(path + ".bak");
            if (stored != null)
            {
                Log.Warn($"{path} could not be read; using the backup");
                SetAside(path);
                Write(stored);
                return stored;
            }
            if (File.Exists(path))
            {
                Log.Warn($"{path} could not be read and there is no backup; starting with the defaults");
                SetAside(path);
                return new Dictionary<string, string>();
            }

            // First start with this version of the settings: take over what an earlier version saved.
            string legacy = FindLegacyFile();
            Dictionary<string, string> imported = legacy != null ? ReadUserConfig(legacy) : null;
            if (imported == null)
                return new Dictionary<string, string>();
            Log.Info($"Settings taken over from {legacy}");
            Write(imported);
            return imported;
        }

        // A damaged file is kept for inspection, not overwritten.
        private static void SetAside(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Copy(path, path + ".damaged", overwrite: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not keep a copy of the damaged settings file", ex);
            }
        }

        // null when the file is missing or cannot be read.
        private static Dictionary<string, string> Load(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                var document = new XmlDocument();
                document.Load(path);
                if (document.DocumentElement?.Name != "settings")
                    return null;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (XmlElement setting in document.DocumentElement.GetElementsByTagName("setting"))
                {
                    string name = setting.GetAttribute("name");
                    if (!string.IsNullOrEmpty(name))
                        values[name] = setting.InnerText;
                }
                return values;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read {path}", ex);
                return null;
            }
        }

        private static void Write(Dictionary<string, string> values)
        {
            string path = FilePath;
            System.IO.Directory.CreateDirectory(Directory);
            string temporary = path + ".tmp";
            var xmlSettings = new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
            using (XmlWriter writer = XmlWriter.Create(temporary, xmlSettings))
            {
                writer.WriteStartElement("settings");
                foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartElement("setting");
                    writer.WriteAttributeString("name", pair.Key);
                    writer.WriteString(pair.Value);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }

            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, path);
        }

        // The newest user.config of earlier versions: of this exe's folder if there is one, otherwise of any folder.
        internal static string FindLegacyFile()
        {
            try
            {
                if (!System.IO.Directory.Exists(LegacyDirectory))
                    return null;
                var candidates = new DirectoryInfo(LegacyDirectory).GetDirectories()
                    .SelectMany(exeFolder => exeFolder.GetDirectories()
                        .Select(versionFolder => new
                        {
                            ExeFolder = exeFolder.Name,
                            Version = Version.TryParse(versionFolder.Name, out Version v) ? v : null,
                            File = new FileInfo(Path.Combine(versionFolder.FullName, "user.config")),
                        }))
                    .Where(c => c.Version != null && c.File.Exists)
                    .ToList();
                if (candidates.Count == 0)
                    return null;

                string own = OwnLegacyFolderName();
                var mine = candidates.Where(c => string.Equals(c.ExeFolder, own, StringComparison.OrdinalIgnoreCase)).ToList();
                return (mine.Count > 0
                    ? mine.OrderByDescending(c => c.Version).First()
                    : candidates.OrderByDescending(c => c.File.LastWriteTimeUtc).First()).File.FullName;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not look for the settings of earlier versions", ex);
                return null;
            }
        }

        // The folder .NET would use for this exe (e.g. Muter.exe_Url_xxxx), or null.
        internal static string OwnLegacyFolderName()
        {
            try
            {
                string userConfig = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
                return new DirectoryInfo(Path.GetDirectoryName(userConfig)).Parent?.Name;
            }
            catch (Exception ex)
            {
                Log.Warn("Could not work out the settings folder of earlier versions", ex);
                return null;
            }
        }

        // The values in a user.config of an earlier version (configuration/userSettings/Muter.Properties.Settings).
        internal static Dictionary<string, string> ReadUserConfig(string path)
        {
            try
            {
                var document = new XmlDocument();
                document.Load(path);
                XmlNode section = document.SelectSingleNode("/configuration/userSettings/Muter.Properties.Settings");
                if (section == null)
                    return null;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (XmlElement setting in section.SelectNodes("setting"))
                {
                    string name = setting.GetAttribute("name");
                    XmlNode value = setting.SelectSingleNode("value");
                    if (!string.IsNullOrEmpty(name) && value != null)
                        values[name] = value.InnerText;
                }
                return values;
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not read the earlier settings in {path}", ex);
                return null;
            }
        }
    }
}
