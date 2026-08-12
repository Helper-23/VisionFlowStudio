using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace VisionFlowStudio.App
{
    /// <summary>
    /// Lightweight application-wide localization for the existing WPF views.
    /// Static UI text is translated while bound values (recipe names, results and
    /// operator input) are deliberately left untouched.
    /// </summary>
    public static class LocalizationService
    {
        private static readonly Dictionary<string, string> ZhToEn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "文件(_F)", "File (_F)" }, { "新建项目", "New Project" }, { "打开项目...", "Open Project..." },
            { "保存项目", "Save Project" }, { "另存为项目...", "Save Project As..." }, { "退出", "Exit" },
            { "项目(_P)", "Project (_P)" }, { "项目属性", "Project Properties" },
            { "型号 / 工位 / 相机 / 通信配置", "Recipes / Stations / Cameras / Communications" },
            { "流程(_W)", "Flow (_W)" }, { "新建流程", "New Flow" }, { "打开流程...", "Open Flow..." },
            { "保存流程", "Save Flow" }, { "另存为流程...", "Save Flow As..." },
            { "选择并添加节点...", "Select and Add Node..." }, { "添加 VisionMaster 节点", "Add VisionMaster Node" },
            { "添加 VisionPro 节点", "Add VisionPro Node" }, { "添加 HALCON 节点", "Add HALCON Node" },
            { "添加 C# 高级脚本节点", "Add C# Advanced Script Node" }, { "添加通信写入节点", "Add Communication Write Node" },
            { "复制节点", "Copy Node" }, { "删除节点", "Delete Node" }, { "上移", "Move Up" },
            { "下移", "Move Down" }, { "启用/禁用", "Enable/Disable" },
            { "运行(_R)", "Run (_R)" }, { "单次运行整个流程", "Run Flow Once" }, { "连续运行", "Continuous Run" },
            { "启动通讯触发", "Start Communication Trigger" }, { "单步运行", "Run Selected Node" },
            { "从当前节点运行", "Run from Current Node" }, { "停止", "Stop" },
            { "工具(_T)", "Tools (_T)" }, { "系统设置", "System Settings" }, { "工业相机管理", "Industrial Camera Manager" },
            { "通信配置", "Communication Configuration" }, { "视觉平台状态", "Vision Platform Status" },
            { "打开运行数据目录", "Open Runtime Data Folder" },
            { "帮助(_H)", "Help (_H)" }, { "用户手册", "User Manual" }, { "快速入门", "Quick Start" }, { "关于", "About" },
            { "＋ 选择节点", "+ Select Node" }, { "VM 节点", "VM Node" }, { "VP 节点", "VP Node" },
            { "HALCON 节点", "HALCON Node" }, { "C# 脚本", "C# Script" }, { "通讯节点", "Communication Node" },
            { "复制", "Copy" }, { "删除", "Delete" }, { "↑ 上移", "↑ Move Up" }, { "↓ 下移", "↓ Move Down" },
            { "▶ 单次运行", "▶ Run Once" }, { "⟳ 连续运行", "⟳ Continuous" }, { "⚡ 通讯触发", "⚡ PLC Trigger" },
            { "▷ 单步", "▷ Single Step" }, { "⇥ 从当前运行", "⇥ Run from Current" }, { "■ 停止", "■ Stop" },
            { "项目结构", "Project Explorer" }, { "流程编辑", "Flow Editor" }, { "流程名称：", "Flow Name:" },
            { "  双击单元格可编辑，右侧可修改完整参数", "  Double-click a cell to edit; use the property panel for full parameters" },
            { "启用", "Enabled" }, { "节点名称", "Node Name" }, { "类型", "Type" }, { "平台", "Platform" },
            { "状态", "Status" }, { "耗时", "Duration" }, { "信息", "Message" }, { "图像与结果", "Images & Results" },
            { "多画面监控", "Multi-view Monitor" }, { "所有工站 / 型号 / 流程画面", "All Station / Recipe / Flow Views" },
            { "弹出选中画面", "Open Selected View" }, { "单画面查看", "Single View" },
            { "节点属性", "Node Properties" }, { "节点类型", "Node Type" }, { "类别", "Category" },
            { "启用节点", "Enable Node" }, { "超时(ms)", "Timeout (ms)" }, { "错误策略", "Error Policy" },
            { "节点参数", "Node Parameters" }, { "判定数据源", "Judge Data Source" }, { "下限 Min", "Minimum" }, { "上限 Max", "Maximum" },
            { "VisionMaster", "VisionMaster" }, { "VisionPro", "VisionPro" }, { "通讯", "Communication" },
            { "运行日志", "Runtime Log" }, { "时间", "Time" }, { "级别", "Level" },
            { "软件设置", "Software" }, { "权限设置", "Permissions" },
            { "方案设置", "Project" }, { "运行策略", "Runtime" }, { "采集策略", "Acquisition" },
            { "启动关闭设置", "Startup and Shutdown" }, { "开机软件自启动", "Start with Windows" },
            { "启动时最大化", "Start Maximized" }, { "自动加载指定方案", "Automatically Load a Project" },
            { "载入路径", "Project Path" }, { "方案密码", "Project Password" }, { "选择加密方案", "Select Encrypted Project" },
            { "密码使用 Windows 当前用户凭据保护，不会明文保存。", "The password is protected by the current Windows account and is never stored as plain text." },
            { "自动保存设置", "Automatic Save" }, { "自动保存方案", "Automatically Save Project" },
            { "保存时间间隔", "Save Interval" }, { "分钟", "minutes" },
            { "仅保存已经指定路径和密码的加密项目；流程运行期间会自动顺延到下一次。", "Only encrypted projects with a path and password are saved. Saving is deferred while a flow is running." },
            { "语言与界面", "Language and Interface" }, { "界面语言", "Display Language" },
            { "语言切换会立即应用到当前窗口，并在下次启动时继续使用。", "The selected language is applied immediately and retained for the next launch." },
            { "简体中文", "Simplified Chinese" }, { "英语", "English" }, { "确定", "OK" }, { "取消", "Cancel" },
            { "保存", "Save" }, { "重命名", "Rename" }, { "重命名流程", "Rename Flow" }, { "删除流程", "Delete Flow" },
            { "产品型号", "Recipe" }, { "工位名称", "Station Name" }, { "项目名称", "Project Name" }, { "流程名称", "Flow Name" },
            { "相机参数", "Camera Parameters" }, { "刷新设备", "Refresh Devices" }, { "连接", "Connect" },
            { "采集一帧", "Grab One Frame" }, { "断开", "Disconnect" }, { "应用参数", "Apply Parameters" },
            { "加载 UserSet", "Load UserSet" }, { "保存到相机 UserSet", "Save to Camera UserSet" },
            { "新增通道", "Add Channel" }, { "测试连接", "Test Connection" }, { "完成", "Done" },
            { "名称", "Name" }, { "协议", "Protocol" }, { "端口", "Port" }, { "串口", "Serial Port" },
            { "波特率", "Baud Rate" }, { "数据位", "Data Bits" }, { "校验", "Parity" }, { "停止位", "Stop Bits" },
            { "编辑", "Edit" }, { "浏览", "Browse" }, { "关闭", "Close" }, { "运行选中节点", "Run Selected Node" },
            { "管理通信通道", "Manage Communication Channels" }, { "添加写入", "Add Write" }, { "删除写入", "Remove Write" },

            // Bound and runtime text. These entries are also used by TDynamic so
            // project data remains canonical while the UI follows the language.
            { "产品型号组", "Recipes" }, { "工站组", "Stations" }, { "通信配置组", "Communications" },
            { "工站", "Stations" }, { "参数名", "Parameter" }, { "值", "Value" }, { "消息", "Message" },
            { "项目", "Project" }, { "流程尚未保存", "Flow not saved" }, { "共 {0} 个画面", "{0} views" },
            { "工业相机采图", "Industrial Camera Grab" }, { "图像准备", "Image Preparation" },
            { "VisionMaster 流程", "VisionMaster Flow" }, { "VisionPro ToolBlock", "VisionPro ToolBlock" },
            { "HALCON Procedure", "HALCON Procedure" }, { "C# 高级脚本", "C# Advanced Script" },
            { "工业协议写入", "Industrial Protocol Write" }, { "通信结果写入", "Communication Result Write" }, { "结果判定", "Result Judge" },
            { "采集", "Acquisition" }, { "判定", "Judgment" }, { "数据", "Data" }, { "视觉", "Vision" },
            { "脚本", "Script" }, { "通信", "Communication" }, { "空闲", "Idle" }, { "成功", "OK" },
            { "错误", "Error" }, { "已跳过", "Skipped" }, { "就绪", "Ready" }, { "等待运行", "Waiting to run" },
            { "运行完成", "Completed" }, { "运行中", "Running" }, { "已停止", "Stopped" },
            { "流程已停止", "Flow stopped" }, { "节点已禁用", "Node disabled" },
            { "图像输入已准备", "Image input ready" }, { "流程执行完成", "Flow completed" },
            { "等待硬件触发", "Waiting for hardware trigger" }, { "采图完成", "Image acquisition completed" },
            { "工程已启动。VisionMaster 是流程节点之一，可编辑并组合通用节点。", "Application started. VisionMaster is available as an editable flow node alongside common nodes." },
            { "加密项目已加载：", "Encrypted project loaded: " }, { "已切换到 ", "Switched to " },
            { "已创建新项目。", "New project created." }, { "已创建新流程。", "New flow created." },
            { "流程已保存：", "Flow saved: " }, { "项目已保存：", "Project saved: " },
            { "已打开流程：", "Flow opened: " }, { "图像预览失败：", "Image preview failed: " },
            { "自动保存项目失败：", "Automatic project save failed: " },
            { "输出刷新失败，继续使用缓存：", "Output refresh failed; cached outputs retained: " },
            { "通信通道不存在：", "Communication channel does not exist: " },
            { "流程数据不存在：", "Flow data does not exist: " },
            { "节点调试中", "Debugging node" }, { "方案已加载", "Project loaded" },
            { "加载方案中", "Loading project" }, { "加载失败", "Load failed" }, { "方案已关闭", "Project closed" },
            { "连续运行等待中", "Waiting for continuous run" }, { "等待通讯触发", "Waiting for communication trigger" },
            { "通讯触发读取失败", "Communication trigger read failed" },

            { "Solution 文件", "Solution File" }, { "密码", "Password" }, { "测试图像（可选）", "Test Image (Optional)" },
            { "流程图像来源（测试图像留空时使用）", "Flow Image Source (used when test image is empty)" },
            { "动态图像输入名", "Dynamic Image Input Name" }, { "图像输入名", "Image Input Name" },
            { "OK 输出名（可留空）", "OK Output Name (Optional)" }, { "OK 输出名", "OK Output Name" },
            { "加载 Solution", "Load Solution" }, { "打开 VM 调试窗口", "Open VM Debugger" }, { "测试运行", "Test Run" },
            { "控制参数请在节点参数表中添加 Input.参数名", "Add control parameters as Input.ParameterName in the node parameter table." },
            { "打开 HDVP 脚本调试", "Open HDVP Script Debugger" }, { "C# 高级脚本节点", "C# Advanced Script Node" },
            { "支持 Roslyn 编译、语义智能提示、编译诊断、外部 DLL 引用，以及访问当前流程中任意节点的输入与输出。脚本声明的输出也可供后续节点和通讯节点选择。", "Supports Roslyn compilation, semantic IntelliSense, diagnostics, external DLL references, and access to any node input or output in the current flow. Declared script outputs are available to downstream and communication nodes." },
            { "打开脚本编辑器", "Open Script Editor" }, { "常用 API", "Common APIs" },
            { "HslCommunication 通道", "HslCommunication Channel" }, { "多地址写入映射", "Multi-address Write Mapping" },
            { "视觉输出", "Vision Output" }, { "PLC 地址", "PLC Address" }, { "数据类型", "Data Type" },
            { "+添加写入", "+ Add Write" }, { "添加通讯节点", "Add Communication Node" },
            { "流程运行与 PLC 触发", "Flow Execution and PLC Trigger" }, { "连续间隔(ms)", "Continuous Interval (ms)" },
            { "触发通道", "Trigger Channel" }, { "读取地址", "Read Address" }, { "触发方式", "Trigger Mode" },
            { "读取地址（TCP不使用）", "Read Address (not used by TCP)" }, { "数据类型（TCP固定文本）", "Data Type (TCP uses text)" },
            { "指定字符串", "Expected Text" }, { "启动通信触发", "Start Communication Trigger" },
            { "目标值", "Expected Value" }, { "轮询周期(ms)", "Polling Interval (ms)" },
            { "启动 PLC 触发", "Start PLC Trigger" }, { "单次运行", "Run Once" },
            { "未找到帮助文档，请重新编译或修复安装。", "Help documentation was not found. Rebuild it or repair the installation." },
            { "打开帮助文档失败", "Failed to Open Help" },

            // Node catalogue and code examples are data-bound rather than part of
            // the logical XAML tree, so they need explicit display translations.
            { "选择流程节点", "Select Flow Node" }, { "从节点目录中选择", "Select from the node catalog" },
            { "添加节点", "Add Node" }, { "配置并从 Basler/海康/大华相机采集命名图像", "Configure and acquire a named image from a Basler, Hikrobot, or Dahua camera" },
            { "延时", "Delay" }, { "等待指定时间", "Wait for the specified duration" },
            { "变量赋值", "Set Variable" }, { "通用", "Common" }, { "向运行上下文写入变量", "Write a value to the runtime context" },
            { "上下限判定", "Range Judge" }, { "按上下限判定数值", "Judge a numeric value against lower and upper limits" },
            { "日志", "Log" }, { "写入运行日志", "Write to the runtime log" }, { "流程节点执行完成", "Flow node completed" },
            { "算法", "Algorithm" }, { "运行 VisionMaster Solution 中的流程", "Run a procedure in a VisionMaster Solution" },
            { "加载并运行 CogToolBlock VPP", "Load and run a CogToolBlock VPP" },
            { "通过 HDevEngine 运行 external procedure", "Run an external procedure through HDevEngine" },
            { "通过 HslCommunication 将视觉节点数据写入 PLC", "Write vision-node data to a PLC through HslCommunication" },
            { "将流程结果写入 PLC 地址或通过 TCP/IP 客户端/服务器发送文本", "Write flow results to PLC addresses or send text through a TCP/IP client/server" },
            { "使用 Roslyn 执行 C# 脚本，可读取任意前序节点输入输出并加载外部 DLL", "Run C# code with Roslyn, read any preceding node input or output, and load external DLLs" },
            { "保存图像", "Save Image" }, { "将当前图像保存到指定目录", "Save the current image to the specified folder" },
            { "流程1", "Procedure 1" },
            { "Get<T>(\"节点.输出\")\nGetNodeOutput<T>(\"节点\", \"输出\")\nGetNodeInput<T>(\"节点\", \"输入\")\nTool(\"节点\").Inputs / Outputs\nSetOutput(\"名称\", value)",
              "Get<T>(\"Node.Output\")\nGetNodeOutput<T>(\"Node\", \"Output\")\nGetNodeInput<T>(\"Node\", \"Input\")\nTool(\"Node\").Inputs / Outputs\nSetOutput(\"Name\", value)" },
            { "项目：", "Project:" }
        };

        private static readonly Dictionary<string, string> EnToZh = BuildReverseDictionary();
        private static bool _registered;

        public static string CurrentLanguage { get; private set; } = "zh-CN";
        public static bool IsEnglish { get { return string.Equals(CurrentLanguage, "en-US", StringComparison.OrdinalIgnoreCase); } }
        public static event EventHandler LanguageChanged;

        public static void Initialize(string language)
        {
            if (!_registered)
            {
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(Window_Loaded));
                _registered = true;
            }
            SetLanguage(language, false);
        }

        public static void SetLanguage(string language) { SetLanguage(language, true); }

        public static string T(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string translated;
            var map = IsEnglish ? ZhToEn : EnToZh;
            return map.TryGetValue(text, out translated) ? translated : text;
        }

        public static string TDynamic(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Chinese is the canonical language of persisted project/runtime
            // data. Never reverse-translate English fragments while displaying
            // that data: doing so corrupts SDK names, identifiers and paths such
            // as VisionPro, IsOK and C:\Users\CloudVision.
            if (!IsEnglish) return text;

            var exact = T(text);
            if (!string.Equals(exact, text, StringComparison.Ordinal)) return exact;

            // Runtime messages often contain a translated phrase followed by a
            // node name, address or path. Replace only known UI phrases; never
            // mutate the underlying project value.
            return TranslateKnownPhrases(text, ZhToEn);
        }

        private static string TStatic(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var exact = T(text);
            if (!string.Equals(exact, text, StringComparison.Ordinal)) return exact;

            // Chinese switching is exact-only so branded/product text embedded
            // in a title (VisionFlow, VisionMaster, VisionPro, IsOK...) remains
            // untouched. English still supports sentences with dynamic suffixes.
            return IsEnglish ? TranslateKnownPhrases(text, ZhToEn) : text;
        }

        private static string TranslateKnownPhrases(string text, IDictionary<string, string> map)
        {
            var result = text;
            foreach (var pair in map.OrderByDescending(x => x.Key.Length))
            {
                if (string.IsNullOrEmpty(pair.Key) || result.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                result = ReplaceOrdinalIgnoreCase(result, pair.Key, pair.Value);
            }
            return result;
        }

        public static string ToCanonical(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsEnglish) return text;
            string canonical;
            return EnToZh.TryGetValue(text, out canonical) ? canonical : text;
        }

        public static void Apply(Window window)
        {
            if (window == null) return;
            var visited = new HashSet<DependencyObject>();
            ApplyCore(window, visited);
            window.FontFamily = new FontFamily(IsEnglish ? "Segoe UI" : "Microsoft YaHei UI");
        }

        private static void SetLanguage(string language, bool notify)
        {
            CurrentLanguage = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";
            var culture = CultureInfo.GetCultureInfo(CurrentLanguage);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            // Apply the new captions after the settings dialog has returned to the
            // dispatcher. Translating a loaded production workspace synchronously
            // from the OK click used to make the language switch look frozen.
            if (Application.Current != null)
            {
                var application = Application.Current;
                application.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    var windows = application.Windows.Cast<Window>().ToArray();
                    foreach (var window in windows) Apply(window);
                }));
            }
            if (notify && LanguageChanged != null) LanguageChanged(null, EventArgs.Empty);
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e) { Apply(sender as Window); }

        private static void ApplyCore(DependencyObject item, HashSet<DependencyObject> visited)
        {
            if (item == null || !visited.Add(item)) return;
            var window = item as Window;
            if (window != null && !BindingOperations.IsDataBound(window, Window.TitleProperty)) window.Title = TStatic(window.Title);

            var textBlock = item as TextBlock;
            // Do not assign an empty Text value. TextBlocks created by WPF control
            // templates often render Inlines/AccessText while Text itself is empty;
            // assigning String.Empty clears those generated runs (notably menus).
            if (textBlock != null && !string.IsNullOrEmpty(textBlock.Text) &&
                !BindingOperations.IsDataBound(textBlock, TextBlock.TextProperty))
                textBlock.Text = TStatic(textBlock.Text);

            var headeredContent = item as HeaderedContentControl;
            if (headeredContent != null && headeredContent.Header is string) headeredContent.Header = TStatic((string)headeredContent.Header);
            var headeredItems = item as HeaderedItemsControl;
            if (headeredItems != null && headeredItems.Header is string) headeredItems.Header = TStatic((string)headeredItems.Header);
            var content = item as ContentControl;
            if (content != null && content.Content is string && !string.IsNullOrEmpty((string)content.Content) &&
                !BindingOperations.IsDataBound(content, ContentControl.ContentProperty))
                content.Content = TStatic((string)content.Content);

            var frameworkElement = item as FrameworkElement;
            if (frameworkElement != null && frameworkElement.ToolTip is string) frameworkElement.ToolTip = TStatic((string)frameworkElement.ToolTip);

            var grid = item as DataGrid;
            if (grid != null)
                foreach (var column in grid.Columns) if (column.Header is string) column.Header = TStatic((string)column.Header);

            var itemsControl = item as ItemsControl;
            if (itemsControl != null)
            {
                // Take snapshots before recursing. Some WPF controls regenerate
                // containers when a header/content changes; enumerating their live
                // collections at that moment can throw or repeatedly rebuild them.
                var itemChildren = itemsControl.Items.Cast<object>().OfType<DependencyObject>().ToArray();
                foreach (var child in itemChildren) ApplyCore(child, visited);
            }

            // Static WPF content declared in XAML is reachable through the logical
            // tree. Walking the visual tree additionally visits every generated
            // DataGrid cell, template presenter and image-viewer element. On a
            // loaded industrial project that caused seconds of UI blocking and
            // could rebuild templates while they were being enumerated.
            var logicalChildren = LogicalTreeHelper.GetChildren(item).Cast<object>().OfType<DependencyObject>().ToArray();
            foreach (var child in logicalChildren) ApplyCore(child, visited);
        }

        private static Dictionary<string, string> BuildReverseDictionary()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in ZhToEn) if (!result.ContainsKey(pair.Value)) result.Add(pair.Value, pair.Key);
            return result;
        }

        private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
        {
            var start = 0;
            while (true)
            {
                var index = source.IndexOf(oldValue, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) return source;
                source = source.Substring(0, index) + newValue + source.Substring(index + oldValue.Length);
                start = index + newValue.Length;
            }
        }
    }
}
