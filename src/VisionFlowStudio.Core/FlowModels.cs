using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VisionFlowStudio.Core
{
    public enum NodeRunStatus
    {
        Idle,
        Running,
        Ok,
        Ng,
        Error,
        Timeout,
        Skipped
    }

    public sealed class VisionContext
    {
        private readonly Dictionary<string, object> _data =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public string ProjectName { get; set; } = "DemoProject";
        public string RecipeName { get; set; } = "Model_A";
        public string StationName { get; set; } = "Station_01";
        public DateTime TriggerTime { get; set; } = DateTime.Now;
        public IDictionary<string, object> Data { get { return _data; } }

        public void Set(string key, object value)
        {
            _data[key] = value;
        }

        public T Get<T>(string key, T fallback = default(T))
        {
            object value;
            if (!_data.TryGetValue(key, out value) || value == null)
                return fallback;
            if (value is T)
                return (T)value;
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public object GetValue(string key)
        {
            object value; return _data.TryGetValue(key, out value) ? value : null;
        }
    }

    public sealed class NodeRunResult
    {
        public NodeRunStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public double CostMs { get; set; }
        public IDictionary<string, object> Outputs { get; set; } =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public bool IsSuccess
        {
            get { return Status == NodeRunStatus.Ok || Status == NodeRunStatus.Ng; }
        }
    }

    public sealed class VisionMasterRunConfig
    {
        public string SolutionPath { get; set; } = string.Empty;
        public string SolutionPassword { get; set; } = string.Empty;
        public string ProcedureName { get; set; } = "Flow1";
        public string ImagePath { get; set; } = string.Empty;
        public string ImageInputName { get; set; } = "InputImage";
        public string OkOutputName { get; set; } = "IsOK";
        public IDictionary<string, VisionMasterVariable> Variables { get; set; } =
            new Dictionary<string, VisionMasterVariable>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class VisionMasterVariable
    {
        public string Type { get; set; } = "String";
        public string Value { get; set; } = string.Empty;
    }

    public sealed class VisionProRunConfig
    {
        public string ToolBlockPath { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string ImageInputName { get; set; } = "InputImage";
        public string OkOutputName { get; set; } = "IsOK";
    }

    public sealed class HalconRunConfig
    {
        public string ProcedurePath { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string ImageInputName { get; set; } = "Image";
        public string OkOutputName { get; set; } = "IsOK";
        public IDictionary<string, string> ControlInputs { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    [DataContract]
    public sealed class ProjectDocument
    {
        [DataMember] public string ConfigVersion { get; set; } = "1.0.0";
        [DataMember] public string ProjectName { get; set; } = "VisionFlowStudio";
        [DataMember] public string RecipeName { get; set; } = "Model_A";
        [DataMember] public string StationName { get; set; } = "Station_01";
        public string FlowFile { get; set; } = string.Empty;
        [DataMember] public DateTime ModifiedTime { get; set; } = DateTime.Now;
        [DataMember] public List<RecipeDefinition> Recipes { get; set; } = new List<RecipeDefinition>();
        [DataMember] public List<StationDefinition> Stations { get; set; } = new List<StationDefinition>();
        [DataMember] public List<StationRecipeFlowDefinition> StationFlows { get; set; } = new List<StationRecipeFlowDefinition>();
        [DataMember] public List<CameraDefinition> Cameras { get; set; } = new List<CameraDefinition>();
        [DataMember] public List<CommunicationDefinition> Communications { get; set; } = new List<CommunicationDefinition>();
    }

    [DataContract]
    public sealed class RecipeDefinition
    {
        [DataMember] public string Name { get; set; } = "Model_A";
        [DataMember] public string ProductCode { get; set; } = string.Empty;
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public sealed class StationDefinition
    {
        [DataMember] public string Name { get; set; } = "Station_01";
        public string RecipeName { get; set; } = string.Empty;
        public string FlowName { get; set; } = "MainFlow";
        public string FlowFile { get; set; } = string.Empty;
        public FlowDocument Flow { get; set; } = new FlowDocument();
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public sealed class StationRecipeFlowDefinition
    {
        [DataMember] public string StationName { get; set; } = "Station_01";
        [DataMember] public string RecipeName { get; set; } = "Model_A";
        [DataMember] public string FlowId { get; set; } = "MainFlow";
        [DataMember] public string FlowName { get; set; } = "MainFlow";
        public string FlowFile { get; set; } = string.Empty;
        [DataMember] public FlowDocument Flow { get; set; } = new FlowDocument();
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public sealed class CameraDefinition
    {
        [DataMember] public string Name { get; set; } = "Camera_01";
        // Legacy compatibility: cameras are now station-level resources; RecipeName can be blank.
        [DataMember] public string RecipeName { get; set; } = "Model_A";
        [DataMember] public string StationName { get; set; } = "Station_01";
        [DataMember] public string Vendor { get; set; } = "Hikrobot";
        [DataMember] public string DeviceId { get; set; } = string.Empty;
        [DataMember] public double ExposureUs { get; set; } = 10000;
        [DataMember] public double Gain { get; set; }
        [DataMember] public string TriggerMode { get; set; } = "Off";
        [DataMember] public string TriggerSource { get; set; } = "Software";
        [DataMember] public string PixelFormat { get; set; } = "Mono8";
        [DataMember] public double FrameRate { get; set; } = 10;
        [DataMember] public bool FrameRateEnabled { get; set; }
        [DataMember] public string UserSet { get; set; } = "UserSet1";
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public sealed class CommunicationDefinition
    {
        [DataMember] public string Name { get; set; } = "PLC_01";
        [DataMember] public string Protocol { get; set; } = "Siemens S7Net";
        [DataMember] public string PlcModel { get; set; } = "S1200";
        [DataMember] public string Host { get; set; } = "127.0.0.1";
        [DataMember] public int Port { get; set; } = 102;
        [DataMember] public int Station { get; set; } = 1;
        [DataMember] public int Rack { get; set; }
        [DataMember] public int Slot { get; set; }
        [DataMember] public string SerialPort { get; set; } = "COM1";
        [DataMember] public int BaudRate { get; set; } = 9600;
        [DataMember] public int DataBits { get; set; } = 8;
        [DataMember] public string Parity { get; set; } = "None";
        [DataMember] public string StopBits { get; set; } = "One";
        [DataMember] public int ConnectTimeoutMs { get; set; } = 3000;
        [DataMember] public int ReceiveTimeoutMs { get; set; } = 3000;
        [DataMember] public int HeartbeatIntervalMs { get; set; } = 1000;
        [DataMember] public bool Enabled { get; set; } = true;
    }

    [DataContract]
    public sealed class CommunicationTriggerDefinition
    {
        [DataMember] public string Channel { get; set; } = "PLC_01";
        [DataMember] public string Address { get; set; } = "DB1.0";
        [DataMember] public string DataType { get; set; } = "Bool";
        [DataMember] public string Mode { get; set; } = "RisingEdge";
        [DataMember] public string ExpectedValue { get; set; } = "True";
        [DataMember] public int PollIntervalMs { get; set; } = 100;
    }

    [DataContract]
    public sealed class FlowDocument
    {
        [DataMember] public string ConfigVersion { get; set; } = "1.0.0";
        [DataMember] public string ProjectName { get; set; } = "VisionFlowStudio";
        [DataMember] public string StationName { get; set; } = "Station_01";
        [DataMember] public string RecipeName { get; set; } = "Model_A";
        [DataMember] public string FlowName { get; set; } = "MainFlow";
        [DataMember] public int ContinuousIntervalMs { get; set; } = 100;
        [DataMember] public CommunicationTriggerDefinition CommunicationTrigger { get; set; } = new CommunicationTriggerDefinition();
        [DataMember] public List<FlowNodeConfig> Nodes { get; set; } = new List<FlowNodeConfig>();
    }

    [DataContract]
    public sealed class FlowNodeConfig
    {
        [DataMember] public string NodeId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        [DataMember] public string NodeName { get; set; } = "新节点";
        [DataMember] public string NodeType { get; set; } = "DelayNode";
        [DataMember] public string Category { get; set; } = "通用";
        [DataMember] public string Platform { get; set; } = "Common";
        [DataMember] public bool Enabled { get; set; } = true;
        [DataMember] public int TimeoutMs { get; set; } = 1000;
        [DataMember] public string OnError { get; set; } = "StopFlow";
        [DataMember] public List<NodeParameter> Parameters { get; set; } = new List<NodeParameter>();

        public FlowNodeConfig Clone()
        {
            var clone = new FlowNodeConfig
            {
                NodeName = NodeName + " 副本",
                NodeType = NodeType,
                Category = Category,
                Platform = Platform,
                Enabled = Enabled,
                TimeoutMs = TimeoutMs,
                OnError = OnError
            };
            foreach (var parameter in Parameters)
                clone.Parameters.Add(new NodeParameter { Key = parameter.Key, Value = parameter.Value });
            return clone;
        }
    }

    [DataContract]
    public sealed class NodeParameter
    {
        [DataMember] public string Key { get; set; } = string.Empty;
        [DataMember] public string Value { get; set; } = string.Empty;
    }
}
