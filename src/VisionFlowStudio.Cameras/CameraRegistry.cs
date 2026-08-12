using System;
using System.Collections.Generic;
using System.Linq;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Cameras
{
    public sealed class CameraRegistry : IDisposable
    {
        private readonly Dictionary<string, ICameraProvider> _providers;
        public CameraRegistry()
        {
            _providers = new ICameraProvider[] { new BaslerCameraProvider(), new HikrobotCameraProvider(), new DahuaCameraProvider() }
                .ToDictionary(x => x.Vendor, StringComparer.OrdinalIgnoreCase);
        }
        public IReadOnlyCollection<string> Vendors { get { return _providers.Keys.ToArray(); } }
        public ICameraProvider Get(string vendor) { ICameraProvider provider; return _providers.TryGetValue(vendor ?? string.Empty, out provider) ? provider : null; }
        public ICameraProvider Connect(string vendor, string deviceId)
        {
            var selected = Get(vendor);
            if (selected == null) throw new InvalidOperationException("未注册相机厂商：" + vendor);
            foreach (var provider in _providers.Values)
                if (!ReferenceEquals(provider, selected) && provider.IsConnected) provider.Disconnect();
            if (!selected.IsConnected) selected.Connect(deviceId);
            return selected;
        }
        public void Disconnect(string vendor)
        {
            var provider = Get(vendor);
            if (provider != null) provider.Disconnect();
        }
        public IReadOnlyList<CameraDeviceInfo> EnumerateAll()
        {
            var result = new List<CameraDeviceInfo>();
            foreach (var provider in _providers.Values)
            {
                try { result.AddRange(provider.Enumerate()); }
                catch (Exception ex) { result.Add(new CameraDeviceInfo { Vendor = provider.Vendor, DeviceId = string.Empty, DisplayName = "SDK错误：" + ex.Message }); }
            }
            return result;
        }
        public void Dispose() { foreach (var provider in _providers.Values) provider.Dispose(); }
    }
}
