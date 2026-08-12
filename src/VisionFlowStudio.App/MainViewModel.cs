using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionFlowStudio.Core;
using VisionFlowStudio.Cameras;
using VisionFlowStudio.Communications;
using VisionFlowStudio.Scripting;
using System.Windows.Media;

namespace VisionFlowStudio.App
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly IVisionMasterAdapter _visionMaster;
        private readonly IVisionProAdapter _visionPro;
        private readonly IHalconAdapter _halcon;
        private readonly CameraRegistry _cameras;
        private readonly CommunicationRegistry _communications;
        private readonly CSharpScriptEngine _scriptEngine = new CSharpScriptEngine();
        private FlowNodeViewModel _selectedNode;
        private CommunicationWriteItemViewModel _selectedCommunicationWrite;
        private CommunicationFieldExtractionViewModel _selectedTriggerField;
        private ImageViewDocumentViewModel _selectedImageDocument;
        private bool _loadingCommunicationWrites;
        private bool _refreshingRouteChoices;
        private CancellationTokenSource _runCancellation;
        private string _projectName = "VisionFlowStudio";
        private string _recipeName = "Model_A";
        private string _stationName = "Station_01";
        private string _flowName = "MainFlow";
        private string _currentFlowId = "MainFlow";
        private string _currentProjectPath;
        private string _currentFlowPath;
        private string _currentTimeText;
        private string _solutionPath;
        private string _solutionPassword = string.Empty;
        private string _selectedProcedure = "流程1";
        private string _imagePath = string.Empty;
        private string _imageInputName = "InputImage";
        private string _okOutputName = "IsOK";
        private string _visionProToolBlockPath = string.Empty;
        private string _visionProImageInputName = "InputImage";
        private string _visionProOkOutputName = "IsOK";
        private string _halconProcedurePath = string.Empty;
        private string _halconImageInputName = "Image";
        private string _halconOkOutputName = "IsOK";
        private string _selectedImageSourceKey = "CameraImagePath";
        private string _communicationChannel = "PLC_01";
        private string _communicationAddress = "DB1.0";
        private string _communicationSourceKey = string.Empty;
        private string _communicationDataType = "Bool";
        private int _continuousIntervalMs = 100;
        private string _triggerChannel = "PLC_01";
        private string _triggerAddress = "DB1.0";
        private string _triggerDataType = "Bool";
        private string _triggerMode = "RisingEdge";
        private string _triggerExpectedValue = "True";
        private string _triggerMatchField = string.Empty;
        private string _recipeSwitchCommandField = string.Empty;
        private string _recipeSwitchCommandValue = "SetMode";
        private string _recipeSwitchValueField = string.Empty;
        private int _triggerPollIntervalMs = 100;
        private bool _isContinuousRunning;
        private bool _isCommunicationTriggerRunning;
        private string _platformMessage;
        private string _runState = "就绪";
        private string _resultState = "READY";
        private string _resultMessage = "等待运行";
        private string _cycleTime = "--";
        private bool _isBusy;
        private BitmapImage _previewImage;
        private readonly DispatcherTimer _clockTimer;

        public MainViewModel(IVisionMasterAdapter visionMaster, IVisionProAdapter visionPro, IHalconAdapter halcon, CameraRegistry cameras, CommunicationRegistry communications)
        {
            _visionMaster = visionMaster;
            _visionPro = visionPro;
            _halcon = halcon;
            _cameras = cameras;
            _communications = communications;
            _communications.RuntimeValueProvider = ResolveCommunicationRuntimeValue;
            Procedures = new ObservableCollection<string>();
            FlowSteps = new ObservableCollection<FlowNodeViewModel>();
            Logs = new ObservableCollection<LogEntryViewModel>();
            Recipes = new ObservableCollection<RecipeDefinition> { new RecipeDefinition() };
            Stations = new ObservableCollection<StationDefinition> { new StationDefinition() };
            StationFlows = new ObservableCollection<StationRecipeFlowDefinition>();
            Cameras = new ObservableCollection<CameraDefinition> { new CameraDefinition() };
            Communications = new ObservableCollection<CommunicationDefinition> { new CommunicationDefinition() };
            CommunicationWrites = new ObservableCollection<CommunicationWriteItemViewModel>();
            CommunicationTriggerFields = new ObservableCollection<CommunicationFieldExtractionViewModel>();
            ImageDocuments = new ObservableCollection<ImageViewDocumentViewModel>();
            ProjectTree = new ObservableCollection<ProjectTreeNodeViewModel>();
            AvailableNodeTypes = new ObservableCollection<string>(NodeCatalog.All.Select(x => x.NodeType));
            AvailablePlatforms = new ObservableCollection<string>(new[] { "Common", "Camera", "VisionMaster", "VisionPro", "HALCON", "Communication", "CSharp" });
            AvailableImageSources = new ObservableCollection<string>(); AvailableDataSources = new ObservableCollection<string>(); AvailableJudgeDataSources = new ObservableCollection<string>();
            AvailableCommunicationChannels = new ObservableCollection<string>(); AvailableCommunicationDataTypes = new ObservableCollection<string>(CommunicationRegistry.DataTypes);
            AvailableTriggerDataTypes = new ObservableCollection<string>(new[] { "Bool", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Float", "Double", "String" });
            AvailableTriggerModes = new ObservableCollection<string>(new[] { "RisingEdge", "ValueEquals", "AnyChange", "TextEquals", "TextContains" });
            AvailableCommunicationFieldModes = new ObservableCollection<string>(new[] { "Delimited", "Position", "JsonPath" });
            AvailableTriggerMatchFields = new ObservableCollection<string>(new[] { string.Empty });

            LoadSolutionCommand = new AsyncRelayCommand(LoadSolutionAsync, CanUseVisionMaster);
            RunCommand = new AsyncRelayCommand(RunVisionMasterAsync, CanRunVisionMaster);
            CloseSolutionCommand = new AsyncRelayCommand(CloseSolutionAsync, CanUseVisionMaster);
            AddNodeCommand = new RelayCommand(AddNode, () => !IsBusy);
            DeleteNodeCommand = new RelayCommand(DeleteNode, HasSelection);
            CopyNodeCommand = new RelayCommand(CopyNode, HasSelection);
            MoveUpCommand = new RelayCommand(() => MoveNode(-1), () => CanMove(-1));
            MoveDownCommand = new RelayCommand(() => MoveNode(1), () => CanMove(1));
            ToggleNodeCommand = new RelayCommand(ToggleNode, HasSelection);
            RunAllCommand = new AsyncRelayCommand(() => RunFlowAsync(0, FlowSteps.Count - 1), CanRunFlow);
            RunContinuousCommand = new AsyncRelayCommand(RunContinuousAsync, CanRunFlow);
            RunCommunicationTriggerCommand = new AsyncRelayCommand(RunCommunicationTriggerAsync, CanRunFlow);
            RunSelectedCommand = new AsyncRelayCommand(RunSelectedAsync, HasSelection);
            RunFromSelectedCommand = new AsyncRelayCommand(RunFromSelectedAsync, HasSelection);
            StopCommand = new RelayCommand(Stop, () => _runCancellation != null);
            CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _clockTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(250) };
            _clockTimer.Tick += delegate
            {
                var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (!string.Equals(CurrentTimeText, now, StringComparison.Ordinal)) CurrentTimeText = now;
            };
            _clockTimer.Start();

            LocalizationService.LanguageChanged += OnLanguageChanged;

            var defaultSolution = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionPrograms", "2DInspection.sol");
            _solutionPath = File.Exists(defaultSolution) ? defaultSolution : string.Empty;
            var defaultVpp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionPrograms", "2DInspection.vpp");
            _visionProToolBlockPath = File.Exists(defaultVpp) ? defaultVpp : string.Empty;
            var defaultHalcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VisionPrograms", "VisionFlowSmoke.hdvp");
            _halconProcedurePath = File.Exists(defaultHalcon) ? defaultHalcon : string.Empty;
            NewFlow();
            NormalizeProjectStructure();
            RefreshImageDocuments();
            CaptureCurrentStationFlow();
            RefreshProjectTree();
            RefreshPlatformStatus();
            AddLog("INFO", "工程已启动。VisionMaster 是流程节点之一，可编辑并组合通用节点。");
        }

        public ObservableCollection<string> Procedures { get; private set; }
        public ObservableCollection<FlowNodeViewModel> FlowSteps { get; private set; }
        public ObservableCollection<LogEntryViewModel> Logs { get; private set; }
        public ObservableCollection<RecipeDefinition> Recipes { get; private set; }
        public ObservableCollection<StationDefinition> Stations { get; private set; }
        public ObservableCollection<StationRecipeFlowDefinition> StationFlows { get; private set; }
        public ObservableCollection<CameraDefinition> Cameras { get; private set; }
        public ObservableCollection<CommunicationDefinition> Communications { get; private set; }
        public ObservableCollection<CommunicationWriteItemViewModel> CommunicationWrites { get; private set; }
        public ObservableCollection<CommunicationFieldExtractionViewModel> CommunicationTriggerFields { get; private set; }
        public ObservableCollection<ImageViewDocumentViewModel> ImageDocuments { get; private set; }
        public ObservableCollection<ProjectTreeNodeViewModel> ProjectTree { get; private set; }
        public CameraRegistry CameraRegistry { get { return _cameras; } }
        public ScriptNodeConfig GetScriptConfig(FlowNodeViewModel node)
        {
            if (node == null) throw new ArgumentNullException("node");
            return new ScriptNodeConfig
            {
                Code = node.Get("Code", string.Empty), ScriptFile = node.Get("ScriptFile", string.Empty),
                References = CSharpScriptEngine.ParseList(node.Get("References", string.Empty)), Imports = CSharpScriptEngine.ParseList(node.Get("Imports", string.Empty)),
                DeclaredOutputs = CSharpScriptEngine.ParseList(node.Get("OutputNames", string.Empty))
            };
        }
        public void SaveScriptConfig(FlowNodeViewModel node, ScriptNodeConfig config)
        {
            if (node == null || config == null) return;
            node.SetParameter("Code", config.Code ?? string.Empty); node.SetParameter("ScriptFile", config.ScriptFile ?? string.Empty);
            node.SetParameter("References", string.Join(";", config.References ?? new List<string>())); node.SetParameter("Imports", string.Join(";", config.Imports ?? new List<string>()));
            node.SetParameter("OutputNames", string.Join(";", config.DeclaredOutputs ?? new List<string>())); RefreshScriptOutputChoices(node, config.DeclaredOutputs);
        }
        private void RefreshScriptOutputChoices(FlowNodeViewModel node, IEnumerable<string> outputs)
        {
            if (node == null || AvailableDataSources == null) return;
            var prefix = node.NodeName + ".";
            var desired = new HashSet<string>((outputs ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => prefix + x.Trim()), StringComparer.OrdinalIgnoreCase);
            foreach (var existing in AvailableDataSources.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !desired.Contains(x)).ToArray()) AvailableDataSources.Remove(existing);
            foreach (var key in desired) if (!AvailableDataSources.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) AvailableDataSources.Add(key);
        }
        public ScriptCompileResult CompileScript(FlowNodeViewModel node, ScriptNodeConfig config) { return _scriptEngine.Compile(config ?? GetScriptConfig(node)); }
        public IReadOnlyList<ScriptCompletionItem> GetScriptCompletions(ScriptNodeConfig config, int position) { return _scriptEngine.GetCompletions(config, position); }
        public ScriptSignatureHelp GetScriptSignatureHelp(ScriptNodeConfig config, int position) { return _scriptEngine.GetSignatureHelp(config, position); }
        public ICameraProvider ConnectCamera(string vendor, string deviceId)
        {
            // VisionMaster keeps the Hikrobot transport layer reserved while a
            // Solution is resident. Camera access owns the transport first; the
            // vision node will load the Solution again when it runs.
            _visionMaster.CloseSolution();
            RefreshPlatformStatus();
            return _cameras.Connect(vendor, deviceId);
        }
        public ObservableCollection<string> AvailableNodeTypes { get; private set; }
        public ObservableCollection<string> AvailablePlatforms { get; private set; }
        public ObservableCollection<string> AvailableImageSources { get; private set; }
        public ObservableCollection<string> AvailableDataSources { get; private set; }
        public ObservableCollection<string> AvailableJudgeDataSources { get; private set; }
        public ObservableCollection<string> AvailableCommunicationChannels { get; private set; }
        public ObservableCollection<string> AvailableCommunicationDataTypes { get; private set; }
        public ObservableCollection<string> AvailableTriggerDataTypes { get; private set; }
        public ObservableCollection<string> AvailableTriggerModes { get; private set; }
        public ObservableCollection<string> AvailableCommunicationFieldModes { get; private set; }
        public ObservableCollection<string> AvailableTriggerMatchFields { get; private set; }
        public CommunicationRegistry CommunicationRegistry { get { return _communications; } }
        public CommunicationWriteItemViewModel SelectedCommunicationWrite { get { return _selectedCommunicationWrite; } set { Set(ref _selectedCommunicationWrite, value); } }
        public CommunicationFieldExtractionViewModel SelectedTriggerField { get { return _selectedTriggerField; } set { Set(ref _selectedTriggerField, value); } }
        public ImageViewDocumentViewModel SelectedImageDocument
        {
            get { return _selectedImageDocument; }
            set
            {
                if (Set(ref _selectedImageDocument, value) && value != null)
                {
                    _previewImage = value.Source;
                    OnPropertyChanged("PreviewImage");
                }
            }
        }
        public AsyncRelayCommand LoadSolutionCommand { get; private set; }
        public AsyncRelayCommand RunCommand { get; private set; }
        public AsyncRelayCommand CloseSolutionCommand { get; private set; }
        public RelayCommand AddNodeCommand { get; private set; }
        public RelayCommand DeleteNodeCommand { get; private set; }
        public RelayCommand CopyNodeCommand { get; private set; }
        public RelayCommand MoveUpCommand { get; private set; }
        public RelayCommand MoveDownCommand { get; private set; }
        public RelayCommand ToggleNodeCommand { get; private set; }
        public AsyncRelayCommand RunAllCommand { get; private set; }
        public AsyncRelayCommand RunContinuousCommand { get; private set; }
        public AsyncRelayCommand RunCommunicationTriggerCommand { get; private set; }
        public AsyncRelayCommand RunSelectedCommand { get; private set; }
        public AsyncRelayCommand RunFromSelectedCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }

        public string ProjectName { get { return _projectName; } set { if (Set(ref _projectName, value)) OnPropertyChanged("ProjectStatusText"); } }
        public string RecipeName { get { return _recipeName; } set { Set(ref _recipeName, value); } }
        public string StationName { get { return _stationName; } set { Set(ref _stationName, value); } }
        public string FlowName { get { return _flowName; } set { Set(ref _flowName, value); } }
        public string CurrentProjectPath { get { return _currentProjectPath; } private set { Set(ref _currentProjectPath, value); } }
        public string CurrentFlowPath { get { return _currentFlowPath; } private set { if (Set(ref _currentFlowPath, value)) OnPropertyChanged("CurrentFlowStatusText"); } }
        public string ProjectStatusText { get { return LocalizationService.T("项目") + (LocalizationService.IsEnglish ? ": " : "：") + ProjectName; } }
        public string CurrentFlowStatusText { get { return string.IsNullOrWhiteSpace(CurrentFlowPath) ? LocalizationService.T("流程尚未保存") : CurrentFlowPath; } }
        public string ImageDocumentCountText { get { return string.Format(LocalizationService.T("共 {0} 个画面"), ImageDocuments == null ? 0 : ImageDocuments.Count); } }

        public FlowNodeViewModel SelectedNode
        {
            get { return _selectedNode; }
            set
            {
                if (Set(ref _selectedNode, value))
                {
                    if (value != null)
                        ApplyNodeToPlatformEditors(value);
                    RaiseCommands();
                }
            }
        }

        public string SolutionPath { get { return _solutionPath; } set { if (Set(ref _solutionPath, value)) { SetSelectedParameterFor("VisionMasterProcedureNode", "SolutionPath", value); RefreshRouteChoices(); RaiseCommands(); } } }
        public string SolutionPassword { get { return _solutionPassword; } set { if (Set(ref _solutionPassword, value)) SetSelectedParameterFor("VisionMasterProcedureNode", "SolutionPassword", value); } }
        public string SelectedProcedure { get { return _selectedProcedure; } set { if (Set(ref _selectedProcedure, value)) { SetSelectedParameterFor("VisionMasterProcedureNode", "ProcedureName", value); RefreshRouteChoices(); RaiseCommands(); } } }
        public string ImagePath { get { return _imagePath; } set { if (Set(ref _imagePath, value)) { SetSelectedVisualParameter("ImagePath", value); LoadPreview(value); } } }
        public string ImageInputName { get { return _imageInputName; } set { if (Set(ref _imageInputName, value)) SetSelectedParameterFor("VisionMasterProcedureNode", "ImageInputName", value); } }
        public string OkOutputName { get { return _okOutputName; } set { if (Set(ref _okOutputName, value)) SetSelectedParameterFor("VisionMasterProcedureNode", "OkOutputName", value); } }
        public string VisionProToolBlockPath { get { return _visionProToolBlockPath; } set { if (Set(ref _visionProToolBlockPath, value)) { SetSelectedParameterFor("VisionProToolBlockNode", "ToolBlockPath", value); RefreshRouteChoices(); } } }
        public string VisionProImageInputName { get { return _visionProImageInputName; } set { if (Set(ref _visionProImageInputName, value)) SetSelectedParameterFor("VisionProToolBlockNode", "ImageInputName", value); } }
        public string VisionProOkOutputName { get { return _visionProOkOutputName; } set { if (Set(ref _visionProOkOutputName, value)) SetSelectedParameterFor("VisionProToolBlockNode", "OkOutputName", value); } }
        public string HalconProcedurePath { get { return _halconProcedurePath; } set { if (Set(ref _halconProcedurePath, value)) { SetSelectedParameterFor("HalconProcedureNode", "ProcedurePath", value); RefreshRouteChoices(); } } }
        public string HalconImageInputName { get { return _halconImageInputName; } set { if (Set(ref _halconImageInputName, value)) SetSelectedParameterFor("HalconProcedureNode", "ImageInputName", value); } }
        public string HalconOkOutputName { get { return _halconOkOutputName; } set { if (Set(ref _halconOkOutputName, value)) SetSelectedParameterFor("HalconProcedureNode", "OkOutputName", value); } }
        public string SelectedImageSourceKey { get { return _selectedImageSourceKey; } set { if (Set(ref _selectedImageSourceKey, value)) SetSelectedVisualParameter("ImageSourceKey", value); } }
        public string CommunicationChannel { get { return _communicationChannel; } set { if (Set(ref _communicationChannel, value)) SetSelectedParameterFor("CommunicationWriteNode", "Channel", value); } }
        public string CommunicationAddress { get { return _communicationAddress; } set { if (Set(ref _communicationAddress, value)) SetSelectedParameterFor("CommunicationWriteNode", "Address", value); } }
        public string CommunicationSourceKey { get { return _communicationSourceKey; } set { if (Set(ref _communicationSourceKey, value)) SetSelectedParameterFor("CommunicationWriteNode", "SourceKey", value); } }
        public string CommunicationDataType { get { return _communicationDataType; } set { if (Set(ref _communicationDataType, value)) SetSelectedParameterFor("CommunicationWriteNode", "DataType", value); } }
        public int ContinuousIntervalMs { get { return _continuousIntervalMs; } set { Set(ref _continuousIntervalMs, Math.Max(0, value)); } }
        public string TriggerChannel
        {
            get { return _triggerChannel; }
            set
            {
                if (!Set(ref _triggerChannel, value)) return;
                RefreshCommunicationTriggerUi(true);
            }
        }
        public string TriggerAddress { get { return _triggerAddress; } set { Set(ref _triggerAddress, value); } }
        public string TriggerDataType { get { return _triggerDataType; } set { Set(ref _triggerDataType, value); } }
        public string TriggerMode { get { return _triggerMode; } set { Set(ref _triggerMode, value); } }
        public string TriggerExpectedValue { get { return _triggerExpectedValue; } set { Set(ref _triggerExpectedValue, value); } }
        public string TriggerMatchField { get { return _triggerMatchField; } set { Set(ref _triggerMatchField, value ?? string.Empty); } }
        public string RecipeSwitchCommandField { get { return _recipeSwitchCommandField; } set { Set(ref _recipeSwitchCommandField, value ?? string.Empty); } }
        public string RecipeSwitchCommandValue { get { return _recipeSwitchCommandValue; } set { Set(ref _recipeSwitchCommandValue, value ?? string.Empty); } }
        public string RecipeSwitchValueField { get { return _recipeSwitchValueField; } set { Set(ref _recipeSwitchValueField, value ?? string.Empty); } }
        public int TriggerPollIntervalMs { get { return _triggerPollIntervalMs; } set { Set(ref _triggerPollIntervalMs, Math.Max(20, value)); } }
        public bool IsTcpTriggerChannel
        {
            get
            {
                var channel = Communications == null ? null : Communications.FirstOrDefault(x => string.Equals(x.Name, TriggerChannel, StringComparison.OrdinalIgnoreCase));
                return channel != null && CommunicationRegistry.IsTcpProtocol(channel.Protocol);
            }
        }
        public bool IsPlcTriggerChannel { get { return !IsTcpTriggerChannel; } }
        public string TriggerAddressLabel { get { return IsTcpTriggerChannel ? "读取地址（TCP不使用）" : "读取地址"; } }
        public string TriggerDataTypeLabel { get { return IsTcpTriggerChannel ? "数据类型（TCP固定文本）" : "数据类型"; } }
        public string TriggerExpectedValueLabel { get { return IsTcpTriggerChannel ? "指定字符串" : "目标值"; } }
        public string TriggerHelpText { get { return IsTcpTriggerChannel ? "每个流程可配置独立的匹配字段和值；启动后，共享 TCP 通道会在当前型号的所有已启用流程中路由。TextEquals：字段完全相等；TextContains：字段包含指定值。" : "RisingEdge：0→非0；ValueEquals：进入目标值时触发；AnyChange：数值变化时触发。"; } }
        public bool IsContinuousRunning { get { return _isContinuousRunning; } private set { Set(ref _isContinuousRunning, value); } }
        public bool IsCommunicationTriggerRunning { get { return _isCommunicationTriggerRunning; } private set { Set(ref _isCommunicationTriggerRunning, value); } }
        public string PlatformMessage { get { return _platformMessage; } private set { if (Set(ref _platformMessage, value)) OnPropertyChanged("PlatformMessageDisplay"); } }
        public string PlatformMessageDisplay { get { return LocalizationService.TDynamic(PlatformMessage); } }
        public string RunState { get { return _runState; } private set { if (Set(ref _runState, value)) OnPropertyChanged("RunStateDisplay"); } }
        public string RunStateDisplay { get { return LocalizationService.TDynamic(RunState); } }
        public string CurrentTimeText { get { return _currentTimeText; } private set { Set(ref _currentTimeText, value); } }
        public string ResultState { get { return _resultState; } private set { Set(ref _resultState, value); } }
        public string ResultMessage { get { return _resultMessage; } private set { if (Set(ref _resultMessage, value)) OnPropertyChanged("ResultMessageDisplay"); } }
        public string ResultMessageDisplay { get { return LocalizationService.TDynamic(ResultMessage); } }
        public string CycleTime { get { return _cycleTime; } private set { Set(ref _cycleTime, value); } }
        public bool IsBusy { get { return _isBusy; } private set { if (Set(ref _isBusy, value)) RaiseCommands(); } }
        public BitmapImage PreviewImage
        {
            get { return _previewImage; }
            private set
            {
                if (Set(ref _previewImage, value) && SelectedImageDocument != null)
                    SelectedImageDocument.Source = value;
            }
        }

        public void NewProject()
        {
            if (_runCancellation != null) _runCancellation.Cancel();
            ProjectName = "VisionFlowStudio";
            RecipeName = "Model_A";
            StationName = "Station_01";
            FlowName = "MainFlow";
            _currentFlowId = "MainFlow";
            CurrentProjectPath = null;
            CurrentFlowPath = null;
            Recipes.Clear(); Recipes.Add(new RecipeDefinition { Name = "Model_A", Enabled = true });
            Stations.Clear(); Stations.Add(new StationDefinition { Name = "Station_01", Enabled = true });
            StationFlows.Clear();
            Cameras.Clear(); Cameras.Add(new CameraDefinition { Name = "Camera_01", RecipeName = string.Empty, StationName = "Station_01" });
            Communications.Clear(); Communications.Add(new CommunicationDefinition());
            CommunicationWrites.Clear();
            Logs.Clear();
            NewFlow();
            NormalizeProjectStructure();
            RefreshImageDocuments();
            CaptureCurrentStationFlow();
            RefreshProjectTree();
            RefreshRouteChoices();
            AddLog("EDIT", "已创建新项目。");
        }

        public void NewFlow()
        {
            FlowSteps.Clear();
            FlowSteps.Add(FlowNodeViewModel.Create("图像准备", "LogNode", "采集", "Common", "Message", "图像输入已准备"));
            FlowSteps.Add(CreateVisionMasterNode());
            FlowSteps.Add(FlowNodeViewModel.Create("结果判定", "LimitJudgeNode", "判定", "Common", "InputKey", "VisionMaster 流程.VisionMasterOK", "Min", "1", "Max", "1"));
            FlowSteps.Add(FlowNodeViewModel.Create("运行日志", "LogNode", "数据", "Common", "Message", "流程执行完成"));
            FlowName = "MainFlow";
            _currentFlowId = "MainFlow";
            ApplyFlowRuntimeSettings(new FlowDocument());
            CurrentFlowPath = null;
            SelectedNode = FlowSteps.FirstOrDefault();
            Renumber();
            CaptureCurrentStationFlow();
            RefreshProjectTree();
            AddLog("EDIT", "已创建新流程。");
        }

        public void NewStationRecipeFlow()
        {
            CaptureCurrentStationFlow();
            if (!Recipes.Any(x => string.Equals(x.Name, RecipeName, StringComparison.OrdinalIgnoreCase)))
            {
                var recipe = Recipes.FirstOrDefault() ?? new RecipeDefinition { Name = "Model_A", Enabled = true };
                if (!Recipes.Contains(recipe)) Recipes.Add(recipe);
                RecipeName = recipe.Name;
            }
            var station = GetActiveStation();
            if (station == null)
            {
                station = new StationDefinition { Name = string.IsNullOrWhiteSpace(StationName) ? "Station_01" : StationName, Enabled = true };
                Stations.Add(station);
            }
            var flowName = NextFlowName(station.Name, RecipeName);
            var document = CreateDefaultFlowDocument(station.Name, RecipeName);
            document.FlowName = flowName;
            StampFlowDocument(document, station.Name, RecipeName, flowName);
            var stationFlow = new StationRecipeFlowDefinition
            {
                StationName = station.Name,
                RecipeName = RecipeName,
                FlowId = CreateUniqueFlowId(station.Name, RecipeName, flowName),
                FlowName = flowName,
                FlowFile = string.Empty,
                Flow = document,
                Enabled = true
            };
            StationFlows.Add(stationFlow);
            ActivateStationFlow(stationFlow, false);
            RefreshProjectTree();
            RefreshImageDocuments();
            AddLog("EDIT", "已新增流程：" + station.Name + " / " + RecipeName + " / " + flowName);
        }

        public void LoadFlow(string path)
        {
            CaptureCurrentStationFlow();
            var hasStationRecipeContext = FlowDocumentStore.HasStationRecipeContext(path);
            var document = FlowDocumentStore.Load(path);
            var stationFlow = ResolveStationFlowForLoadedDocument(document, path, hasStationRecipeContext);
            if (stationFlow != null)
            {
                ApplyLoadedDocumentToStationFlow(stationFlow, document, path);
                RecipeName = stationFlow.RecipeName;
                ActivateStationFlow(stationFlow, false);
            }
            else
            {
                FlowSteps.Clear();
                foreach (var node in (document.Nodes ?? new List<FlowNodeConfig>()).ToArray())
                    FlowSteps.Add(new FlowNodeViewModel(node));
                FlowName = string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName;
                ApplyFlowRuntimeSettings(document);
                CurrentFlowPath = path;
                SelectedNode = FlowSteps.FirstOrDefault();
                Renumber();
                CaptureCurrentStationFlow();
            }
            RefreshProjectTree();
            AddLog("INFO", "已打开流程：" + path);
        }

        public void SaveFlow(string path)
        {
            var document = CreateCurrentFlowDocument();
            FlowDocumentStore.Save(document, path);
            CurrentFlowPath = path;
            CaptureCurrentStationFlow();
            RefreshProjectTree();
            AddLog("INFO", "流程已保存：" + path);
        }

        public void SaveProject(string path, string password, bool automatic = false)
        {
            CaptureCurrentStationFlow();
            NormalizeProjectStructure();
            StampAllStationFlows();
            var document = new ProjectDocument { ProjectName = ProjectName, RecipeName = RecipeName, StationName = StationName, FlowFile = string.Empty, ModifiedTime = DateTime.Now };
            document.Recipes.AddRange(Recipes); document.Stations.AddRange(Stations); document.StationFlows.AddRange(StationFlows); document.Cameras.AddRange(Cameras); document.Communications.AddRange(Communications);
            ProjectDataStore.Save(document, path, password); CurrentProjectPath = path;
            AddLog(automatic ? "AUTO" : "INFO", (automatic ? "项目已自动保存：" : "加密项目已保存：") + path);
        }

        public void ReportApplicationLog(string level, string message) { AddLog(level, message); }

        public void LoadProject(string path, string password)
        {
            var document = ProjectDataStore.Load(path, password); ProjectName = document.ProjectName; CurrentProjectPath = path;
            var savedRecipeName = document.RecipeName;
            var savedStationName = document.StationName;
            Recipes.Clear(); foreach (var item in document.Recipes ?? new List<RecipeDefinition>()) Recipes.Add(item);
            Stations.Clear(); foreach (var item in document.Stations ?? new List<StationDefinition>()) Stations.Add(item);
            StationFlows.Clear(); foreach (var item in document.StationFlows ?? new List<StationRecipeFlowDefinition>()) StationFlows.Add(item);
            Cameras.Clear(); foreach (var item in document.Cameras ?? new List<CameraDefinition>()) Cameras.Add(item);
            Communications.Clear(); foreach (var item in document.Communications ?? new List<CommunicationDefinition>()) Communications.Add(item);
            NormalizeProjectStructure();
            RecipeName = Recipes.Any(x => string.Equals(x.Name, savedRecipeName, StringComparison.OrdinalIgnoreCase))
                ? savedRecipeName
                : (Recipes.FirstOrDefault() == null ? "Model_A" : Recipes.First().Name);
            StationName = Stations.Any(x => string.Equals(x.Name, savedStationName, StringComparison.OrdinalIgnoreCase))
                ? savedStationName
                : (Stations.FirstOrDefault() == null ? "Station_01" : Stations.First().Name);
            StampAllStationFlows();
            var station = Stations.FirstOrDefault(x => string.Equals(x.Name, StationName, StringComparison.OrdinalIgnoreCase)) ?? Stations.FirstOrDefault();
            if (station != null) ActivateStation(station, false);
            else NewFlow();
            RefreshProjectTree(); AddLog("INFO", "加密项目已加载：" + path);
        }

        public void ShowCameraFrame(CameraFrameData frame, string source)
        {
            var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null, frame.BgrPixels, frame.Stride); bitmap.Freeze(); PreviewImage = ToBitmapImage(bitmap);
            ResultState = "CAM"; ResultMessage = string.Format("{0}  {1}×{2}  {3:HH:mm:ss.fff}", source, frame.Width, frame.Height, frame.Timestamp); CycleTime = "--";
            UpdateSelectedImageDocumentState(); AddLog("CAM", ResultMessage);
        }

        public string ResolveDebugImagePath(FlowNodeViewModel visualNode)
        {
            if (visualNode == null) return string.Empty;
            var sourceKey = visualNode.Get("ImageSourceKey", "CameraImagePath");
            var visualIndex = FlowSteps.IndexOf(visualNode);
            var cameras = FlowSteps.Where(x => x.NodeType == "CameraGrabNode" && (visualIndex < 0 || FlowSteps.IndexOf(x) < visualIndex)).Reverse().ToList();
            foreach (var camera in cameras)
            {
                var outputKey = camera.Get("OutputPathKey", "CameraImagePath");
                var matches = string.Equals(sourceKey, outputKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKey, camera.NodeId + "." + outputKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKey, camera.NodeName + "." + outputKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(sourceKey, "ImagePath", StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
                var captured = camera.Get("LastImagePath", string.Empty);
                if (!string.IsNullOrWhiteSpace(captured) && File.Exists(captured)) return captured;
            }
            var direct = visualNode.Get("ImagePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(direct) && File.Exists(direct)) return direct;
            return !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath) ? ImagePath : string.Empty;
        }

        public RecipeDefinition AddRecipe()
        {
            CaptureCurrentStationFlow();
            var recipe = new RecipeDefinition { Name = NextModelName(), Enabled = true };
            Recipes.Add(recipe);
            if (Stations.Count == 0) AddStationForActiveRecipe();
            foreach (var station in Stations.ToArray()) EnsureStationRecipeFlow(station.Name, recipe.Name);
            RecipeName = recipe.Name;
            ActivateStation(Stations.FirstOrDefault(x => string.Equals(x.Name, StationName, StringComparison.OrdinalIgnoreCase)) ?? Stations.FirstOrDefault(), false);
            RefreshProjectTree();
            AddLog("EDIT", "已新增型号 " + recipe.Name + "，并为现有工站创建对应流程。");
            return recipe;
        }

        public StationDefinition AddStationForActiveRecipe()
        {
            if (Recipes.Count == 0) Recipes.Add(new RecipeDefinition { Name = string.IsNullOrWhiteSpace(RecipeName) ? "Model_A" : RecipeName });
            CaptureCurrentStationFlow();
            var index = 1; string name;
            do { name = "Station_" + index.ToString("00"); index++; }
            while (Stations.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
            var station = new StationDefinition { Name = name, Enabled = true };
            Stations.Add(station);
            foreach (var recipe in Recipes.ToArray()) EnsureStationRecipeFlow(station.Name, recipe.Name);
            Cameras.Add(new CameraDefinition { Name = "Camera_01", RecipeName = string.Empty, StationName = station.Name });
            ActivateStation(station, false); RefreshProjectTree();
            AddLog("EDIT", "已新增工站 " + name + "，并为所有型号创建对应流程。");
            return station;
        }

        public CameraDefinition AddCameraForActiveStation()
        {
            var station = GetActiveStation() ?? AddStationForActiveRecipe();
            var index = 1; string name;
            do { name = "Camera_" + index.ToString("00"); index++; }
            while (Cameras.Any(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
            var camera = new CameraDefinition { Name = name, RecipeName = string.Empty, StationName = station.Name };
            Cameras.Add(camera); RefreshImageDocuments(); RefreshProjectTree(); return camera;
        }

        public CommunicationDefinition AddCommunication()
        {
            var index = 1; string name;
            do { name = "PLC_" + index.ToString("00"); index++; } while (Communications.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
            var item = new CommunicationDefinition { Name = name }; Communications.Add(item); RefreshProjectTree(); return item;
        }

        public void RemoveRecipe(RecipeDefinition recipe)
        {
            if (recipe == null || Recipes.Count <= 1) return;
            CaptureCurrentStationFlow();
            foreach (var flow in StationFlows.Where(x => string.Equals(x.RecipeName, recipe.Name, StringComparison.OrdinalIgnoreCase)).ToList()) StationFlows.Remove(flow);
            Recipes.Remove(recipe);
            var nextRecipe = Recipes.FirstOrDefault(); if (nextRecipe != null) RecipeName = nextRecipe.Name;
            var next = Stations.FirstOrDefault(x => string.Equals(x.Name, StationName, StringComparison.OrdinalIgnoreCase)) ?? Stations.FirstOrDefault(); if (next != null) ActivateStation(next, false);
            RefreshProjectTree();
        }

        public void RemoveStation(StationDefinition station)
        {
            if (station == null) return;
            if (Stations.Count <= 1) return;
            CaptureCurrentStationFlow(); RemoveStationCore(station);
            var next = Stations.FirstOrDefault();
            if (next != null) ActivateStation(next, false); RefreshImageDocuments(); RefreshProjectTree();
        }

        public void RemoveCamera(CameraDefinition camera) { if (camera != null) { Cameras.Remove(camera); RefreshImageDocuments(); RefreshProjectTree(); } }
        public void RemoveCommunication(CommunicationDefinition item) { if (item != null) { Communications.Remove(item); RefreshProjectTree(); } }

        public void ActivateRecipe(RecipeDefinition recipe)
        {
            if (recipe == null) return;
            CaptureCurrentStationFlow();
            RecipeName = recipe.Name;
            var station = GetActiveStation() ?? Stations.FirstOrDefault();
            ActivateStation(station, false);
        }

        public void ActivateStation(StationDefinition station) { ActivateStation(station, true); }

        public void ActivateStationRecipe(StationRecipeFlowDefinition stationFlow)
        {
            if (stationFlow == null) return;
            ActivateStationFlow(stationFlow, true);
        }

        public void RenameStationFlow(StationRecipeFlowDefinition stationFlow, string newName)
        {
            if (stationFlow == null) return;
            newName = string.IsNullOrWhiteSpace(newName) ? string.Empty : newName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) throw new InvalidOperationException("流程名称不能为空。");
            CaptureCurrentStationFlow();
            EnsureFlowIdentity(stationFlow);
            if (StationFlows.Any(x => !object.ReferenceEquals(x, stationFlow) &&
                string.Equals(x.StationName, stationFlow.StationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, stationFlow.RecipeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.FlowName, newName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("同一工站/型号下已存在同名流程：" + newName);

            var active = IsActiveStationFlow(stationFlow);
            stationFlow.FlowName = newName;
            if (stationFlow.Flow == null) stationFlow.Flow = CreateDefaultFlowDocument(stationFlow.StationName, stationFlow.RecipeName);
            StampFlowDocument(stationFlow.Flow, stationFlow.StationName, stationFlow.RecipeName, newName);
            if (active) FlowName = newName;
            RefreshImageDocuments();
            RefreshProjectTree();
            SelectActiveFlowImageDocument();
            AddLog("EDIT", "流程已重命名：" + stationFlow.StationName + " / " + stationFlow.RecipeName + " / " + newName);
        }

        public void DeleteStationFlow(StationRecipeFlowDefinition stationFlow)
        {
            if (stationFlow == null) return;
            CaptureCurrentStationFlow();
            EnsureFlowIdentity(stationFlow);
            var siblings = StationFlows.Where(x =>
                string.Equals(x.StationName, stationFlow.StationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, stationFlow.RecipeName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (siblings.Count <= 1) throw new InvalidOperationException("同一工站/型号下至少需要保留一个流程。");

            var stationName = stationFlow.StationName;
            var recipeName = stationFlow.RecipeName;
            var flowName = stationFlow.FlowName;
            var active = IsActiveStationFlow(stationFlow);
            var next = siblings.FirstOrDefault(x => !object.ReferenceEquals(x, stationFlow));
            StationFlows.Remove(stationFlow);
            AddLog("EDIT", "流程已删除：" + stationName + " / " + recipeName + " / " + flowName);
            if (active && next != null) ActivateStationFlow(next, false);
            else
            {
                RefreshImageDocuments();
                RefreshProjectTree();
                SelectActiveFlowImageDocument();
            }
        }
        public void ActivateImageDocument(ImageViewDocumentViewModel document)
        {
            if (document == null || string.IsNullOrWhiteSpace(document.Key)) return;
            var parts = document.Key.Split('|');
            if (parts.Length >= 3 && string.Equals(parts[0], "FLOW", StringComparison.OrdinalIgnoreCase))
            {
                CaptureCurrentStationFlow();
                var stationName = parts[1];
                var recipeName = parts[2];
                var flowId = parts.Length >= 4 ? parts[3] : string.Empty;
                var stationFlow = FindStationFlow(stationName, recipeName, flowId) ?? EnsureStationRecipeFlow(stationName, recipeName);
                ActivateStationFlow(stationFlow, false);
                return;
            }
            if (parts.Length >= 3 && string.Equals(parts[0], "CAMERA", StringComparison.OrdinalIgnoreCase))
            {
                var station = Stations.FirstOrDefault(x => string.Equals(x.Name, parts[1], StringComparison.OrdinalIgnoreCase));
                ActivateStation(station);
            }
        }

        private void ActivateStation(StationDefinition station, bool captureCurrent)
        {
            if (station == null) return;
            if (captureCurrent) CaptureCurrentStationFlow();
            StationName = station.Name;
            if (!Recipes.Any(x => string.Equals(x.Name, RecipeName, StringComparison.OrdinalIgnoreCase)))
            {
                var firstRecipe = Recipes.FirstOrDefault();
                RecipeName = firstRecipe == null ? "Model_A" : firstRecipe.Name;
            }
            var stationFlow = FindStationFlow(station.Name, RecipeName, _currentFlowId)
                ?? StationFlows.FirstOrDefault(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.RecipeName, RecipeName, StringComparison.OrdinalIgnoreCase))
                ?? EnsureStationRecipeFlow(station.Name, RecipeName);
            EnsureFlowIdentity(stationFlow);
            _currentFlowId = stationFlow.FlowId;
            var document = stationFlow.Flow;
            if (document == null || document.Nodes == null || document.Nodes.Count == 0)
                document = stationFlow.Flow = CreateDefaultFlowDocument(station.Name, RecipeName);
            FlowSteps.Clear();
            foreach (var node in document.Nodes) FlowSteps.Add(new FlowNodeViewModel(node));
            FlowName = string.IsNullOrWhiteSpace(stationFlow.FlowName) ? (string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName) : stationFlow.FlowName;
            ApplyFlowRuntimeSettings(document);
            CurrentFlowPath = !string.IsNullOrWhiteSpace(stationFlow.FlowFile) && File.Exists(stationFlow.FlowFile) ? stationFlow.FlowFile : null;
            SelectedNode = FlowSteps.FirstOrDefault(); Renumber(); RefreshProjectTree(); RefreshImageDocuments(); SelectActiveFlowImageDocument();
            AddLog("INFO", "已切换到 " + StationName + " / " + RecipeName + " / " + FlowName);
        }

        private void ActivateStationFlow(StationRecipeFlowDefinition stationFlow, bool captureCurrent)
        {
            if (stationFlow == null) return;
            if (captureCurrent) CaptureCurrentStationFlow();
            EnsureFlowIdentity(stationFlow);
            if (!Stations.Any(x => string.Equals(x.Name, stationFlow.StationName, StringComparison.OrdinalIgnoreCase)))
                Stations.Add(new StationDefinition { Name = stationFlow.StationName, Enabled = true });
            if (!Recipes.Any(x => string.Equals(x.Name, stationFlow.RecipeName, StringComparison.OrdinalIgnoreCase)))
                Recipes.Add(new RecipeDefinition { Name = stationFlow.RecipeName, Enabled = true });
            StationName = stationFlow.StationName;
            RecipeName = stationFlow.RecipeName;
            _currentFlowId = stationFlow.FlowId;
            var document = stationFlow.Flow;
            if (document == null || document.Nodes == null || document.Nodes.Count == 0)
                document = stationFlow.Flow = CreateDefaultFlowDocument(stationFlow.StationName, stationFlow.RecipeName);
            FlowSteps.Clear();
            foreach (var node in document.Nodes) FlowSteps.Add(new FlowNodeViewModel(node));
            FlowName = string.IsNullOrWhiteSpace(stationFlow.FlowName) ? (string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName) : stationFlow.FlowName;
            ApplyFlowRuntimeSettings(document);
            CurrentFlowPath = !string.IsNullOrWhiteSpace(stationFlow.FlowFile) && File.Exists(stationFlow.FlowFile) ? stationFlow.FlowFile : null;
            SelectedNode = FlowSteps.FirstOrDefault();
            Renumber();
            RefreshProjectTree();
            RefreshImageDocuments();
            SelectActiveFlowImageDocument();
            AddLog("INFO", "已切换到 " + StationName + " / " + RecipeName + " / " + FlowName);
        }

        public void CommitProjectStructure()
        {
            CaptureCurrentStationFlow(); NormalizeProjectStructure(); RefreshImageDocuments(); RefreshProjectTree(); RefreshCommunicationTriggerUi(true);
        }

        private void RefreshCommunicationTriggerUi(bool normalizeMode)
        {
            if (normalizeMode)
            {
                if (IsTcpTriggerChannel && !string.Equals(TriggerMode, "TextEquals", StringComparison.OrdinalIgnoreCase) && !string.Equals(TriggerMode, "TextContains", StringComparison.OrdinalIgnoreCase))
                    TriggerMode = "TextEquals";
                else if (!IsTcpTriggerChannel && (string.Equals(TriggerMode, "TextEquals", StringComparison.OrdinalIgnoreCase) || string.Equals(TriggerMode, "TextContains", StringComparison.OrdinalIgnoreCase)))
                    TriggerMode = "ValueEquals";
            }
            OnPropertyChanged("IsTcpTriggerChannel"); OnPropertyChanged("IsPlcTriggerChannel");
            OnPropertyChanged("TriggerAddressLabel"); OnPropertyChanged("TriggerDataTypeLabel"); OnPropertyChanged("TriggerExpectedValueLabel"); OnPropertyChanged("TriggerHelpText");
        }

        public void RefreshProjectTree(bool refreshRouteChoices = true)
        {
            if (ProjectTree == null) return;
            var root = new ProjectTreeNodeViewModel { Header = ProjectName, Kind = "Project", Model = this };
            var recipesGroup = new ProjectTreeNodeViewModel { Header = LocalizationService.IsEnglish ? LocalizationService.T("产品型号组") : "产品型号", Kind = "Recipes", Model = Recipes };
            foreach (var recipe in Recipes)
                recipesGroup.Children.Add(new ProjectTreeNodeViewModel { Header = recipe.Name, Kind = "Recipe", Model = recipe });
            root.Children.Add(recipesGroup);

            var stationsGroup = new ProjectTreeNodeViewModel { Header = LocalizationService.IsEnglish ? LocalizationService.T("工站组") : "工站", Kind = "Stations", Model = Stations };
            foreach (var station in Stations)
            {
                var stationNode = new ProjectTreeNodeViewModel { Header = station.Name, Kind = "Station", Model = station };
                foreach (var camera in Cameras.Where(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase)))
                    stationNode.Children.Add(new ProjectTreeNodeViewModel { Header = camera.Name, Kind = "Camera", Model = camera });
                foreach (var recipe in Recipes)
                {
                    var flows = StationFlows
                        .Where(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase) && string.Equals(x.RecipeName, recipe.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.FlowName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (flows.Count == 0) flows.Add(EnsureStationRecipeFlow(station.Name, recipe.Name));
                    var recipeNode = new ProjectTreeNodeViewModel { Header = recipe.Name, Kind = "StationRecipe", Model = flows.FirstOrDefault() };
                    foreach (var stationFlow in flows)
                    {
                        EnsureFlowIdentity(stationFlow);
                        recipeNode.Children.Add(new ProjectTreeNodeViewModel { Header = string.IsNullOrWhiteSpace(stationFlow.FlowName) ? "MainFlow" : stationFlow.FlowName, Kind = "StationFlow", Model = stationFlow });
                    }
                    stationNode.Children.Add(recipeNode);
                }
                stationsGroup.Children.Add(stationNode);
            }
            root.Children.Add(stationsGroup);
            var communication = new ProjectTreeNodeViewModel { Header = LocalizationService.IsEnglish ? LocalizationService.T("通信配置组") : "通信配置", Kind = "Communications", Model = Communications };
            foreach (var item in Communications) communication.Children.Add(new ProjectTreeNodeViewModel { Header = item.Name + "  [" + item.Protocol + "]", Kind = "Communication", Model = item });
            root.Children.Add(communication);
            ProjectTree.Clear(); ProjectTree.Add(root);
            if (refreshRouteChoices) RefreshRouteChoices();
        }
        public IReadOnlyList<string> LoadVisionMasterForDebug()
        {
            var names = _visionMaster.LoadSolution(SolutionPath, SolutionPassword);
            Procedures.Clear(); foreach (var name in names) Procedures.Add(name);
            if (Procedures.Count > 0 && !Procedures.Contains(SelectedProcedure)) SelectedProcedure = Procedures[0];
            RefreshPlatformStatus(); return names;
        }

        public NodeRunResult RunVisionMasterDebugImage(FlowNodeViewModel node, string procedureName, string imagePath, string imageInputName)
        {
            if (node == null) throw new ArgumentNullException("node");
            return _visionMaster.Run(new VisionMasterRunConfig
            {
                SolutionPath = node.Get("SolutionPath", SolutionPath),
                SolutionPassword = node.Get("SolutionPassword", SolutionPassword),
                ProcedureName = procedureName,
                ImagePath = imagePath,
                ImageInputName = imageInputName,
                OkOutputName = node.Get("OkOutputName", OkOutputName)
            }, new VisionContext { ProjectName = ProjectName, RecipeName = RecipeName, StationName = StationName });
        }

        public Task DebugRunSelectedAsync()
        {
            return SelectedNode == null ? Task.FromResult(0) : RunFlowNodeStandaloneAsync(SelectedNode);
        }

        public void ReloadHalconProcedure(string path)
        {
            _halcon.ReloadProcedure(path); RefreshRouteChoices(true); AddLog("HALCON", "已重新加载 Procedure：" + path);
        }

        public void RefreshVisionOutputChoices()
        {
            RefreshRouteChoices(true); AddLog("VM", "已刷新视觉平台发布输出列表。");
        }

        public void ReloadVisionMasterOutputChoices(FlowNodeViewModel node)
        {
            try
            {
                _visionMaster.CloseSolution();
                if (node != null) _visionMaster.LoadSolution(node.Get("SolutionPath", SolutionPath), node.Get("SolutionPassword", SolutionPassword));
                RefreshRouteChoices(true); AddLog("VM", "Solution 已重新加载，发布输出列表已同步。");
            }
            catch (Exception ex) { AddLog("ERROR", "刷新 VisionMaster 输出失败：" + ex.Message); }
        }

        private StationRecipeFlowDefinition ResolveStationFlowForLoadedDocument(FlowDocument document, string path, bool hasStationRecipeContext)
        {
            var pathMatch = FindStationFlowByPath(path);
            var documentStationName = hasStationRecipeContext && document != null ? document.StationName : null;
            var documentRecipeName = hasStationRecipeContext && document != null ? document.RecipeName : null;
            var stationName = hasStationRecipeContext
                ? FirstNonEmpty(documentStationName, pathMatch == null ? null : pathMatch.StationName, StationName)
                : FirstNonEmpty(pathMatch == null ? null : pathMatch.StationName, StationName);
            var recipeName = hasStationRecipeContext
                ? FirstNonEmpty(documentRecipeName, pathMatch == null ? null : pathMatch.RecipeName, RecipeName)
                : FirstNonEmpty(pathMatch == null ? null : pathMatch.RecipeName, RecipeName);
            if (string.IsNullOrWhiteSpace(stationName) || string.IsNullOrWhiteSpace(recipeName)) return GetActiveStationFlow();

            if (!Recipes.Any(x => string.Equals(x.Name, recipeName, StringComparison.OrdinalIgnoreCase)))
                Recipes.Add(new RecipeDefinition { Name = recipeName, Enabled = true });

            if (!Stations.Any(x => string.Equals(x.Name, stationName, StringComparison.OrdinalIgnoreCase)))
                Stations.Add(new StationDefinition
                {
                    Name = stationName,
                    RecipeName = string.Empty,
                    FlowName = document == null || string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName,
                    FlowFile = path ?? string.Empty,
                    Flow = document ?? CreateDefaultFlowDocument(stationName, recipeName),
                    Enabled = true
                });

            return pathMatch ?? FindStationFlow(stationName, recipeName, _currentFlowId) ?? EnsureStationRecipeFlow(stationName, recipeName);
        }

        private StationRecipeFlowDefinition FindStationFlowByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath;
            try { fullPath = Path.GetFullPath(path); }
            catch { fullPath = path; }

            return StationFlows.FirstOrDefault(x =>
            {
                if (string.IsNullOrWhiteSpace(x.FlowFile)) return false;
                string candidate;
                try { candidate = Path.GetFullPath(x.FlowFile); }
                catch { candidate = x.FlowFile; }
                return string.Equals(candidate, fullPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        private void ApplyLoadedDocumentToStationFlow(StationRecipeFlowDefinition stationFlow, FlowDocument document, string path)
        {
            if (stationFlow == null || document == null) return;
            if (document.Nodes == null) document.Nodes = new List<FlowNodeConfig>();
            StampFlowDocument(document, stationFlow.StationName, stationFlow.RecipeName, document.FlowName);
            stationFlow.Flow = document;
            EnsureFlowIdentity(stationFlow);
            stationFlow.FlowName = string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName;
            if (string.IsNullOrWhiteSpace(stationFlow.FlowId)) stationFlow.FlowId = CreateUniqueFlowId(stationFlow.StationName, stationFlow.RecipeName, stationFlow.FlowName);
            stationFlow.FlowFile = path ?? string.Empty;
        }

        private void StampAllStationFlows()
        {
            foreach (var flow in StationFlows.ToArray()) StampStationFlow(flow);
        }

        private void StampStationFlow(StationRecipeFlowDefinition flow)
        {
            if (flow == null) return;
            EnsureFlowIdentity(flow);
            if (flow.Flow == null || flow.Flow.Nodes == null) flow.Flow = CreateDefaultFlowDocument(flow.StationName, flow.RecipeName);
            var flowName = string.IsNullOrWhiteSpace(flow.FlowName) ? flow.Flow.FlowName : flow.FlowName;
            StampFlowDocument(flow.Flow, flow.StationName, flow.RecipeName, flowName);
            flow.FlowName = string.IsNullOrWhiteSpace(flow.Flow.FlowName) ? "MainFlow" : flow.Flow.FlowName;
        }

        private void StampFlowDocument(FlowDocument document, string stationName, string recipeName, string flowName)
        {
            if (document == null) return;
            document.ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? "VisionFlowStudio" : ProjectName;
            document.StationName = string.IsNullOrWhiteSpace(stationName) ? (string.IsNullOrWhiteSpace(StationName) ? "Station_01" : StationName) : stationName;
            document.RecipeName = string.IsNullOrWhiteSpace(recipeName) ? (string.IsNullOrWhiteSpace(RecipeName) ? "Model_A" : RecipeName) : recipeName;
            document.FlowName = string.IsNullOrWhiteSpace(flowName)
                ? (string.IsNullOrWhiteSpace(document.FlowName) ? "MainFlow" : document.FlowName)
                : flowName;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
                if (!string.IsNullOrWhiteSpace(value)) return value;
            return string.Empty;
        }

        private void CaptureCurrentStationFlow()
        {
            if (StationFlows == null || FlowSteps == null) return;
            var stationFlow = GetActiveStationFlow(); if (stationFlow == null) return;
            EnsureFlowIdentity(stationFlow);
            var document = CreateCurrentFlowDocument();
            stationFlow.FlowName = document.FlowName; stationFlow.Flow = document;
            stationFlow.FlowFile = CurrentFlowPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_currentFlowId)) _currentFlowId = stationFlow.FlowId;
        }

        private FlowDocument CreateDefaultFlowDocument()
        {
            return CreateDefaultFlowDocument(StationName, RecipeName);
        }

        private FlowDocument CreateDefaultFlowDocument(string stationName, string recipeName)
        {
            var document = new FlowDocument { FlowName = "MainFlow" };
            document.Nodes.Add(FlowNodeViewModel.Create("图像准备", "LogNode", "采集", "Common", "Message", "图像输入已准备").ToConfig());
            document.Nodes.Add(CreateVisionMasterNode().ToConfig());
            document.Nodes.Add(FlowNodeViewModel.Create("结果判定", "LimitJudgeNode", "判定", "Common", "InputKey", "VisionMaster 流程.VisionMasterOK", "Min", "1", "Max", "1").ToConfig());
            document.Nodes.Add(FlowNodeViewModel.Create("运行日志", "LogNode", "数据", "Common", "Message", "流程执行完成").ToConfig());
            StampFlowDocument(document, stationName, recipeName, "MainFlow");
            return document;
        }
        private FlowDocument CreateCurrentFlowDocument()
        {
            var document = new FlowDocument
            {
                ProjectName = string.IsNullOrWhiteSpace(ProjectName) ? "VisionFlowStudio" : ProjectName,
                StationName = string.IsNullOrWhiteSpace(StationName) ? "Station_01" : StationName,
                RecipeName = string.IsNullOrWhiteSpace(RecipeName) ? "Model_A" : RecipeName,
                FlowName = string.IsNullOrWhiteSpace(FlowName) ? "MainFlow" : FlowName,
                ContinuousIntervalMs = ContinuousIntervalMs,
                CommunicationTrigger = new CommunicationTriggerDefinition
                {
                    Channel = TriggerChannel ?? string.Empty, Address = TriggerAddress ?? string.Empty, DataType = TriggerDataType ?? "Bool",
                    Mode = TriggerMode ?? "RisingEdge", ExpectedValue = TriggerExpectedValue ?? "True", MatchField = TriggerMatchField ?? string.Empty,
                    RecipeSwitchCommandField = RecipeSwitchCommandField ?? string.Empty, RecipeSwitchCommandValue = RecipeSwitchCommandValue ?? "SetMode", RecipeSwitchValueField = RecipeSwitchValueField ?? string.Empty, PollIntervalMs = TriggerPollIntervalMs,
                    Fields = CommunicationTriggerFields.Select(x => x.ToDefinition()).ToList()
                }
            };
            foreach (var node in FlowSteps.ToArray()) document.Nodes.Add(node.ToConfig());
            return document;
        }
        private void ApplyFlowRuntimeSettings(FlowDocument document)
        {
            if (document == null) return;
            ContinuousIntervalMs = document.ContinuousIntervalMs <= 0 ? 100 : document.ContinuousIntervalMs;
            var trigger = document.CommunicationTrigger ?? new CommunicationTriggerDefinition();
            TriggerChannel = string.IsNullOrWhiteSpace(trigger.Channel) ? (AvailableCommunicationChannels.FirstOrDefault() ?? "PLC_01") : trigger.Channel;
            TriggerAddress = string.IsNullOrWhiteSpace(trigger.Address) ? "DB1.0" : trigger.Address;
            TriggerDataType = string.IsNullOrWhiteSpace(trigger.DataType) ? "Bool" : trigger.DataType;
            TriggerMode = string.IsNullOrWhiteSpace(trigger.Mode) ? "RisingEdge" : trigger.Mode;
            TriggerExpectedValue = trigger.ExpectedValue ?? "True";
            TriggerMatchField = trigger.MatchField ?? string.Empty;
            RecipeSwitchCommandField = trigger.RecipeSwitchCommandField ?? string.Empty;
            RecipeSwitchCommandValue = trigger.RecipeSwitchCommandValue ?? "SetMode";
            RecipeSwitchValueField = trigger.RecipeSwitchValueField ?? string.Empty;
            TriggerPollIntervalMs = trigger.PollIntervalMs <= 0 ? 100 : trigger.PollIntervalMs;
            LoadCommunicationTriggerFields(trigger.Fields);
            RefreshCommunicationTriggerUi(true);
        }

        private string NextFlowName(string stationName, string recipeName)
        {
            var names = new HashSet<string>(StationFlows
                .Where(x => string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) && string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase))
                .Select(x => string.IsNullOrWhiteSpace(x.FlowName) ? "MainFlow" : x.FlowName), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("MainFlow")) return "MainFlow";
            for (var i = 2; i < 10000; i++)
            {
                var candidate = "MainFlow_" + i.ToString("00", CultureInfo.InvariantCulture);
                if (!names.Contains(candidate)) return candidate;
            }
            return "MainFlow_" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        }

        private string CreateUniqueFlowId(string stationName, string recipeName, string preferred)
        {
            var baseId = MakeSafeFlowId(preferred);
            var used = new HashSet<string>(StationFlows
                .Where(x => string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) && string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase))
                .Select(x => MakeSafeFlowId(x.FlowId)), StringComparer.OrdinalIgnoreCase);
            var candidate = baseId;
            for (var i = 2; used.Contains(candidate); i++)
                candidate = baseId + "_" + i.ToString("00", CultureInfo.InvariantCulture);
            return candidate;
        }

        private static string MakeSafeFlowId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "MainFlow";
            var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            var id = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(id) ? "MainFlow" : id;
        }

        private void EnsureFlowIdentity(StationRecipeFlowDefinition flow)
        {
            if (flow == null) return;
            if (string.IsNullOrWhiteSpace(flow.FlowName)) flow.FlowName = flow.Flow == null || string.IsNullOrWhiteSpace(flow.Flow.FlowName) ? "MainFlow" : flow.Flow.FlowName;
            if (string.IsNullOrWhiteSpace(flow.FlowId)) flow.FlowId = MakeSafeFlowId(flow.FlowName);
            else flow.FlowId = MakeSafeFlowId(flow.FlowId);
            if (flow.Flow == null || flow.Flow.Nodes == null) flow.Flow = CreateDefaultFlowDocument(flow.StationName, flow.RecipeName);
        }

        private void EnsureUniqueFlowIdentities()
        {
            foreach (var group in StationFlows.GroupBy(x => (x.StationName ?? string.Empty).ToUpperInvariant() + "|" + (x.RecipeName ?? string.Empty).ToUpperInvariant()))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var flow in group)
                {
                    EnsureFlowIdentity(flow);
                    var baseId = MakeSafeFlowId(flow.FlowId);
                    var candidate = baseId;
                    for (var i = 2; used.Contains(candidate); i++)
                        candidate = baseId + "_" + i.ToString("00", CultureInfo.InvariantCulture);
                    flow.FlowId = candidate;
                    used.Add(candidate);
                }
            }
        }

        private StationRecipeFlowDefinition FindStationFlow(string stationName, string recipeName, string flowId)
        {
            if (StationFlows == null) return null;
            if (string.IsNullOrWhiteSpace(flowId)) return null;
            return StationFlows.FirstOrDefault(x =>
                string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(MakeSafeFlowId(x.FlowId), MakeSafeFlowId(flowId), StringComparison.OrdinalIgnoreCase));
        }

        private bool IsActiveStationFlow(StationRecipeFlowDefinition stationFlow)
        {
            if (stationFlow == null) return false;
            EnsureFlowIdentity(stationFlow);
            return string.Equals(stationFlow.StationName, StationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(stationFlow.RecipeName, RecipeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(MakeSafeFlowId(stationFlow.FlowId), MakeSafeFlowId(_currentFlowId), StringComparison.OrdinalIgnoreCase);
        }

        private StationDefinition GetActiveStation()
        {
            return Stations.FirstOrDefault(x => string.Equals(x.Name, StationName, StringComparison.OrdinalIgnoreCase));
        }

        private StationRecipeFlowDefinition GetActiveStationFlow()
        {
            var station = GetActiveStation();
            if (station == null) return null;
            return FindStationFlow(station.Name, RecipeName, _currentFlowId) ?? EnsureStationRecipeFlow(station.Name, RecipeName);
        }

        private StationRecipeFlowDefinition EnsureStationRecipeFlow(string stationName, string recipeName)
        {
            if (StationFlows == null) return null;
            if (string.IsNullOrWhiteSpace(stationName)) stationName = "Station_01";
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                var firstRecipe = Recipes == null ? null : Recipes.FirstOrDefault();
                recipeName = firstRecipe == null || string.IsNullOrWhiteSpace(firstRecipe.Name) ? "Model_A" : firstRecipe.Name;
            }

            var existing = StationFlows.FirstOrDefault(x =>
                string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                EnsureFlowIdentity(existing);
                return existing;
            }

            var flow = new StationRecipeFlowDefinition
            {
                StationName = stationName,
                RecipeName = recipeName,
                FlowId = CreateUniqueFlowId(stationName, recipeName, "MainFlow"),
                FlowName = "MainFlow",
                FlowFile = string.Empty,
                Flow = CreateDefaultFlowDocument(stationName, recipeName),
                Enabled = true
            };
            StationFlows.Add(flow);
            return flow;
        }

        private void NormalizeProjectStructure()
        {
            if (Recipes.Count == 0) Recipes.Add(new RecipeDefinition());
            if (Stations.Count == 0) Stations.Add(new StationDefinition { Name = "Station_01", Enabled = true });

            foreach (var station in Stations)
            {
                if (string.IsNullOrWhiteSpace(station.Name)) station.Name = "Station_" + (Stations.IndexOf(station) + 1).ToString("00");
                station.RecipeName = string.Empty;
                station.FlowName = "MainFlow";
                station.FlowFile = string.Empty;
                station.Flow = null;
            }

            foreach (var station in Stations.ToArray())
                foreach (var recipe in Recipes.ToArray())
                    EnsureStationRecipeFlow(station.Name, recipe.Name);

            foreach (var flow in StationFlows.Where(x =>
                !Stations.Any(s => string.Equals(s.Name, x.StationName, StringComparison.OrdinalIgnoreCase)) ||
                !Recipes.Any(r => string.Equals(r.Name, x.RecipeName, StringComparison.OrdinalIgnoreCase))).ToArray())
                StationFlows.Remove(flow);

            EnsureUniqueFlowIdentities();

            foreach (var camera in Cameras)
            {
                var station = Stations.FirstOrDefault(x => string.Equals(x.Name, camera.StationName, StringComparison.OrdinalIgnoreCase)) ?? Stations.First();
                camera.RecipeName = string.Empty; camera.StationName = station.Name;
            }
            if (Communications.Count == 0) Communications.Add(new CommunicationDefinition());

            if (!Recipes.Any(x => string.Equals(x.Name, RecipeName, StringComparison.OrdinalIgnoreCase))) RecipeName = Recipes[0].Name;
            if (!Stations.Any(x => string.Equals(x.Name, StationName, StringComparison.OrdinalIgnoreCase))) StationName = Stations[0].Name;
            StampAllStationFlows();
        }

        private void RemoveStationCore(StationDefinition station)
        {
            foreach (var camera in Cameras.Where(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase)).ToList()) Cameras.Remove(camera);
            foreach (var flow in StationFlows.Where(x => string.Equals(x.StationName, station.Name, StringComparison.OrdinalIgnoreCase)).ToList()) StationFlows.Remove(flow);
            Stations.Remove(station);
        }

        private static string FlowDocumentKey(StationRecipeFlowDefinition flow)
        {
            return "FLOW|" + (flow == null ? string.Empty : flow.StationName) + "|" + (flow == null ? string.Empty : flow.RecipeName) + "|" + (flow == null ? string.Empty : MakeSafeFlowId(flow.FlowId));
        }

        private string ActiveFlowDocumentKey()
        {
            var flow = GetActiveStationFlow();
            if (flow != null) return FlowDocumentKey(flow);
            return "FLOW|" + StationName + "|" + RecipeName + "|" + MakeSafeFlowId(_currentFlowId);
        }

        private void RefreshImageDocuments()
        {
            if (ImageDocuments == null) return;

            var desired = new List<Tuple<string, string>>();
            foreach (var flow in StationFlows
                .OrderBy(x => x.StationName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RecipeName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.FlowName, StringComparer.OrdinalIgnoreCase))
            {
                EnsureFlowIdentity(flow);
                var title = string.Format("{0} / {1} / {2}", flow.StationName, flow.RecipeName, string.IsNullOrWhiteSpace(flow.FlowName) ? "MainFlow" : flow.FlowName);
                desired.Add(Tuple.Create(FlowDocumentKey(flow), title));
            }
            foreach (var old in ImageDocuments.Where(x => !desired.Any(d => string.Equals(d.Item1, x.Key, StringComparison.OrdinalIgnoreCase))).ToArray())
                ImageDocuments.Remove(old);

            for (var i = 0; i < desired.Count; i++)
            {
                var key = desired[i].Item1;
                var title = desired[i].Item2;
                var existing = ImageDocuments.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new ImageViewDocumentViewModel { Key = key, Title = title, ResultState = "READY", ResultMessage = "等待运行", CycleTime = "--" };
                    ImageDocuments.Insert(Math.Min(i, ImageDocuments.Count), existing);
                }
                else
                {
                    existing.Title = title;
                    var currentIndex = ImageDocuments.IndexOf(existing);
                    if (currentIndex != i && i < ImageDocuments.Count) ImageDocuments.Move(currentIndex, i);
                }
            }

            var activeKey = ActiveFlowDocumentKey();
            var active = ImageDocuments.FirstOrDefault(x => string.Equals(x.Key, activeKey, StringComparison.OrdinalIgnoreCase));
            if (SelectedImageDocument == null || !ImageDocuments.Contains(SelectedImageDocument))
                SelectedImageDocument = active ?? ImageDocuments.FirstOrDefault();
            OnPropertyChanged("ImageDocumentCountText");
        }

        private void SelectActiveFlowImageDocument()
        {
            if (ImageDocuments == null) return;
            var activeKey = ActiveFlowDocumentKey();
            var active = ImageDocuments.FirstOrDefault(x => string.Equals(x.Key, activeKey, StringComparison.OrdinalIgnoreCase));
            if (active != null) SelectedImageDocument = active;
        }

        private void UpdateSelectedImageDocumentState()
        {
            if (SelectedImageDocument == null) return;
            SelectedImageDocument.ResultState = ResultState;
            SelectedImageDocument.ResultMessage = ResultMessage;
            SelectedImageDocument.CycleTime = CycleTime;
        }

        private string NextModelName()
        {
            for (var c = 'A'; c <= 'Z'; c++) { var candidate = "Model_" + c; if (!Recipes.Any(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate; }
            return "Model_" + (Recipes.Count + 1).ToString("00");
        }

        private void AddNode()
        {
            AddNode("DelayNode");
        }

        public void AddNode(string nodeType)
        {
            var node = new FlowNodeViewModel(NodeCatalog.CreateConfig(nodeType));
            if (node.NodeType == "CommunicationWriteNode") EnsureCommunicationNodeDefaults(node);
            var index = SelectedNode == null ? FlowSteps.Count : FlowSteps.IndexOf(SelectedNode) + 1;
            FlowSteps.Insert(index, node);
            SelectedNode = node;
            Renumber();
        }

        public void AddVisionMasterNode()
        {
            var node = CreateVisionMasterNode();
            FlowSteps.Add(node);
            SelectedNode = node;
            Renumber();
        }

        public void AddVisionProNode()
        {
            var node = FlowNodeViewModel.Create("VisionPro ToolBlock", "VisionProToolBlockNode", "算法", "VisionPro",
                "ToolBlockPath", VisionProToolBlockPath ?? string.Empty, "ImagePath", ImagePath ?? string.Empty,
                "ImageSourceKey", SelectedImageSourceKey, "ImageInputName", VisionProImageInputName, "OkOutputName", VisionProOkOutputName);
            FlowSteps.Add(node); SelectedNode = node; Renumber();
        }

        public void AddHalconNode()
        {
            var node = FlowNodeViewModel.Create("HALCON Procedure", "HalconProcedureNode", "算法", "HALCON",
                "ProcedurePath", HalconProcedurePath ?? string.Empty, "ImagePath", ImagePath ?? string.Empty,
                "ImageSourceKey", SelectedImageSourceKey, "ImageInputName", HalconImageInputName, "OkOutputName", HalconOkOutputName);
            FlowSteps.Add(node); SelectedNode = node; Renumber();
        }

        private FlowNodeViewModel CreateVisionMasterNode()
        {
            return FlowNodeViewModel.Create("VisionMaster 流程", "VisionMasterProcedureNode", "算法", "VisionMaster",
                "SolutionPath", SolutionPath ?? string.Empty, "ProcedureName", SelectedProcedure ?? "流程1",
                "ImagePath", ImagePath ?? string.Empty, "ImageSourceKey", SelectedImageSourceKey, "ImageInputName", ImageInputName,
                "OkOutputName", OkOutputName, "SolutionPassword", string.Empty);
        }

        private void DeleteNode()
        {
            var index = FlowSteps.IndexOf(SelectedNode);
            if (index < 0) return;
            FlowSteps.RemoveAt(index);
            SelectedNode = FlowSteps.Count == 0 ? null : FlowSteps[Math.Min(index, FlowSteps.Count - 1)];
            Renumber();
        }

        private void CopyNode()
        {
            var index = FlowSteps.IndexOf(SelectedNode);
            if (index < 0) return;
            var copy = new FlowNodeViewModel(SelectedNode.ToConfig().Clone());
            FlowSteps.Insert(index + 1, copy);
            SelectedNode = copy;
            Renumber();
        }

        private void MoveNode(int offset)
        {
            var index = FlowSteps.IndexOf(SelectedNode);
            var target = index + offset;
            if (index < 0 || target < 0 || target >= FlowSteps.Count) return;
            FlowSteps.Move(index, target);
            Renumber();
            RaiseCommands();
        }

        private void ToggleNode() { if (SelectedNode != null) SelectedNode.Enabled = !SelectedNode.Enabled; }
        private bool HasSelection() { return !IsBusy && SelectedNode != null; }
        private bool CanMove(int offset) { var i = FlowSteps.IndexOf(SelectedNode); return !IsBusy && i >= 0 && i + offset >= 0 && i + offset < FlowSteps.Count; }
        private bool CanRunFlow() { return !IsBusy && FlowSteps.Count > 0; }

        private async Task RunSelectedAsync() { var i = FlowSteps.IndexOf(SelectedNode); await RunFlowAsync(i, i); }
        private async Task RunFromSelectedAsync() { var i = FlowSteps.IndexOf(SelectedNode); await RunFlowAsync(i, FlowSteps.Count - 1); }

        public async Task RunToNodeForDebugAsync(FlowNodeViewModel node)
        {
            var index = FlowSteps.IndexOf(node);
            if (index < 0) throw new InvalidOperationException("The script node is not part of the current flow.");
            await RunFlowAsync(0, index);
        }

        private async Task RunFlowAsync(int start, int end)
        {
            if (!BeginRun()) return;
            try { await RunFlowCoreAsync(start, end, _runCancellation.Token); }
            catch (OperationCanceledException) { SetStoppedState(); }
            finally { EndRun(); }
        }

        private async Task RunContinuousAsync()
        {
            if (!BeginRun()) return;
            IsContinuousRunning = true;
            try
            {
                while (!_runCancellation.IsCancellationRequested)
                {
                    await RunFlowCoreAsync(0, FlowSteps.Count - 1, _runCancellation.Token);
                    RunState = "连续运行等待中";
                    if (ContinuousIntervalMs > 0) await Task.Delay(ContinuousIntervalMs, _runCancellation.Token);
                }
            }
            catch (OperationCanceledException) { SetStoppedState(); }
            finally { IsContinuousRunning = false; EndRun(); }
        }

        private async Task RunCommunicationTriggerAsync()
        {
            if (!BeginRun()) return;
            IsCommunicationTriggerRunning = true;
            try
            {
                CaptureCurrentStationFlow();
                var channel = Communications.FirstOrDefault(x => string.Equals(x.Name, TriggerChannel, StringComparison.OrdinalIgnoreCase));
                if (channel == null) throw new InvalidOperationException("通信触发通道不存在：" + TriggerChannel);
                if (CommunicationRegistry.IsTcpProtocol(channel.Protocol))
                    await RunTcpFlowDispatcherAsync(_runCancellation.Token);
                else
                    await RunPlcCommunicationTriggerAsync(channel, _runCancellation.Token);
            }
            catch (OperationCanceledException) { SetStoppedState(); }
            catch (Exception ex) { ResultState = "ERROR"; ResultMessage = ex.Message; RunState = "通讯触发失败"; AddLog("ERROR", ex.Message); }
            finally { IsCommunicationTriggerRunning = false; EndRun(); }
        }

        private async Task RunTcpFlowDispatcherAsync(CancellationToken token)
        {
            var routes = GetTcpFlowRoutesForRecipe(RecipeName);
            if (routes.Count == 0) throw new InvalidOperationException("当前型号没有配置可用的 TCP/IP 流程触发路由：" + RecipeName);
            SetTcpDispatcherWaitingState(routes);
            while (!token.IsCancellationRequested)
            {
                routes = GetTcpFlowRoutesForRecipe(RecipeName);
                if (routes.Count == 0) throw new InvalidOperationException("当前型号没有配置可用的 TCP/IP 流程触发路由：" + RecipeName);
                var pollInterval = Math.Max(20, routes.Min(x => x.Flow.Flow.CommunicationTrigger.PollIntervalMs <= 0 ? 100 : x.Flow.Flow.CommunicationTrigger.PollIntervalMs));
                foreach (var routeGroup in routes.GroupBy(x => x.Channel.Name, StringComparer.OrdinalIgnoreCase).ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    var channel = routeGroup.First().Channel;
                    var read = _communications.ReceiveText(channel);
                    if (!read.Success)
                    {
                        RunState = "通讯触发读取失败"; ResultState = "ERROR"; ResultMessage = read.Message;
                        AddLog("ERROR", "通讯触发：" + read.Message);
                        continue;
                    }
                    if (!read.HasValue) continue;
                    await DispatchTcpFrameAsync(channel, read, routeGroup.Select(x => x.Flow).ToArray(), token);
                    if (!token.IsCancellationRequested) SetTcpDispatcherWaitingState(GetTcpFlowRoutesForRecipe(RecipeName));
                }
                await Task.Delay(pollInterval, token);
            }
        }

        private List<TcpFlowRoute> GetTcpFlowRoutesForRecipe(string recipeName)
        {
            var channels = Communications
                .Where(x => x != null && x.Enabled && CommunicationRegistry.IsTcpProtocol(x.Protocol) && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
            return StationFlows
                .Where(x => x != null && x.Enabled && string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase))
                .Where(x => !Stations.Any(s => string.Equals(s.Name, x.StationName, StringComparison.OrdinalIgnoreCase) && !s.Enabled))
                .Select(x => new { Flow = x, Trigger = x.Flow == null ? null : x.Flow.CommunicationTrigger })
                .Where(x => x.Trigger != null && !string.IsNullOrWhiteSpace(x.Trigger.Channel) && channels.ContainsKey(x.Trigger.Channel))
                .Select(x => new TcpFlowRoute { Flow = x.Flow, Channel = channels[x.Trigger.Channel] })
                .ToList();
        }

        private void SetTcpDispatcherWaitingState(IReadOnlyCollection<TcpFlowRoute> routes)
        {
            RunState = "等待通讯触发"; ResultState = "ARMED";
            ResultMessage = string.Format("型号 {0} / {1} 个流程 / {2} 个 TCP 通道待命", RecipeName, routes == null ? 0 : routes.Count, routes == null ? 0 : routes.Select(x => x.Channel.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        private async Task DispatchTcpFrameAsync(CommunicationDefinition channel, CommunicationOperationResult read, IEnumerable<StationRecipeFlowDefinition> flows, CancellationToken token)
        {
            var message = Convert.ToString(read.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            var evaluations = TcpFlowRouteEvaluator.Evaluate(flows, channel, message, read.ConnectionId);
            foreach (var evaluation in evaluations.Where(x => !string.IsNullOrWhiteSpace(x.Error)))
                AddLog("WARN", string.Format("TCP/IP 报文对流程 {0} 提取失败，已跳过：{1}", GetFlowRouteName(evaluation.Flow), evaluation.Error));

            foreach (var evaluation in evaluations.Where(x => string.IsNullOrWhiteSpace(x.Error)))
            {
                var trigger = evaluation.Flow.Flow.CommunicationTrigger;
                if (TryHandleRecipeSwitch(evaluation.TriggerData, trigger)) return;
            }

            var matches = evaluations.Where(x => string.IsNullOrWhiteSpace(x.Error) && x.Matched).ToList();
            if (matches.Count == 0)
            {
                AddLog("INFO", string.Format("{0} 收到报文，但当前型号 {1} 没有匹配流程。", channel.Name, RecipeName));
                return;
            }
            if (matches.Count > 1)
            {
                var names = string.Join("、", matches.Select(x => GetFlowRouteName(x.Flow)));
                ResultState = "ERROR"; RunState = "通讯路由冲突"; ResultMessage = "一条报文匹配到多个流程：" + names;
                AddLog("ERROR", ResultMessage + "。请为各流程设置不同的匹配字段/指定值，本次未执行。");
                return;
            }

            var selected = matches[0];
            ActivateStationFlow(selected.Flow, true);
            AddLog("TRIGGER", string.Format("{0} 收到触发，字段值 \"{1}\"，启动 {2}", channel.Name, selected.MatchValue, GetFlowRouteName(selected.Flow)));
            await RunFlowCoreAsync(0, FlowSteps.Count - 1, token, selected.TriggerData);
        }

        private async Task RunPlcCommunicationTriggerAsync(CommunicationDefinition channel, CancellationToken token)
        {
            object previous = null; var hasPrevious = false; var triggerLatched = false;
            RunState = "等待通讯触发"; ResultState = "ARMED"; ResultMessage = TriggerChannel + " / " + TriggerAddress;
            while (!token.IsCancellationRequested)
            {
                var read = _communications.Read(channel, TriggerAddress, TriggerDataType);
                if (!read.Success)
                {
                    RunState = "通讯触发读取失败"; ResultState = "ERROR"; ResultMessage = read.Message;
                    AddLog("ERROR", "通讯触发：" + read.Message);
                    await Task.Delay(Math.Max(TriggerPollIntervalMs, 200), token); continue;
                }
                if (!read.HasValue) { await Task.Delay(TriggerPollIntervalMs, token); continue; }
                var matched = IsTriggerMatched(read.Value, previous, hasPrevious, ref triggerLatched);
                previous = read.Value; hasPrevious = true;
                if (matched)
                {
                    AddLog("TRIGGER", string.Format("{0}.{1}={2}，启动流程 {3}", TriggerChannel, TriggerAddress, Convert.ToString(read.Value, CultureInfo.InvariantCulture), FlowName));
                    await RunFlowCoreAsync(0, FlowSteps.Count - 1, token);
                    RunState = "等待通讯触发";
                }
                await Task.Delay(TriggerPollIntervalMs, token);
            }
        }

        private bool TryHandleRecipeSwitch(IDictionary<string, object> data, CommunicationTriggerDefinition trigger)
        {
            if (data == null || trigger == null || string.IsNullOrWhiteSpace(trigger.RecipeSwitchCommandField) || string.IsNullOrWhiteSpace(trigger.RecipeSwitchValueField)) return false;
            object command; object requested;
            if (!data.TryGetValue("CommunicationTrigger." + trigger.RecipeSwitchCommandField.Trim(), out command) || !string.Equals(Convert.ToString(command, CultureInfo.InvariantCulture), trigger.RecipeSwitchCommandValue ?? string.Empty, StringComparison.Ordinal)) return false;
            if (!data.TryGetValue("CommunicationTrigger." + trigger.RecipeSwitchValueField.Trim(), out requested)) throw new InvalidOperationException("配方切换字段不存在：" + trigger.RecipeSwitchValueField);
            var value = Convert.ToString(requested, CultureInfo.InvariantCulture) ?? string.Empty;
            RecipeDefinition target = Recipes.FirstOrDefault(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase) || string.Equals(x.ProductCode, value, StringComparison.OrdinalIgnoreCase));
            int index;
            if (target == null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 0 && index < Recipes.Count) target = Recipes[index];
            if (target == null) throw new InvalidOperationException("找不到配方/型号：" + value);
            ActivateRecipe(target); AddLog("MODE", string.Format("收到 {0}，已切换配方/型号为 {1}", trigger.RecipeSwitchCommandValue, target.Name)); return true;
        }

        private static string GetFlowRouteName(StationRecipeFlowDefinition flow)
        {
            return flow == null ? "<未知流程>" : string.Format("{0} / {1} / {2}", flow.StationName, flow.RecipeName, flow.FlowName);
        }

        private sealed class TcpFlowRoute
        {
            public StationRecipeFlowDefinition Flow { get; set; }
            public CommunicationDefinition Channel { get; set; }
        }

        private bool IsTriggerMatched(object current, object previous, bool hasPrevious, ref bool latched)
        {
            if (string.Equals(TriggerMode, "AnyChange", StringComparison.OrdinalIgnoreCase)) return hasPrevious && !ValuesEqual(current, previous, TriggerDataType);
            var active = string.Equals(TriggerMode, "RisingEdge", StringComparison.OrdinalIgnoreCase) ? IsNonZero(current, TriggerDataType) : ValuesEqual(current, TriggerExpectedValue, TriggerDataType);
            var fire = active && !latched; latched = active; return fire;
        }
        private static bool IsNonZero(object value, string dataType)
        {
            if (value == null) return false; bool boolean;
            if (string.Equals(dataType, "Bool", StringComparison.OrdinalIgnoreCase) && bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out boolean)) return boolean;
            double number; return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out number) && Math.Abs(number) > double.Epsilon;
        }
        private static bool ValuesEqual(object left, object right, string dataType)
        {
            if (string.Equals(dataType, "Bool", StringComparison.OrdinalIgnoreCase))
            {
                bool a; bool b; return bool.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), out a) && bool.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), out b) && a == b;
            }
            if (string.Equals(dataType, "String", StringComparison.OrdinalIgnoreCase)) return string.Equals(Convert.ToString(left, CultureInfo.InvariantCulture), Convert.ToString(right, CultureInfo.InvariantCulture), StringComparison.Ordinal);
            double x; double y; return double.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out x) && double.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out y) && Math.Abs(x - y) < 0.0000001;
        }

        private bool BeginRun()
        {
            if (_runCancellation != null || FlowSteps.Count == 0) return false;
            IsBusy = true; _runCancellation = new CancellationTokenSource(); RaiseCommands(); return true;
        }
        private void EndRun()
        {
            if (_runCancellation != null) { _runCancellation.Dispose(); _runCancellation = null; }
            IsBusy = false; RaiseCommands();
        }
        private void SetStoppedState() { ResultState = "STOP"; ResultMessage = "流程已停止"; RunState = "已停止"; UpdateSelectedImageDocumentState(); }

        private async Task RunFlowCoreAsync(int start, int end, CancellationToken token, IDictionary<string, object> initialData = null)
        {
            if (start < 0 || end < start || end >= FlowSteps.Count) return;
            RunState = "流程运行中"; ResultState = "RUN";
            var total = Stopwatch.StartNew(); var context = new VisionContext { ProjectName = ProjectName, RecipeName = RecipeName, StationName = StationName }; var finalOk = true;
            if (initialData != null) foreach (var pair in initialData) context.Set(pair.Key, pair.Value);
            for (var i = start; i <= end; i++)
            {
                token.ThrowIfCancellationRequested(); var node = FlowSteps[i];
                if (!node.Enabled) { node.ApplyResult(new NodeRunResult { Status = NodeRunStatus.Skipped, Message = "节点已禁用" }); continue; }
                string skipReason;
                if (!ShouldRunNode(node, context, out skipReason)) { node.ApplyResult(new NodeRunResult { Status = NodeRunStatus.Skipped, Message = skipReason }); continue; }
                node.Status = "Running"; var result = await ExecuteNodeAsync(node, context, token); node.ApplyResult(result);
                AddLog(result.Status.ToString().ToUpperInvariant(), node.NodeName + "：" + result.Message);
                if (result.Status == NodeRunStatus.Ng) finalOk = false;
                if (result.Status == NodeRunStatus.Error) { finalOk = false; if (string.Equals(node.OnError, "StopFlow", StringComparison.OrdinalIgnoreCase)) break; }
            }
            total.Stop(); SelectActiveFlowImageDocument(); ResultState = finalOk ? "OK" : "NG"; ResultMessage = finalOk ? "流程执行完成：OK" : "流程执行完成：NG / Error";
            CycleTime = total.Elapsed.TotalMilliseconds.ToString("0.0 ms"); RunState = "运行完成"; UpdateSelectedImageDocumentState();
        }

        private async Task<NodeRunResult> ExecuteNodeAsync(FlowNodeViewModel node, VisionContext context, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                foreach (var parameter in node.Parameters.Where(x => !string.IsNullOrWhiteSpace(x.Key))) context.Set(node.NodeName + ".Input." + parameter.Key, parameter.Value);
                NodeRunResult result;
                switch (node.NodeType)
                {
                    case "CameraGrabNode":
                        var vendor = node.Get("Vendor", "Hikrobot");
                        var provider = ConnectCamera(vendor, node.Get("DeviceId", string.Empty));
                        var cameraSettings = new CameraSettings
                        {
                            ExposureUs = node.GetDouble("ExposureUs", 10000), Gain = node.GetDouble("Gain", 0),
                            TriggerMode = node.Get("TriggerMode", "Off"), TriggerSource = node.Get("TriggerSource", "Software"),
                            PixelFormat = node.Get("PixelFormat", "Mono8"), FrameRateEnabled = node.GetBool("FrameRateEnabled", false),
                            FrameRate = node.GetDouble("FrameRate", 10), UserSet = node.Get("UserSet", "UserSet1")
                        };
                        provider.ApplySettings(cameraSettings);
                        var hardwareTrigger = string.Equals(cameraSettings.TriggerMode, "On", StringComparison.OrdinalIgnoreCase) && !string.Equals(cameraSettings.TriggerSource, "Software", StringComparison.OrdinalIgnoreCase);
                        var captureTimeout = node.GetInt("TimeoutMs", hardwareTrigger ? 30000 : 3000);
                        if (hardwareTrigger) node.Message = string.Format("{0} 等待硬件触发({1})，超时 {2} ms", vendor, cameraSettings.TriggerSource, captureTimeout);
                        var frame = provider.Acquire(captureTimeout);
                        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null, frame.BgrPixels, frame.Stride);
                        bitmap.Freeze(); SelectActiveFlowImageDocument(); PreviewImage = ToBitmapImage(bitmap);
                        var capturedPath = SaveCapturedFrame(bitmap, vendor);
                        node.SetParameter("LastImagePath", capturedPath);
                        var outputImageKey = node.Get("OutputImageKey", "CameraImage"); var outputPathKey = node.Get("OutputPathKey", "CameraImagePath");
                        var exposureTimeUtc = frame.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                        context.Set(outputImageKey, frame); context.Set(outputPathKey, capturedPath); context.Set("CameraFrame", frame); context.Set("ImagePath", capturedPath); context.Set("CameraExposureTime", frame.Timestamp); context.Set("CameraExposureTimeUtc", exposureTimeUtc);
                        result = Ok(string.Format("{0} 采图完成{3}，{1}×{2}", vendor, frame.Width, frame.Height, hardwareTrigger ? "（硬触发）" : string.Empty));
                        result.Outputs["ImagePath"] = capturedPath; result.Outputs[outputPathKey] = capturedPath; result.Outputs[outputImageKey] = frame; result.Outputs["ExposureTime"] = frame.Timestamp; result.Outputs["ExposureTimeUtc"] = exposureTimeUtc;
                        break;
                    case "DelayNode":
                        var delay = node.GetInt("DelayMs", 100);
                        await Task.Delay(delay, token);
                        result = Ok("等待 " + delay + " ms");
                        break;
                    case "SetValueNode":
                        var key = node.Get("Key", "Value");
                        var value = node.Get("Value", "0");
                        double number;
                        context.Set(key, double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? (object)number : value);
                        result = Ok("写入 " + key + " = " + value);
                        break;
                    case "LimitJudgeNode":
                        var inputKey = node.Get("InputKey", "Value");
                        var input = context.Get<double>(inputKey, 0);
                        var min = node.GetDouble("Min", 0);
                        var max = node.GetDouble("Max", 1);
                        result = new NodeRunResult { Status = input >= min && input <= max ? NodeRunStatus.Ok : NodeRunStatus.Ng, Message = string.Format("{0}={1:0.###}，规格 [{2}, {3}]", inputKey, input, min, max) };
                        break;
                    case "VisionMasterProcedureNode":
                        var vmImage = ResolveImagePath(node, context, ImagePath);
                        result = _visionMaster.Run(new VisionMasterRunConfig
                        {
                            SolutionPath = node.Get("SolutionPath", SolutionPath), SolutionPassword = node.Get("SolutionPassword", string.Empty),
                            ProcedureName = node.Get("ProcedureName", SelectedProcedure), ImagePath = vmImage,
                            ImageInputName = node.Get("ImageInputName", ImageInputName), OkOutputName = node.Get("OkOutputName", OkOutputName)
                        }, context);
                        break;
                    case "VisionProToolBlockNode":
                        var vpImage = ResolveImagePath(node, context, ImagePath);
                        result = _visionPro.Run(new VisionProRunConfig
                        {
                            ToolBlockPath = node.Get("ToolBlockPath", VisionProToolBlockPath),
                            ImagePath = vpImage,
                            ImageInputName = node.Get("ImageInputName", VisionProImageInputName),
                            OkOutputName = node.Get("OkOutputName", VisionProOkOutputName)
                        }, context);
                        break;
                    case "HalconProcedureNode":
                        var haImage = ResolveImagePath(node, context, ImagePath);
                        var halconConfig = new HalconRunConfig
                        {
                            ProcedurePath = node.Get("ProcedurePath", HalconProcedurePath),
                            ImagePath = haImage,
                            ImageInputName = node.Get("ImageInputName", HalconImageInputName),
                            OkOutputName = node.Get("OkOutputName", HalconOkOutputName)
                        };
                        foreach (var parameter in node.Parameters.Where(p => p.Key != null && p.Key.StartsWith("Input.", StringComparison.OrdinalIgnoreCase)))
                            halconConfig.ControlInputs[parameter.Key.Substring(6)] = parameter.Value;
                        result = _halcon.Run(halconConfig, context);
                        break;
                    case "CommunicationWriteNode":
                        var channelName = node.Get("Channel", CommunicationChannel); var channel = Communications.FirstOrDefault(x => string.Equals(x.Name, channelName, StringComparison.OrdinalIgnoreCase));
                        if (channel == null) throw new InvalidOperationException("通信通道不存在：" + channelName);
                        var writeMessages = new List<string>(); var allWritesOk = true; var writeIndex = 0; var tcpFields = new List<CommunicationTextField>(); var jsonFields = new List<CommunicationJsonField>();
                        var tcpJson = CommunicationRegistry.IsTcpProtocol(channel.Protocol) && string.Equals(channel.PayloadFormat, "Json", StringComparison.OrdinalIgnoreCase);
                        var connectionId = Convert.ToString(context.GetValue("CommunicationTrigger.ConnectionId"), CultureInfo.InvariantCulture);
                        foreach (var mapping in ReadCommunicationWrites(node).Where(x => x.Enabled))
                        {
                            writeIndex++;
                            var sourceValue = ResolveCommunicationWriteValue(context, mapping);
                            if (sourceValue == null)
                            {
                                allWritesOk = false; writeMessages.Add(string.Format("#{0} 流程数据不存在：{1}", writeIndex, mapping.SourceKey)); continue;
                            }
                            if (CommunicationRegistry.IsTcpProtocol(channel.Protocol))
                            {
                                if (tcpJson) jsonFields.Add(new CommunicationJsonField { Path = mapping.Address, DataType = mapping.DataType, Value = sourceValue });
                                else tcpFields.Add(new CommunicationTextField { Template = mapping.Address, DataType = mapping.DataType, Value = sourceValue });
                            }
                            else
                            {
                                var writeResult = _communications.Write(channel, mapping.Address, mapping.DataType, sourceValue);
                                if (!writeResult.Success) allWritesOk = false;
                                writeMessages.Add("#" + writeIndex + " " + writeResult.Message);
                            }
                        }
                        if (writeIndex == 0) throw new InvalidOperationException("通讯节点没有启用的写入映射");
                        if (CommunicationRegistry.IsTcpProtocol(channel.Protocol) && allWritesOk)
                        {
                            var writeResult = tcpJson ? _communications.WriteJson(channel, jsonFields, connectionId) : _communications.WriteCombined(channel, tcpFields, connectionId);
                            if (!writeResult.Success) allWritesOk = false;
                            writeMessages.Add(writeResult.Message);
                        }
                        result = new NodeRunResult { Status = allWritesOk ? NodeRunStatus.Ok : NodeRunStatus.Error, Message = string.Join(" | ", writeMessages) };
                        break;
                    case "CSharpScriptNode":
                        var scriptConfig = GetScriptConfig(node);
                        var scriptGlobals = new ScriptGlobals(context, CreateScriptToolSnapshots(context), token);
                        result = await _scriptEngine.RunAsync(scriptConfig, scriptGlobals, token);
                        break;
                    default:
                        result = Ok(node.Get("Message", "节点执行完成"));
                        break;
                }
                watch.Stop();
                if (result.CostMs <= 0) result.CostMs = watch.Elapsed.TotalMilliseconds;
                foreach (var output in result.Outputs)
                {
                    context.Set(output.Key, output.Value); context.Set(node.NodeId + "." + output.Key, output.Value); context.Set(node.NodeName + "." + output.Key, output.Value);
                    AddDataSourceChoice(node, output.Key);
                }
                return result;
            }
            catch (Exception ex)
            {
                watch.Stop();
                return new NodeRunResult { Status = NodeRunStatus.Error, Message = ex.Message, CostMs = watch.Elapsed.TotalMilliseconds };
            }
        }

        private static NodeRunResult Ok(string message) { return new NodeRunResult { Status = NodeRunStatus.Ok, Message = message }; }
        private static bool ShouldRunNode(FlowNodeViewModel node, VisionContext context, out string reason)
        {
            reason = string.Empty;
            var key = node.Get("RunWhenKey", string.Empty);
            if (string.IsNullOrWhiteSpace(key)) return true;
            var current = ResolveCommunicationSource(context, key);
            var mode = node.Get("RunWhenMode", "Equals");
            var expected = node.Get("RunWhenValue", string.Empty);
            var actual = current == null ? string.Empty : Convert.ToString(current, CultureInfo.InvariantCulture);
            bool run;
            if (string.Equals(mode, "Exists", StringComparison.OrdinalIgnoreCase)) run = current != null;
            else if (string.Equals(mode, "NotExists", StringComparison.OrdinalIgnoreCase)) run = current == null;
            else if (string.Equals(mode, "NotEquals", StringComparison.OrdinalIgnoreCase)) run = !string.Equals(actual, expected, StringComparison.Ordinal);
            else if (string.Equals(mode, "EqualsIgnoreCase", StringComparison.OrdinalIgnoreCase)) run = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            else if (string.Equals(mode, "Contains", StringComparison.OrdinalIgnoreCase)) run = actual.IndexOf(expected ?? string.Empty, StringComparison.Ordinal) >= 0;
            else if (string.Equals(mode, "NotContains", StringComparison.OrdinalIgnoreCase)) run = actual.IndexOf(expected ?? string.Empty, StringComparison.Ordinal) < 0;
            else run = string.Equals(actual, expected, StringComparison.Ordinal);
            if (!run) reason = string.Format("条件未满足：{0} {1} {2}（当前 {3}）", key, mode, expected, current == null ? "<不存在>" : actual);
            return run;
        }
        private IEnumerable<ScriptToolSnapshot> CreateScriptToolSnapshots(VisionContext context)
        {
            foreach (var flowNode in FlowSteps)
            {
                var snapshot = new ScriptToolSnapshot { Name = flowNode.NodeName, NodeId = flowNode.NodeId, NodeType = flowNode.NodeType, Platform = flowNode.Platform };
                foreach (var input in flowNode.Parameters.Where(x => !string.IsNullOrWhiteSpace(x.Key))) snapshot.Inputs[input.Key] = input.Value;
                foreach (var pair in context.Data)
                {
                    var namePrefix = flowNode.NodeName + "."; var idPrefix = flowNode.NodeId + "."; string outputName = null;
                    if (pair.Key.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)) outputName = pair.Key.Substring(namePrefix.Length);
                    else if (pair.Key.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase)) outputName = pair.Key.Substring(idPrefix.Length);
                    if (!string.IsNullOrWhiteSpace(outputName) && !outputName.StartsWith("Input.", StringComparison.OrdinalIgnoreCase)) snapshot.Outputs[outputName] = pair.Value;
                }
                yield return snapshot;
            }
            var communication = new ScriptToolSnapshot { Name = "CommunicationTrigger", NodeId = "CommunicationTrigger", NodeType = "CommunicationTrigger", Platform = "Communication" };
            foreach (var pair in context.Data.Where(x => x.Key.StartsWith("CommunicationTrigger.", StringComparison.OrdinalIgnoreCase)))
                communication.Outputs[pair.Key.Substring("CommunicationTrigger.".Length)] = pair.Value;
            yield return communication;
        }
        private static object ResolveCommunicationSource(VisionContext context, string sourceKey)
        {
            var value = context.GetValue(sourceKey);
            if (value != null) return value;
            var normalized = NormalizeDataSourceKey(sourceKey);
            return string.Equals(normalized, sourceKey, StringComparison.Ordinal) ? null : context.GetValue(normalized);
        }
        private static object ResolveCommunicationWriteValue(VisionContext context, CommunicationWriteItemViewModel mapping)
        {
            if (mapping == null) return null;
            if (mapping.UseConstant) return mapping.ConstantValue ?? string.Empty;
            if (string.Equals(mapping.SourceKey, "$Now", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            if (string.Equals(mapping.SourceKey, "$UtcNow", StringComparison.OrdinalIgnoreCase)) return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            return ResolveCommunicationSource(context, mapping.SourceKey);
        }
        private object ResolveCommunicationRuntimeValue(string key)
        {
            var name = (key ?? string.Empty).Trim();
            if (string.Equals(name, "Ready", StringComparison.OrdinalIgnoreCase)) return _runCancellation == null;
            if (string.Equals(name, "Busy", StringComparison.OrdinalIgnoreCase)) return _runCancellation != null;
            if (string.Equals(name, "ProjectName", StringComparison.OrdinalIgnoreCase)) return ProjectName;
            if (string.Equals(name, "StationName", StringComparison.OrdinalIgnoreCase)) return StationName;
            if (string.Equals(name, "RecipeName", StringComparison.OrdinalIgnoreCase)) return RecipeName;
            if (string.Equals(name, "FlowName", StringComparison.OrdinalIgnoreCase)) return FlowName;
            if (string.Equals(name, "ResultState", StringComparison.OrdinalIgnoreCase)) return ResultState;
            return null;
        }
        private static string NormalizeDataSourceKey(string sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey)) return string.Empty;
            return string.Join(".", sourceKey.Split('.').Select(x => x.Trim().Trim('%')));
        }
        private static string ResolveImagePath(FlowNodeViewModel node, VisionContext context, string fallback)
        {
            // During a complete flow run the explicitly selected upstream image wins.
            // A node-local test image remains a fallback so the visual node can still be
            // debugged independently when no camera node has run in the current context.
            var sourceKey = node.Get("ImageSourceKey", "CameraImagePath");
            var routed = context.Get<string>(sourceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(routed)) return routed;
            var direct = node.Get("ImagePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
            return context.Get<string>("ImagePath", string.Empty);
        }
        private static BitmapImage ToBitmapImage(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new MemoryStream()) { encoder.Save(stream); stream.Position = 0; var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
        }
        private static string SaveCapturedFrame(BitmapSource source, string vendor)
        {
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RunData", "CameraTemp"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Format("{0}_{1:yyyyMMdd_HHmmss_fff}.bmp", vendor, DateTime.Now));
            var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using (var stream = File.Create(path)) encoder.Save(stream); return path;
        }
        private void Stop() { if (_runCancellation != null) _runCancellation.Cancel(); }

        private bool CanUseVisionMaster() { return !IsBusy && !string.IsNullOrWhiteSpace(SolutionPath); }
        private bool CanRunVisionMaster() { return CanUseVisionMaster() && !string.IsNullOrWhiteSpace(SelectedProcedure); }
        private async Task LoadSolutionAsync()
        {
            IsBusy = true; RunState = "加载方案中"; await Task.Delay(1);
            try
            {
                var names = _visionMaster.LoadSolution(SolutionPath, SolutionPassword);
                Procedures.Clear(); foreach (var name in names) Procedures.Add(name);
                if (Procedures.Count > 0 && !Procedures.Contains(SelectedProcedure)) SelectedProcedure = Procedures[0];
                RefreshRouteChoices(true);
                RunState = "方案已加载"; AddLog("OK", "已加载 " + Path.GetFileName(SolutionPath)); RefreshPlatformStatus();
            }
            catch (Exception ex) { RunState = "加载失败"; ResultState = "ERROR"; ResultMessage = ex.Message; AddLog("ERROR", ex.Message); }
            finally { IsBusy = false; }
        }

        private async Task RunVisionMasterAsync()
        {
            var node = SelectedNode != null && SelectedNode.NodeType == "VisionMasterProcedureNode" ? SelectedNode : CreateVisionMasterNode();
            var old = SelectedNode; SelectedNode = node;
            await RunFlowNodeStandaloneAsync(node);
            if (!FlowSteps.Contains(node)) SelectedNode = old;
        }

        private async Task RunFlowNodeStandaloneAsync(FlowNodeViewModel node)
        {
            IsBusy = true; RunState = "节点调试中"; await Task.Delay(1);
            try
            {
                var result = await ExecuteNodeAsync(node, new VisionContext { ProjectName = ProjectName, RecipeName = RecipeName, StationName = StationName }, CancellationToken.None);
                node.ApplyResult(result); ResultState = result.Status == NodeRunStatus.Ok ? "OK" : result.Status == NodeRunStatus.Ng ? "NG" : "ERROR";
                ResultMessage = result.Message; CycleTime = result.CostMs.ToString("0.0 ms"); UpdateSelectedImageDocumentState(); AddLog(ResultState, result.Message); RunState = "运行完成";
            }
            finally { IsBusy = false; }
        }

        private async Task CloseSolutionAsync() { IsBusy = true; await Task.Delay(1); try { _visionMaster.CloseSolution(); Procedures.Clear(); RunState = "方案已关闭"; RefreshPlatformStatus(); } finally { IsBusy = false; } }
        private void RefreshPlatformStatus()
        {
            var vm = _visionMaster.GetStatus(); var vp = _visionPro.GetStatus(); var ha = _halcon.GetStatus();
            PlatformMessage = string.Format("VM:{0}  VP:{1}  HALCON:{2}", vm.Installed ? "OK" : "--", vp.Installed ? "OK" : "--", ha.Installed ? "OK" : "--");
        }

        private void ApplyNodeToPlatformEditors(FlowNodeViewModel node)
        {
            if (node.NodeType == "VisionMasterProcedureNode")
            {
                _solutionPath = node.Get("SolutionPath", _solutionPath); _selectedProcedure = node.Get("ProcedureName", _selectedProcedure);
                _imageInputName = node.Get("ImageInputName", _imageInputName); _okOutputName = node.Get("OkOutputName", _okOutputName); _solutionPassword = node.Get("SolutionPassword", string.Empty);
                OnPropertyChanged("SolutionPath"); OnPropertyChanged("SelectedProcedure"); OnPropertyChanged("ImageInputName"); OnPropertyChanged("OkOutputName"); OnPropertyChanged("SolutionPassword");
            }
            else if (node.NodeType == "VisionProToolBlockNode")
            {
                _visionProToolBlockPath = node.Get("ToolBlockPath", _visionProToolBlockPath); _visionProImageInputName = node.Get("ImageInputName", _visionProImageInputName); _visionProOkOutputName = node.Get("OkOutputName", _visionProOkOutputName);
                OnPropertyChanged("VisionProToolBlockPath"); OnPropertyChanged("VisionProImageInputName"); OnPropertyChanged("VisionProOkOutputName");
            }
            else if (node.NodeType == "HalconProcedureNode")
            {
                _halconProcedurePath = node.Get("ProcedurePath", _halconProcedurePath); _halconImageInputName = node.Get("ImageInputName", _halconImageInputName); _halconOkOutputName = node.Get("OkOutputName", _halconOkOutputName);
                OnPropertyChanged("HalconProcedurePath"); OnPropertyChanged("HalconImageInputName"); OnPropertyChanged("HalconOkOutputName");
            }
            if (node.NodeType == "VisionMasterProcedureNode" || node.NodeType == "VisionProToolBlockNode" || node.NodeType == "HalconProcedureNode")
            {
                _selectedImageSourceKey = node.Get("ImageSourceKey", "CameraImagePath"); OnPropertyChanged("SelectedImageSourceKey");
            }
            else if (node.NodeType == "CommunicationWriteNode")
            {
                EnsureCommunicationNodeDefaults(node);
                _communicationChannel = node.Get("Channel", "PLC_01"); _communicationAddress = node.Get("Address", "DB1.0"); _communicationSourceKey = node.Get("SourceKey", string.Empty); _communicationDataType = node.Get("DataType", "Bool");
                OnPropertyChanged("CommunicationChannel"); OnPropertyChanged("CommunicationAddress"); OnPropertyChanged("CommunicationSourceKey"); OnPropertyChanged("CommunicationDataType");
                LoadCommunicationWrites(node);
            }
            _imagePath = node.Get("LastImagePath", node.Get("ImagePath", _imagePath)); OnPropertyChanged("ImagePath"); LoadPreview(_imagePath);
        }

        public void AddCommunicationWrite()
        {
            if (SelectedNode == null || SelectedNode.NodeType != "CommunicationWriteNode") return;
            var item = CreateCommunicationWrite(true, "DB1.0", AvailableDataSources.FirstOrDefault() ?? string.Empty, "Bool", false, string.Empty);
            CommunicationWrites.Add(item); SelectedCommunicationWrite = item; SaveCommunicationWrites();
        }
        public void AddCommunicationTriggerField()
        {
            var index = 1; string name;
            do { name = "Field" + index.ToString("00", CultureInfo.InvariantCulture); index++; }
            while (CommunicationTriggerFields.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
            var item = new CommunicationFieldExtractionViewModel { Name = name, Mode = "Delimited", FieldIndex = CommunicationTriggerFields.Count, Trim = true };
            item.PropertyChanged += CommunicationTriggerFieldChanged;
            CommunicationTriggerFields.Add(item); SelectedTriggerField = item; RefreshCommunicationTriggerFieldChoices();
        }
        public void RemoveSelectedCommunicationTriggerField()
        {
            if (SelectedTriggerField == null) return;
            SelectedTriggerField.PropertyChanged -= CommunicationTriggerFieldChanged;
            CommunicationTriggerFields.Remove(SelectedTriggerField);
            SelectedTriggerField = CommunicationTriggerFields.FirstOrDefault(); RefreshCommunicationTriggerFieldChoices();
        }
        private void LoadCommunicationTriggerFields(IEnumerable<CommunicationFieldExtractionDefinition> definitions)
        {
            foreach (var old in CommunicationTriggerFields) old.PropertyChanged -= CommunicationTriggerFieldChanged;
            CommunicationTriggerFields.Clear();
            foreach (var definition in definitions ?? Enumerable.Empty<CommunicationFieldExtractionDefinition>())
            {
                var item = new CommunicationFieldExtractionViewModel(definition);
                item.PropertyChanged += CommunicationTriggerFieldChanged;
                CommunicationTriggerFields.Add(item);
            }
            SelectedTriggerField = CommunicationTriggerFields.FirstOrDefault(); RefreshCommunicationTriggerFieldChoices();
        }
        private void CommunicationTriggerFieldChanged(object sender, PropertyChangedEventArgs e) { RefreshCommunicationTriggerFieldChoices(); }
        private void RefreshCommunicationTriggerFieldChoices()
        {
            var names = CommunicationTriggerFields.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ReplaceItems(AvailableTriggerMatchFields, new[] { string.Empty }.Concat(names));
            if (!string.IsNullOrWhiteSpace(TriggerMatchField) && !names.Any(x => string.Equals(x, TriggerMatchField, StringComparison.OrdinalIgnoreCase))) TriggerMatchField = string.Empty;
            if (!string.IsNullOrWhiteSpace(RecipeSwitchCommandField) && !names.Any(x => string.Equals(x, RecipeSwitchCommandField, StringComparison.OrdinalIgnoreCase))) RecipeSwitchCommandField = string.Empty;
            if (!string.IsNullOrWhiteSpace(RecipeSwitchValueField) && !names.Any(x => string.Equals(x, RecipeSwitchValueField, StringComparison.OrdinalIgnoreCase))) RecipeSwitchValueField = string.Empty;
            RefreshRouteChoices();
        }
        private void EnsureCommunicationNodeDefaults(FlowNodeViewModel node)
        {
            if (node == null || node.NodeType != "CommunicationWriteNode") return;
            var visualOnlyKeys = new HashSet<string>(new[] { "SolutionPath", "SolutionPassword", "ProcedureName", "ToolBlockPath", "ProcedurePath", "ImagePath", "LastImagePath", "ImageSourceKey", "ImageInputName", "OkOutputName" }, StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in node.Parameters.Where(x => visualOnlyKeys.Contains(x.Key)).ToList()) node.Parameters.Remove(parameter);
            var channel = node.Get("Channel", string.Empty);
            if (string.IsNullOrWhiteSpace(channel)) node.SetParameter("Channel", AvailableCommunicationChannels.FirstOrDefault() ?? Communications.Select(x => x.Name).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "PLC_01");
            var source = node.Get("SourceKey", string.Empty);
            if (string.IsNullOrWhiteSpace(source)) node.SetParameter("SourceKey", AvailableDataSources.FirstOrDefault() ?? string.Empty);
            var count = node.GetInt("WriteCount", 0);
            for (var index = 0; index < count; index++)
                if (string.IsNullOrWhiteSpace(node.Get("Write" + index + ".SourceKey", string.Empty))) node.SetParameter("Write" + index + ".SourceKey", node.Get("SourceKey", string.Empty));
        }
        public void RemoveSelectedCommunicationWrite()
        {
            if (SelectedCommunicationWrite == null) return;
            SelectedCommunicationWrite.PropertyChanged -= CommunicationWriteChanged;
            CommunicationWrites.Remove(SelectedCommunicationWrite);
            if (CommunicationWrites.Count == 0) CommunicationWrites.Add(CreateCommunicationWrite(true, "DB1.0", string.Empty, "Bool", false, string.Empty));
            SelectedCommunicationWrite = CommunicationWrites.FirstOrDefault(); SaveCommunicationWrites();
        }
        private CommunicationWriteItemViewModel CreateCommunicationWrite(bool enabled, string address, string sourceKey, string dataType, bool useConstant, string constantValue)
        {
            var item = new CommunicationWriteItemViewModel { Enabled = enabled, Address = address, SourceKey = ToDisplayDataSourceKey(sourceKey), DataType = dataType, UseConstant = useConstant, ConstantValue = constantValue };
            item.PropertyChanged += CommunicationWriteChanged; return item;
        }
        private string ToDisplayDataSourceKey(string sourceKey)
        {
            var normalized = NormalizeDataSourceKey(sourceKey);
            var dot = normalized.IndexOf('.');
            if (dot <= 0) return normalized;
            var prefix = normalized.Substring(0, dot);
            var node = FlowSteps.FirstOrDefault(x => string.Equals(x.NodeId, prefix, StringComparison.OrdinalIgnoreCase));
            return node == null ? normalized : node.NodeName + normalized.Substring(dot);
        }
        private void CommunicationWriteChanged(object sender, PropertyChangedEventArgs e) { SaveCommunicationWrites(); }
        private void LoadCommunicationWrites(FlowNodeViewModel node)
        {
            _loadingCommunicationWrites = true;
            try
            {
                foreach (var old in CommunicationWrites) old.PropertyChanged -= CommunicationWriteChanged;
                CommunicationWrites.Clear();
                var count = node.GetInt("WriteCount", 0);
                if (count > 0)
                {
                    for (var index = 0; index < count; index++)
                        CommunicationWrites.Add(CreateCommunicationWrite(node.GetBool("Write" + index + ".Enabled", true), node.Get("Write" + index + ".Address", "DB1.0"), node.Get("Write" + index + ".SourceKey", string.Empty), node.Get("Write" + index + ".DataType", "Bool"), node.GetBool("Write" + index + ".UseConstant", false), node.Get("Write" + index + ".ConstantValue", string.Empty)));
                }
                else
                {
                    CommunicationWrites.Add(CreateCommunicationWrite(true, node.Get("Address", "DB1.0"), node.Get("SourceKey", string.Empty), node.Get("DataType", "Bool"), false, string.Empty));
                }
                SelectedCommunicationWrite = CommunicationWrites.FirstOrDefault();
            }
            finally { _loadingCommunicationWrites = false; }
        }
        private void SaveCommunicationWrites()
        {
            if (_loadingCommunicationWrites || SelectedNode == null || SelectedNode.NodeType != "CommunicationWriteNode") return;
            foreach (var old in SelectedNode.Parameters.Where(x => IsCommunicationWriteParameter(x.Key)).ToList()) SelectedNode.Parameters.Remove(old);
            SelectedNode.SetParameter("WriteCount", CommunicationWrites.Count.ToString(CultureInfo.InvariantCulture));
            for (var index = 0; index < CommunicationWrites.Count; index++)
            {
                var item = CommunicationWrites[index]; var prefix = "Write" + index + ".";
                SelectedNode.SetParameter(prefix + "Enabled", item.Enabled.ToString());
                SelectedNode.SetParameter(prefix + "Address", item.Address ?? string.Empty);
                SelectedNode.SetParameter(prefix + "SourceKey", item.SourceKey ?? string.Empty);
                SelectedNode.SetParameter(prefix + "DataType", item.DataType ?? "Bool");
                SelectedNode.SetParameter(prefix + "UseConstant", item.UseConstant.ToString());
                SelectedNode.SetParameter(prefix + "ConstantValue", item.ConstantValue ?? string.Empty);
            }
            var first = CommunicationWrites.FirstOrDefault();
            if (first != null)
            {
                SelectedNode.SetParameter("Address", first.Address ?? string.Empty); SelectedNode.SetParameter("SourceKey", first.SourceKey ?? string.Empty); SelectedNode.SetParameter("DataType", first.DataType ?? "Bool");
            }
        }
        private static bool IsCommunicationWriteParameter(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            if (string.Equals(key, "WriteCount", StringComparison.OrdinalIgnoreCase)) return true;
            if (!key.StartsWith("Write", StringComparison.OrdinalIgnoreCase)) return false;
            var dot = key.IndexOf('.'); int index; return dot > 5 && int.TryParse(key.Substring(5, dot - 5), out index);
        }
        private static List<CommunicationWriteItemViewModel> ReadCommunicationWrites(FlowNodeViewModel node)
        {
            var items = new List<CommunicationWriteItemViewModel>(); var count = node.GetInt("WriteCount", 0);
            if (count <= 0)
            {
                items.Add(new CommunicationWriteItemViewModel { Enabled = true, Address = node.Get("Address", "DB1.0"), SourceKey = node.Get("SourceKey", string.Empty), DataType = node.Get("DataType", "Bool") });
                return items;
            }
            for (var index = 0; index < count; index++) items.Add(new CommunicationWriteItemViewModel { Enabled = node.GetBool("Write" + index + ".Enabled", true), Address = node.Get("Write" + index + ".Address", "DB1.0"), SourceKey = node.Get("Write" + index + ".SourceKey", string.Empty), DataType = node.Get("Write" + index + ".DataType", "Bool"), UseConstant = node.GetBool("Write" + index + ".UseConstant", false), ConstantValue = node.Get("Write" + index + ".ConstantValue", string.Empty) });
            return items;
        }

        private void SetSelectedParameterFor(string nodeType, string key, string value)
        {
            if (SelectedNode != null && string.Equals(SelectedNode.NodeType, nodeType, StringComparison.OrdinalIgnoreCase)) SelectedNode.SetParameter(key, value ?? string.Empty);
        }
        private void SetSelectedVisualParameter(string key, string value)
        {
            if (SelectedNode == null) return;
            if (SelectedNode.NodeType == "VisionMasterProcedureNode" || SelectedNode.NodeType == "VisionProToolBlockNode" || SelectedNode.NodeType == "HalconProcedureNode") SelectedNode.SetParameter(key, value ?? string.Empty);
        }
        private void Renumber() { for (var i = 0; i < FlowSteps.Count; i++) FlowSteps[i].Order = i + 1; RefreshRouteChoices(); }
        private void RefreshRouteChoices(bool discoverVisionOutputs = false)
        {
            if (AvailableImageSources == null || _refreshingRouteChoices) return;
            _refreshingRouteChoices = true;
            try
            {
                var nodes = FlowSteps.ToArray();
                var imageSources = new List<string> { "CameraImagePath", "ImagePath" };
                foreach (var node in nodes.Where(x => x.NodeType == "CameraGrabNode"))
                {
                    var outputKey = node.Get("OutputPathKey", "CameraImagePath");
                    imageSources.Add(outputKey);
                    imageSources.Add(node.NodeName + "." + outputKey);
                }
                ReplaceItems(AvailableImageSources, imageSources);
                var dataSources = new List<string>();
                var judgeSources = new List<string>();
                foreach (var node in nodes)
                {
                    IEnumerable<string> fixedOutputs = node.NodeType == "VisionMasterProcedureNode" ? new[] { "VisionMasterOK", "VisionMasterProcessTimeMs", "ProcedureName" }
                        : node.NodeType == "VisionProToolBlockNode" ? new[] { "VisionProOK", "VisionProRunStatus" }
                        : node.NodeType == "HalconProcedureNode" ? new[] { "HalconOK", "Procedure" }
                        : node.NodeType == "CameraGrabNode" ? new[] { "ImagePath", node.Get("OutputPathKey", "CameraImagePath"), "ExposureTime", "ExposureTimeUtc" }
                        : node.NodeType == "CSharpScriptNode" ? CSharpScriptEngine.ParseList(node.Get("OutputNames", string.Empty)).ToArray()
                        : new string[0];
                    var outputs = new List<string>(fixedOutputs);
                    // Loading a VM Solution or a vendor tool file is an expensive SDK
                    // operation and some drivers can wait for hardware. It must never
                    // run as a side effect of renumbering nodes, rebuilding the tree,
                    // opening a project, or changing the UI language. Explicit refresh,
                    // platform debug and execution update this persistent cache instead.
                    var discovered = CSharpScriptEngine.ParseList(node.Get("DiscoveredOutputs", string.Empty)).ToList();
                    if (discoverVisionOutputs && IsVisionPlatformNode(node))
                    {
                        try
                        {
                            discovered = DiscoverVisionOutputs(node)
                                .Select(x => x.Name)
                                .Where(x => !string.IsNullOrWhiteSpace(x))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            node.SetParameter("DiscoveredOutputs", string.Join(";", discovered));
                        }
                        catch (Exception ex)
                        {
                            AddLog("WARN", node.NodeName + " 输出刷新失败，继续使用缓存：" + ex.Message);
                        }
                    }
                    outputs.AddRange(discovered);
                    foreach (var output in outputs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                    {
                        dataSources.Add(node.NodeName + "." + output);
                        judgeSources.Add(node.NodeName + "." + output);
                        judgeSources.Add(output);
                    }
                }
                ReplaceItems(AvailableDataSources, dataSources);
                ReplaceItems(AvailableJudgeDataSources, judgeSources);
                ReplaceItems(AvailableCommunicationChannels, Communications.ToArray().Select(x => x.Name));
            foreach (var triggerKey in new[] { "CommunicationTrigger.Raw", "CommunicationTrigger.ConnectionId" }.Concat(CommunicationTriggerFields.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => "CommunicationTrigger." + x.Name.Trim())))
                {
                    if (!AvailableDataSources.Any(x => string.Equals(x, triggerKey, StringComparison.OrdinalIgnoreCase))) AvailableDataSources.Add(triggerKey);
                    if (!AvailableJudgeDataSources.Any(x => string.Equals(x, triggerKey, StringComparison.OrdinalIgnoreCase))) AvailableJudgeDataSources.Add(triggerKey);
                }
            }
            finally { _refreshingRouteChoices = false; }
        }
        private IReadOnlyList<VisionOutputDefinition> DiscoverVisionOutputs(FlowNodeViewModel node)
        {
            if (node.NodeType == "VisionMasterProcedureNode")
                return _visionMaster.GetOutputs(new VisionMasterRunConfig { SolutionPath = node.Get("SolutionPath", SolutionPath), SolutionPassword = node.Get("SolutionPassword", string.Empty), ProcedureName = node.Get("ProcedureName", SelectedProcedure) });
            if (node.NodeType == "VisionProToolBlockNode")
                return _visionPro.GetOutputs(new VisionProRunConfig { ToolBlockPath = node.Get("ToolBlockPath", VisionProToolBlockPath) });
            if (node.NodeType == "HalconProcedureNode")
                return _halcon.GetOutputs(new HalconRunConfig { ProcedurePath = node.Get("ProcedurePath", HalconProcedurePath) });
            return new List<VisionOutputDefinition>();
        }
        private static bool IsVisionPlatformNode(FlowNodeViewModel node)
        {
            return node != null && (node.NodeType == "VisionMasterProcedureNode" ||
                node.NodeType == "VisionProToolBlockNode" || node.NodeType == "HalconProcedureNode");
        }
        private void AddDataSourceChoice(FlowNodeViewModel node, string outputName)
        {
            if (AvailableDataSources == null || node == null || string.IsNullOrWhiteSpace(outputName)) return;
            if (IsVisionPlatformNode(node))
            {
                var cached = CSharpScriptEngine.ParseList(node.Get("DiscoveredOutputs", string.Empty)).ToList();
                if (!cached.Any(x => string.Equals(x, outputName, StringComparison.OrdinalIgnoreCase)))
                {
                    cached.Add(outputName);
                    node.SetParameter("DiscoveredOutputs", string.Join(";", cached));
                }
            }
            var key = node.NodeName + "." + outputName;
            if (!AvailableDataSources.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase))) AvailableDataSources.Add(key);
        }
        private static void ReplaceItems(ObservableCollection<string> target, IEnumerable<string> values)
        {
            var desired = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            for (var index = 0; index < desired.Count; index++)
            {
                if (index < target.Count && string.Equals(target[index], desired[index], StringComparison.OrdinalIgnoreCase)) continue;
                var existing = -1;
                for (var oldIndex = index + 1; oldIndex < target.Count; oldIndex++)
                    if (string.Equals(target[oldIndex], desired[index], StringComparison.OrdinalIgnoreCase)) { existing = oldIndex; break; }
                if (existing >= 0) target.Move(existing, index);
                else target.Insert(index, desired[index]);
            }
            while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
        }
        private void AddLog(string level, string message) { Logs.Insert(0, new LogEntryViewModel { Time = DateTime.Now.ToString("HH:mm:ss.fff"), Level = level, Message = message }); while (Logs.Count > 300) Logs.RemoveAt(Logs.Count - 1); }
        private void OnLanguageChanged(object sender, EventArgs e)
        {
            foreach (var node in FlowSteps) node.RefreshLocalization();
            foreach (var entry in Logs) entry.RefreshLocalization();
            foreach (var document in ImageDocuments) document.RefreshLocalization();
            RefreshProjectTree(false);
            OnPropertyChanged("ProjectStatusText");
            OnPropertyChanged("CurrentFlowStatusText");
            OnPropertyChanged("ImageDocumentCountText");
            OnPropertyChanged("PlatformMessageDisplay");
            OnPropertyChanged("RunStateDisplay");
            OnPropertyChanged("ResultMessageDisplay");
        }
        private void LoadPreview(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try { var image = new BitmapImage(); using (var stream = File.OpenRead(path)) { image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); } PreviewImage = image; }
            catch (Exception ex) { AddLog("WARN", "图像预览失败：" + ex.Message); }
        }

        private void RaiseCommands()
        {
            LoadSolutionCommand.RaiseCanExecuteChanged(); RunCommand.RaiseCanExecuteChanged(); CloseSolutionCommand.RaiseCanExecuteChanged();
            AddNodeCommand.RaiseCanExecuteChanged(); DeleteNodeCommand.RaiseCanExecuteChanged(); CopyNodeCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged(); MoveDownCommand.RaiseCanExecuteChanged(); ToggleNodeCommand.RaiseCanExecuteChanged();
            RunAllCommand.RaiseCanExecuteChanged(); RunContinuousCommand.RaiseCanExecuteChanged(); RunCommunicationTriggerCommand.RaiseCanExecuteChanged(); RunSelectedCommand.RaiseCanExecuteChanged(); RunFromSelectedCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
        }
    }

    public sealed class FlowNodeViewModel : ObservableObject
    {
        private int _order; private string _nodeName; private string _nodeType; private string _category; private string _platform;
        private bool _enabled; private int _timeoutMs; private string _onError; private string _status = "Idle"; private string _message = string.Empty; private string _cost = "--";
        public FlowNodeViewModel(FlowNodeConfig config)
        {
            NodeId = config.NodeId; _nodeName = config.NodeName; _nodeType = config.NodeType; _category = config.Category; _platform = config.Platform;
            _enabled = config.Enabled; _timeoutMs = config.TimeoutMs; _onError = config.OnError; Parameters = new ObservableCollection<NodeParameterViewModel>((config.Parameters ?? new List<NodeParameter>()).ToArray().Select(p => new NodeParameterViewModel { Key = p.Key, Value = p.Value }));
        }
        public string NodeId { get; private set; }
        public int Order { get { return _order; } set { Set(ref _order, value); } }
        public string NodeName { get { return _nodeName; } set { if (Set(ref _nodeName, value)) OnPropertyChanged("DisplayNodeName"); } }
        public string DisplayNodeName { get { return LocalizationService.TDynamic(NodeName); } set { NodeName = LocalizationService.ToCanonical(value); } }
        public string NodeType { get { return _nodeType; } set { if (Set(ref _nodeType, value)) Platform = value == "VisionMasterProcedureNode" ? "VisionMaster" : value == "VisionProToolBlockNode" ? "VisionPro" : value == "HalconProcedureNode" ? "HALCON" : value == "CameraGrabNode" ? "Camera" : value == "CommunicationWriteNode" ? "Communication" : value == "CSharpScriptNode" ? "CSharp" : "Common"; } }
        public string Category { get { return _category; } set { if (Set(ref _category, value)) OnPropertyChanged("DisplayCategory"); } }
        public string DisplayCategory { get { return LocalizationService.TDynamic(Category); } set { Category = LocalizationService.ToCanonical(value); } }
        public string Platform { get { return _platform; } set { Set(ref _platform, value); } }
        public bool Enabled { get { return _enabled; } set { Set(ref _enabled, value); } }
        public int TimeoutMs { get { return _timeoutMs; } set { Set(ref _timeoutMs, value); } }
        public string OnError { get { return _onError; } set { Set(ref _onError, value); } }
        public string Status { get { return _status; } set { if (Set(ref _status, value)) OnPropertyChanged("DisplayStatus"); } }
        public string DisplayStatus { get { return LocalizationService.TDynamic(Status); } }
        public string Message { get { return _message; } set { if (Set(ref _message, value)) OnPropertyChanged("DisplayMessage"); } }
        public string DisplayMessage { get { return LocalizationService.TDynamic(Message); } }
        public string Cost { get { return _cost; } set { Set(ref _cost, value); } }
        public ObservableCollection<NodeParameterViewModel> Parameters { get; private set; }
        public string LimitJudgeInputKey { get { return Get("InputKey", string.Empty); } set { SetParameter("InputKey", value ?? string.Empty); } }
        public string LimitJudgeMin { get { return Get("Min", "0"); } set { SetParameter("Min", value ?? string.Empty); } }
        public string LimitJudgeMax { get { return Get("Max", "1"); } set { SetParameter("Max", value ?? string.Empty); } }
        public string Get(string key, string fallback) { var p = Parameters.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)); return p == null ? fallback : p.Value; }
        public int GetInt(string key, int fallback) { int v; return int.TryParse(Get(key, fallback.ToString()), out v) ? v : fallback; }
        public double GetDouble(string key, double fallback) { double v; return double.TryParse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : fallback; }
        public bool GetBool(string key, bool fallback) { bool value; return bool.TryParse(Get(key, fallback.ToString()), out value) ? value : fallback; }
        public void SetParameter(string key, string value)
        {
            var p = Parameters.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            if (p == null) Parameters.Add(new NodeParameterViewModel { Key = key, Value = value });
            else p.Value = value;
            if (string.Equals(key, "InputKey", StringComparison.OrdinalIgnoreCase)) OnPropertyChanged("LimitJudgeInputKey");
            else if (string.Equals(key, "Min", StringComparison.OrdinalIgnoreCase)) OnPropertyChanged("LimitJudgeMin");
            else if (string.Equals(key, "Max", StringComparison.OrdinalIgnoreCase)) OnPropertyChanged("LimitJudgeMax");
        }
        public void ApplyResult(NodeRunResult r) { Status = r.Status.ToString(); Message = r.Message; Cost = r.CostMs > 0 ? r.CostMs.ToString("0.0 ms") : "--"; }
        public void RefreshLocalization()
        {
            OnPropertyChanged("DisplayNodeName"); OnPropertyChanged("DisplayCategory");
            OnPropertyChanged("DisplayStatus"); OnPropertyChanged("DisplayMessage");
            foreach (var parameter in Parameters) parameter.RefreshLocalization();
        }
        public FlowNodeConfig ToConfig() { var c = new FlowNodeConfig { NodeId = NodeId, NodeName = NodeName, NodeType = NodeType, Category = Category, Platform = Platform, Enabled = Enabled, TimeoutMs = TimeoutMs, OnError = OnError }; foreach (var p in Parameters.ToArray().Where(x => !string.IsNullOrWhiteSpace(x.Key))) c.Parameters.Add(new NodeParameter { Key = p.Key, Value = p.Value ?? string.Empty }); return c; }
        public static FlowNodeViewModel Create(string name, string type, string category, string platform, params string[] pairs) { var c = new FlowNodeConfig { NodeName = name, NodeType = type, Category = category, Platform = platform }; if (type == "CameraGrabNode") c.TimeoutMs = 30000; for (var i = 0; i + 1 < pairs.Length; i += 2) c.Parameters.Add(new NodeParameter { Key = pairs[i], Value = pairs[i + 1] }); return new FlowNodeViewModel(c); }
    }

    public sealed class NodeParameterViewModel : ObservableObject
    {
        private string _key; private string _value;
        public string Key { get { return _key; } set { Set(ref _key, value); } }
        public string Value { get { return _value; } set { if (Set(ref _value, value)) OnPropertyChanged("DisplayValue"); } }
        public string DisplayValue { get { return LocalizationService.TDynamic(Value); } set { Value = LocalizationService.ToCanonical(value); } }
        public void RefreshLocalization() { OnPropertyChanged("DisplayValue"); }
    }
    public sealed class CommunicationWriteItemViewModel : ObservableObject
    {
        private bool _enabled = true;
        private string _address = "DB1.0";
        private string _sourceKey = string.Empty;
        private string _dataType = "Bool";
        private bool _useConstant;
        private string _constantValue = string.Empty;
        public bool Enabled { get { return _enabled; } set { Set(ref _enabled, value); } }
        public string Address { get { return _address; } set { Set(ref _address, value); } }
        public string SourceKey { get { return _sourceKey; } set { Set(ref _sourceKey, value); } }
        public string DataType { get { return _dataType; } set { Set(ref _dataType, value); } }
        public bool UseConstant { get { return _useConstant; } set { Set(ref _useConstant, value); } }
        public string ConstantValue { get { return _constantValue; } set { Set(ref _constantValue, value); } }
    }

    public sealed class CommunicationFieldExtractionViewModel : ObservableObject
    {
        private string _name = "SerialNumber";
        private string _mode = "Delimited";
        private int _fieldIndex;
        private int _start;
        private int _length;
        private string _jsonPath = string.Empty;
        private bool _optional;
        private bool _trim = true;
        public CommunicationFieldExtractionViewModel() { }
        public CommunicationFieldExtractionViewModel(CommunicationFieldExtractionDefinition definition)
        {
            if (definition == null) return;
            _name = definition.Name ?? string.Empty; _mode = definition.Mode ?? "Delimited"; _fieldIndex = definition.FieldIndex;
            _start = definition.Start; _length = definition.Length; _jsonPath = definition.JsonPath ?? string.Empty; _optional = definition.Optional; _trim = definition.Trim;
        }
        public string Name { get { return _name; } set { Set(ref _name, value); } }
        public string Mode { get { return _mode; } set { Set(ref _mode, value); } }
        public int FieldIndex { get { return _fieldIndex; } set { Set(ref _fieldIndex, Math.Max(0, value)); } }
        public int Start { get { return _start; } set { Set(ref _start, Math.Max(0, value)); } }
        public int Length { get { return _length; } set { Set(ref _length, Math.Max(0, value)); } }
        public string JsonPath { get { return _jsonPath; } set { Set(ref _jsonPath, value); } }
        public bool Optional { get { return _optional; } set { Set(ref _optional, value); } }
        public bool Trim { get { return _trim; } set { Set(ref _trim, value); } }
        public CommunicationFieldExtractionDefinition ToDefinition()
        {
            return new CommunicationFieldExtractionDefinition { Name = Name ?? string.Empty, Mode = Mode ?? "Delimited", FieldIndex = FieldIndex, Start = Start, Length = Length, JsonPath = JsonPath ?? string.Empty, Optional = Optional, Trim = Trim };
        }
    }

    public sealed class ImageViewDocumentViewModel : ObservableObject
    {
        private string _title;
        private BitmapImage _source;
        private string _resultState = "READY";
        private string _resultMessage = "等待运行";
        private string _cycleTime = "--";
        public string Key { get; set; }
        public string Title { get { return _title; } set { Set(ref _title, value); } }
        public BitmapImage Source { get { return _source; } set { Set(ref _source, value); } }
        public string ResultState { get { return _resultState; } set { Set(ref _resultState, value); } }
        public string ResultMessage { get { return _resultMessage; } set { if (Set(ref _resultMessage, value)) OnPropertyChanged("DisplayResultMessage"); } }
        public string DisplayResultMessage { get { return LocalizationService.TDynamic(ResultMessage); } }
        public string CycleTime { get { return _cycleTime; } set { Set(ref _cycleTime, value); } }
        public void RefreshLocalization() { OnPropertyChanged("DisplayResultMessage"); }
    }

    public sealed class LogEntryViewModel : ObservableObject
    {
        private string _message;
        public string Time { get; set; }
        public string Level { get; set; }
        public string Message { get { return _message; } set { if (Set(ref _message, value)) OnPropertyChanged("DisplayMessage"); } }
        public string DisplayMessage { get { return LocalizationService.TDynamic(Message); } }
        public void RefreshLocalization() { OnPropertyChanged("DisplayMessage"); }
    }
}
