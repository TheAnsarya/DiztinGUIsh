using System;
using System.ComponentModel;
using System.Configuration;
using Diz.Core.Interfaces;

namespace Diz.Core.Mesen2
{
    /// <summary>
    /// Configuration implementation for Mesen2 streaming client
    /// </summary>
    public class Mesen2Configuration : IMesen2Configuration
    {
        private const string SECTION_NAME = "Mesen2Integration";
        
        public string DefaultHost { get; set; } = "127.0.0.1";
        public int DefaultPort { get; set; } = 9998;
        public int ConnectionTimeoutMs { get; set; } = 5000;
        public bool AutoReconnect { get; set; } = true;
        public int AutoReconnectDelayMs { get; set; } = 2000;
        public int MaxReconnectAttempts { get; set; } = 5;
        public bool VerboseLogging { get; set; } = false;

        public Mesen2Configuration()
        {
            Load(); // Load settings on initialization
        }

        public void Save()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                
                // Remove existing section
                if (config.Sections[SECTION_NAME] != null)
                {
                    config.Sections.Remove(SECTION_NAME);
                }

                // Add new section with current settings
                var section = new AppSettingsSection();
                section.Settings.Add("DefaultHost", DefaultHost);
                section.Settings.Add("DefaultPort", DefaultPort.ToString());
                section.Settings.Add("ConnectionTimeoutMs", ConnectionTimeoutMs.ToString());
                section.Settings.Add("AutoReconnect", AutoReconnect.ToString());
                section.Settings.Add("AutoReconnectDelayMs", AutoReconnectDelayMs.ToString());
                section.Settings.Add("MaxReconnectAttempts", MaxReconnectAttempts.ToString());
                section.Settings.Add("VerboseLogging", VerboseLogging.ToString());

                config.Sections.Add(SECTION_NAME, section);
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch (Exception)
            {
                // Silently fail if configuration cannot be saved
                // This prevents the application from crashing due to permission issues
            }
        }

        public void Load()
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var section = config.Sections[SECTION_NAME] as AppSettingsSection;
                
                if (section?.Settings != null)
                {
                    LoadSetting(() => DefaultHost = GetStringSetting(section, "DefaultHost", DefaultHost));
                    LoadSetting(() => DefaultPort = GetIntSetting(section, "DefaultPort", DefaultPort));
                    LoadSetting(() => ConnectionTimeoutMs = GetIntSetting(section, "ConnectionTimeoutMs", ConnectionTimeoutMs));
                    LoadSetting(() => AutoReconnect = GetBoolSetting(section, "AutoReconnect", AutoReconnect));
                    LoadSetting(() => AutoReconnectDelayMs = GetIntSetting(section, "AutoReconnectDelayMs", AutoReconnectDelayMs));
                    LoadSetting(() => MaxReconnectAttempts = GetIntSetting(section, "MaxReconnectAttempts", MaxReconnectAttempts));
                    LoadSetting(() => VerboseLogging = GetBoolSetting(section, "VerboseLogging", VerboseLogging));
                }
            }
            catch (Exception)
            {
                // Use default values if configuration cannot be loaded
            }
        }

        private void LoadSetting(Action action)
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // Ignore individual setting load failures
            }
        }

        private string GetStringSetting(AppSettingsSection section, string key, string defaultValue)
        {
            var setting = section.Settings[key];
            return setting?.Value ?? defaultValue;
        }

        private int GetIntSetting(AppSettingsSection section, string key, int defaultValue)
        {
            var setting = section.Settings[key];
            return int.TryParse(setting?.Value, out var result) ? result : defaultValue;
        }

        private bool GetBoolSetting(AppSettingsSection section, string key, bool defaultValue)
        {
            var setting = section.Settings[key];
            return bool.TryParse(setting?.Value, out var result) ? result : defaultValue;
        }
    }
}