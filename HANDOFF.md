# VisionFlow Studio 任务交接

> 本文仅依据当前任务上下文整理，未重新遍历整个仓库。最后更新：2026-08-12。

## 1. 当前项目架构和主要模块

- 技术栈：WPF、.NET Framework 4.8、x64。
- `VisionFlowStudio.App`：主界面、MVVM、项目/流程管理、设置窗口、平台调试窗口、图像视图和启动界面。
- `VisionFlowStudio.Core`：流程、节点、项目结构、视觉平台适配接口及公共数据模型。
- `VisionFlowStudio.Cameras`：Basler、Hikrobot/海康、大华工业相机枚举、连接、采图和参数控制。
- `VisionFlowStudio.Communications`：基于 HslCommunication 7.0 的 PLC/工业协议通信。
- `VisionFlowStudio.VisionMaster`：VisionMaster 4.4 `.sol` 加载、运行、输入图像注入和输出读取。
- `VisionFlowStudio.VisionPro`：VisionPro ToolBlock/VPP 加载、运行和调试控件集成。
- `VisionFlowStudio.Halcon`：HALCON/HDevEngine、`.hdvp` 过程调用和参数编辑。
- `VisionFlowStudio.Scripting`/相关脚本代码：Roslyn C# 高级脚本编译、运行、补全、签名帮助及外部 DLL 引用。
- `VisionFlowStudio.Licensing`：机器码、许可证校验和软加密相关代码。
- `VisionFlowStudio.SmokeTests`：平台与流程冒烟验证。

## 2. 已完成的功能

- 新项目结构：产品型号、工站、相机、通信通道及一个型号下的多个流程。
- 项目级新建、保存、另存为、加载；流程级新建、保存、另存为、重命名和删除。
- 新版项目序列化以完整 Project 结构为准，不再以旧 `.flow.json` 为设计中心。
- 项目方案采用 `.vfsproj`，已加入密码/加密保存与加载逻辑。
- 流程节点的新增、选择、复制、删除、排序、启用/禁用、单次、连续、单步、从当前节点运行和停止。
- VisionMaster、VisionPro、HALCON 三个平台的基本加载、调试和执行接入。
- 工业相机发现、连接、单帧采集、曝光、增益、触发模式/触发源、像素格式、帧率和 UserSet 操作。
- 相机管理实时预览；总览页面按流程显示画面，已取消独立相机画面卡片。
- 图像缩放、拖动、适应、1:1、鼠标坐标和灰度显示。
- 多工站/多型号/多流程画面总览与单独弹出。
- 通信通道管理、连接测试、多地址写入、视觉输出选择和 PLC 地址触发流程。
- 支持 Siemens S7Net、Mitsubishi MC ASCII、Modbus TCP/RTU、Omron FINS TCP、Allen-Bradley EtherNet/IP 的设计入口。
- C# 高级脚本节点：完整类式脚本、读取节点数据、声明输出、外部 DLL、`using`、编译检查、运行及编辑器补全基础能力。
- 浅蓝色工业风 UI、启动画面、状态栏平台状态和实时时钟。
- 系统设置：启动选项、自动加载方案、方案密码、定时自动保存等。
- 中英文切换框架和 CHM 用户手册入口已加入。

## 3. 当前正在处理的问题

- 软件软加密/机器绑定授权刚加入，许可证工具的项目输出类型和项目引用尚未整理完成。
- 部署方案尚未最终确定：第三方视觉平台 DLL、原生 Runtime、驱动、服务和许可证不能只靠复制少量托管 DLL 解决。
- 第三方 SDK 的开发机引用路径与目标机运行时路径仍需解耦。

## 4. 已经修改过的主要文件

- `src/VisionFlowStudio.App/MainWindow.xaml`
- `src/VisionFlowStudio.App/MainWindow.xaml.cs`
- `src/VisionFlowStudio.App/MainViewModel.cs`
- `src/VisionFlowStudio.App/Mvvm.cs`
- `src/VisionFlowStudio.App/ManagementWindows.cs`
- `src/VisionFlowStudio.App/PlatformDebugWindows.cs`
- `src/VisionFlowStudio.App/ProjectDataStore.cs`
- `src/VisionFlowStudio.App/ProjectTree.cs`
- `src/VisionFlowStudio.App/ScriptEditorWindow.cs`
- `src/VisionFlowStudio.App/SystemSettingsWindow.xaml`
- `src/VisionFlowStudio.App/ApplicationSettings.cs`
- `src/VisionFlowStudio.App/App.config`
- `src/VisionFlowStudio.Core/FlowModels.cs`
- `src/VisionFlowStudio.Core/IVisionPlatformAdapter.cs`
- `src/VisionFlowStudio.Cameras/CameraRegistry.cs`
- `src/VisionFlowStudio.Cameras/HikrobotCameraProvider.cs`
- `src/VisionFlowStudio.Cameras/DahuaCameraProvider.cs`
- `src/VisionFlowStudio.Communications/CommunicationRegistry.cs`
- `src/VisionFlowStudio.VisionMaster/VisionMasterAdapter.cs`
- `src/VisionFlowStudio.VisionMaster/VisionMasterRuntime.cs`
- `src/VisionFlowStudio.VisionPro/VisionProAdapter.cs`
- `src/VisionFlowStudio.Halcon/HalconAdapter.cs`
- `src/VisionFlowStudio.Scripting/CSharpScriptEngine.cs`
- `src/VisionFlowStudio.Licensing/*`
- 各模块对应的 `.csproj`、解决方案文件、帮助文档源文件和 CHM 工程文件。

## 5. 关键类、接口和调用关系

