using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using VisionFlowStudio.Core;
using VisionFlowStudio.Scripting;

namespace VisionFlowStudio.App
{
    public sealed class NodeDefinition
    {
        public string NodeType { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string Platform { get; set; }
        public string Description { get; set; }
        public string LocalizedDisplayName { get { return LocalizationService.TDynamic(DisplayName); } }
        public string LocalizedDescription { get { return LocalizationService.TDynamic(Description); } }
        public IList<NodeParameter> DefaultParameters { get; set; } = new List<NodeParameter>();
        public override string ToString() { return DisplayName; }
    }

    public static class NodeCatalog
    {
        public static readonly IReadOnlyList<NodeDefinition> All = new[]
        {
            Define("CameraGrabNode", "工业相机采图", "采集", "Camera", "配置并从 Basler/海康/大华相机采集命名图像", "Vendor","Hikrobot","DeviceId","","TimeoutMs","30000","ExposureUs","10000","Gain","0","TriggerMode","Off","TriggerSource","Software","PixelFormat","Mono8","FrameRateEnabled","False","FrameRate","10","UserSet","UserSet1","OutputImageKey","CameraImage","OutputPathKey","CameraImagePath"),
            Define("DelayNode", "延时", "通用", "Common", "等待指定时间", "DelayMs","100"),
            Define("SetValueNode", "变量赋值", "数据", "Common", "向运行上下文写入变量", "Key","Value","Value","0"),
            Define("LimitJudgeNode", "上下限判定", "判定", "Common", "按上下限判定数值", "InputKey","Value","Min","0","Max","1"),
            Define("LogNode", "日志", "数据", "Common", "写入运行日志", "Message","流程节点执行完成"),
            Define("VisionMasterProcedureNode", "VisionMaster 流程", "算法", "VisionMaster", "运行 VisionMaster Solution 中的流程", "SolutionPath","","ProcedureName","流程1","ImagePath","","ImageSourceKey","CameraImagePath","ImageInputName","InputImage","OkOutputName","IsOK"),
            Define("VisionProToolBlockNode", "VisionPro ToolBlock", "算法", "VisionPro", "加载并运行 CogToolBlock VPP", "ToolBlockPath","","ImagePath","","ImageSourceKey","CameraImagePath","ImageInputName","InputImage","OkOutputName","IsOK"),
            Define("HalconProcedureNode", "HALCON Procedure", "算法", "HALCON", "通过 HDevEngine 运行 external procedure", "ProcedurePath","","ImagePath","","ImageSourceKey","CameraImagePath","ImageInputName","Image","OkOutputName","IsOK"),
            Define("CommunicationWriteNode", "工业协议写入", "通讯", "Communication", "通过 HslCommunication 将视觉节点数据写入 PLC", "Channel","PLC_01","Address","DB1.0","SourceKey","","DataType","Bool"),
            Define("CSharpScriptNode", "C# 高级脚本", "脚本", "CSharp", "使用 Roslyn 执行 C# 脚本，可读取任意前序节点输入输出并加载外部 DLL",
                "ScriptFile","", "References","", "Imports","System;System.Linq;System.Collections.Generic", "OutputNames","Result;IsOK",
                "Code",CSharpScriptEngine.DefaultClassTemplate),
            Define("SaveImageNode", "保存图像", "数据", "Common", "将当前图像保存到指定目录", "Folder","RunData\\Images","Format","bmp")
        };

        public static FlowNodeConfig CreateConfig(string nodeType)
        {
            var definition = All.First(x => x.NodeType == nodeType);
            var config = new FlowNodeConfig { NodeName = definition.DisplayName, NodeType = definition.NodeType, Category = definition.Category, Platform = definition.Platform };
            if (definition.NodeType == "CameraGrabNode") config.TimeoutMs = 30000;
            foreach (var item in definition.DefaultParameters) config.Parameters.Add(new NodeParameter { Key = item.Key, Value = item.Value });
            return config;
        }

        private static NodeDefinition Define(string type, string name, string category, string platform, string description, params string[] values)
        {
            var result = new NodeDefinition { NodeType = type, DisplayName = name, Category = category, Platform = platform, Description = description };
            for (var i = 0; i + 1 < values.Length; i += 2) result.DefaultParameters.Add(new NodeParameter { Key = values[i], Value = values[i + 1] });
            return result;
        }
    }

    public sealed class NodePickerWindow : Window
    {
        public NodeDefinition SelectedDefinition { get; private set; }
        private readonly ListBox _list;
        public NodePickerWindow()
        {
            Title = "选择流程节点"; Width = 620; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner; MinWidth = 500; MinHeight = 400;
            var root = new DockPanel { Margin = new Thickness(12) };
            var title = new TextBlock { Text = "从节点目录中选择", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 2, 4, 10) }; DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "添加节点", Width = 90, IsDefault = true }; var cancel = new Button { Content = "取消", Width = 76, IsCancel = true }; buttons.Children.Add(ok); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            _list = new ListBox { ItemsSource = NodeCatalog.All };
            _list.ItemTemplate = BuildTemplate(); root.Children.Add(_list); Content = root;
            ok.Click += delegate { SelectedDefinition = _list.SelectedItem as NodeDefinition; if (SelectedDefinition != null) DialogResult = true; };
            _list.MouseDoubleClick += delegate { SelectedDefinition = _list.SelectedItem as NodeDefinition; if (SelectedDefinition != null) DialogResult = true; };
        }
        private static DataTemplate BuildTemplate()
        {
            var template = new DataTemplate(typeof(NodeDefinition));
            var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.MarginProperty, new Thickness(6));
            var name = new FrameworkElementFactory(typeof(TextBlock)); name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LocalizedDisplayName")); name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); panel.AppendChild(name);
            var detail = new FrameworkElementFactory(typeof(TextBlock)); detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LocalizedDescription")); detail.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.SlateGray); detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(detail);
            template.VisualTree = panel; return template;
        }
    }
}
