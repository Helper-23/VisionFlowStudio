using System;
using System.Collections.Generic;

namespace VisionFlowStudio.Core
{
    public sealed class CameraDeviceInfo
    {
        public string Vendor { get; set; }
        public string DeviceId { get; set; }
        public string DisplayName { get; set; }
        public string SerialNumber { get; set; }
        public string IpAddress { get; set; }
    }

    public sealed class CameraFrameData
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int Stride { get; set; }
        public byte[] BgrPixels { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class CameraSettings
    {
        public double ExposureUs { get; set; } = 10000;
        public double Gain { get; set; }
        public string TriggerMode { get; set; } = "Off";
        public string TriggerSource { get; set; } = "Software";
        public string PixelFormat { get; set; } = "Mono8";
        public double FrameRate { get; set; } = 10;
        public bool FrameRateEnabled { get; set; }
        public string UserSet { get; set; } = "UserSet1";
    }

    public interface ICameraProvider : IDisposable
    {
        string Vendor { get; }
        bool IsConnected { get; }
        IReadOnlyList<CameraDeviceInfo> Enumerate();
        void Connect(string deviceId);
        CameraSettings GetSettings();
        void ApplySettings(CameraSettings settings);
        void LoadUserSet(string userSet);
        void SaveUserSet(string userSet);
        CameraFrameData Acquire(int timeoutMs);
        void Disconnect();
    }

    public sealed class VisionPlatformStatus
    {
        public string Name { get; set; }
        public bool Installed { get; set; }
        public bool Loaded { get; set; }
        public string Message { get; set; }
    }

    public sealed class VisionOutputDefinition
    {
        public string Name { get; set; }
        public string DataType { get; set; }
    }

    public interface IVisionMasterAdapter : IDisposable
    {
        VisionPlatformStatus GetStatus();
        IReadOnlyList<string> LoadSolution(string path, string password);
        IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionMasterRunConfig config);
        NodeRunResult Run(VisionMasterRunConfig config, VisionContext context);
        void CloseSolution();
    }

    public interface IVisionProAdapter : IDisposable
    {
        VisionPlatformStatus GetStatus();
        IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionProRunConfig config);
        NodeRunResult Run(VisionProRunConfig config, VisionContext context);
    }

    public interface IHalconAdapter : IDisposable
    {
        VisionPlatformStatus GetStatus();
        IReadOnlyList<VisionOutputDefinition> GetOutputs(HalconRunConfig config);
        NodeRunResult Run(HalconRunConfig config, VisionContext context);
        void ReloadProcedure(string path);
    }
}
