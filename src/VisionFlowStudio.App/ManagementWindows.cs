using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionFlowStudio.Core;
using VisionFlowStudio.Communications;

namespace VisionFlowStudio.App
{
    public sealed class ProjectManagementWindow : Window
    {
        public ProjectManagementWindow(MainViewModel vm)
        {
            Title = "型号 / 工位 / 相机 / 通信配置管理"; Width = 980; Height = 650; MinWidth = 760; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var tabs = new TabControl { Margin = new Thickness(10) };
            tabs.Items.Add(CreateRecipeTab(vm));
            tabs.Items.Add(CreateStationTab(vm));
            tabs.Items.Add(CreateCameraTab(vm));
            tabs.Items.Add(CreateTab("通信配置", vm.Communications, vm.AddCommunication, vm.RemoveCommunication));
            var root = new DockPanel(); var close = new Button { Content = "完成", Width = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10), IsDefault = true };
            close.Click += delegate { vm.CommitProjectStructure(); Close(); }; Closed += delegate { vm.CommitProjectStructure(); };
            DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close); root.Children.Add(tabs); Content = root;
        }

        private static TabItem CreateRecipeTab(MainViewModel vm)
        {
            var grid = CreateBaseGrid(vm.Recipes);
            grid.Columns.Add(new DataGridTextColumn { Header = "型号名称", Binding = new System.Windows.Data.Binding("Name"), Width = 180 });
            grid.Columns.Add(new DataGridTextColumn { Header = "产品编码", Binding = new System.Windows.Data.Binding("ProductCode"), Width = 160 });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 70 });
            return CreatePanelTab("型号管理", grid, vm.AddRecipe, vm.RemoveRecipe);
        }

        private static TabItem CreateStationTab(MainViewModel vm)
        {
            var grid = CreateBaseGrid(vm.Stations);
            grid.Columns.Add(new DataGridTextColumn { Header = "工站名称", Binding = new System.Windows.Data.Binding("Name"), Width = 180 });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 70 });
            return CreatePanelTab("工位管理", grid, vm.AddStationForActiveRecipe, vm.RemoveStation);
        }

        private static TabItem CreateCameraTab(MainViewModel vm)
        {
            var grid = CreateBaseGrid(vm.Cameras);
            grid.Columns.Add(new DataGridTextColumn { Header = "相机名称", Binding = new System.Windows.Data.Binding("Name"), Width = 140 });
            grid.Columns.Add(new DataGridTextColumn { Header = "所属工站", Binding = new System.Windows.Data.Binding("StationName"), Width = 140 });
            grid.Columns.Add(new DataGridComboBoxColumn { Header = "厂商", ItemsSource = new[] { "Hikrobot", "Basler", "Dahua" }, SelectedItemBinding = new System.Windows.Data.Binding("Vendor"), Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "设备ID/序列号", Binding = new System.Windows.Data.Binding("DeviceId"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 70 });
            return CreatePanelTab("相机配置", grid, vm.AddCameraForActiveStation, vm.RemoveCamera);
        }

        private static DataGrid CreateBaseGrid<T>(ObservableCollection<T> source)
        {
            return new DataGrid { ItemsSource = source, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new Thickness(4), SelectionMode = DataGridSelectionMode.Single };
        }

        private static TabItem CreatePanelTab<T>(string title, DataGrid grid, Func<T> addItem, Action<T> removeItem)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var add = new Button { Content = "＋ 新增" }; var delete = new Button { Content = "删除" };
            add.Click += delegate { var item = addItem(); grid.SelectedItem = item; grid.ScrollIntoView(item); };
            delete.Click += delegate { var item = grid.SelectedItem; if (item != null) removeItem((T)item); };
            buttons.Children.Add(add); buttons.Children.Add(delete); DockPanel.SetDock(buttons, Dock.Top);
            var panel = new DockPanel(); panel.Children.Add(buttons); panel.Children.Add(grid); return new TabItem { Header = title, Content = panel };
        }

        private static TabItem CreateTab<T>(string title, ObservableCollection<T> source, Func<T> addItem, Action<T> removeItem)
        {
            var grid = new DataGrid { ItemsSource = source, AutoGenerateColumns = true, CanUserAddRows = false, Margin = new Thickness(4), SelectionMode = DataGridSelectionMode.Single };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            var add = new Button { Content = "＋ 新增" }; var delete = new Button { Content = "删除" };
            add.Click += delegate { var item = addItem(); grid.SelectedItem = item; grid.ScrollIntoView(item); };
            delete.Click += delegate { var item = grid.SelectedItem; if (item != null) removeItem((T)item); };
            buttons.Children.Add(add); buttons.Children.Add(delete); DockPanel.SetDock(buttons, Dock.Top);
            var panel = new DockPanel(); panel.Children.Add(buttons); panel.Children.Add(grid); return new TabItem { Header = title, Content = panel };
        }
    }

    public sealed class CommunicationManagerWindow : Window
    {
        private sealed class AutoResponseRow
        {
            public bool Enabled { get; set; } = true;
            public string MatchPath { get; set; } = "Command";
            public string MatchMode { get; set; } = "Equals";
            public string ExpectedValue { get; set; } = "Heartbeat";
            public string ResponseTemplate { get; set; } = "{\"CmdId\":{{CmdId}},\"Command\":\"HeartbeatAck\"}";
            public bool ConsumeMessage { get; set; } = true;
            public CommunicationAutoResponseDefinition ToDefinition() { return new CommunicationAutoResponseDefinition { Enabled = Enabled, MatchPath = MatchPath ?? string.Empty, MatchMode = MatchMode ?? "Equals", ExpectedValue = ExpectedValue ?? string.Empty, ResponseTemplate = ResponseTemplate ?? string.Empty, ConsumeMessage = ConsumeMessage }; }
        }

        private readonly MainViewModel _vm;
        private readonly DataGrid _grid;
        private readonly DataGrid _responseGrid;
        private readonly ObservableCollection<AutoResponseRow> _responses = new ObservableCollection<AutoResponseRow>();
        private CommunicationDefinition _responseOwner;
        private readonly TextBlock _status;
        private bool _testingConnections;

        public CommunicationManagerWindow(MainViewModel vm)
        {
            _vm = vm; Title = "通信配置"; Width = 1280; Height = 720; MinWidth = 900; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Margin = new Thickness(10) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var add = new Button { Content = "＋ 新增通道" }; var delete = new Button { Content = "删除" }; var test = new Button { Content = "测试连接" }; var close = new Button { Content = "完成" };
            add.Click += delegate { var item = _vm.AddCommunication(); _grid.SelectedItem = item; _grid.ScrollIntoView(item); };
            delete.Click += delegate { _vm.RemoveCommunication(_grid.SelectedItem as CommunicationDefinition); };
            test.Click += delegate { TestSelected(); };
            close.Click += delegate { SaveAutoResponses(); _vm.CommitProjectStructure(); Close(); };
            buttons.Children.Add(add); buttons.Children.Add(delete); buttons.Children.Add(test); buttons.Children.Add(close); DockPanel.SetDock(buttons, Dock.Top); root.Children.Add(buttons);
            _status = new TextBlock { Text = "TCP/IP 支持结束符和长度头分帧；Payload 可选文本字段或 JSON。长度头模式会按字节长度循环拼包。", Margin = new Thickness(5, 10, 5, 4), Foreground = System.Windows.Media.Brushes.SlateGray, TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            _grid = new DataGrid { ItemsSource = vm.Communications, AutoGenerateColumns = false, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, Margin = new Thickness(0, 8, 0, 0) };
            _grid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new System.Windows.Data.Binding("Name"), Width = 100 });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "协议", ItemsSource = CommunicationRegistry.Protocols, SelectedItemBinding = new System.Windows.Data.Binding("Protocol"), Width = 170 });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "PLC型号", ItemsSource = new[] { "S1200", "S1500", "S300", "S400", "S200Smart", "S200" }, SelectedItemBinding = new System.Windows.Data.Binding("PlcModel"), Width = 100 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "IP/主机", Binding = new System.Windows.Data.Binding("Host"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "端口", Binding = new System.Windows.Data.Binding("Port"), Width = 65 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "站号", Binding = new System.Windows.Data.Binding("Station"), Width = 55 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Rack", Binding = new System.Windows.Data.Binding("Rack"), Width = 55 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "Slot", Binding = new System.Windows.Data.Binding("Slot"), Width = 55 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "串口", Binding = new System.Windows.Data.Binding("SerialPort"), Width = 65 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "波特率", Binding = new System.Windows.Data.Binding("BaudRate"), Width = 75 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "数据位", Binding = new System.Windows.Data.Binding("DataBits"), Width = 60 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "校验", Binding = new System.Windows.Data.Binding("Parity"), Width = 65 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "停止位", Binding = new System.Windows.Data.Binding("StopBits"), Width = 65 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "文本编码", Binding = new System.Windows.Data.Binding("TextEncoding"), Width = 85 });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "帧格式", ItemsSource = new[] { "Terminator", "LengthPrefix" }, SelectedItemBinding = new System.Windows.Data.Binding("FrameMode"), Width = 105 });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "负载格式", ItemsSource = new[] { "TextFields", "Json" }, SelectedItemBinding = new System.Windows.Data.Binding("PayloadFormat"), Width = 95 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "长度头字节", Binding = new System.Windows.Data.Binding("LengthPrefixBytes"), Width = 78 });
            _grid.Columns.Add(new DataGridComboBoxColumn { Header = "字节序", ItemsSource = new[] { "BigEndian", "LittleEndian" }, SelectedItemBinding = new System.Windows.Data.Binding("LengthByteOrder"), Width = 95 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "最大帧字节", Binding = new System.Windows.Data.Binding("MaxFrameBytes"), Width = 90 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "字段分隔符", Binding = new System.Windows.Data.Binding("FieldSeparator"), Width = 85 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "发送结束符", Binding = new System.Windows.Data.Binding("SendTerminator"), Width = 85 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "接收结束符", Binding = new System.Windows.Data.Binding("ReceiveTerminator"), Width = 85 });
            _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 50 });
            var responsePanel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var responseHeader = new StackPanel { Orientation = Orientation.Horizontal };
            responseHeader.Children.Add(new TextBlock { Text = "TCP JSON 自动应答", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
            var addResponse = new Button { Content = "＋ 添加规则" }; var deleteResponse = new Button { Content = "删除规则" };
            addResponse.Click += delegate { _responses.Add(new AutoResponseRow()); _responseGrid.SelectedItem = _responses.Last(); };
            deleteResponse.Click += delegate { var row = _responseGrid.SelectedItem as AutoResponseRow; if (row != null) _responses.Remove(row); };
            responseHeader.Children.Add(addResponse); responseHeader.Children.Add(deleteResponse); DockPanel.SetDock(responseHeader, Dock.Top); responsePanel.Children.Add(responseHeader);
            _responseGrid = new DataGrid { ItemsSource = _responses, AutoGenerateColumns = false, CanUserAddRows = false, MinHeight = 145, Margin = new Thickness(0, 6, 0, 0) };
            _responseGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = new System.Windows.Data.Binding("Enabled"), Width = 48 });
            _responseGrid.Columns.Add(new DataGridTextColumn { Header = "匹配JSON路径", Binding = new System.Windows.Data.Binding("MatchPath"), Width = 130 });
            _responseGrid.Columns.Add(new DataGridComboBoxColumn { Header = "方式", ItemsSource = new[] { "Equals", "Contains" }, SelectedItemBinding = new System.Windows.Data.Binding("MatchMode"), Width = 90 });
            _responseGrid.Columns.Add(new DataGridTextColumn { Header = "目标值", Binding = new System.Windows.Data.Binding("ExpectedValue"), Width = 110 });
            _responseGrid.Columns.Add(new DataGridTextColumn { Header = "应答JSON模板（{{路径}}引用请求）", Binding = new System.Windows.Data.Binding("ResponseTemplate"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _responseGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "应答后消费", Binding = new System.Windows.Data.Binding("ConsumeMessage"), Width = 88 });
            responsePanel.Children.Add(_responseGrid); DockPanel.SetDock(responsePanel, Dock.Bottom); root.Children.Add(responsePanel);
            root.Children.Add(_grid); Content = root; Closed += delegate { SaveAutoResponses(); _vm.CommitProjectStructure(); };
            _grid.SelectionChanged += delegate { LoadAutoResponses(); };
            Loaded += async delegate { await TestEnabledOnOpenAsync(); };
        }

        private void LoadAutoResponses()
        {
            SaveAutoResponses();
            _responses.Clear(); var selected = _grid.SelectedItem as CommunicationDefinition; _responseOwner = selected;
            if (selected == null) return;
            foreach (var item in selected.AutoResponses ?? new List<CommunicationAutoResponseDefinition>())
                _responses.Add(new AutoResponseRow { Enabled = item.Enabled, MatchPath = item.MatchPath, MatchMode = item.MatchMode, ExpectedValue = item.ExpectedValue, ResponseTemplate = item.ResponseTemplate, ConsumeMessage = item.ConsumeMessage });
        }

        private void SaveAutoResponses()
        {
            if (_responseOwner == null) return;
            _responseOwner.AutoResponses = _responses.Select(x => x.ToDefinition()).ToList();
        }

        private void TestSelected()
        {
            if (_testingConnections) return;
            SaveAutoResponses();
            var item = _grid.SelectedItem as CommunicationDefinition;
            if (item == null) { _status.Text = "请先选择通信通道。"; return; }
            var result = _vm.CommunicationRegistry.TestConnection(item); _status.Text = result.Message;
        }

        private async Task TestEnabledOnOpenAsync()
        {
            if (_testingConnections) return;
            var items = _vm.Communications.Where(x => x.Enabled)
                .OrderBy(x => string.Equals(x.Protocol, "TCP/IP Server", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToArray();
            if (items.Length == 0) { _status.Text = "没有启用的通信通道。"; return; }
            if (_grid.SelectedItem == null) _grid.SelectedItem = items[0];
            _testingConnections = true;
            _status.Text = "正在测试 " + items.Length + " 个通信通道...";
            try
            {
                var results = await Task.Run(() => items.Select(x => _vm.CommunicationRegistry.TestConnection(x)).ToArray());
                var okCount = results.Count(x => x.Success);
                _status.Text = string.Format("通信自检完成：{0}/{1} 成功。{2}", okCount, results.Length, string.Join("；", results.Select(x => x.Message)));
            }
            finally { _testingConnections = false; }
        }
    }

    public sealed class CameraManagerWindow : Window
    {
        private readonly MainViewModel _vm;
        private readonly ObservableCollection<CameraDeviceInfo> _devices = new ObservableCollection<CameraDeviceInfo>();
        private readonly DataGrid _grid;
        private readonly TextBlock _status;
        private readonly TextBox _exposure;
        private readonly TextBox _gain;
        private readonly TextBox _frameRate;
        private readonly ComboBox _triggerMode;
        private readonly ComboBox _triggerSource;
        private readonly ComboBox _pixelFormat;
        private readonly ComboBox _userSet;
        private readonly CheckBox _frameRateEnabled;
        private readonly Image _preview;
        private readonly Button _liveButton;
        private CancellationTokenSource _liveCancellation;

        public CameraManagerWindow(MainViewModel vm)
        {
            _vm = vm; Title = "工业相机管理"; Width = 1180; Height = 720; MinWidth = 900; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Margin = new Thickness(10) };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            var refresh = new Button { Content = "刷新设备" }; var connect = new Button { Content = "连接" }; var grab = new Button { Content = "采集一帧" }; _liveButton = new Button { Content = "实时显示" }; var disconnect = new Button { Content = "断开" }; var apply = new Button { Content = "应用参数" }; var loadSet = new Button { Content = "加载 UserSet" }; var saveSet = new Button { Content = "保存到相机 UserSet" };
            refresh.Click += delegate { RefreshDevices(); }; connect.Click += delegate { Connect(); }; grab.Click += delegate { Grab(); }; _liveButton.Click += delegate { ToggleLive(); }; disconnect.Click += delegate { Disconnect(); }; apply.Click += delegate { ApplySettings(false); }; loadSet.Click += delegate { LoadUserSet(); }; saveSet.Click += delegate { ApplySettings(true); };
            tools.Children.Add(refresh); tools.Children.Add(connect); tools.Children.Add(grab); tools.Children.Add(_liveButton); tools.Children.Add(disconnect); tools.Children.Add(apply); tools.Children.Add(loadSet); tools.Children.Add(saveSet); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
            _status = new TextBlock { Text = "点击“刷新设备”枚举 Basler、海康和大华相机。", Margin = new Thickness(6), Foreground = System.Windows.Media.Brushes.SlateGray }; DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            _grid = new DataGrid { ItemsSource = _devices, AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
            _grid.Columns.Add(new DataGridTextColumn { Header = "厂商", Binding = new System.Windows.Data.Binding("Vendor"), Width = 100 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "设备", Binding = new System.Windows.Data.Binding("DisplayName"), Width = 220 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "序列号", Binding = new System.Windows.Data.Binding("SerialNumber"), Width = 180 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "IP", Binding = new System.Windows.Data.Binding("IpAddress"), Width = 130 });
            _grid.Columns.Add(new DataGridTextColumn { Header = "设备ID", Binding = new System.Windows.Data.Binding("DeviceId"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(190) });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_grid, 0); left.Children.Add(_grid);
            _preview = new Image { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            var previewPanel = new Border { Background = Brushes.Black, BorderBrush = Brushes.LightSteelBlue, BorderThickness = new Thickness(1), Child = _preview };
            Grid.SetRow(previewPanel, 2); left.Children.Add(previewPanel);
            Grid.SetColumn(left, 0); content.Children.Add(left);
            var editor = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            editor.Children.Add(new TextBlock { Text = "相机参数", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
            _exposure = AddEditor(editor, "曝光时间 (μs)", "10000"); _gain = AddEditor(editor, "增益", "0");
            _triggerMode = AddCombo(editor, "触发模式", new[] { "Off", "On" }, "Off"); _triggerSource = AddCombo(editor, "触发源", new[] { "Software", "Line0", "Line1", "Line2" }, "Software");
            _pixelFormat = AddCombo(editor, "像素格式", new[] { "Mono8", "BayerRG8", "BayerBG8", "RGB8Packed", "BGR8" }, "Mono8");
            _frameRateEnabled = new CheckBox { Content = "启用采集帧率", Margin = new Thickness(0, 6, 0, 4) }; editor.Children.Add(_frameRateEnabled); _frameRate = AddEditor(editor, "采集帧率 (fps)", "10");
            _userSet = AddCombo(editor, "相机用户配置", new[] { "UserSet1", "UserSet2", "UserSet3" }, "UserSet1");
            Grid.SetColumn(editor, 1); content.Children.Add(editor); root.Children.Add(content); Content = root;
            Loaded += delegate { Dispatcher.BeginInvoke(new Action(RefreshDevices), DispatcherPriority.Background); };
            Closed += delegate { StopLive(); };
        }

        private CameraDeviceInfo Selected { get { return _grid.SelectedItem as CameraDeviceInfo; } }
        private void RefreshDevices() { _devices.Clear(); try { foreach (var item in _vm.CameraRegistry.EnumerateAll()) _devices.Add(item); if (_grid.SelectedItem == null && _devices.Count > 0) _grid.SelectedItem = _devices[0]; _status.Text = "发现 " + _devices.Count(x => !string.IsNullOrWhiteSpace(x.DeviceId)) + " 台相机。"; } catch (Exception ex) { ShowError(ex); } }
        private void Connect() { try { if (Selected == null || string.IsNullOrWhiteSpace(Selected.DeviceId)) return; var provider = _vm.ConnectCamera(Selected.Vendor, Selected.DeviceId); SetEditor(provider.GetSettings()); _status.Text = Selected.Vendor + " 已连接：" + Selected.DisplayName; } catch (Exception ex) { ShowError(ex); } }
        private void Grab() { try { if (Selected == null) return; var provider = _vm.ConnectCamera(Selected.Vendor, Selected.DeviceId); provider.ApplySettings(ReadEditor()); var frame = provider.Acquire(3000); DisplayFrame(frame, Selected); _status.Text = "采集成功：" + frame.Width + "×" + frame.Height; } catch (Exception ex) { ShowError(ex); } }
        private void Disconnect() { try { StopLive(); if (Selected != null) _vm.CameraRegistry.Disconnect(Selected.Vendor); _status.Text = "相机已断开。"; } catch (Exception ex) { ShowError(ex); } }
        private void ShowError(Exception ex) { _status.Text = ex.Message; MessageBox.Show(this, ex.Message, "相机操作失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        private void ToggleLive()
        {
            if (_liveCancellation != null) { StopLive(); return; }
            StartLive();
        }
        private void StartLive()
        {
            try
            {
                if (Selected == null || string.IsNullOrWhiteSpace(Selected.DeviceId)) return;
                var device = Selected;
                var provider = _vm.ConnectCamera(device.Vendor, device.DeviceId);
                provider.ApplySettings(ReadEditor());
                var cts = new CancellationTokenSource();
                _liveCancellation = cts;
                _liveButton.Content = "停止实时";
                _status.Text = device.Vendor + " 实时显示中：" + device.DisplayName;
                RunLiveLoopAsync(provider, device, cts.Token);
            }
            catch (Exception ex) { StopLive(); ShowError(ex); }
        }
        private async void RunLiveLoopAsync(ICameraProvider provider, CameraDeviceInfo device, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var frame = await Task.Run(() => provider.Acquire(3000), token);
                    if (token.IsCancellationRequested) break;
                    DisplayFrame(frame, device);
                    _status.Text = string.Format("{0} 实时：{1}×{2}  {3:HH:mm:ss.fff}", device.DisplayName, frame.Width, frame.Height, frame.Timestamp);
                    await Task.Delay(10, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    StopLive();
                    ShowError(ex);
                    break;
                }
            }
        }
        private void StopLive()
        {
            var cts = _liveCancellation;
            _liveCancellation = null;
            if (cts != null) cts.Cancel();
            if (_liveButton != null) _liveButton.Content = "实时显示";
        }
        private void DisplayFrame(CameraFrameData frame, CameraDeviceInfo device)
        {
            if (frame == null || frame.BgrPixels == null) return;
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null, frame.BgrPixels, frame.Stride);
            bitmap.Freeze();
            _preview.Source = bitmap;
        }
        private void ApplySettings(bool saveUserSet)
        {
            try
            {
                if (Selected == null) return; var provider = _vm.ConnectCamera(Selected.Vendor, Selected.DeviceId); var settings = ReadEditor(); provider.ApplySettings(settings); if (saveUserSet) provider.SaveUserSet(settings.UserSet); SaveCameraDefinition(settings);
                _status.Text = saveUserSet ? "参数已应用并保存到相机 " + settings.UserSet : "相机参数已应用。";
            }
            catch (Exception ex) { ShowError(ex); }
        }
        private void LoadUserSet() { try { if (Selected == null) return; var provider = _vm.ConnectCamera(Selected.Vendor, Selected.DeviceId); provider.LoadUserSet(Convert.ToString(_userSet.SelectedItem)); SetEditor(provider.GetSettings()); _status.Text = "已从相机加载 " + _userSet.SelectedItem; } catch (Exception ex) { ShowError(ex); } }
        private CameraSettings ReadEditor() { double exposure, gain, rate; double.TryParse(_exposure.Text, out exposure); double.TryParse(_gain.Text, out gain); double.TryParse(_frameRate.Text, out rate); return new CameraSettings { ExposureUs = exposure, Gain = gain, TriggerMode = Convert.ToString(_triggerMode.SelectedItem), TriggerSource = Convert.ToString(_triggerSource.SelectedItem), PixelFormat = Convert.ToString(_pixelFormat.SelectedItem), FrameRateEnabled = _frameRateEnabled.IsChecked == true, FrameRate = rate, UserSet = Convert.ToString(_userSet.SelectedItem) }; }
        private void SetEditor(CameraSettings value) { _exposure.Text = value.ExposureUs.ToString("0.###"); _gain.Text = value.Gain.ToString("0.###"); _triggerMode.SelectedItem = value.TriggerMode; _triggerSource.SelectedItem = value.TriggerSource; _pixelFormat.SelectedItem = value.PixelFormat; _frameRateEnabled.IsChecked = value.FrameRateEnabled; _frameRate.Text = value.FrameRate.ToString("0.###"); _userSet.SelectedItem = value.UserSet; }
        private void SaveCameraDefinition(CameraSettings s) { var item = _vm.Cameras.FirstOrDefault(x => Selected != null && string.Equals(x.Vendor, Selected.Vendor, StringComparison.OrdinalIgnoreCase) && string.Equals(x.DeviceId, Selected.DeviceId, StringComparison.OrdinalIgnoreCase)); if (item == null) { item = new CameraDefinition { Name = Selected.DisplayName, Vendor = Selected.Vendor, DeviceId = Selected.DeviceId }; _vm.Cameras.Add(item); } item.ExposureUs = s.ExposureUs; item.Gain = s.Gain; item.TriggerMode = s.TriggerMode; item.TriggerSource = s.TriggerSource; item.PixelFormat = s.PixelFormat; item.FrameRateEnabled = s.FrameRateEnabled; item.FrameRate = s.FrameRate; item.UserSet = s.UserSet; _vm.CommitProjectStructure(); }
        private static TextBox AddEditor(Panel panel, string label, string value) { panel.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 2) }); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
        private static ComboBox AddCombo(Panel panel, string label, object source, string value) { panel.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 2) }); var box = new ComboBox { ItemsSource = source as System.Collections.IEnumerable, IsEditable = true, SelectedItem = value }; panel.Children.Add(box); return box; }
    }
}
