using System;
using System.Collections.Generic;
using System.Linq;
using Basler.Pylon;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Cameras
{
    public sealed class BaslerCameraProvider : ICameraProvider
    {
        private const string RuntimeDirectory = @"C:\Program Files\Basler\pylon 6\Runtime\x64";
        private Camera _camera;
        private CameraSettings _settings = new CameraSettings();
        public BaslerCameraProvider() { var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty; if (path.IndexOf(RuntimeDirectory, StringComparison.OrdinalIgnoreCase) < 0) Environment.SetEnvironmentVariable("PATH", RuntimeDirectory + ";" + path); }
        public string Vendor { get { return "Basler"; } }
        public bool IsConnected { get { return _camera != null && _camera.IsOpen; } }

        public IReadOnlyList<CameraDeviceInfo> Enumerate()
        {
            return CameraFinder.Enumerate().Select(info => new CameraDeviceInfo
            {
                Vendor = Vendor,
                DeviceId = info[CameraInfoKey.SerialNumber],
                SerialNumber = info[CameraInfoKey.SerialNumber],
                DisplayName = info[CameraInfoKey.FriendlyName]
            }).ToArray();
        }

        public void Connect(string deviceId)
        {
            Disconnect();
            var info = CameraFinder.Enumerate().FirstOrDefault(x => string.IsNullOrWhiteSpace(deviceId) || x[CameraInfoKey.SerialNumber] == deviceId);
            if (info == null) throw new InvalidOperationException("未找到 Basler 相机：" + deviceId);
            _camera = new Camera(info);
            _camera.CameraOpened += Configuration.AcquireSingleFrame;
            _camera.Open();
        }

        public CameraSettings GetSettings()
        {
            EnsureConnected();
            return new CameraSettings
            {
                ExposureUs = GetFloat("ExposureTime", _settings.ExposureUs), Gain = GetFloat("Gain", _settings.Gain),
                TriggerMode = GetEnum("TriggerMode", _settings.TriggerMode), TriggerSource = GetEnum("TriggerSource", _settings.TriggerSource),
                PixelFormat = GetEnum("PixelFormat", _settings.PixelFormat), FrameRate = GetFloat("AcquisitionFrameRate", _settings.FrameRate),
                FrameRateEnabled = GetBool("AcquisitionFrameRateEnable", _settings.FrameRateEnabled), UserSet = _settings.UserSet
            };
        }

        public void ApplySettings(CameraSettings settings)
        {
            EnsureConnected(); if (settings == null) return;
            SetEnum("ExposureAuto", "Off"); SetFloat("ExposureTime", settings.ExposureUs); SetEnum("GainAuto", "Off"); SetFloat("Gain", settings.Gain);
            SetEnum("PixelFormat", settings.PixelFormat); SetEnum("TriggerMode", settings.TriggerMode); if (string.Equals(settings.TriggerMode, "On", StringComparison.OrdinalIgnoreCase)) SetEnum("TriggerSource", settings.TriggerSource);
            SetBool("AcquisitionFrameRateEnable", settings.FrameRateEnabled); if (settings.FrameRateEnabled) SetFloat("AcquisitionFrameRate", settings.FrameRate);
            _settings = settings;
        }

        public void LoadUserSet(string userSet) { EnsureConnected(); SetEnum("UserSetSelector", NormalizeUserSet(userSet)); Execute("UserSetLoad"); _settings.UserSet = NormalizeUserSet(userSet); }
        public void SaveUserSet(string userSet) { EnsureConnected(); SetEnum("UserSetSelector", NormalizeUserSet(userSet)); Execute("UserSetSave"); _settings.UserSet = NormalizeUserSet(userSet); }

        public CameraFrameData Acquire(int timeoutMs)
        {
            if (!IsConnected) throw new InvalidOperationException("Basler 相机未连接");
            _camera.StreamGrabber.Start(1, GrabStrategy.OneByOne, GrabLoop.ProvidedByUser);
            if (string.Equals(_settings.TriggerMode, "On", StringComparison.OrdinalIgnoreCase) && string.Equals(_settings.TriggerSource, "Software", StringComparison.OrdinalIgnoreCase))
            { _camera.WaitForFrameTriggerReady(timeoutMs, TimeoutHandling.ThrowException); _camera.ExecuteSoftwareTrigger(); }
            using (var result = _camera.StreamGrabber.RetrieveResult(timeoutMs, TimeoutHandling.ThrowException))
            {
                if (!result.GrabSucceeded) throw new InvalidOperationException(result.ErrorDescription);
                var pixels = new byte[result.Width * result.Height * 3];
                using (var converter = new PixelDataConverter())
                {
                    converter.OutputPixelFormat = PixelType.BGR8packed;
                    converter.Convert(pixels, result);
                }
                return new CameraFrameData { Width = result.Width, Height = result.Height, Stride = result.Width * 3, BgrPixels = pixels, Timestamp = DateTime.Now };
            }
        }
        private void EnsureConnected() { if (!IsConnected) throw new InvalidOperationException("Basler 相机未连接"); }
        private static string NormalizeUserSet(string value) { return string.IsNullOrWhiteSpace(value) ? "UserSet1" : value; }
        private void SetFloat(string name, double value) { var parameter = _camera.Parameters[name] as IFloatParameter; if (parameter != null && parameter.IsWritable) parameter.SetValue(value); }
        private double GetFloat(string name, double fallback) { var parameter = _camera.Parameters[name] as IFloatParameter; return parameter != null && parameter.IsReadable ? parameter.GetValue() : fallback; }
        private void SetEnum(string name, string value) { if (string.IsNullOrWhiteSpace(value)) return; var parameter = _camera.Parameters[name] as IEnumParameter; if (parameter != null && parameter.IsWritable && parameter.CanSetValue(value)) parameter.SetValue(value); }
        private string GetEnum(string name, string fallback) { var parameter = _camera.Parameters[name] as IEnumParameter; return parameter != null && parameter.IsReadable ? parameter.GetValue() : fallback; }
        private void SetBool(string name, bool value) { var parameter = _camera.Parameters[name] as IBooleanParameter; if (parameter != null && parameter.IsWritable) parameter.SetValue(value); }
        private bool GetBool(string name, bool fallback) { var parameter = _camera.Parameters[name] as IBooleanParameter; return parameter != null && parameter.IsReadable ? parameter.GetValue() : fallback; }
        private void Execute(string name) { var parameter = _camera.Parameters[name] as ICommandParameter; if (parameter == null || !parameter.IsWritable) throw new InvalidOperationException("相机不支持命令：" + name); parameter.Execute(); }

        public void Disconnect()
        {
            if (_camera == null) return;
            if (_camera.StreamGrabber.IsGrabbing) _camera.StreamGrabber.Stop();
            if (_camera.IsOpen) _camera.Close();
            _camera.Dispose(); _camera = null;
        }
        public void Dispose() { Disconnect(); }
    }
}
