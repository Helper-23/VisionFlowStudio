using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using VisionFlowStudio.Licensing;

namespace VisionFlowStudio.App
{
    public partial class ActivationWindow : Window
    {
        private readonly LicenseValidationResult _initialResult;

        public ActivationWindow(LicenseValidationResult initialResult)
        {
            InitializeComponent();
            _initialResult = initialResult;
            MachineCodeBox.Text = MachineFingerprint.GetMachineCode();
            ApplyLanguage();
            if (initialResult != null && initialResult.ErrorCode != LicenseErrorCode.Missing)
                StatusText.Text = LocalizeValidation(initialResult);
        }

        private static string L(string chinese, string english)
        {
            return LocalizationService.IsEnglish ? english : chinese;
        }

        private void ApplyLanguage()
        {
            Title = L("VisionFlow Studio - 软件激活", "VisionFlow Studio - Activation");
            TitleText.Text = L("软件尚未激活", "Software activation required");
            DescriptionText.Text = L(
                "请复制本机机器码并发送给软件供应商，收到许可证后粘贴或导入完成激活。",
                "Copy this machine code and send it to your software supplier. Paste or import the issued license to activate this computer.");
            MachineLabel.Text = L("本机机器码", "Machine code");
            LicenseLabel.Text = L("许可证密钥", "License key");
            CopyButton.Content = L("复制机器码", "Copy code");
            ImportButton.Content = L("导入许可证文件...", "Import license...");
            ExitButton.Content = L("退出", "Exit");
            ActivateButton.Content = L("激活", "Activate");
            StatusText.Text = _initialResult == null || _initialResult.ErrorCode == LicenseErrorCode.Missing
                ? L("等待输入许可证。机器码不会因修改 IP 或更换网卡而变化。", "Waiting for a license. The machine code does not change when the IP address or network adapter changes.")
                : LocalizeValidation(_initialResult);
        }

        private void CopyMachineCode_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(MachineCodeBox.Text);
            StatusText.Text = L("机器码已复制。", "Machine code copied.");
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = L("选择许可证文件", "Select license file"),
                Filter = L("VisionFlow 许可证 (*.vfslic)|*.vfslic|所有文件 (*.*)|*.*", "VisionFlow license (*.vfslic)|*.vfslic|All files (*.*)|*.*")
            };
            if (dialog.ShowDialog(this) == true)
                LicenseKeyBox.Text = File.ReadAllText(dialog.FileName).Trim();
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            var result = LicenseStore.Install(LicenseKeyBox.Text);
            if (!result.IsValid)
            {
                StatusText.Text = LocalizeValidation(result);
                MessageBox.Show(this, LocalizeValidation(result), L("激活失败", "Activation failed"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            StatusText.Text = L("激活成功，正在启动软件...", "Activation succeeded. Starting the application...");
            DialogResult = true;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static string LocalizeValidation(LicenseValidationResult result)
        {
            if (result == null) return L("许可证无效。", "Invalid license.");
            if (LocalizationService.IsEnglish) return result.Message;
            switch (result.ErrorCode)
            {
                case LicenseErrorCode.Missing: return "尚未安装许可证。";
                case LicenseErrorCode.InvalidFormat: return "许可证格式不正确。";
                case LicenseErrorCode.InvalidSignature: return "许可证签名无效，文件可能已被修改。";
                case LicenseErrorCode.WrongProduct: return "该许可证不属于 VisionFlow Studio。";
                case LicenseErrorCode.WrongMachine: return "该许可证与本机机器码不匹配。";
                case LicenseErrorCode.NotYetValid: return "许可证尚未生效，请检查系统时间。";
                case LicenseErrorCode.Expired: return "许可证已过期。";
                case LicenseErrorCode.ClockRollback: return "检测到系统时间回退，请校正时间；如仍失败请联系供应商。";
                case LicenseErrorCode.StorageError: return "许可证存储失败：" + result.Message;
                default: return "许可证有效。";
            }
        }
    }
}
