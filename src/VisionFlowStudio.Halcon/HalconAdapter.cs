using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Xml.Linq;
using HalconDotNet;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Halcon
{
    public sealed class HalconAdapter : IHalconAdapter
    {
        public const string RootDirectory = @"D:\Program Files\MVTec\HALCON-18.11-Progress";
        public const string ManagedDirectory = RootDirectory + @"\bin\dotnet35";
        public const string NativeDirectory = RootDirectory + @"\bin\x64-win64";
        private readonly object _syncRoot = new object();
        private HDevEngine _engine;
        private string _loadedProcedurePath = string.Empty;
        private DateTime _loadedWriteTimeUtc;
        private long _loadedFileLength = -1;
        private bool _disposed;

        public HalconAdapter()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            PrependPath(NativeDirectory);
            Environment.SetEnvironmentVariable("HALCONROOT", RootDirectory);
            Environment.SetEnvironmentVariable("HALCONARCH", "x64-win64");
            _engine = new HDevEngine();
        }

        public VisionPlatformStatus GetStatus()
        {
            var installed = File.Exists(Path.Combine(ManagedDirectory, "halcondotnet.dll")) && File.Exists(Path.Combine(NativeDirectory, "halcon.dll"));
            var message = installed ? "HALCON 18.11 SDK 已安装" : "未检测到 HALCON 18.11 SDK";
            if (installed)
            {
                try { HTuple version; HOperatorSet.GetSystem("version", out version); message = "HALCON " + version.S + " Runtime 可用"; }
                catch (Exception ex) { message = "HALCON 已安装，授权检测失败：" + ex.Message; }
            }
            return new VisionPlatformStatus { Name = "HALCON 18.11", Installed = installed, Loaded = installed, Message = message };
        }

        public IReadOnlyList<VisionOutputDefinition> GetOutputs(HalconRunConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");
            var path = Path.GetFullPath(config.ProcedurePath ?? string.Empty);
            if (!File.Exists(path)) throw new FileNotFoundException("HALCON Procedure not found", path);
            var document = XDocument.Load(path);
            var procedure = document.Descendants("procedure").FirstOrDefault();
            var outputGroup = procedure == null || procedure.Element("interface") == null ? null : procedure.Element("interface").Element("oc");
            if (outputGroup == null) return new List<VisionOutputDefinition>();
            return outputGroup.Elements("par")
                .Select(x => new VisionOutputDefinition
                {
                    Name = (string)x.Attribute("name") ?? string.Empty,
                    DataType = ((int?)x.Attribute("dimension") ?? 0) == 0 ? "String" : "StringArray"
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToList();
        }

        public NodeRunResult Run(HalconRunConfig config, VisionContext context)
        {
            lock (_syncRoot)
            {
                var timer = Stopwatch.StartNew();
                HObject image = null;
                try
                {
                    ThrowIfDisposed();
                    var procedureFile = Path.GetFullPath(config.ProcedurePath ?? string.Empty);
                    if (!File.Exists(procedureFile)) throw new FileNotFoundException("找不到 HALCON Procedure", procedureFile);
                    EnsureFreshEngine(procedureFile);
                    _engine.SetProcedurePath(Path.GetDirectoryName(procedureFile));
                    var procedure = new HDevProcedure(Path.GetFileNameWithoutExtension(procedureFile));
                    var call = new HDevProcedureCall(procedure);
                    if (!string.IsNullOrWhiteSpace(config.ImagePath))
                    {
                        var imagePath = Path.GetFullPath(config.ImagePath);
                        if (!File.Exists(imagePath)) throw new FileNotFoundException("找不到 HALCON 输入图像", imagePath);
                        HOperatorSet.ReadImage(out image, imagePath);
                        call.SetInputIconicParamObject(config.ImageInputName, image);
                    }
                    foreach (var pair in config.ControlInputs)
                        call.SetInputCtrlParamTuple(pair.Key, ParseTuple(pair.Value));
                    call.Execute();
                    var outputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var output in GetOutputs(config))
                    {
                        try { outputs[output.Name] = ConvertTuple(call.GetOutputCtrlParamTuple(output.Name)); }
                        catch { }
                    }
                    var ok = true;
                    if (!string.IsNullOrWhiteSpace(config.OkOutputName))
                    {
                        var tuple = call.GetOutputCtrlParamTuple(config.OkOutputName);
                        ok = tuple.Length > 0 && (tuple[0].Type == HTupleType.STRING
                            ? !string.Equals(tuple[0].S, "false", StringComparison.OrdinalIgnoreCase) && tuple[0].S != "0"
                            : tuple[0].D != 0);
                    }
                    timer.Stop();
                    context.Set("HalconOK", ok);
                    outputs["HalconOK"] = ok;
                    outputs["Procedure"] = Path.GetFileName(procedureFile);
                    return new NodeRunResult
                    {
                        Status = ok ? NodeRunStatus.Ok : NodeRunStatus.Ng,
                        Message = string.Format("HALCON Procedure：{0}，耗时 {1:0.0} ms", ok ? "OK" : "NG", timer.Elapsed.TotalMilliseconds),
                        CostMs = timer.Elapsed.TotalMilliseconds,
                        Outputs = outputs
                    };
                }
                catch (HOperatorException ex)
                {
                    timer.Stop();
                    return new NodeRunResult { Status = NodeRunStatus.Error, Message = string.Format("HALCON[{0}]：{1}", ex.GetErrorCode(), ex.Message), CostMs = timer.Elapsed.TotalMilliseconds };
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    return new NodeRunResult { Status = NodeRunStatus.Error, Message = "HALCON：" + ex.Message, CostMs = timer.Elapsed.TotalMilliseconds };
                }
                finally { if (image != null) image.Dispose(); }
            }
        }

        private static HTuple ParseTuple(string value)
        {
            int intValue; double doubleValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)) return new HTuple(intValue);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue)) return new HTuple(doubleValue);
            return new HTuple(value ?? string.Empty);
        }
        private static object ConvertTuple(HTuple tuple)
        {
            if (tuple == null || tuple.Length == 0) return null;
            if (tuple.Type == HTupleType.INTEGER) return tuple.Length == 1 ? (object)tuple.I : tuple.ToIArr();
            if (tuple.Type == HTupleType.LONG) return tuple.Length == 1 ? (object)tuple.L : tuple.ToLArr();
            if (tuple.Type == HTupleType.DOUBLE) return tuple.Length == 1 ? (object)tuple.D : tuple.ToDArr();
            if (tuple.Type == HTupleType.STRING) return tuple.Length == 1 ? (object)tuple.S : tuple.ToSArr();
            return tuple.ToString();
        }
        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args) { var path = Path.Combine(ManagedDirectory, new AssemblyName(args.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; }
        private static void PrependPath(string path) { var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty; if (current.IndexOf(path, StringComparison.OrdinalIgnoreCase) < 0) Environment.SetEnvironmentVariable("PATH", path + ";" + current); }
        private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(GetType().Name); }

        public void ReloadProcedure(string path)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                var fullPath = Path.GetFullPath(path ?? string.Empty);
                if (!File.Exists(fullPath)) throw new FileNotFoundException("找不到 HALCON Procedure", fullPath);
                RecreateEngine(); UpdateFingerprint(fullPath);
            }
        }

        private void EnsureFreshEngine(string path)
        {
            var file = new FileInfo(path);
            if (!string.Equals(_loadedProcedurePath, file.FullName, StringComparison.OrdinalIgnoreCase) || _loadedWriteTimeUtc != file.LastWriteTimeUtc || _loadedFileLength != file.Length)
            {
                RecreateEngine(); UpdateFingerprint(file.FullName);
            }
        }

        private void UpdateFingerprint(string path)
        {
            var file = new FileInfo(path); _loadedProcedurePath = file.FullName; _loadedWriteTimeUtc = file.LastWriteTimeUtc; _loadedFileLength = file.Length;
        }

        private void RecreateEngine()
        {
            if (_engine != null)
            {
                // HALCON 18.11 keeps loaded procedures in a process-level cache.
                // Disposing only the managed HDevEngine wrapper is not enough.
                _engine.UnloadAllProcedures(); _engine.Dispose();
            }
            _engine = new HDevEngine();
        }

        public void Dispose() { if (_disposed) return; _disposed = true; if (_engine != null) { _engine.UnloadAllProcedures(); _engine.Dispose(); } _engine = null; AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly; }
    }
}
