using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace VisionFlowStudio.App
{
    public partial class SystemSettingsWindow : Window
    {
        private readonly ApplicationSettings _settings;

        public SystemSettingsWindow(ApplicationSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? new ApplicationSettings();
            StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
            StartMaximizedCheck.IsChecked = _settings.StartMaximized;
            AutoLoadProjectCheck.IsChecked = _settings.AutoLoadProject;
            ProjectPathBox.Text = _settings.AutoLoadProjectPath ?? string.Empty;
            ProjectPasswordBox.Password = _settings.GetProjectPassword();
            AutoSaveProjectCheck.IsChecked = _settings.AutoSaveProject;
            AutoSaveIntervalBox.Text = Math.Max(1, _settings.AutoSaveIntervalMinutes).ToString();
            LanguageCombo.SelectedValue = string.Equals(_settings.Language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
            UpdateAutoLoadPanel();
            UpdateAutoSavePanel();
        }

        private void BrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = LocalizationService.IsEnglish ? "Encrypted Vision Project (*.vfsproj)|*.vfsproj|All Files (*.*)|*.*" : "加密视觉方案 (*.vfsproj)|*.vfsproj|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) ProjectPathBox.Text = dialog.FileName;
        }

        private void AutoLoadProject_Changed(object sender, RoutedEventArgs e) { UpdateAutoLoadPanel(); }
        private void AutoSaveProject_Changed(object sender, RoutedEventArgs e) { UpdateAutoSavePanel(); }

        private void UpdateAutoLoadPanel()
        {
            if (AutoLoadPanel == null || AutoLoadProjectCheck == null) return;
            AutoLoadPanel.IsEnabled = AutoLoadProjectCheck.IsChecked == true;
            AutoLoadPanel.Opacity = AutoLoadPanel.IsEnabled ? 1.0 : 0.55;
        }

        private void UpdateAutoSavePanel()
        {
            if (AutoSavePanel == null || AutoSaveProjectCheck == null) return;
            AutoSavePanel.IsEnabled = AutoSaveProjectCheck.IsChecked == true;
            AutoSavePanel.Opacity = AutoSavePanel.IsEnabled ? 1.0 : 0.55;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var autoLoad = AutoLoadProjectCheck.IsChecked == true;
                var path = (ProjectPathBox.Text ?? string.Empty).Trim();
                var password = ProjectPasswordBox.Password ?? string.Empty;
                var autoSave = AutoSaveProjectCheck.IsChecked == true;
                int autoSaveInterval;
                if (!int.TryParse((AutoSaveIntervalBox.Text ?? string.Empty).Trim(), out autoSaveInterval) || autoSaveInterval < 1 || autoSaveInterval > 1440)
                    throw new InvalidOperationException("自动保存时间间隔必须是 1 到 1440 分钟之间的整数。");
                if (autoLoad)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidOperationException("请选择存在的加密视觉方案。");
                    if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("自动加载加密方案时必须填写方案密码。");
                    ProjectDataStore.Load(path, password);
                }
                _settings.StartWithWindows = StartWithWindowsCheck.IsChecked == true;
                _settings.StartMaximized = StartMaximizedCheck.IsChecked == true;
                _settings.AutoLoadProject = autoLoad;
                _settings.AutoLoadProjectPath = path;
                _settings.AutoSaveProject = autoSave;
                _settings.AutoSaveIntervalMinutes = autoSaveInterval;
                _settings.Language = LanguageCombo.SelectedValue as string ?? "zh-CN";
                _settings.SetProjectPassword(password);
                ApplicationSettingsStore.Save(_settings);
                LocalizationService.SetLanguage(_settings.Language);
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.T("保存系统设置失败"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
