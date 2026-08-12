# VisionFlow Studio

独立的工业视觉流程平台项目，技术栈为 **WPF + .NET Framework 4.8 + x64**，统一接入 VisionMaster 4.4、VisionPro 7.3 与 HALCON 18.11。

## 为什么采用 WPF

- WPF 使用设备无关单位、Grid 与可伸缩布局，多 DPI 和窗口缩放时不容易出现 WinForms 的控件错位。
- VisionMaster 4.4 官方 C# 示例基于 .NET Framework，进程内接入兼容性高。
- VisionMaster/VisionPro 的 WinForms 控件后续仍可通过 `WindowsFormsHost` 嵌入。
- 流程卡片、时间线、主题与后续节点画布更适合用 WPF 实现。

## 当前功能

- 文件、项目、流程、运行、工具和帮助主菜单。
- 流程 JSON 新建、打开、保存和另存为。
- 节点添加、复制、删除、上移、下移、启用和禁用。
- 节点名称、类型、类别、平台、超时、错误策略和参数表编辑。
- 全部运行、单步运行、从当前节点运行和停止。
- Delay、SetValue、LimitJudge、Log 与 VisionMasterProcedure 通用节点。
- VisionPro ToolBlock 节点：加载 VPP、设置图像输入、运行并读取 OK 输出。
- HALCON Procedure 节点：加载 HDVP、设置 iconic/control 输入并读取结果。
- 元数据驱动的节点目录与选择窗口；节点默认参数由定义生成，不再由界面写死。
- Basler pylon 6、海康 MVS、大华 MVSDK 相机枚举、连接和单帧采集。
- 型号、工位、相机配置的新增、删除、编辑及项目 JSON 持久化。
- 专业图像视窗：滚轮缩放、左键拖动、适应、1:1、实时坐标与灰度。
- 选中图像/相机节点时联动显示其输入或最近采集图像。
- 工具菜单包含相机管理、平台状态和运行数据目录；帮助菜单包含快速入门。
- 响应式三栏工程界面，所有主要区域支持 GridSplitter 调整。
- 检测 VisionMaster 4.4 SDK 安装状态。
- 加载 `.sol` 并读取其中的流程列表。
- 选择流程并运行。
- 可选 BMP/PNG/JPG/TIFF 图像输入，并转为 VisionMaster `ImageBaseData`。
- 校验流程是否发布指定 IMAGE 动态输入。
- 读取指定整型 OK 输出；留空时使用 `VmProcedure.IsRunOK`。
- 返回统一的 OK / NG / Error、VM 原生耗时和错误码。
- 结果写入统一 `VisionContext`，便于后续流程节点复用。

## 项目结构

```text
src/
├─ VisionFlowStudio.Core/          # 平台无关模型与接口
├─ VisionFlowStudio.VisionMaster/  # VisionMaster 4.4 进程内适配器
├─ VisionFlowStudio.VisionPro/     # Cognex VisionPro ToolBlock 适配器
├─ VisionFlowStudio.Halcon/        # HALCON HDevEngine 适配器
├─ VisionFlowStudio.Cameras/       # Basler/Hikrobot/Dahua 相机适配器
└─ VisionFlowStudio.App/           # WPF 主程序
```

## 编译与运行

环境要求：

- Visual Studio 2022 或 .NET SDK
- .NET Framework 4.8 Developer Pack
- VisionMaster 4.4.0，默认安装在 `C:\Program Files\VisionMaster4.4.0`
- x64 运行环境及对应 VisionMaster 授权

```powershell
dotnet build VisionFlowStudio.sln -c Debug
dotnet run --project src\VisionFlowStudio.App\VisionFlowStudio.App.csproj
```

不启动 UI 的 SDK/Solution 自检：

```powershell
dotnet run --project tests\VisionFlowStudio.SmokeTests -- VisionPrograms\2DInspection.sol
```

首次测试：

1. 选择一个 `.sol` 文件并点击“加载 Solution”。
2. 选择 Solution 内的流程。
3. 如流程需要外部图像，选择测试图像并填写已发布的动态输入名。
4. 填写整型 OK 输出名，例如 `IsOK`；如果流程没有该输出，可以留空。
5. 点击“运行流程”，查看统一结果、耗时和 VM 错误码。

## 下一步

1. 加入 VisionMaster 原生流程编辑控件 `VmMainViewConfigControl`。
2. 将 VisionMaster 调用封装成正式 `IFlowNode`。
3. 完成项目/型号/工位 JSON 配置。
4. 接入海康相机、PLC 与生产运行界面。
