using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace VisionFlowStudio.App
{
    [DataContract]
    public sealed class ApplicationSettings
    {
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public bool StartMaximized { get; set; }
        [DataMember] public bool AutoLoadProject { get; set; }
        [DataMember] public string AutoLoadProjectPath { get; set; } = string.Empty;
        [DataMember] public string ProtectedProjectPassword { get; set; } = string.Empty;
        [DataMember] public bool AutoSaveProject { get; set; }
        [DataMember] public int AutoSaveIntervalMinutes { get; set; } = 5;
        [DataMember] public string Language { get; set; } = "zh-CN";

        public string GetProjectPassword()
        {
            if (string.IsNullOrWhiteSpace(ProtectedProjectPassword)) return string.Empty;
            try
            {
                var protectedBytes = Convert.FromBase64String(ProtectedProjectPassword);
                var plainBytes = ProtectedData.Unprotect(protectedBytes, ApplicationSettingsStore.PasswordEntropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch { return string.Empty; }
        }

        public void SetProjectPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) { ProtectedProjectPassword = string.Empty; return; }
            var plainBytes = Encoding.UTF8.GetBytes(password);
            ProtectedProjectPassword = Convert.ToBase64String(ProtectedData.Protect(plainBytes, ApplicationSettingsStore.PasswordEntropy, DataProtectionScope.CurrentUser));
        }
    }

    public static class ApplicationSettingsStore
    {
        internal static readonly byte[] PasswordEntropy = Encoding.UTF8.GetBytes("VisionFlowStudio.Settings.Password.v1");
        private const string AutoStartKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartValueName = "VisionFlowStudio";

        public static string SettingsPath
        {
            get
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisionFlowStudio");
                return Path.Combine(folder, "settings.json");
            }
        }

        public static ApplicationSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new ApplicationSettings();
                var serializer = new DataContractJsonSerializer(typeof(ApplicationSettings));
                using (var stream = File.OpenRead(SettingsPath))
                {
                    var settings = (ApplicationSettings)serializer.ReadObject(stream) ?? new ApplicationSettings();
                    if (settings.AutoSaveIntervalMinutes < 1) settings.AutoSaveIntervalMinutes = 5;
                    if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = "zh-CN";
                    return settings;
                }
            }
            catch { return new ApplicationSettings(); }
        }

        public static void Save(ApplicationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var folder = Path.GetDirectoryName(SettingsPath);
            Directory.CreateDirectory(folder);
            var temporary = SettingsPath + ".tmp";
            var serializer = new DataContractJsonSerializer(typeof(ApplicationSettings));
            using (var stream = File.Create(temporary)) serializer.WriteObject(stream, settings);
            if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
            else File.Move(temporary, SettingsPath);
            ApplyAutoStart(settings.StartWithWindows);
        }

        private static void ApplyAutoStart(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(AutoStartKeyPath, true))
            {
                if (key == null) throw new InvalidOperationException("无法打开当前用户的开机启动注册表项。");
                if (!enabled) { key.DeleteValue(AutoStartValueName, false); return; }
                var executable = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrWhiteSpace(executable)) executable = Process.GetCurrentProcess().MainModule.FileName;
                key.SetValue(AutoStartValueName, "\"" + executable + "\" --autostart", RegistryValueKind.String);
            }
        }
    }
}