```text
MainWindow
  -> MainViewModel
       -> ProjectDataStore             项目加密序列化/反序列化
       -> Flow/FlowNodeViewModel       流程与节点编辑、执行状态
       -> IVisionMasterAdapter         VM 加载、注图、运行、取输出
       -> IVisionProAdapter            VP ToolBlock 运行与调试
       -> IHalconAdapter               HDVP/HDevEngine 调用
       -> CameraRegistry/Icamera...    相机发现、连接、采图、参数
       -> CommunicationRegistry        PLC 连接、读写和触发
       -> CSharpScriptEngine           脚本编译、补全、运行
       -> Licensing                    启动许可证校验
```

- 节点按流程顺序执行，节点输出写入共享流程数据字典。
- 后续视觉、脚本、判定和通信节点通过稳定的输出 Key 读取前序节点数据。
- 通信写入节点保存多条映射：视觉输出 Key → PLC 地址 → 数据类型。
- CameraGrabNode 输出图像供 VM、VP、HALCON 或脚本节点使用。
- 项目树结构为：Project → 工站 → 型号 → 多个流程；型号列表也作为全局配置存在。

## 6. 当前编译状态

- 最近一次明确看到的状态不是成功构建。
- Visual Studio 报 `CS0006`：找不到 `VisionFlowStudio.exe` 和 `VisionFlowStudio.Licensing.dll` 元数据文件。
- 根因倾向于 `VisionFlowStudio.Licensing`/许可证生成工具的输出类型或项目依赖配置错误：用户指出预期的 `VisionFlowStudio.LicenseTool.exe` 并未生成，现有项目看起来仍是类库。
- 此后没有完成一次可确认的全解决方案编译和实际启动验证，因此应视为“编译待修复、待验证”。

## 7. 尚未解决的问题

- 将许可证生成器做成真正的可执行工具，并避免 App、Licensing、LicenseTool 之间的循环或错误引用。
- 完整验证软加密流程：机器码生成、签发、导入、过期时间、篡改检测和无许可证启动拦截。
- 中英文切换仍有部分运行日志、树节点、状态值、属性名和默认节点名混用或误翻译。
- CHM 内容区排版和目录体验仍需继续检查；英文/中文手册最好按语言分别打开。
- C# 脚本的 IntelliSense、第三方 DLL 依赖解析、关闭卡顿及签名帮助仍需持续验证。
- 第三方平台 Runtime 的安装检测和友好提示不完整。
- VisionMaster、HALCON、大华仍存在硬编码安装目录，部署到不同路径可能失败。
- 相机、PLC、VM/VP/HALCON 的异常退出资源释放需要继续做长期稳定性验证。

## 8. 下一步应该继续做什么

1. 先修正 Licensing 解决方案结构：保留授权类库，另建 x64 .NET Framework 4.8 Console/WinForms `VisionFlowStudio.LicenseTool.exe`。
2. 清理项目引用并完成一次 `Rebuild Solution`，确认 App、Licensing、LicenseTool 和 SmokeTests 均产生正确输出。
3. 验证无许可证、有效许可证、错误机器码、过期许可证四种启动场景。
4. 增加部署前置检查：.NET 4.8、x64、VM、VP、HALCON、相机 Runtime/驱动及许可证。
5. 把 SDK 路径改为“设置值 → 注册表/环境变量 → 标准目录探测”，移除固定盘符依赖。
6. 制作正式安装包/Bootstrapper，并在一台干净 PC 上完成端到端部署验证。
7. 最后再统一检查中英文资源和 CHM 页面。

## 9. 不允许破坏的现有设计

- 保持 WPF + .NET Framework 4.8 + x64，不迁移到 .NET 9。
- 保持模块化适配器结构，VM、VP、HALCON、相机、通信、脚本不能重新堆回 App 工程。
- 保持完整 Project 结构及加密 `.vfsproj`；不要恢复旧 `.flow.json` 兼容逻辑作为主存储模型。
- 保持“一个工站可有多个型号、一个型号可有多个流程”。
- 保持每个流程独立画面以及多流程同时监看；不要重新加入总览中的独立相机画面。
- 保持节点输出使用稳定内部 Key；显示名称可本地化，但不能改变保存数据和节点连接。
- 保持通信节点的多地址映射，不能退回单地址/单值。
- 保持视觉平台官方调试控件或适配层，不能用静态占位 UI 代替真实运行。
- 不要把品牌名、类型名、文件路径、输出 Key 和用户数据做中文字符串替换。

## 10. 重要技术约束

- 第三方视觉平台及相机 SDK 的版本、位数必须与应用一致，当前统一为 x64。
- `HintPath` 仅用于开发机编译；不能把目标机部署设计成依赖开发机绝对路径。
- `Private=true` 只代表托管 DLL 被复制，不代表原生 DLL、驱动、服务、插件和许可证齐全。
- VisionMaster、VisionPro、HALCON 通常需要在目标机安装匹配 Runtime；VisionPro/HALCON 还需要有效运行许可证。
- Basler、海康、大华相机需要匹配的传输层、驱动和原生 Runtime，不能只复制 .NET 包装 DLL。
- Vendor DLL 的再分发必须遵守各厂商许可，不要无条件把整个安装目录打包。
- WPF 嵌入 WinForms/ActiveX 厂商控件时要在 UI 线程创建和释放，退出时必须关闭 Solution、相机流和通信连接。
- 项目加密与软件授权是两套机制：项目密码保护 `.vfsproj`，软件许可证控制应用能否在目标机器运行。
- 密钥签发私钥不得放入客户端；客户端只能包含公钥和验签逻辑。
- 对第三方 SDK 的可选集成应允许“未安装但主程序可启动”，只有使用对应节点时才给出明确错误。
