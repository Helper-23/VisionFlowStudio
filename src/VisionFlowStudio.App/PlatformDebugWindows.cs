using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using VM.Core;
using VMControls.WPF.Release;

namespace VisionFlowStudio.App
{
    public sealed class VisionProDebugWindow : Window
    {
        private readonly string _path;
        private readonly CogToolBlockEditV2 _editor;
        private readonly TextBlock _status;
        private string _imagePath;
        private readonly string _imageInputName;
        private IDisposable _inputImage;

        public VisionProDebugWindow(string path, string imagePath, string imageInputName)
        {
            _path = Path.GetFullPath(path ?? string.Empty);
            if (!File.Exists(_path)) throw new FileNotFoundException("找不到 VisionPro VPP", _path);
            _imagePath = imagePath ?? string.Empty;
            _imageInputName = string.IsNullOrWhiteSpace(imageInputName) ? "InputImage" : imageInputName.Trim();
            var toolBlock = CogSerializer.LoadObjectFromFile(_path) as CogToolBlock;
            if (toolBlock == null) throw new InvalidOperationException("VPP 中不包含 CogToolBlock");

            Title = "VisionPro ToolBlock 调试 - " + Path.GetFileName(_path); Width = 1280; Height = 820; MinWidth = 900; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel();
            var top = new DockPanel { Margin = new Thickness(8) };
            var save = new Button { Content = "保存 VPP", Width = 100 }; var reload = new Button { Content = "重新载入", Width = 100 };
            var inject = new Button { Content = "重新注入图像", Width = 110 }; var choose = new Button { Content = "选择图像", Width = 90 };
            _status = new TextBlock { Text = _path, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            save.Click += delegate { Save(); }; reload.Click += delegate { Reload(); }; inject.Click += delegate { InjectInputImage(true); }; choose.Click += delegate { ChooseImage(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(save); buttons.Children.Add(reload); buttons.Children.Add(inject); buttons.Children.Add(choose);
            DockPanel.SetDock(buttons, Dock.Left); top.Children.Add(buttons); top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

            _editor = new CogToolBlockEditV2 { Dock = System.Windows.Forms.DockStyle.Fill, Subject = toolBlock };
            var host = new WindowsFormsHost { Child = _editor }; root.Children.Add(host); Content = root;
            Loaded += delegate { InjectInputImage(false); };
            Closed += delegate { _editor.Subject = null; if (_inputImage != null) _inputImage.Dispose(); _editor.Dispose(); };
        }

        private void Save()
        {
            try { CogSerializer.SaveObjectToFile(_editor.Subject, _path); _status.Text = "已保存：" + _path; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存 VPP 失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Reload()
        {
            try
            {
                var loaded = CogSerializer.LoadObjectFromFile(_path) as CogToolBlock;
                if (loaded == null) throw new InvalidOperationException("VPP 中不包含 CogToolBlock");
                if (_inputImage != null) { _inputImage.Dispose(); _inputImage = null; }
                _editor.Subject = loaded; InjectInputImage(false);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "重新载入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ChooseImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图像|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return; _imagePath = dialog.FileName; InjectInputImage(true);
        }

        private void InjectInputImage(bool showError)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath)) { _status.Text = "未找到调试图像，请点击“选择图像”。"; return; }
                var toolBlock = _editor.Subject as CogToolBlock; if (toolBlock == null) throw new InvalidOperationException("当前编辑器没有 CogToolBlock");
                using (var source = new System.Drawing.Bitmap(_imagePath))
                using (var normalized = new System.Drawing.Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                {
                    using (var graphics = System.Drawing.Graphics.FromImage(normalized)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    var image = new CogImage24PlanarColor(normalized); toolBlock.Inputs[_imageInputName].Value = image;
                    if (_inputImage != null) _inputImage.Dispose(); _inputImage = image as IDisposable;
                }
                _editor.Subject = toolBlock;
                _status.Text = string.Format("已注入 {0} → {1}（{2}）", Path.GetFileName(_imagePath), _imageInputName, _imagePath);
            }
            catch (Exception ex) { _status.Text = "图像注入失败：" + ex.Message; if (showError) MessageBox.Show(this, ex.Message, "VisionPro 图像注入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    public sealed class VisionMasterDebugWindow : Window
    {
        private readonly TextBlock _status;
        private readonly VmMainViewConfigControl _procedureControl;
        private readonly MainViewModel _vm;
        private readonly FlowNodeViewModel _node;
        private readonly string _procedureName;
        private readonly string _imageInputName;
        private string _imagePath;

        public VisionMasterDebugWindow(MainViewModel vm, FlowNodeViewModel node, string solutionPath, string procedureName, string imagePath, string imageInputName)
        {
            _vm = vm; _node = node;
            _procedureName = procedureName;
            _imagePath = imagePath ?? string.Empty;
            _imageInputName = string.IsNullOrWhiteSpace(imageInputName) ? "InputImage" : imageInputName.Trim();
            Title = "VisionMaster Solution 调试 - " + Path.GetFileName(solutionPath); Width = 1100; Height = 760; MinWidth = 820; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel(); var top = new DockPanel { Margin = new Thickness(8) };
            var save = new Button { Content = "保存 Solution", Width = 110 }; var injectRun = new Button { Content = "注入图像并运行", Width = 125 }; var choose = new Button { Content = "选择图像", Width = 90 };
            _status = new TextBlock { Text = solutionPath, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            save.Click += delegate { try { VmSolution.Save(); _vm.RefreshVisionOutputChoices(); _status.Text = "Solution 已保存，通讯输出列表已刷新。"; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error); } };
            injectRun.Click += delegate { InjectImageAndRun(true); }; choose.Click += delegate { ChooseImage(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(save); buttons.Children.Add(injectRun); buttons.Children.Add(choose);
            DockPanel.SetDock(buttons, Dock.Left); top.Children.Add(buttons); top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var workspace = new Grid();
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _procedureControl = new VmMainViewConfigControl { IsOpenParams = true };
            Grid.SetColumn(_procedureControl, 0); workspace.Children.Add(_procedureControl);
            root.Children.Add(workspace); Content = root;
            Loaded += delegate { BindAndInitialize(); };
            Closed += delegate { _procedureControl.Dispose(); _vm.ReloadVisionMasterOutputChoices(_node); };
        }

        private void BindAndInitialize()
        {
            try { _procedureControl.BindSingleProcedure(_procedureName); InjectImageAndRun(false); }
            catch (Exception ex) { _status.Text = "调试控件初始化失败：" + ex.Message; MessageBox.Show(this, ex.Message, "VisionMaster 调试初始化失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ChooseImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图像|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return; _imagePath = dialog.FileName; InjectImageAndRun(true);
        }

        private void InjectImageAndRun(bool showError)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_imagePath) || !File.Exists(_imagePath)) { _status.Text = "流程已绑定，但未找到调试图像；请点击“选择图像”。"; return; }
                var result = _vm.RunVisionMasterDebugImage(_node, _procedureName, _imagePath, _imageInputName);
                if (result.Status == VisionFlowStudio.Core.NodeRunStatus.Error) throw new InvalidOperationException(result.Message);
                _procedureControl.BindSingleProcedure(_procedureName);
                _status.Text = string.Format("已注入 {0} → {1}，{2}（{3:0.0} ms）", Path.GetFileName(_imagePath), _imageInputName, result.Status, result.CostMs);
            }
            catch (Exception ex) { _status.Text = "图像注入/运行失败：" + ex.Message; if (showError) MessageBox.Show(this, ex.Message, "VisionMaster 图像注入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

    }

    public sealed class HalconScriptDebugWindow : Window
    {
        private static readonly string[] InterfaceGroups = { "图形输入", "图形输出", "控制输入", "控制输出" };
        private readonly MainViewModel _vm;
        private readonly FlowNodeViewModel _node;
        private readonly string _path;
        private readonly TextBox _editor;
        private readonly TextBlock _status;
        private readonly DataGrid _interfaceGrid;
        private readonly ObservableCollection<HalconInterfaceParameter> _interfaceParameters = new ObservableCollection<HalconInterfaceParameter>();
        private XDocument _document;
        private XElement _procedure;
        private XElement _interface;
        private XElement _body;

        public HalconScriptDebugWindow(MainViewModel vm, FlowNodeViewModel node, string path)
        {
            _vm = vm; _node = node; _path = Path.GetFullPath(path ?? string.Empty);
            if (!File.Exists(_path)) throw new FileNotFoundException("找不到 HALCON Procedure", _path);
            Title = "HALCON HDVP 脚本调试 - " + Path.GetFileName(_path); Width = 1180; Height = 780; MinWidth = 820; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel(); var top = new DockPanel { Margin = new Thickness(8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var save = new Button { Content = "保存脚本", Width = 90 }; var reload = new Button { Content = "重新载入", Width = 90 }; var run = new Button { Content = "保存并运行", Width = 105 }; var hdevelop = new Button { Content = "用 HDevelop 打开", Width = 125 };
            save.Click += delegate { Save(); }; reload.Click += delegate { LoadText(); }; run.Click += async delegate { if (!Save()) return; _vm.SelectedNode = _node; await _vm.DebugRunSelectedAsync(); _status.Text = _node.Status + "  " + _node.Message; };
            hdevelop.Click += delegate { OpenHDevelop(); };
            buttons.Children.Add(save); buttons.Children.Add(reload); buttons.Children.Add(run); buttons.Children.Add(hdevelop);
            _status = new TextBlock { Text = _path, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            DockPanel.SetDock(buttons, Dock.Left); top.Children.Add(buttons); top.Children.Add(_status); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

            var split = new Grid(); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            _editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 14, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(8) };
            Grid.SetColumn(_editor, 0); split.Children.Add(_editor); var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(splitter, 1); split.Children.Add(splitter);
            var right = new TabControl { Margin = new Thickness(8) };
            var interfacePanel = new DockPanel();
            var interfaceTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var groupPicker = new ComboBox { ItemsSource = InterfaceGroups, SelectedIndex = 0, Width = 100 };
            var addParameter = new Button { Content = "＋ 新增" }; var deleteParameter = new Button { Content = "删除" }; var moveUp = new Button { Content = "上移" }; var moveDown = new Button { Content = "下移" }; var toggleVector = new Button { Content = "标量/向量" };
            addParameter.Click += delegate { AddInterfaceParameter((string)groupPicker.SelectedItem); };
            deleteParameter.Click += delegate { var item = _interfaceGrid.SelectedItem as HalconInterfaceParameter; if (item != null) _interfaceParameters.Remove(item); };
            moveUp.Click += delegate { MoveInterfaceParameter(-1); }; moveDown.Click += delegate { MoveInterfaceParameter(1); };
            toggleVector.Click += delegate { var item = _interfaceGrid.SelectedItem as HalconInterfaceParameter; if (item != null) { item.Dimension = item.Dimension == 0 ? 1 : 0; _interfaceGrid.Items.Refresh(); } };
            interfaceTools.Children.Add(groupPicker); interfaceTools.Children.Add(addParameter); interfaceTools.Children.Add(deleteParameter); interfaceTools.Children.Add(moveUp); interfaceTools.Children.Add(moveDown); interfaceTools.Children.Add(toggleVector);
            DockPanel.SetDock(interfaceTools, Dock.Top); interfacePanel.Children.Add(interfaceTools);
            _interfaceGrid = new DataGrid { ItemsSource = _interfaceParameters, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
            _interfaceGrid.Columns.Add(new DataGridComboBoxColumn { Header = "参数类别", ItemsSource = InterfaceGroups, SelectedItemBinding = new System.Windows.Data.Binding("Group") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = 105 });
            _interfaceGrid.Columns.Add(new DataGridTextColumn { Header = "参数名称", Binding = new System.Windows.Data.Binding("Name") { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _interfaceGrid.Columns.Add(new DataGridComboBoxColumn { Header = "维度", ItemsSource = new[] { 0, 1 }, SelectedItemBinding = new System.Windows.Data.Binding("Dimension") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = 60 });
            interfacePanel.Children.Add(_interfaceGrid); right.Items.Add(new TabItem { Header = "函数接口", Content = interfacePanel });

            var callPanel = new DockPanel(); var hint = new TextBlock { Text = "调用参数（Input.参数名 会作为控制输入）", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 6, 4, 8) }; DockPanel.SetDock(hint, Dock.Top); callPanel.Children.Add(hint);
            var parameters = new DataGrid { ItemsSource = node.Parameters, AutoGenerateColumns = false, CanUserAddRows = true, CanUserDeleteRows = true };
            parameters.Columns.Add(new DataGridTextColumn { Header = "参数名", Binding = new System.Windows.Data.Binding("Key"), Width = 170 }); parameters.Columns.Add(new DataGridTextColumn { Header = "值", Binding = new System.Windows.Data.Binding("Value"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            callPanel.Children.Add(parameters); right.Items.Add(new TabItem { Header = "调用参数", Content = callPanel });
            Grid.SetColumn(right, 2); split.Children.Add(right); root.Children.Add(split); Content = root; LoadText();
        }

        private void LoadText()
        {
            try
            {
                _document = XDocument.Load(_path, LoadOptions.PreserveWhitespace);
                _procedure = _document.Descendants("procedure").FirstOrDefault();
                if (_procedure == null) throw new InvalidDataException("HDVP 中找不到 procedure 节点");
                _interface = _procedure.Element("interface");
                if (_interface == null) { _interface = new XElement("interface"); _procedure.AddFirst(_interface); }
                _body = _procedure.Element("body");
                if (_body == null) throw new InvalidDataException("HDVP 中找不到 procedure/body 节点");
                _editor.Text = string.Join(Environment.NewLine, _body.Elements("l").Select(x => x.Value));
                LoadInterfaceParameters();
                _status.Text = "已载入过程 " + (string)_procedure.Attribute("name") + "：" + _path;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "载入 HDVP 失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private bool Save()
        {
            try
            {
                if (_document == null || _body == null || _interface == null) throw new InvalidOperationException("HDVP 尚未正确载入");
                ValidateInterfaceParameters();
                _body.Elements("l").Remove();
                foreach (var line in _editor.Text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)) _body.Add(new XElement("l", line));
                SaveInterfaceParameters();
                var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.None };
                using (var writer = XmlWriter.Create(_path, settings)) _document.Save(writer);
                SyncNodeInvocationParameters();
                _vm.ReloadHalconProcedure(_path);
                _status.Text = "函数体和接口参数已写回 HDVP：" + _path; return true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存 HDVP 失败", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        }

        private void LoadInterfaceParameters()
        {
            _interfaceParameters.Clear();
            foreach (var mapping in GetGroupMappings())
                foreach (var parameter in _interface.Elements(mapping.Value).Elements("par"))
                    _interfaceParameters.Add(new HalconInterfaceParameter { Group = mapping.Key, Name = (string)parameter.Attribute("name") ?? string.Empty, Dimension = (int?)parameter.Attribute("dimension") ?? 0 });
        }

        private void SaveInterfaceParameters()
        {
            foreach (var mapping in GetGroupMappings()) _interface.Elements(mapping.Value).Remove();
            foreach (var mapping in GetGroupMappings())
            {
                var items = _interfaceParameters.Where(x => x.Group == mapping.Key).ToList(); if (items.Count == 0) continue;
                var group = new XElement(mapping.Value);
                foreach (var item in items) group.Add(new XElement("par", new XAttribute("name", item.Name), new XAttribute("base_type", mapping.Value == "io" || mapping.Value == "oo" ? "iconic" : "ctrl"), new XAttribute("dimension", item.Dimension)));
                _interface.Add(group);
            }
            SyncParameterDocumentation();
        }

        private void SyncParameterDocumentation()
        {
            var docu = _procedure.Element("docu"); if (docu == null) return;
            var parameters = docu.Element("parameters"); if (parameters == null) { parameters = new XElement("parameters"); docu.AddFirst(parameters); }
            var existing = parameters.Elements("parameter").Where(x => x.Attribute("id") != null).GroupBy(x => (string)x.Attribute("id"), StringComparer.Ordinal).ToDictionary(x => x.Key, x => new XElement(x.First()), StringComparer.Ordinal);
            parameters.Elements("parameter").Remove();
            foreach (var item in _interfaceParameters) { XElement old; parameters.Add(existing.TryGetValue(item.Name, out old) ? old : new XElement("parameter", new XAttribute("id", item.Name))); }
        }

        private void ValidateInterfaceParameters()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in _interfaceParameters)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || !Regex.IsMatch(item.Name, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidOperationException("参数名必须是合法的 HDevelop 标识符：" + item.Name);
                if (!InterfaceGroups.Contains(item.Group)) throw new InvalidOperationException("未知参数类别：" + item.Group);
                if (item.Dimension != 0 && item.Dimension != 1) throw new InvalidOperationException("参数维度只能是 0（标量）或 1（向量）");
                if (!names.Add(item.Name)) throw new InvalidOperationException("函数接口参数名重复：" + item.Name);
            }
        }

        private void SyncNodeInvocationParameters()
        {
            _vm.SelectedNode = _node;
            var imageInput = _interfaceParameters.FirstOrDefault(x => x.Group == "图形输入"); if (imageInput != null) _vm.HalconImageInputName = imageInput.Name;
            var controlOutputs = _interfaceParameters.Where(x => x.Group == "控制输出").ToList();
            var okOutput = controlOutputs.FirstOrDefault(x => string.Equals(x.Name, "IsOK", StringComparison.OrdinalIgnoreCase)) ?? controlOutputs.FirstOrDefault(); if (okOutput != null) _vm.HalconOkOutputName = okOutput.Name;
            var controlInputNames = new HashSet<string>(_interfaceParameters.Where(x => x.Group == "控制输入").Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var old in _node.Parameters.Where(x => x.Key != null && x.Key.StartsWith("Input.", StringComparison.OrdinalIgnoreCase) && !controlInputNames.Contains(x.Key.Substring(6))).ToList()) _node.Parameters.Remove(old);
            foreach (var name in controlInputNames) if (!_node.Parameters.Any(x => string.Equals(x.Key, "Input." + name, StringComparison.OrdinalIgnoreCase))) _node.SetParameter("Input." + name, "0");
        }

        private void AddInterfaceParameter(string group)
        {
            if (string.IsNullOrWhiteSpace(group)) group = InterfaceGroups[0];
            var prefix = group == "图形输入" ? "Image" : group == "图形输出" ? "ResultImage" : group == "控制输入" ? "Param" : "Result";
            var index = 1; var name = prefix; while (_interfaceParameters.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) name = prefix + index++;
            var item = new HalconInterfaceParameter { Group = group, Name = name, Dimension = 0 }; _interfaceParameters.Add(item); _interfaceGrid.SelectedItem = item; _interfaceGrid.ScrollIntoView(item);
        }

        private void MoveInterfaceParameter(int direction)
        {
            var item = _interfaceGrid.SelectedItem as HalconInterfaceParameter; if (item == null) return;
            var sameGroup = _interfaceParameters.Where(x => x.Group == item.Group).ToList(); var position = sameGroup.IndexOf(item); var targetPosition = position + direction;
            if (targetPosition < 0 || targetPosition >= sameGroup.Count) return;
            _interfaceParameters.Move(_interfaceParameters.IndexOf(item), _interfaceParameters.IndexOf(sameGroup[targetPosition])); _interfaceGrid.SelectedItem = item;
        }

        private static IEnumerable<KeyValuePair<string, string>> GetGroupMappings()
        {
            yield return new KeyValuePair<string, string>("图形输入", "io"); yield return new KeyValuePair<string, string>("图形输出", "oo"); yield return new KeyValuePair<string, string>("控制输入", "ic"); yield return new KeyValuePair<string, string>("控制输出", "oc");
        }
        private void OpenHDevelop()
        {
            try
            {
                var exe = @"D:\Program Files\MVTec\HALCON-18.11-Progress\bin\x64-win64\hdevelop.exe";
                if (!File.Exists(exe)) throw new FileNotFoundException("找不到 HDevelop", exe);
                Process.Start(exe, "\"" + _path + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开 HDevelop 失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    public sealed class HalconInterfaceParameter
    {
        public string Group { get; set; }
        public string Name { get; set; }
        public int Dimension { get; set; }
    }
}
