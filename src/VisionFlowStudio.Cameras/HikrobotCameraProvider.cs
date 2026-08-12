using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using MvCamCtrl.NET;
using MvCamCtrl.NET.CameraParams;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Cameras
{
    public sealed class HikrobotCameraProvider : ICameraProvider
    {
        private const string RuntimeDirectory = @"C:\Program Files (x86)\Common Files\MVS\Runtime\Win64_x64";
        private CCamera _camera;
        private string _connectedDeviceId = string.Empty;
        private CameraSettings _settings = new CameraSettings();
        public string Vendor { get { return "Hikrobot"; } }
        public bool IsConnected { get; private set; }

        public IReadOnlyList<CameraDeviceInfo> Enumerate()
        {
            EnsureRuntime();
            var devices = new List<CCameraInfo>();
            EnsureOk(CSystem.EnumDevices(CSystem.MV_GIGE_DEVICE | CSystem.MV_USB_DEVICE, ref devices), "枚举海康相机");
            return devices.Select(ToInfo).ToArray();
        }

        public void Connect(string deviceId)
        {
            if (IsConnected && _camera != null && string.Equals(_connectedDeviceId, deviceId ?? string.Empty, StringComparison.OrdinalIgnoreCase)) return;
            Disconnect(); EnsureRuntime();
            var devices = new List<CCameraInfo>();
            EnsureOk(CSystem.EnumDevices(CSystem.MV_GIGE_DEVICE | CSystem.MV_USB_DEVICE, ref devices), "枚举海康相机");
            var selected = devices.FirstOrDefault(x => string.IsNullOrWhiteSpace(deviceId) || GetSerial(x) == deviceId);
            if (selected == null) throw new InvalidOperationException("未找到海康相机：" + deviceId);
            var camera = new CCamera(); var handleCreated = false; var opened = false;
            try
            {
                EnsureOk(camera.CreateHandle(ref selected), "创建海康相机句柄"); handleCreated = true;
                // Keep the access mode identical to Hikrobot's official BasicDemo.
                // The SDK chooses the correct exclusive/control privilege for the device.
                EnsureOk(camera.OpenDevice(), "打开海康相机"); opened = true;
                if (selected.nTLayerType == CSystem.MV_GIGE_DEVICE)
                {
                    var packetSize = camera.GIGE_GetOptimalPacketSize();
                    if (packetSize > 0) camera.SetIntValue("GevSCPSPacketSize", (uint)packetSize);
                }
                camera.SetEnumValue("TriggerMode", (uint)MV_CAM_TRIGGER_MODE.MV_TRIGGER_MODE_OFF);
                EnsureOk(camera.StartGrabbing(), "启动海康采集");
                _camera = camera; _connectedDeviceId = GetSerial(selected); IsConnected = true;
            }
            catch
            {
                if (opened) { try { camera.CloseDevice(); } catch { } }
                if (handleCreated) { try { camera.DestroyHandle(); } catch { } }
                throw;
            }
        }

        public CameraSettings GetSettings()
        {
            EnsureConnected(); var exposure = new CFloatValue(); var gain = new CFloatValue(); var rate = new CFloatValue(); bool rateEnabled = _settings.FrameRateEnabled;
            if (_camera.GetFloatValue("ExposureTime", ref exposure) == CErrorDefine.MV_OK) _settings.ExposureUs = exposure.CurValue;
            if (_camera.GetFloatValue("Gain", ref gain) == CErrorDefine.MV_OK) _settings.Gain = gain.CurValue;
            if (_camera.GetFloatValue("AcquisitionFrameRate", ref rate) == CErrorDefine.MV_OK) _settings.FrameRate = rate.CurValue;
            if (_camera.GetBoolValue("AcquisitionFrameRateEnable", ref rateEnabled) == CErrorDefine.MV_OK) _settings.FrameRateEnabled = rateEnabled;
            return CloneSettings(_settings);
        }

        public void ApplySettings(CameraSettings settings)
        {
            EnsureConnected(); if (settings == null) return; _camera.StopGrabbing();
            try
            {
                _camera.SetEnumValueByString("ExposureAuto", "Off"); EnsureOk(_camera.SetFloatValue("ExposureTime", (float)settings.ExposureUs), "设置海康曝光");
                _camera.SetEnumValueByString("GainAuto", "Off"); EnsureOk(_camera.SetFloatValue("Gain", (float)settings.Gain), "设置海康增益");
                if (!string.IsNullOrWhiteSpace(settings.PixelFormat)) _camera.SetEnumValueByString("PixelFormat", settings.PixelFormat);
                if (!string.IsNullOrWhiteSpace(settings.TriggerSource)) _camera.SetEnumValueByString("TriggerSource", settings.TriggerSource);
                EnsureOk(_camera.SetEnumValueByString("TriggerMode", settings.TriggerMode), "设置海康触发模式");
                _camera.SetBoolValue("AcquisitionFrameRateEnable", settings.FrameRateEnabled); if (settings.FrameRateEnabled) _camera.SetFloatValue("AcquisitionFrameRate", (float)settings.FrameRate);
                _settings = CloneSettings(settings);
            }
            finally { EnsureOk(_camera.StartGrabbing(), "恢复海康采集"); }
        }

        public void LoadUserSet(string userSet) { ExecuteUserSet(userSet, "UserSetLoad"); }
        public void SaveUserSet(string userSet) { ExecuteUserSet(userSet, "UserSetSave"); }

        public CameraFrameData Acquire(int timeoutMs)
        {
            if (!IsConnected) throw new InvalidOperationException("海康相机未连接");
            if (string.Equals(_settings.TriggerMode, "On", StringComparison.OrdinalIgnoreCase) && string.Equals(_settings.TriggerSource, "Software", StringComparison.OrdinalIgnoreCase)) EnsureOk(_camera.SetCommandValue("TriggerSoftware"), "海康软触发");
            var frame = new CFrameout(); EnsureOk(_camera.GetImageBuffer(ref frame, timeoutMs), "采集海康图像");
            try { using (var bitmap = _camera.ImageToBitmap(ref frame)) { if (bitmap == null) throw new InvalidOperationException("海康像素格式暂不支持"); return FrameUtility.FromBitmap(bitmap); } }
            finally { _camera.FreeImageBuffer(ref frame); }
        }

        public void Disconnect()
        {
            var camera = _camera; _camera = null; _connectedDeviceId = string.Empty; IsConnected = false;
            if (camera == null) return;
            try { camera.StopGrabbing(); } catch { }
            try { camera.CloseDevice(); } catch { }
            try { camera.DestroyHandle(); } catch { }
        }
        private static CameraDeviceInfo ToInfo(CCameraInfo info) { return new CameraDeviceInfo { Vendor = "Hikrobot", DeviceId = GetSerial(info), SerialNumber = GetSerial(info), IpAddress = info is CGigECameraInfo ? FormatIp(((CGigECameraInfo)info).nCurrentIp) : string.Empty, DisplayName = info is CGigECameraInfo ? ((CGigECameraInfo)info).chModelName : ((CUSBCameraInfo)info).chModelName }; }
        private static string GetSerial(CCameraInfo info) { return info is CGigECameraInfo ? ((CGigECameraInfo)info).chSerialNumber : info is CUSBCameraInfo ? ((CUSBCameraInfo)info).chSerialNumber : string.Empty; }
        private static string FormatIp(uint ip) { return string.Format("{0}.{1}.{2}.{3}", (ip >> 24) & 255, (ip >> 16) & 255, (ip >> 8) & 255, ip & 255); }
        private void EnsureConnected() { if (!IsConnected || _camera == null) throw new InvalidOperationException("海康相机未连接"); }
        private void ExecuteUserSet(string userSet, string command) { EnsureConnected(); var value = string.IsNullOrWhiteSpace(userSet) ? "UserSet1" : userSet; _camera.StopGrabbing(); try { EnsureOk(_camera.SetEnumValueByString("UserSetSelector", value), "选择海康 UserSet"); EnsureOk(_camera.SetCommandValue(command), "执行海康 " + command); _settings.UserSet = value; } finally { EnsureOk(_camera.StartGrabbing(), "恢复海康采集"); } }
        private static CameraSettings CloneSettings(CameraSettings value) { return new CameraSettings { ExposureUs = value.ExposureUs, Gain = value.Gain, TriggerMode = value.TriggerMode, TriggerSource = value.TriggerSource, PixelFormat = value.PixelFormat, FrameRate = value.FrameRate, FrameRateEnabled = value.FrameRateEnabled, UserSet = value.UserSet }; }
        private static void EnsureRuntime() { if (!Directory.Exists(RuntimeDirectory)) throw new DirectoryNotFoundException(RuntimeDirectory); var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty; if (path.IndexOf(RuntimeDirectory, StringComparison.OrdinalIgnoreCase) < 0) Environment.SetEnvironmentVariable("PATH", RuntimeDirectory + ";" + path); }
        private static void EnsureOk(int code, string operation)
        {
            if (code == CErrorDefine.MV_OK) return;
            if (code == CErrorDefine.MV_E_ACCESS_DENIED) throw new InvalidOperationException(string.Format("{0}失败：设备无访问权限，请确认 MVS/MV Viewer 等软件已断开该相机。MVS=0x{1:X8}", operation, code));
            throw new InvalidOperationException(string.Format("{0}失败，MVS=0x{1:X8}", operation, code));
        }
        public void Dispose() { Disconnect(); }
    }
}
