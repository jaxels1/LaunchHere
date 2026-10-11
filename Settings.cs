using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace LaunchHere
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] internal bool Enabled = true;
        [DataMember] internal bool EarlyPositioning = true;
        [DataMember] internal bool ThumbnailPreviews = true;
        [DataMember] internal bool RetryMovement = true;
        [DataMember] internal int LaunchTimeoutSeconds = 12;
        [DataMember] internal List<string> ExcludedApps = new List<string>();

        [OnDeserializing]
        private void Defaults(StreamingContext context)
        {
            Enabled = true;
            EarlyPositioning = true;
            ThumbnailPreviews = true;
            RetryMovement = true;
            LaunchTimeoutSeconds = 12;
            ExcludedApps = new List<string>();
        }

        internal static string ProcessName(string input)
        {
            if (String.IsNullOrWhiteSpace(input)) return null;
            string name = input.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            if (name.Length == 0 || name.Length > 255 || name == "." || name == ".." || Regex.IsMatch(name, @"[<>:""/\\|?*\x00-\x1F]")) return null;
            return name;
        }

        internal void Normalize()
        {
            LaunchTimeoutSeconds = Math.Max(3, Math.Min(30, LaunchTimeoutSeconds));
            var clean = new List<string>();
            foreach (string item in ExcludedApps ?? new List<string>())
            {
                string name = ProcessName(item);
                if (name != null && !clean.Exists(x => String.Equals(x, name, StringComparison.OrdinalIgnoreCase))) clean.Add(name);
            }
            clean.Sort(StringComparer.OrdinalIgnoreCase);
            ExcludedApps = clean;
        }

        internal bool IsExcluded(string name)
        {
            return ExcludedApps.Exists(x => String.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class SettingsStore
    {
        internal static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchHere", "settings.json"); } }
        private static string LegacyPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarMonitor", "settings.json"); } }
        private readonly string path;
        private readonly string legacyPath;
        internal SettingsStore(string path, string legacyPath = null)
        {
            this.path = path;
            this.legacyPath = legacyPath ?? (String.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultPath), StringComparison.OrdinalIgnoreCase) ? LegacyPath : null);
        }
        internal AppSettings Load(out string notice)
        {
            notice = null;
            try
            {
                string loadPath = path;
                if (!File.Exists(loadPath))
                {
                    if (legacyPath == null || !File.Exists(legacyPath)) return new AppSettings();
                    loadPath = legacyPath;
                    notice = "Preferences carried over from Taskbar Monitor. Changes save automatically.";
                }
                using (var stream = File.OpenRead(loadPath))
                {
                    if (stream.Length > 1024 * 1024) throw new SerializationException("Settings file is too large.");
                    var settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                    if (settings == null) throw new SerializationException("Settings are empty.");
                    settings.Normalize();
                    return settings;
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) && !(ex is System.Security.SecurityException) && !(ex is SerializationException) && !(ex is System.Xml.XmlException)) throw;
                notice = "Saved settings could not be loaded. Defaults are active; changes will replace the settings file.";
                return new AppSettings();
            }
        }

        internal void Save(AppSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                {
                    new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
        }
    }
}
