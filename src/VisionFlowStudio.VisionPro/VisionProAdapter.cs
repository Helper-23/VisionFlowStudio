using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.VisionPro
{
    public sealed class VisionProAdapter : IVisionProAdapter
    {
        public const string AssemblyDirectory = @"D:\Program Files\Cognex\VisionPro\ReferencedAssemblies";
        private readonly object _syncRoot = new object();
        private CogToolBlock _toolBlock;
        private string _loadedPath = string.Empty;
        private DateTime _loadedWriteTimeUtc;
        private bool _disposed;

        public VisionProAdapter()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            PrependPath(AssemblyDirectory);
        }

        public VisionPlatformStatus GetStatus()
        {
            var installed = File.Exists(Path.Combine(AssemblyDirectory, "Cognex.VisionPro.dll"));
            return new VisionPlatformStatus
            {
                Name = "VisionPro 7.3", Installed = installed, Loaded = _toolBlock != null,
                Message = !installed ? "未检测到 VisionPro SDK" : _toolBlock == null ? "VisionPro 7.3 SDK 已安装" : "已加载 " + Path.GetFileName(_loadedPath)
            };
        }

        public IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionProRunConfig config)
        {
            lock (_syncRoot)
            {
                if (config == null) throw new ArgumentNullException("config");
                EnsureLoaded(config.ToolBlockPath);
                var outputs = new List<VisionOutputDefinition>();
                foreach (CogToolBlockTerminal output in _toolBlock.Outputs)
                {
                    if (output == null || string.IsNullOrWhiteSpace(output.Name)) continue;
                    object value = null; try { value = output.Value; } catch { }
                    if (value is ICogImage) continue;
                    outputs.Add(new VisionOutputDefinition { Name = output.Name, DataType = MapDataType(value) });
                }
                return outputs;
            }
        }

        public NodeRunResult Run(VisionProRunConfig config, VisionContext context)
        {
            lock (_syncRoot)
            {
                var timer = Stopwatch.StartNew();
                ICogImage image = null;
                try
                {
                    ThrowIfDisposed();
                    EnsureLoaded(config.ToolBlockPath);
                    if (!string.IsNullOrWhiteSpace(config.ImagePath))
                    {
                        image = LoadImage(config.ImagePath);
                        SetRequiredInput(config.ImageInputName, image);
                    }
                    _toolBlock.Run();
                    var ok = ReadOk(config.OkOutputName);
                    timer.Stop();
                    var outputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "VisionProOK", ok }, { "VisionProRunStatus", _toolBlock.RunStatus == null ? string.Empty : _toolBlock.RunStatus.Message }
                    };
                    foreach (CogToolBlockTerminal output in _toolBlock.Outputs)
                    {
                        if (output == null || string.IsNullOrWhiteSpace(output.Name)) continue;
                        object value = null; try { value = output.Value; } catch { }
                        if (IsCommunicationValue(value)) outputs[output.Name] = value;
                    }
                    context.Set("VisionProOK", ok);
                    return new NodeRunResult
                    {
                        Status = ok ? NodeRunStatus.Ok : NodeRunStatus.Ng,
                        Message = string.Format("VisionPro ToolBlock：{0}，耗时 {1:0.0} ms", ok ? "OK" : "NG", timer.Elapsed.TotalMilliseconds),
                        CostMs = timer.Elapsed.TotalMilliseconds, Outputs = outputs
                    };
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    return new NodeRunResult { Status = NodeRunStatus.Error, Message = "VisionPro：" + ex.Message, CostMs = timer.Elapsed.TotalMilliseconds };
                }
                finally
                {
                    var disposable = image as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            }
        }

        private static bool IsCommunicationValue(object value)
        {
            if (value == null || value is ICogImage) return false;
            var type = value.GetType();
            if (type.IsPrimitive || value is decimal || value is string) return true;
            if (!type.IsArray) return false;
            var elementType = type.GetElementType();
            return elementType != null && (elementType.IsPrimitive || elementType == typeof(decimal) || elementType == typeof(string));
        }

        private static string MapDataType(object value)
        {
            if (value == null) return "String";
            var type = value.GetType(); var array = type.IsArray; if (array) type = type.GetElementType();
            var name = type == typeof(bool) ? "Bool" : type == typeof(byte) ? "Byte" : type == typeof(short) ? "Int16" : type == typeof(ushort) ? "UInt16" : type == typeof(int) ? "Int32" : type == typeof(uint) ? "UInt32" : type == typeof(long) ? "Int64" : type == typeof(ulong) ? "UInt64" : type == typeof(float) ? "Float" : type == typeof(double) || type == typeof(decimal) ? "Double" : "String";
            return array && name != "String" ? name + "Array" : name;
        }

        private void EnsureLoaded(string path)
        {
            var fullPath = Path.GetFullPath(path ?? string.Empty);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到 VisionPro VPP", fullPath);
            var writeTime = File.GetLastWriteTimeUtc(fullPath);
            if (_toolBlock != null && string.Equals(_loadedPath, fullPath, StringComparison.OrdinalIgnoreCase) && writeTime == _loadedWriteTimeUtc) return;
            _toolBlock = CogSerializer.LoadObjectFromFile(fullPath) as CogToolBlock;
            if (_toolBlock == null) throw new InvalidOperationException("VPP 中不包含 CogToolBlock");
            _loadedPath = fullPath; _loadedWriteTimeUtc = writeTime;
        }

        private static ICogImage LoadImage(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到 VisionPro 输入图像", fullPath);
            using (var source = new Bitmap(fullPath))
            using (var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb))
            {
                using (var graphics = Graphics.FromImage(normalized)) graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                return new CogImage24PlanarColor(normalized);
            }
        }

        private void SetRequiredInput(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("VisionPro 图像输入名不能为空");
            try { _toolBlock.Inputs[name].Value = value; }
            catch (Exception ex) { throw new InvalidOperationException("ToolBlock 不存在输入或类型不匹配：" + name, ex); }
        }

        private bool ReadOk(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return _toolBlock.RunStatus != null && _toolBlock.RunStatus.Result == CogToolResultConstants.Accept;
            try { var value = _toolBlock.Outputs[name].Value; return value is bool ? (bool)value : value != null && Convert.ToInt32(value) != 0; }
            catch (Exception ex) { throw new InvalidOperationException("ToolBlock 不存在 OK 输出：" + name, ex); }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            var path = Path.Combine(AssemblyDirectory, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        private static void PrependPath(string path) { var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty; if (current.IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0) Environment.SetEnvironmentVariable("PATH", path + ";" + current); }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(GetType().Name); }
        public void Dispose() { _disposed = true; _toolBlock = null; _loadedWriteTimeUtc = DateTime.MinValue; AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly; }
    }
}
