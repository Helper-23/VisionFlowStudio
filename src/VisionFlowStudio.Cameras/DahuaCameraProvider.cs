using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MVSDK_Net;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Cameras
{
    public sealed class DahuaCameraProvider : ICameraProvider
    {
        private const string RuntimeDirectory = @"D:\Program Files\MV Viewer\Runtime\x64";
        private MyCamera _camera;
        private CameraSettings _settings = new CameraSettings();
        public string Vendor { get { return "Dahua"; } }
        public bool IsConnected { get; private set; }

        public IReadOnlyList<CameraDeviceInfo> Enumerate()
        {
            EnsureRuntime();
            var list = GetNativeDevices().Where(IsDahuaDevice).ToList();
            return list.Select(x => new CameraDeviceInfo { Vendor = Vendor, DeviceId = x.serialNumber, SerialNumber = x.serialNumber, DisplayName = x.cameraName }).ToArray();
        }

        public void Connect(string deviceId)
        {
            Disconnect(); EnsureRuntime();
            var devices = GetNativeDevices().Where(IsDahuaDevice).ToList();
            var index = devices.FindIndex(x => string.IsNullOrWhiteSpace(deviceId) || x.serialNumber == deviceId);
            if (index < 0) throw new InvalidOperationException("未找到大华相机：" + deviceId);
            _camera = new MyCamera();
            EnsureOk(_camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, index), "创建大华相机句柄");
            EnsureOk(_camera.IMV_Open(), "打开大华相机");
            _camera.IMV_SetEnumFeatureSymbol("TriggerMode", "Off");
            EnsureOk(_camera.IMV_StartGrabbing(), "启动大华采集"); IsConnected = true;
        }

        public CameraSettings GetSettings()
        {
            EnsureConnected(); double exposure = 0, gain = 0, rate = 0; bool enabled = _settings.FrameRateEnabled;
            if (_camera.IMV_GetDoubleFeatureValue("ExposureTime", ref exposure) == IMVDefine.IMV_OK) _settings.ExposureUs = exposure;
            if (_camera.IMV_GetDoubleFeatureValue("GainRaw", ref gain) != IMVDefine.IMV_OK) _camera.IMV_GetDoubleFeatureValue("Gain", ref gain); if (gain >= 0) _settings.Gain = gain;
            if (_camera.IMV_GetDoubleFeatureValue("AcquisitionFrameRate", ref rate) == IMVDefine.IMV_OK) _settings.FrameRate = rate;
            if (_camera.IMV_GetBoolFeatureValue("AcquisitionFrameRateEnable", ref enabled) == IMVDefine.IMV_OK) _settings.FrameRateEnabled = enabled;
            return CloneSettings(_settings);
        }

        public void ApplySettings(CameraSettings settings)
        {
            EnsureConnected(); if (settings == null) return; var wasGrabbing = _camera.IMV_IsGrabbing(); if (wasGrabbing) _camera.IMV_StopGrabbing();
            try
            {
                _camera.IMV_SetEnumFeatureSymbol("ExposureAuto", "Off"); EnsureOk(_camera.IMV_SetDoubleFeatureValue("ExposureTime", settings.ExposureUs), "设置大华曝光");
                _camera.IMV_SetEnumFeatureSymbol("GainAuto", "Off"); var gainCode = _camera.IMV_SetDoubleFeatureValue("GainRaw", settings.Gain); if (gainCode != IMVDefine.IMV_OK) EnsureOk(_camera.IMV_SetDoubleFeatureValue("Gain", settings.Gain), "设置大华增益");
                if (!string.IsNullOrWhiteSpace(settings.PixelFormat)) _camera.IMV_SetEnumFeatureSymbol("PixelFormat", settings.PixelFormat);
                if (!string.IsNullOrWhiteSpace(settings.TriggerSource)) _camera.IMV_SetEnumFeatureSymbol("TriggerSource", settings.TriggerSource);
                EnsureOk(_camera.IMV_SetEnumFeatureSymbol("TriggerMode", settings.TriggerMode), "设置大华触发模式");
                _camera.IMV_SetBoolFeatureValue("AcquisitionFrameRateEnable", settings.FrameRateEnabled); if (settings.FrameRateEnabled) _camera.IMV_SetDoubleFeatureValue("AcquisitionFrameRate", settings.FrameRate);
                _settings = CloneSettings(settings);
            }
            finally { if (wasGrabbing) EnsureOk(_camera.IMV_StartGrabbing(), "恢复大华采集"); }
        }

        public void LoadUserSet(string userSet) { ExecuteUserSet(userSet, "UserSetLoad"); }
        public void SaveUserSet(string userSet) { ExecuteUserSet(userSet, "UserSetSave"); }

        public CameraFrameData Acquire(int timeoutMs)
        {
            if (!IsConnected) throw new InvalidOperationException("大华相机未连接");
            if (string.Equals(_settings.TriggerMode, "On", StringComparison.OrdinalIgnoreCase) && string.Equals(_settings.TriggerSource, "Software", StringComparison.OrdinalIgnoreCase)) EnsureOk(_camera.IMV_ExecuteCommandFeature("TriggerSoftware"), "大华软触发");
            var frame = new IMVDefine.IMV_Frame(); EnsureOk(_camera.IMV_GetFrame(ref frame, (uint)timeoutMs), "采集大华图像");
            try
            {
                var width = (int)frame.frameInfo.width; var height = (int)frame.frameInfo.height; var pixels = new byte[width * height * 3];
                if (frame.frameInfo.pixelFormat == IMVDefine.IMV_EPixelType.gvspPixelBGR8)
                    Marshal.Copy(frame.pData, pixels, 0, pixels.Length);
                else if (frame.frameInfo.pixelFormat == IMVDefine.IMV_EPixelType.gvspPixelMono8)
                {
                    var mono = new byte[width * height]; Marshal.Copy(frame.pData, mono, 0, mono.Length);
                    for (var i = 0; i < mono.Length; i++) pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = mono[i];
                }
                else
                {
                    var buffer = Marshal.AllocHGlobal(pixels.Length);
                    try
                    {
                        var convert = new IMVDefine.IMV_PixelConvertParam
                        {
                            nWidth = frame.frameInfo.width, nHeight = frame.frameInfo.height, ePixelFormat = frame.frameInfo.pixelFormat,
                            pSrcData = frame.pData, nSrcDataLen = frame.frameInfo.size, nPaddingX = frame.frameInfo.paddingX, nPaddingY = frame.frameInfo.paddingY,
                            eBayerDemosaic = IMVDefine.IMV_EBayerDemosaic.demosaicBilinear, eDstPixelFormat = IMVDefine.IMV_EPixelType.gvspPixelBGR8,
                            pDstBuf = buffer, nDstBufSize = (uint)pixels.Length
                        };
                        EnsureOk(_camera.IMV_PixelConvert(ref convert), "转换大华图像"); Marshal.Copy(buffer, pixels, 0, pixels.Length);
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                }
                return new CameraFrameData { Width = width, Height = height, Stride = width * 3, BgrPixels = pixels, Timestamp = DateTime.Now };
            }
            finally { _camera.IMV_ReleaseFrame(ref frame); }
        }

        public void Disconnect() { if (_camera == null) return; try { if (_camera.IMV_IsGrabbing()) _camera.IMV_StopGrabbing(); if (_camera.IMV_IsOpen()) _camera.IMV_Close(); _camera.IMV_DestroyHandle(); } finally { _camera = null; IsConnected = false; } }
        private static List<IMVDefine.IMV_DeviceInfo> GetNativeDevices()
        {
            var native = new IMVDefine.IMV_DeviceList(); EnsureOk(MyCamera.IMV_EnumDevices(ref native, (uint)IMVDefine.IMV_EInterfaceType.interfaceTypeAll), "枚举大华相机");
            var result = new List<IMVDefine.IMV_DeviceInfo>(); var size = Marshal.SizeOf(typeof(IMVDefine.IMV_DeviceInfo));
            for (var i = 0; i < native.nDevNum; i++) result.Add((IMVDefine.IMV_DeviceInfo)Marshal.PtrToStructure(native.pDevInfo + size * i, typeof(IMVDefine.IMV_DeviceInfo)));
            return result;
        }
        private static bool IsDahuaDevice(IMVDefine.IMV_DeviceInfo info)
        {
            var identity = string.Join(" ", info.vendorName ?? string.Empty, info.manufactureInfo ?? string.Empty, info.modelName ?? string.Empty, info.cameraName ?? string.Empty);
            return identity.IndexOf("Dahua", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static void EnsureRuntime() { var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty; if (path.IndexOf(RuntimeDirectory, StringComparison.OrdinalIgnoreCase) < 0) Environment.SetEnvironmentVariable("PATH", RuntimeDirectory + ";" + path); }
        private void EnsureConnected() { if (!IsConnected || _camera == null) throw new InvalidOperationException("大华相机未连接"); }
        private void ExecuteUserSet(string userSet, string command) { EnsureConnected(); var value = string.IsNullOrWhiteSpace(userSet) ? "UserSet1" : userSet; var wasGrabbing = _camera.IMV_IsGrabbing(); if (wasGrabbing) _camera.IMV_StopGrabbing(); try { EnsureOk(_camera.IMV_SetEnumFeatureSymbol("UserSetSelector", value), "选择大华 UserSet"); EnsureOk(_camera.IMV_ExecuteCommandFeature(command), "执行大华 " + command); _settings.UserSet = value; } finally { if (wasGrabbing) EnsureOk(_camera.IMV_StartGrabbing(), "恢复大华采集"); } }
        private static CameraSettings CloneSettings(CameraSettings value) { return new CameraSettings { ExposureUs = value.ExposureUs, Gain = value.Gain, TriggerMode = value.TriggerMode, TriggerSource = value.TriggerSource, PixelFormat = value.PixelFormat, FrameRate = value.FrameRate, FrameRateEnabled = value.FrameRateEnabled, UserSet = value.UserSet }; }
        private static void EnsureOk(int code, string operation) { if (code != IMVDefine.IMV_OK) throw new InvalidOperationException(string.Format("{0}失败，MVSDK=0x{1:X8}", operation, code)); }
        public void Dispose() { Disconnect(); }
    }
}
