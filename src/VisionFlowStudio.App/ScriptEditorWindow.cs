using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VisionFlowStudio.Scripting;

namespace VisionFlowStudio.App
{
    public sealed class ScriptEditorWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly FlowNodeViewModel _node;
        private readonly CSharpCodeEditor _code;
        private readonly TextBox _scriptFile;
        private readonly TextBox _references;
        private readonly TextBox _imports;
        private readonly TextBox _outputs;
        private readonly TextBlock _status;
        private readonly DataGrid _diagnostics;
        private readonly ListBox _completionList;
        private readonly Popup _completionPopup;
        private readonly TextBlock _completionDescription;
        private readonly Popup _signaturePopup;
        private readonly TextBlock _signatureText;
        private readonly ListBox _dataSources;
        private IReadOnlyList<ScriptCompletionItem> _completionItems = new ScriptCompletionItem[0];
        private readonly DispatcherTimer _diagnosticTimer;
        private readonly ComboBox _classNavigation;
        private readonly ComboBox _memberNavigation;
        private bool _refreshingNavigation;
        private bool _syncingImports;
        private int _completionRequestVersion;
        private int _signatureRequestVersion;

        public ScriptEditorWindow(MainViewModel vm, FlowNodeViewModel node)
        {
            _vm = vm; _node = node;
            Title = "C# 高级脚本 - " + node.NodeName; Width = 1280; Height = 820; MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var config = vm.GetScriptConfig(node);
            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(165) });

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            toolbar.Children.Add(Button("保存", delegate { Save(false); }, true));
            toolbar.Children.Add(Button("另存为 C#", delegate { Save(true); }));
            toolbar.Children.Add(Button("重新载入", delegate { Reload(); }));
            toolbar.Children.Add(Button("添加 DLL", delegate { AddDll(); }));
            toolbar.Children.Add(Button("添加 using", delegate { AddUsing(); }));
            toolbar.Children.Add(Button("转换为完整类", delegate { ConvertToClassMode(); }));
            toolbar.Children.Add(Button("编译检查", async delegate { await CompileAsync(); }));
            toolbar.Children.Add(Button("运行到此节点", async delegate { await RunAsync(); }));
            toolbar.Children.Add(Button("用 Visual Studio 打开", delegate { OpenInVisualStudio(); }));
            toolbar.Children.Add(new TextBlock { Text = "  Ctrl+Space 补全 · F6 编译 · F5 运行", Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetRow(toolbar, 0); root.Children.Add(toolbar);

            var settings = new Grid { Margin = new Thickness(10, 0, 10, 8) };
            settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); settings.ColumnDefinitions.Add(new ColumnDefinition()); settings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); settings.ColumnDefinitions.Add(new ColumnDefinition());
            settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); settings.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _scriptFile = Setting(settings, 0, 0, "脚本文件", config.ScriptFile); _references = Setting(settings, 0, 2, "外部 DLL", string.Join(";", config.References));
            _imports = Setting(settings, 1, 0, "using（源码顶部）", string.Join(";", config.Imports)); _outputs = Setting(settings, 1, 2, "声明输出", string.Join(";", config.DeclaredOutputs));
            _imports.LostKeyboardFocus += delegate { SynchronizeUsingDirectives(); };
            Grid.SetRow(settings, 1); root.Children.Add(settings);

            var navigation = new Grid { Margin = new Thickness(10, 0, 10, 5), Background = Brushes.White };
            navigation.ColumnDefinitions.Add(new ColumnDefinition()); navigation.ColumnDefinitions.Add(new ColumnDefinition());
            _classNavigation = new ComboBox { Margin = new Thickness(0, 0, 3, 0), MinHeight = 26, ToolTip = "类型导航" };
            _memberNavigation = new ComboBox { Margin = new Thickness(3, 0, 0, 0), MinHeight = 26, ToolTip = "成员导航" };
            _classNavigation.SelectionChanged += delegate { NavigateToSelection(_classNavigation); }; _memberNavigation.SelectionChanged += delegate { NavigateToSelection(_memberNavigation); };
            navigation.Children.Add(_classNavigation); Grid.SetColumn(_memberNavigation, 1); navigation.Children.Add(_memberNavigation); Grid.SetRow(navigation, 2); root.Children.Add(navigation);

            var workspace = new Grid { Margin = new Thickness(10, 0, 10, 8) };
            workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            var initialCode = CSharpScriptEngine.LoadCode(config);
            if (!CSharpScriptEngine.IsClassCode(config) && initialCode.Length < 600 && initialCode.Contains("SetOutput(\"Result\"") && initialCode.Contains("SetOutput(\"IsOK\"")) initialCode = CSharpScriptEngine.WrapStatementsInClass(initialCode);
            _code = new CSharpCodeEditor { Text = initialCode };
            SynchronizeUsingDirectives();
            _code.CodeChanged += delegate { CodeChanged(); };
            _code.CompletionRequested += async delegate { await ShowCompletionAsync(); };
            _code.SignatureHelpRequested += async delegate { await ShowSignatureHelpAsync(); };
            _code.SignatureHelpCancelRequested += delegate { CloseSignatureHelp(); };
            _code.CompileRequested += async delegate { await CompileAsync(); };
            _code.RunRequested += async delegate { await RunAsync(); };
            _code.CompletionNextRequested += delegate { _completionList.SelectedIndex = Math.Min(_completionList.Items.Count - 1, _completionList.SelectedIndex + 1); };
            _code.CompletionPreviousRequested += delegate { _completionList.SelectedIndex = Math.Max(0, _completionList.SelectedIndex - 1); };
            _code.CompletionCommitRequested += delegate { CommitCompletion(); };
            _code.CompletionCancelRequested += delegate { CloseCompletion(); };
            Grid.SetColumn(_code, 0); workspace.Children.Add(_code);

            var browser = new DockPanel { Margin = new Thickness(8, 0, 0, 0), Background = Brushes.White };
            var apiTitle = new TextBlock { Text = "流程数据 / 脚本 API（双击插入）", Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold, Padding = new Thickness(9) }; DockPanel.SetDock(apiTitle, Dock.Top); browser.Children.Add(apiTitle);
            var apiTabs = new TabControl();
            _dataSources = new ListBox { ItemsSource = vm.AvailableDataSources, FontFamily = new FontFamily("Consolas") }; _dataSources.MouseDoubleClick += delegate { if (_dataSources.SelectedItem != null) InsertAtCaret("Get<object>(\"" + _dataSources.SelectedItem + "\")"); };
            apiTabs.Items.Add(new TabItem { Header = "节点输出", Content = _dataSources });
            var api = new ListBox { ItemsSource = new[] { "Get<T>(\"Key\")", "GetNodeOutput<T>(\"节点\", \"输出\")", "GetNodeInput<T>(\"节点\", \"输入\")", "Tool(\"节点\").Outputs", "Data[\"Key\"]", "SetOutput(\"Name\", value)", "CancellationToken", "ThrowIfCancellationRequested()" }, FontFamily = new FontFamily("Consolas") };
            api.MouseDoubleClick += delegate { if (api.SelectedItem != null) InsertAtCaret(Convert.ToString(api.SelectedItem)); };
            apiTabs.Items.Add(new TabItem { Header = "宿主 API", Content = api }); browser.Children.Add(apiTabs);
            Grid.SetColumn(browser, 1); workspace.Children.Add(browser); Grid.SetRow(workspace, 3); root.Children.Add(workspace);

            var bottom = new DockPanel { Margin = new Thickness(10, 0, 10, 10) };
            _status = new TextBlock { Text = "就绪", Foreground = Brushes.SlateGray, Padding = new Thickness(6) }; DockPanel.SetDock(_status, Dock.Top); bottom.Children.Add(_status);
            _diagnostics = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
            _diagnostics.Columns.Add(new DataGridTextColumn { Header = "级别", Binding = new System.Windows.Data.Binding("Severity"), Width = 70 });
            _diagnostics.Columns.Add(new DataGridTextColumn { Header = "行", Binding = new System.Windows.Data.Binding("Line"), Width = 45 });
            _diagnostics.Columns.Add(new DataGridTextColumn { Header = "列", Binding = new System.Windows.Data.Binding("Column"), Width = 45 });
            _diagnostics.Columns.Add(new DataGridTextColumn { Header = "代码", Binding = new System.Windows.Data.Binding("Id"), Width = 70 });
            _diagnostics.Columns.Add(new DataGridTextColumn { Header = "消息", Binding = new System.Windows.Data.Binding("Message"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _diagnostics.MouseDoubleClick += delegate { var item = _diagnostics.SelectedItem as ScriptDiagnostic; if (item != null) GoTo(item.Line, item.Column); }; bottom.Children.Add(_diagnostics);
            Grid.SetRow(bottom, 4); root.Children.Add(bottom);

            _completionList = new ListBox { MinWidth = 420, MaxHeight = 300, DisplayMemberPath = "DisplayText", FontFamily = new FontFamily("Consolas") }; VirtualizingStackPanel.SetIsVirtualizing(_completionList, true); VirtualizingStackPanel.SetVirtualizationMode(_completionList, VirtualizationMode.Recycling); _completionList.MouseDoubleClick += delegate { CommitCompletion(); };
            _completionDescription = new TextBlock { MaxWidth = 620, Padding = new Thickness(7, 5, 7, 5), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray };
            _completionList.SelectionChanged += delegate { var item = _completionList.SelectedItem as ScriptCompletionItem; _completionDescription.Text = item == null ? string.Empty : item.Description; };
            var completionPanel = new DockPanel(); DockPanel.SetDock(_completionDescription, Dock.Bottom); completionPanel.Children.Add(_completionDescription); completionPanel.Children.Add(_completionList);
            _completionPopup = new Popup { Child = new Border { BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(1), Background = Brushes.White, Child = completionPanel }, PlacementTarget = _code, Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true };
            _completionPopup.Closed += delegate { _code.CompletionOpen = false; };
            _signatureText = new TextBlock { MaxWidth = 850, Padding = new Thickness(10, 7, 10, 7), TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), Foreground = Brushes.Black };
            _signaturePopup = new Popup { Child = new Border { BorderBrush = Brushes.SteelBlue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.FromRgb(255, 255, 225)), Child = _signatureText }, PlacementTarget = _code, Placement = PlacementMode.Relative, StaysOpen = false, AllowsTransparency = true };
            _diagnosticTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) }; _diagnosticTimer.Tick += async delegate { _diagnosticTimer.Stop(); await CompileAsync(false); };
            Content = root; Loaded += async delegate { _code.FocusEditor(); RefreshNavigation(); await CompileAsync(false); };
            Closing += delegate { _diagnosticTimer.Stop(); _completionRequestVersion++; _signatureRequestVersion++; CloseCompletion(); CloseSignatureHelp(); Save(false); };
            Closed += delegate { _code.Dispose(); };
        }

        private Button Button(string text, RoutedEventHandler action, bool primary = false)
        {
            var button = new Button { Content = text, Margin = new Thickness(2), Padding = new Thickness(11, 6, 11, 6), Background = primary ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : Brushes.White, Foreground = primary ? Brushes.White : Brushes.Black };
            button.Click += action; return button;
        }
        private static TextBox Setting(Grid grid, int row, int column, string label, string value)
        {
            var text = new TextBlock { Text = label, Foreground = Brushes.SlateGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) }; Grid.SetRow(text, row); Grid.SetColumn(text, column); grid.Children.Add(text);
            var box = new TextBox { Text = value ?? string.Empty, Margin = new Thickness(4), FontFamily = new FontFamily("Consolas") }; Grid.SetRow(box, row); Grid.SetColumn(box, column + 1); grid.Children.Add(box); return box;
        }
        private ScriptNodeConfig ReadConfig()
        {
            SynchronizeUsingDirectives();
            return new ScriptNodeConfig { Code = _code.Text, ScriptFile = _scriptFile.Text.Trim(), References = CSharpScriptEngine.ParseList(_references.Text), Imports = CSharpScriptEngine.ParseList(_imports.Text), DeclaredOutputs = CollectOutputs() };
        }
        private IList<string> CollectOutputs()
        {
            var names = CSharpScriptEngine.ParseList(_outputs.Text);
            foreach (Match match in Regex.Matches(_code.Text, "SetOutput\\s*\\(\\s*\\\"([^\\\"]+)\\\"")) if (!names.Contains(match.Groups[1].Value, StringComparer.OrdinalIgnoreCase)) names.Add(match.Groups[1].Value);
            _outputs.Text = string.Join(";", names); return names;
        }
        private void Save(bool chooseFile)
        {
            if (chooseFile || (string.IsNullOrWhiteSpace(_scriptFile.Text) && Keyboard.Modifiers == ModifierKeys.Control))
            {
                var classMode = CSharpScriptEngine.IsClassCode(ReadConfig());
                var dialog = new SaveFileDialog { Filter = "C# Source (*.cs)|*.cs|C# Script (*.csx)|*.csx", FileName = _node.NodeName + (classMode ? ".cs" : ".csx") };
                if (dialog.ShowDialog(this) != true) return; _scriptFile.Text = dialog.FileName;
            }
            var config = ReadConfig();
            if (!string.IsNullOrWhiteSpace(config.ScriptFile)) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(config.ScriptFile))); File.WriteAllText(config.ScriptFile, config.Code, new UTF8Encoding(true)); }
            _vm.SaveScriptConfig(_node, config); _status.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss");
        }
        private void Reload() { var path = _scriptFile.Text.Trim(); if (File.Exists(path)) _code.Text = File.ReadAllText(path, Encoding.UTF8); }
        private void AddDll()
        {
            var dialog = new OpenFileDialog { Filter = ".NET 程序集 (*.dll)|*.dll", Multiselect = true }; if (dialog.ShowDialog(this) != true) return;
            var values = CSharpScriptEngine.ParseList(_references.Text); foreach (var path in dialog.FileNames) if (!values.Contains(path, StringComparer.OrdinalIgnoreCase)) values.Add(path); _references.Text = string.Join(";", values);
            OfferAssemblyNamespaces(dialog.FileNames); _diagnosticTimer.Stop(); _diagnosticTimer.Start();
        }
        private void AddUsing()
        {
            var dialog = new Window { Title = "添加 using", Owner = this, Width = 440, Height = 150, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(12) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "添加", Width = 78, IsDefault = true }; var cancel = new Button { Content = "取消", Width = 78, IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            var box = new TextBox { FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 10), ToolTip = "例如：HalconDotNet" }; panel.Children.Add(box); ok.Click += delegate { InsertUsing(box.Text); dialog.DialogResult = true; }; dialog.Content = panel; dialog.ShowDialog();
        }
        private void OfferAssemblyNamespaces(IEnumerable<string> files)
        {
            var namespaces = new List<string>();
            foreach (var file in files)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file); Type[] types;
                    try { types = assembly.GetExportedTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null && x.IsPublic).ToArray(); }
                    namespaces.AddRange(types.Select(x => x.Namespace).Where(x => !string.IsNullOrWhiteSpace(x)));
                }
                catch (Exception ex) { _status.Text = "DLL 已添加，但读取命名空间失败：" + ex.GetBaseException().Message; }
            }
            var items = namespaces.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray(); if (items.Length == 0) return;
            if (items.Length == 1) { InsertUsing(items[0]); return; }
            var dialog = new Window { Title = "选择要添加到源码顶部的 using", Owner = this, Width = 520, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(10) }; var list = new ListBox { ItemsSource = items, SelectionMode = SelectionMode.Multiple, FontFamily = new FontFamily("Consolas") };
            if (items.Length <= 5) foreach (var item in items) list.SelectedItems.Add(item);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; var ok = new Button { Content = "添加 using", Width = 95, IsDefault = true }; var cancel = new Button { Content = "跳过", Width = 75, IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons); panel.Children.Add(list);
            ok.Click += delegate { foreach (var item in list.SelectedItems.Cast<string>().ToArray()) InsertUsing(item); dialog.DialogResult = true; }; dialog.Content = panel; dialog.ShowDialog();
        }
        private void InsertUsing(string namespaceName)
        {
            var name = (namespaceName ?? string.Empty).Trim().TrimEnd(';'); if (name.StartsWith("using ", StringComparison.Ordinal)) name = name.Substring(6).Trim(); if (name.Length == 0) return;
            _code.Text = CSharpScriptEngine.ApplyImportsToClassCode(_code.Text, new[] { name }); UpdateImportsFromCode(); RefreshNavigation();
        }
        private void SynchronizeUsingDirectives()
        {
            if (_syncingImports || _code == null || _imports == null) return; _syncingImports = true;
            try
            {
                var updated = CSharpScriptEngine.ApplyImportsToClassCode(_code.Text, CSharpScriptEngine.ParseList(_imports.Text));
                if (!string.Equals(updated, _code.Text, StringComparison.Ordinal)) { var offset = updated.Length - _code.Text.Length; var caret = _code.CaretIndex; _code.Text = updated; _code.CaretIndex = Math.Max(0, caret + offset); }
                UpdateImportsFromCode();
            }
            finally { _syncingImports = false; }
        }
        private void UpdateImportsFromCode()
        {
            if (_syncingImports && _imports == null) return;
            var values = Regex.Matches(_code.Text, @"(?m)^\s*using\s+([\w\.]+)\s*;").Cast<Match>().Select(x => x.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
            var text = string.Join(";", values); if (!string.Equals(_imports.Text, text, StringComparison.Ordinal)) _imports.Text = text;
        }
        private async Task CompileAsync(bool showStatus = true)
        {
            try
            {
                var config = ReadConfig(); var result = await Task.Run(() => _vm.CompileScript(_node, config)); _diagnostics.ItemsSource = result.Diagnostics; _code.SetDiagnostics(result.Diagnostics);
                if (showStatus || !result.Success) _status.Text = result.Success ? "编译通过" : "编译失败：" + result.Diagnostics.Count(x => x.Severity == "Error") + " 个错误";
            }
            catch (Exception ex)
            {
                var diagnostic = new ScriptDiagnostic { Severity = "Error", Id = "RUNTIME", Message = ex.GetBaseException().Message, Line = 0, Column = 0 };
                _diagnostics.ItemsSource = new[] { diagnostic }; _code.SetDiagnostics(new[] { diagnostic }); _status.Text = "脚本编译器加载失败：" + diagnostic.Message;
            }
        }
        private async Task RunAsync()
        {
            Save(false); await CompileAsync(); if (_diagnostics.Items.Cast<ScriptDiagnostic>().Any(x => x.Severity == "Error")) return;
            await _vm.RunToNodeForDebugAsync(_node); _status.Text = _node.Status + " · " + _node.Message;
        }
        private void CodeChanged()
        {
            if (!_syncingImports) UpdateImportsFromCode(); _diagnosticTimer.Stop(); _diagnosticTimer.Start(); RefreshNavigation(); if (_code.CompletionOpen) FilterCompletionItems();
        }
        private async Task ShowCompletionAsync()
        {
            var requestVersion = ++_completionRequestVersion; var config = ReadConfig(); var position = _code.CaretIndex;
            var items = await Task.Run(() => _vm.GetScriptCompletions(config, position)); if (requestVersion != _completionRequestVersion) return; _completionItems = items;
            if (_completionItems.Count == 0) { CloseCompletion(); return; }
            FilterCompletionItems(); if (_completionList.Items.Count == 0) return;
            var rect = _code.GetCaretRect(); _completionPopup.HorizontalOffset = Math.Max(0, rect.Left); _completionPopup.VerticalOffset = Math.Max(0, rect.Bottom + 3); _completionPopup.IsOpen = true; _code.CompletionOpen = true;
        }
        private async Task ShowSignatureHelpAsync()
        {
            var requestVersion = ++_signatureRequestVersion; var config = ReadConfig(); var position = _code.CaretIndex;
            var help = await Task.Run(() => _vm.GetScriptSignatureHelp(config, position));
            if (requestVersion != _signatureRequestVersion || help == null || help.Signatures.Count == 0) { CloseSignatureHelp(); return; }
            var parameter = help.ActiveParameter + 1;
            _signatureText.Text = "参数 " + parameter + "\n" + string.Join("\n", help.Signatures.Take(12));
            var rect = _code.GetCaretRect(); _signaturePopup.HorizontalOffset = Math.Max(0, rect.Left); _signaturePopup.VerticalOffset = Math.Max(0, rect.Bottom + 5); _signaturePopup.IsOpen = true;
        }
        private void FilterCompletionItems()
        {
            var prefix = GetIdentifierPrefix();
            var filtered = _completionItems.Where(x => prefix.Length == 0 || x.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length == 0 && prefix.Length > 0) filtered = _completionItems.Where(x => x.DisplayText.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            _completionList.ItemsSource = filtered; _completionList.SelectedIndex = filtered.Length == 0 ? -1 : 0;
            if (filtered.Length == 0) CloseCompletion();
        }
        private string GetIdentifierPrefix()
        {
            var position = Math.Max(0, Math.Min(_code.CaretIndex, _code.Text.Length)); var start = position;
            while (start > 0 && (char.IsLetterOrDigit(_code.Text[start - 1]) || _code.Text[start - 1] == '_')) start--;
            return _code.Text.Substring(start, position - start);
        }
        private void CommitCompletion()
        {
            var item = _completionList.SelectedItem as ScriptCompletionItem; if (item == null) return;
            var position = _code.CaretIndex; var start = item.ReplacementStart;
            if (start < 0 || start > position) { var prefix = GetIdentifierPrefix(); start = position - prefix.Length; }
            var length = Math.Max(0, position - start);
            _code.SelectText(start, length); _code.ReplaceSelection(item.InsertText); _code.CaretIndex = start + item.InsertText.Length; CloseCompletion(); _code.FocusEditor();
        }
        private void CloseCompletion() { _completionPopup.IsOpen = false; _code.CompletionOpen = false; }
        private void CloseSignatureHelp() { _signatureRequestVersion++; _signaturePopup.IsOpen = false; }
        private void InsertAtCaret(string text) { _code.InsertAtCaret(text); _code.FocusEditor(); }
        private void GoTo(int line, int column)
        {
            if (line > 0) _code.GoTo(line, column);
        }
        private void RefreshNavigation()
        {
            _refreshingNavigation = true;
            try
            {
                var code = _code.Text; var selectedClass = _classNavigation.SelectedValue; var selectedMember = _memberNavigation.SelectedValue;
                var classes = Regex.Matches(code, @"\b(class|struct|interface)\s+(\w+)").Cast<Match>().Select(x => new NavigationItem(x.Groups[2].Value, x.Index)).ToList();
                var members = Regex.Matches(code, @"(?m)^\s*(?:public|private|protected|internal)\s+(?:static\s+|override\s+|virtual\s+|async\s+|sealed\s+)*(?:[\w<>,\.\[\]\?]+)\s+(\w+)\s*\(").Cast<Match>().Select(x => new NavigationItem(x.Groups[1].Value + "()", x.Index)).ToList();
                _classNavigation.ItemsSource = classes; _classNavigation.DisplayMemberPath = "Name"; _classNavigation.SelectedValuePath = "Index";
                _memberNavigation.ItemsSource = members; _memberNavigation.DisplayMemberPath = "Name"; _memberNavigation.SelectedValuePath = "Index";
                if (selectedClass != null) _classNavigation.SelectedValue = selectedClass; if (selectedMember != null) _memberNavigation.SelectedValue = selectedMember;
            }
            finally { _refreshingNavigation = false; }
        }
        private void NavigateToSelection(ComboBox combo)
        {
            if (_refreshingNavigation) return; var item = combo.SelectedItem as NavigationItem; if (item == null) return; _code.CaretIndex = item.Index; _code.FocusEditor();
        }
        private void ConvertToClassMode()
        {
            var config = ReadConfig(); if (CSharpScriptEngine.IsClassCode(config)) { _status.Text = "当前已经是完整 C# 类模式"; return; }
            _code.Text = CSharpScriptEngine.WrapStatementsInClass(_code.Text); RefreshNavigation(); _status.Text = "已转换为完整 C# 类模式";
        }
        private sealed class NavigationItem { public NavigationItem(string name, int index) { Name = name; Index = index; } public string Name { get; private set; } public int Index { get; private set; } }
        private void OpenInVisualStudio()
        {
            if (string.IsNullOrWhiteSpace(_scriptFile.Text)) Save(true); if (!File.Exists(_scriptFile.Text)) return;
            var devenv = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\devenv.exe";
            Process.Start(new ProcessStartInfo { FileName = File.Exists(devenv) ? devenv : _scriptFile.Text, Arguments = File.Exists(devenv) ? "/Edit \"" + _scriptFile.Text + "\"" : string.Empty, UseShellExecute = true });
        }
    }
}
