using Microsoft.Win32;
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using VisionFlowStudio.Licensing;

namespace VisionFlowStudio.App
{
    public partial class MainWindow : Window
    {
        private const uint HhDisplayTopic = 0x0000;
        private const uint HhDisplayToc = 0x0001;

        [DllImport("hhctrl.ocx", CharSet = CharSet.Unicode, EntryPoint = "HtmlHelpW")]
        private static extern IntPtr HtmlHelp(IntPtr caller, string file, uint command, IntPtr data);

        private readonly MainViewModel _viewModel;
        private readonly DispatcherTimer _autoSaveTimer;
        private string _projectPassword = string.Empty;
        private bool _autoSaveInProgress;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            _autoSaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            Loaded += delegate { ApplyAutoSaveSettings(ApplicationSettingsStore.Load()); };
            Closed += delegate { _autoSaveTimer.Stop(); };
        }

        internal void SetProjectPassword(string password) { _projectPassword = password ?? string.Empty; }

        private void BrowseSolution_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "VisionMaster Solution (*.sol)|*.sol|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
                _viewModel.SolutionPath = dialog.FileName;
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "图像文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
                _viewModel.ImagePath = dialog.FileName;
        }

        private void SolutionPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.SolutionPassword = SolutionPasswordBox.Password;
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(this, "确定要新建项目吗？当前未保存的修改将不会保留。", "新建项目", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            _viewModel.NewProject();
            _projectPassword = string.Empty;
        }

        private void NewFlow_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NewStationRecipeFlow();
        }

        private void OpenFlow_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "视觉流程 (*.flow.json)|*.flow.json|JSON (*.json)|*.json",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true)
                return;
            try { _viewModel.LoadFlow(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开流程失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void SaveFlow_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel.CurrentFlowPath))
                SaveAsFlow_Click(sender, e);
            else
                SaveTo(_viewModel.CurrentFlowPath);
        }

        private void SaveAsFlow_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "视觉流程 (*.flow.json)|*.flow.json",
                FileName = _viewModel.FlowName + ".flow.json"
            };
            if (dialog.ShowDialog(this) == true)
                SaveTo(dialog.FileName);
        }

        private void SaveTo(string path)
        {
            try { _viewModel.SaveFlow(path); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存流程失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void AddVisionMasterNode_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddVisionMasterNode();
        }

        private void OpenNodePicker_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NodePickerWindow { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedDefinition != null)
                _viewModel.AddNode(dialog.SelectedDefinition.NodeType);
        }

        private void AddVisionProNode_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddVisionProNode();
        }

        private void AddHalconNode_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddHalconNode();
        }

        private void AddScriptNode_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddNode("CSharpScriptNode");
        }

        private void OpenScriptEditor_Click(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.SelectedNode;
            if (node == null || node.NodeType != "CSharpScriptNode")
            {
                MessageBox.Show(this, "请先选择一个 C# 高级脚本节点。", "脚本编辑器", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new ScriptEditorWindow(_viewModel, node) { Owner = this }.ShowDialog();
        }

        private void AddCommunicationNode_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddNode("CommunicationWriteNode");
        }

        private void AddCommunicationWrite_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddCommunicationWrite();
        }

        private void RemoveCommunicationWrite_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.RemoveSelectedCommunicationWrite();
        }

        private void AddCommunicationTriggerField_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddCommunicationTriggerField();
        }

        private void RemoveCommunicationTriggerField_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.RemoveSelectedCommunicationTriggerField();
        }

        private void BrowseVpp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "VisionPro ToolBlock (*.vpp)|*.vpp|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true)
                _viewModel.VisionProToolBlockPath = dialog.FileName;
        }

        private void BrowseHalconProcedure_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "HALCON Procedure (*.hdvp)|*.hdvp|HDevelop Program (*.hdev)|*.hdev|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true)
                _viewModel.HalconProcedurePath = dialog.FileName;
        }

        private void ProjectSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Window
            {
                Title = "项目属性", Owner = this, Width = 430, Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            var grid = new Grid { Margin = new Thickness(18) };
            for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var project = AddField(grid, 0, "项目名称", _viewModel.ProjectName);
            var recipe = AddField(grid, 1, "产品型号", _viewModel.RecipeName);
            var station = AddField(grid, 2, "工位名称", _viewModel.StationName);
            var flow = AddField(grid, 3, "流程名称", _viewModel.FlowName);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "确定", Width = 76, IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 76, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); Grid.SetRow(buttons, 5); Grid.SetColumnSpan(buttons, 2); grid.Children.Add(buttons);
            ok.Click += delegate { _viewModel.ProjectName = project.Text; _viewModel.RecipeName = recipe.Text; _viewModel.StationName = station.Text; _viewModel.FlowName = flow.Text; _viewModel.CommitProjectStructure(); dialog.DialogResult = true; };
            dialog.Content = grid; dialog.ShowDialog();
        }

        private static TextBox AddField(Grid grid, int row, string label, string value)
        {
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 8, 5) };
            var box = new TextBox { Text = value, Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(text, row); Grid.SetColumn(text, 0); Grid.SetRow(box, row); Grid.SetColumn(box, 1);
            grid.Children.Add(text); grid.Children.Add(box); return box;
        }

        private void VisionMasterStatus_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, _viewModel.PlatformMessage + "\n相机 SDK：Basler pylon 6、海康 MVS、大华 MVSDK", "平台状态", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ProjectManagement_Click(object sender, RoutedEventArgs e) { new ProjectManagementWindow(_viewModel) { Owner = this }.ShowDialog(); }
        private void CameraManager_Click(object sender, RoutedEventArgs e) { new CameraManagerWindow(_viewModel) { Owner = this }.ShowDialog(); }
        private void CommunicationManager_Click(object sender, RoutedEventArgs e) { new CommunicationManagerWindow(_viewModel) { Owner = this }.ShowDialog(); }
        private void SystemSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationSettingsStore.Load();
            var dialog = new SystemSettingsWindow(settings) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            ApplyAutoSaveSettings(settings);
            if (settings.StartMaximized) WindowState = WindowState.Maximized;
        }

        private void ApplyAutoSaveSettings(ApplicationSettings settings)
        {
            _autoSaveTimer.Stop();
            if (settings == null || !settings.AutoSaveProject) return;
            var minutes = Math.Max(1, Math.Min(1440, settings.AutoSaveIntervalMinutes));
            _autoSaveTimer.Interval = TimeSpan.FromMinutes(minutes);
            _autoSaveTimer.Start();
        }

        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            if (_autoSaveInProgress || _viewModel.IsBusy) return;
            if (string.IsNullOrWhiteSpace(_viewModel.CurrentProjectPath) || string.IsNullOrEmpty(_projectPassword)) return;
            try
            {
                _autoSaveInProgress = true;
                _viewModel.SaveProject(_viewModel.CurrentProjectPath, _projectPassword, true);
            }
            catch (Exception ex)
            {
                _viewModel.ReportApplicationLog("ERROR", "自动保存项目失败：" + ex.Message);
            }
            finally { _autoSaveInProgress = false; }
        }

        private void OpenSelectedImageWindow_Click(object sender, RoutedEventArgs e)
        {
            var document = _viewModel.SelectedImageDocument;
            if (document == null)
            {
                MessageBox.Show(this, "当前没有可弹出的图像画面。", "图像窗口", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            new ImageDocumentWindow(document) { Owner = this }.Show();
        }

        private void ImageDocumentItem_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = sender as FrameworkElement;
            var document = item == null ? null : item.DataContext as ImageViewDocumentViewModel;
            if (document == null) return;
            _viewModel.SelectedImageDocument = document;
            _viewModel.ActivateImageDocument(document);
            WorkspaceTabs.SelectedItem = FlowEditorTab;
            e.Handled = true;
        }

        private void ProjectTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var node = ProjectTree.SelectedItem as ProjectTreeNodeViewModel;
            if (node == null) return;
            switch (node.Kind)
            {
                case "Project": ProjectSettings_Click(sender, e); break;
                case "Recipe": _viewModel.ActivateRecipe(node.Model as VisionFlowStudio.Core.RecipeDefinition); WorkspaceTabs.SelectedItem = FlowEditorTab; break;
                case "Station":
                    _viewModel.ActivateStation(node.Model as VisionFlowStudio.Core.StationDefinition); WorkspaceTabs.SelectedItem = FlowEditorTab; break;
                case "StationRecipe":
                case "StationFlow": _viewModel.ActivateStationRecipe(node.Model as VisionFlowStudio.Core.StationRecipeFlowDefinition); WorkspaceTabs.SelectedItem = FlowEditorTab; break;
                case "Camera": CameraManager_Click(sender, e); break;
                case "Communications":
                case "Communication": CommunicationManager_Click(sender, e); break;
            }
            e.Handled = true;
        }

        private void ProjectTreeItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = sender as TreeViewItem ?? FindVisualParent<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item == null) return;
            item.IsSelected = true;
            item.Focus();
            var node = item.DataContext as ProjectTreeNodeViewModel;
            if (node == null || !string.Equals(node.Kind, "StationFlow", StringComparison.OrdinalIgnoreCase))
            {
                item.ContextMenu = null;
                return;
            }

            var menu = new ContextMenu { PlacementTarget = item };
            menu.Items.Add(CreateFlowMenuItem("保存", ProjectTreeFlowSave_Click));
            menu.Items.Add(CreateFlowMenuItem("重命名", ProjectTreeFlowRename_Click));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateFlowMenuItem("删除", ProjectTreeFlowDelete_Click));
            item.ContextMenu = menu;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private static MenuItem CreateFlowMenuItem(string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            return item;
        }

        private void ProjectTreeFlowSave_Click(object sender, RoutedEventArgs e)
        {
            var flow = GetSelectedStationFlow();
            if (flow == null) return;
            _viewModel.ActivateStationRecipe(flow);
            SaveProject_Click(sender, e);
        }

        private void ProjectTreeFlowRename_Click(object sender, RoutedEventArgs e)
        {
            var flow = GetSelectedStationFlow();
            if (flow == null) return;
            var name = PromptFlowName(flow.FlowName);
            if (string.IsNullOrWhiteSpace(name)) return;
            try { _viewModel.RenameStationFlow(flow, name); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "重命名流程失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ProjectTreeFlowDelete_Click(object sender, RoutedEventArgs e)
        {
            var flow = GetSelectedStationFlow();
            if (flow == null) return;
            var message = string.Format("确定要删除流程 {0} / {1} / {2} 吗？", flow.StationName, flow.RecipeName, flow.FlowName);
            if (MessageBox.Show(this, message, "删除流程", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _viewModel.DeleteStationFlow(flow); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除流程失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private VisionFlowStudio.Core.StationRecipeFlowDefinition GetSelectedStationFlow()
        {
            var node = ProjectTree.SelectedItem as ProjectTreeNodeViewModel;
            return node != null && string.Equals(node.Kind, "StationFlow", StringComparison.OrdinalIgnoreCase) ? node.Model as VisionFlowStudio.Core.StationRecipeFlowDefinition : null;
        }

        private string PromptFlowName(string currentName)
        {
            var dialog = new Window { Title = "重命名流程", Owner = this, Width = 420, Height = 165, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
            var root = new DockPanel { Margin = new Thickness(16) };
            var label = new TextBlock { Text = "流程名称", Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(label, Dock.Top); root.Children.Add(label);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "确定", Width = 78, IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 78, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            var box = new TextBox { Text = string.IsNullOrWhiteSpace(currentName) ? "MainFlow" : currentName, MinWidth = 340 };
            root.Children.Add(box);
            ok.Click += delegate { if (string.IsNullOrWhiteSpace(box.Text)) { MessageBox.Show(dialog, "流程名称不能为空。", "重命名流程", MessageBoxButton.OK, MessageBoxImage.Information); return; } dialog.DialogResult = true; };
            dialog.Loaded += delegate { box.Focus(); box.SelectAll(); };
            dialog.Content = root;
            return dialog.ShowDialog() == true ? box.Text.Trim() : null;
        }

        private static T FindVisualParent<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                var typed = source as T;
                if (typed != null) return typed;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }
            return null;
        }
        private void OpenVisionProDebug_Click(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.SelectedNode;
            if (node == null || node.NodeType != "VisionProToolBlockNode") { MessageBox.Show(this, "请先选择 VisionPro ToolBlock 节点。", "VisionPro 调试", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                new VisionProDebugWindow(
                    node.Get("ToolBlockPath", _viewModel.VisionProToolBlockPath),
                    _viewModel.ResolveDebugImagePath(node),
                    node.Get("ImageInputName", _viewModel.VisionProImageInputName)) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开 VisionPro 调试器失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenVisionMasterDebug_Click(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.SelectedNode;
            if (node == null || node.NodeType != "VisionMasterProcedureNode") { MessageBox.Show(this, "请先选择 VisionMaster 流程节点。", "VisionMaster 调试", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try
            {
                _viewModel.LoadVisionMasterForDebug();
                new VisionMasterDebugWindow(
                    _viewModel,
                    node,
                    node.Get("SolutionPath", _viewModel.SolutionPath),
                    node.Get("ProcedureName", _viewModel.SelectedProcedure),
                    _viewModel.ResolveDebugImagePath(node),
                    node.Get("ImageInputName", _viewModel.ImageInputName)) { Owner = this }.ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开 VisionMaster 调试器失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenHalconDebug_Click(object sender, RoutedEventArgs e)
        {
            var node = _viewModel.SelectedNode;
            if (node == null || node.NodeType != "HalconProcedureNode") { MessageBox.Show(this, "请先选择 HALCON Procedure 节点。", "HALCON 调试", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            try { new HalconScriptDebugWindow(_viewModel, node, node.Get("ProcedurePath", _viewModel.HalconProcedurePath)) { Owner = this }.ShowDialog(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开 HALCON 调试器失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "加密视觉方案 (*.vfsproj)|*.vfsproj|所有文件 (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            var password = PromptProjectPassword(false);
            if (password == null) return;
            try { _viewModel.LoadProject(dialog.FileName, password); _projectPassword = password; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开加密方案失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_viewModel.CurrentProjectPath))
            {
                SaveProjectAs_Click(sender, e);
                return;
            }
            if (string.IsNullOrEmpty(_projectPassword))
            {
                _projectPassword = PromptProjectPassword(true);
                if (_projectPassword == null) { _projectPassword = string.Empty; return; }
            }
            SaveProjectTo(_viewModel.CurrentProjectPath);
        }

        private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "加密视觉方案 (*.vfsproj)|*.vfsproj", FileName = _viewModel.ProjectName + ".vfsproj", AddExtension = true, DefaultExt = ".vfsproj" };
            if (dialog.ShowDialog(this) != true) return;
            var password = PromptProjectPassword(true);
            if (password == null) return;
            _projectPassword = password;
            SaveProjectTo(dialog.FileName);
        }

        private void SaveProjectTo(string path)
        {
            try { _viewModel.SaveProject(path, _projectPassword); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存加密方案失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private string PromptProjectPassword(bool confirm)
        {
            var dialog = new Window
            {
                Title = confirm ? "设置方案密码" : "输入方案密码", Owner = this,
                Width = 440, Height = confirm ? 235 : 185, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 248, 254))
            };
            var root = new Grid { Margin = new Thickness(20) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (confirm) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var password = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 5, 0, 5) };
            var passwordLabel = new TextBlock { Text = "方案密码", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(password, 1); root.Children.Add(passwordLabel); root.Children.Add(password);
            PasswordBox repeated = null;
            if (confirm)
            {
                repeated = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 5, 0, 5) };
                var repeatedLabel = new TextBlock { Text = "确认密码", VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(repeatedLabel, 1); Grid.SetRow(repeated, 1); Grid.SetColumn(repeated, 1); root.Children.Add(repeatedLabel); root.Children.Add(repeated);
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var ok = new Button { Content = "确定", Width = 82, IsDefault = true, Style = TryFindResource("PrimaryButton") as Style };
            var cancel = new Button { Content = "取消", Width = 82, IsCancel = true };
            buttons.Children.Add(cancel); buttons.Children.Add(ok); Grid.SetRow(buttons, confirm ? 2 : 1); Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons);
            ok.Click += delegate
            {
                if (string.IsNullOrEmpty(password.Password)) { MessageBox.Show(dialog, "方案密码不能为空。", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (confirm && password.Password.Length < 6) { MessageBox.Show(dialog, "方案密码至少需要 6 个字符。", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (confirm && !string.Equals(password.Password, repeated.Password, StringComparison.Ordinal)) { MessageBox.Show(dialog, "两次输入的密码不一致。", dialog.Title, MessageBoxButton.OK, MessageBoxImage.Information); return; }
                dialog.DialogResult = true;
            };
            dialog.Loaded += delegate { password.Focus(); };
            dialog.Content = root;
            return dialog.ShowDialog() == true ? password.Password : null;
        }

        private void OpenRunData_Click(object sender, RoutedEventArgs e)
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RunData"); Directory.CreateDirectory(folder); Process.Start("explorer.exe", folder);
        }

        private void QuickStart_Click(object sender, RoutedEventArgs e)
        {
            OpenHelpTopic("quick-start");
        }

        private void HelpContents_Click(object sender, RoutedEventArgs e) { OpenHelpTopic(null); }

        private void OpenHelpTopic(string topic)
        {
            var helpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "VisionFlowStudio.chm");
            var fallbackPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Help", "manual.html");
            try
            {
                if (File.Exists(helpPath))
                {
                    var caller = new WindowInteropHelper(this).Handle;
                    var target = string.IsNullOrWhiteSpace(topic) ? helpPath : string.Format("{0}::/manual.html#{1}", helpPath, topic);
                    var result = HtmlHelp(caller, target, string.IsNullOrWhiteSpace(topic) ? HhDisplayToc : HhDisplayTopic, IntPtr.Zero);
                    if (result == IntPtr.Zero)
                        Process.Start(new ProcessStartInfo("hh.exe", string.Format("\"{0}\"", target)) { UseShellExecute = true });
                    return;
                }
                if (File.Exists(fallbackPath)) { Process.Start(fallbackPath + (string.IsNullOrWhiteSpace(topic) ? string.Empty : "#" + topic)); return; }
                MessageBox.Show(this, LocalizationService.T("未找到帮助文档，请重新编译或修复安装。"), LocalizationService.T("用户手册"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, LocalizationService.T("打开帮助文档失败"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(this, "VisionFlow Studio\nWPF + .NET Framework 4.8 + x64\nVisionMaster 4.4 / VisionPro 7.3 / HALCON 18.11\nBasler / Hikrobot / Dahua Camera", LocalizationService.T("关于"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LicenseInformation_Click(object sender, RoutedEventArgs e)
        {
            var result = LicenseStore.ValidateInstalled();
            if (!result.IsValid || result.License == null)
            {
                MessageBox.Show(this, result.Message, LocalizationService.IsEnglish ? "License" : "软件授权", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var license = result.License;
            var expiration = license.ExpiresUtc.HasValue
                ? license.ExpiresUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                : (LocalizationService.IsEnglish ? "Perpetual" : "永久授权");
            var message = string.Format(
                LocalizationService.IsEnglish
                    ? "Status: Activated\nCustomer: {0}\nEdition: {1}\nExpires: {2}\nLicense ID: {3}\nMachine code: {4}"
                    : "状态：已激活\n客户：{0}\n版本：{1}\n有效期：{2}\n许可证 ID：{3}\n机器码：{4}",
                license.Customer, license.Edition, expiration, license.LicenseId, MachineFingerprint.GetMachineCode());
            MessageBox.Show(this, message, LocalizationService.IsEnglish ? "License Information" : "软件授权", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
