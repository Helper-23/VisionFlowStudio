using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VisionFlowStudio.Licensing;

namespace VisionFlowStudio.LicenseTool
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _privateKey = new TextBox();
        private readonly TextBox _machine = new TextBox();
        private readonly TextBox _customer = new TextBox();
        private readonly ComboBox _edition = new ComboBox();
        private readonly TextBox _features = new TextBox();
        private readonly CheckBox _expiresEnabled = new CheckBox();
        private readonly DateTimePicker _expires = new DateTimePicker();
        private readonly TextBox _license = new TextBox();
        private readonly Label _status = new Label();

        public MainForm()
        {
            Text = "VisionFlow Studio 离线授权签发工具（内部使用）";
            Width = 940;
            Height = 720;
            MinimumSize = new Size(760, 580);
            StartPosition = FormStartPosition.CenterScreen;

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 3, RowCount = 8 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            for (var i = 0; i < 6; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(table);

            AddRow(table, 0, "私钥文件", _privateKey, Button("选择...", SelectPrivateKey));
            AddRow(table, 1, "客户机器码", _machine, Button("本机机器码", delegate { _machine.Text = MachineFingerprint.GetMachineCode(); }));
            AddRow(table, 2, "客户名称", _customer, null);
            _edition.DropDownStyle = ComboBoxStyle.DropDown;
            _edition.Items.AddRange(new object[] { "Professional", "Standard", "Trial" });
            _edition.Text = "Professional";
            AddRow(table, 3, "授权版本", _edition, null);
            _features.Text = "All";
            AddRow(table, 4, "功能（逗号分隔）", _features, null);

            var expiryPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            _expiresEnabled.Text = "设置到期日";
            _expiresEnabled.AutoSize = true;
            _expires.Value = DateTime.Today.AddYears(1);
            _expires.Format = DateTimePickerFormat.Short;
            expiryPanel.Controls.Add(_expiresEnabled);
            expiryPanel.Controls.Add(_expires);
            AddRow(table, 5, "有效期", expiryPanel, null);

            table.Controls.Add(new Label { Text = "许可证", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 8, 3, 3) }, 0, 6);
            _license.Multiline = true;
            _license.ScrollBars = ScrollBars.Vertical;
            _license.Font = new Font("Consolas", 9F);
            _license.Dock = DockStyle.Fill;
            table.Controls.Add(_license, 1, 6);
            table.SetColumnSpan(_license, 2);

            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            actions.Controls.Add(Button("生成许可证", GenerateLicense));
            actions.Controls.Add(Button("保存 .vfslic", SaveLicense));
            actions.Controls.Add(Button("复制许可证", delegate { if (!string.IsNullOrWhiteSpace(_license.Text)) Clipboard.SetText(_license.Text); }));
            actions.Controls.Add(Button("生成新密钥对", GenerateNewKeyPair));
            _status.AutoSize = true;
            _status.ForeColor = Color.FromArgb(30, 95, 130);
            _status.Margin = new Padding(14, 9, 3, 3);
            actions.Controls.Add(_status);
            table.Controls.Add(actions, 0, 7);
            table.SetColumnSpan(actions, 3);
        }

        private static Button Button(string text, EventHandler handler)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(105, 30), Margin = new Padding(3) };
            button.Click += handler;
            return button;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control, Control action)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 8) }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 5, 3, 5);
            table.Controls.Add(control, 1, row);
            if (action != null) table.Controls.Add(action, 2, row);
        }

        private void SelectPrivateKey(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog { Filter = "RSA private key (*.xml)|*.xml|All files (*.*)|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) _privateKey.Text = dialog.FileName;
        }

        private void GenerateLicense(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(_privateKey.Text)) throw new FileNotFoundException("请选择签发私钥。", _privateKey.Text);
                if (string.IsNullOrWhiteSpace(_machine.Text)) throw new InvalidOperationException("请输入客户机器码。 ");
                var expiresUtc = _expiresEnabled.Checked ? _expires.Value.Date.AddDays(1).ToUniversalTime() : (DateTime?)null;
                _license.Text = Program.Issue(File.ReadAllText(_privateKey.Text), _machine.Text, _customer.Text, _edition.Text, _features.Text, expiresUtc);
                _status.Text = "已生成，许可证 ID 已写入签名内容。";
            }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "生成失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveLicense(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_license.Text)) { GenerateLicense(sender, e); if (string.IsNullOrWhiteSpace(_license.Text)) return; }
            using (var dialog = new SaveFileDialog { Filter = "VisionFlow license (*.vfslic)|*.vfslic", FileName = "VisionFlowStudio.vfslic" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, _license.Text.Trim());
                _status.Text = "已保存：" + dialog.FileName;
            }
        }

        private void GenerateNewKeyPair(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { Description = "选择仅管理员可访问的密钥目录" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Program.GenerateKeyPair(dialog.SelectedPath);
                _privateKey.Text = Path.Combine(dialog.SelectedPath, "VisionFlowStudio.private.xml");
                MessageBox.Show(this, "密钥对已生成。请把公钥写入应用后重新编译，并离线保管私钥。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
