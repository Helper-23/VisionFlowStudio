using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using VM.Core;
using VM.PlatformSDKCS;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.VisionMaster
{
    public sealed class VisionMasterAdapter : IVisionMasterAdapter
    {
        private readonly object _syncRoot = new object();
        private string _loadedSolutionPath = string.Empty;
        private ImageBaseData _activeInputImage;
        private bool _disposed;

        public VisionMasterAdapter()
        {
            VisionMasterRuntime.Initialize();
        }

        public VisionPlatformStatus GetStatus()
        {
            return new VisionPlatformStatus
            {
                Name = "VisionMaster 4.4",
                Installed = VisionMasterRuntime.IsInstalled,
                Loaded = !_disposed && VmSolution.Instance != null && !string.IsNullOrWhiteSpace(_loadedSolutionPath),
                Message = !VisionMasterRuntime.IsInstalled
                    ? "未检测到 VisionMaster 4.4 SDK"
                    : string.IsNullOrWhiteSpace(_loadedSolutionPath)
                        ? "SDK 已安装，等待加载 Solution"
                        : "已加载 " + Path.GetFileName(_loadedSolutionPath)
            };
        }

        public IReadOnlyList<string> LoadSolution(string path, string password)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                var fullPath = Path.GetFullPath(path ?? string.Empty);
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("找不到 VisionMaster Solution 文件", fullPath);

                return VisionMasterRuntime.InvokeWithApplicationDirectory(delegate
                {
                    if (!string.Equals(_loadedSolutionPath, fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        CloseSolutionCore();
                        VmSolution.Load(fullPath, password ?? string.Empty);
                        _loadedSolutionPath = fullPath;
                    }

                    var names = new List<string>();
                    var list = VmSolution.Instance.GetAllProcedureList();
                    for (var index = 0; index < list.nNum; index++)
                        names.Add(list.astProcessInfo[index].strProcessName);
                    return names;
                });
            }
        }

        public IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionMasterRunConfig config)
        {
            lock (_syncRoot)
            {
                if (config == null) throw new ArgumentNullException("config");
                var fullPath = Path.GetFullPath(config.SolutionPath ?? string.Empty);
                var keepLoaded = !string.IsNullOrWhiteSpace(_loadedSolutionPath) && string.Equals(_loadedSolutionPath, fullPath, StringComparison.OrdinalIgnoreCase);
                try
                {
                    LoadSolution(fullPath, config.SolutionPassword);
                    return VisionMasterRuntime.InvokeWithApplicationDirectory(delegate
                    {
                        var procedure = VmSolution.Instance[config.ProcedureName] as VmProcedure;
                        if (procedure == null) throw new InvalidOperationException("Solution does not contain procedure: " + config.ProcedureName);
                        var outputs = new List<VisionOutputDefinition>();
                        foreach (VmIO output in procedure.Outputs)
                        {
                            if (output == null || IsImageType(output.TypeName) || !IsPublishedOutputName(output.Name)) continue;
                            outputs.Add(new VisionOutputDefinition { Name = NormalizeOutputName(output.Name), DataType = MapDataType(output.TypeName) });
                        }
                        return outputs;
                    });
                }
                finally
                {
                    // Output discovery is metadata-only. Keeping a temporarily loaded
                    // VisionMaster solution alive claims Hikrobot's transport layer and
                    // makes MVS OpenDevice return MV_E_ACCESS_DENIED.
                    if (!keepLoaded) CloseSolutionCore();
                }
            }
        }

        public NodeRunResult Run(VisionMasterRunConfig config, VisionContext context)
        {
            lock (_syncRoot)
            {
                var timer = Stopwatch.StartNew();
                try
                {
                    ThrowIfDisposed();
                    if (config == null)
                        throw new ArgumentNullException("config");
                    LoadSolution(config.SolutionPath, config.SolutionPassword);

                    var procedure = VmSolution.Instance[config.ProcedureName] as VmProcedure;
                    if (procedure == null)
                        throw new InvalidOperationException("Solution 中不存在流程：" + config.ProcedureName);

                    ApplyVariables(procedure, config.Variables);
                    var injection = VisionMasterRuntime.InvokeWithApplicationDirectory(delegate { return RunWithOptionalImage(procedure, config); });
                    var isOk = ReadOk(procedure, config.OkOutputName);
                    timer.Stop();

                    var outputs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "VisionMasterOK", isOk },
                        { "VisionMasterProcessTimeMs", procedure.ProcessTime },
                        { "ProcedureName", config.ProcedureName }
                    };
                    foreach (VmIO output in procedure.Outputs)
                    {
                        if (output == null || IsImageType(output.TypeName) || !IsPublishedOutputName(output.Name)) continue;
                        object value;
                        if (TryReadPublishedOutput(procedure, output, out value)) outputs[NormalizeOutputName(output.Name)] = value;
                    }
                    if (injection != null)
                    {
                        outputs["VisionMasterInputImagePath"] = injection.Path;
                        outputs["VisionMasterInputImageName"] = injection.InputName;
                        outputs["VisionMasterInputImageWidth"] = injection.SourceWidth;
                        outputs["VisionMasterInputImageHeight"] = injection.SourceHeight;
                        outputs["VisionMasterInputRuntimeReady"] = injection.RuntimeReady;
                        outputs["VisionMasterInputRuntimeType"] = injection.RuntimeType;
                        outputs["VisionMasterInputRuntimeWidth"] = injection.RuntimeWidth;
                        outputs["VisionMasterInputRuntimeHeight"] = injection.RuntimeHeight;
                    }
                    foreach (var pair in outputs)
                        context.Set(pair.Key, pair.Value);

                    return new NodeRunResult
                    {
                        Status = isOk ? NodeRunStatus.Ok : NodeRunStatus.Ng,
                        Message = string.Format(
                            "VisionMaster 流程运行完成：{0}，VM耗时 {1:0.0} ms",
                            isOk ? "OK" : "NG",
                            procedure.ProcessTime),
                        CostMs = timer.Elapsed.TotalMilliseconds,
                        Outputs = outputs
                    };
                }
                catch (Exception ex)
                {
                    timer.Stop();
                    var errorCode = VisionMasterRuntime.GetErrorCode(ex);
                    var suffix = errorCode == 0 ? string.Empty : string.Format("（VM错误码 0x{0:X8}）", errorCode);
                    return new NodeRunResult
                    {
                        Status = NodeRunStatus.Error,
                        Message = ex.Message + suffix,
                        CostMs = timer.Elapsed.TotalMilliseconds,
                        Outputs = new Dictionary<string, object>
                        {
                            { "VisionMasterErrorCode", errorCode }
                        }
                    };
                }
            }
        }

        private static bool IsImageType(string typeName)
        {
            return (typeName ?? string.Empty).IndexOf("IMAGE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPublishedOutputName(string name)
        {
            var value = (name ?? string.Empty).Trim();
            return value.Length > 2 && value[0] == '%' && value[value.Length - 1] == '%';
        }

        private static string NormalizeOutputName(string name)
        {
            return (name ?? string.Empty).Trim().Trim('%');
        }

        private static bool TryReadPublishedOutput(VmProcedure procedure, VmIO output, out object value)
        {
            value = null;
            var typeName = (output.TypeName ?? string.Empty).ToUpperInvariant();
            var rawName = output.Name ?? string.Empty;
            var normalizedName = NormalizeOutputName(rawName);
            var candidates = new[] { rawName, normalizedName }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (var candidate in candidates)
            {
                try
                {
                    if (typeName.Contains("STRING"))
                    {
                        var result = procedure.ModuResult.GetOutputString(candidate);
                        if (result.nValueNum <= 0 || result.astStringVal == null) continue;
                        var values = result.astStringVal.Take(result.nValueNum).Select(x => x.strValue ?? string.Empty).ToArray();
                        value = values.Length == 1 ? (object)values[0] : values;
                        return true;
                    }
                    if (typeName.Contains("FLOAT") || typeName.Contains("DOUBLE"))
                    {
                        var result = procedure.ModuResult.GetOutputFloat(candidate);
                        if (result.nValueNum <= 0 || result.pFloatVal == null) continue;
                        var values = result.pFloatVal.Take(result.nValueNum).ToArray();
                        value = values.Length == 1 ? (object)(double)values[0] : values.Select(x => (double)x).ToArray();
                        return true;
                    }
                    if (typeName.Contains("INT") || typeName.Contains("BOOL"))
                    {
                        var result = procedure.ModuResult.GetOutputInt(candidate);
                        if (result.nValueNum <= 0 || result.pIntVal == null) continue;
                        var values = result.pIntVal.Take(result.nValueNum).ToArray();
                        if (typeName.Contains("BOOL")) value = values.Length == 1 ? (object)(values[0] != 0) : values.Select(x => x != 0).ToArray();
                        else value = values.Length == 1 ? (object)values[0] : values;
                        return true;
                    }
                }
                catch { }
            }
            try
            {
                object current;
                if (!TryGetVmIoCurrentValue(output, out current)) current = null;
                if (IsCommunicationValue(current)) { value = current; return true; }
            }
            catch { }
            return false;
        }

        private static bool TryGetVmIoCurrentValue(VmIO io, out object value)
        {
            value = null;
            if (io == null) return false;
            var type = io.GetType();
            foreach (var methodName in new[] { "GetCurValue", "GetCurrentValue", "GetValue" })
            {
                try
                {
                    var method = type.GetMethod(methodName, Type.EmptyTypes);
                    if (method == null) continue;
                    value = method.Invoke(io, null);
                    return true;
                }
                catch { }
            }
            foreach (var propertyName in new[] { "CurValue", "CurrentValue", "Value" })
            {
                try
                {
                    var property = type.GetProperty(propertyName);
                    if (property == null) continue;
                    value = property.GetValue(io, null);
                    return true;
                }
                catch { }
            }
            return false;
        }

        private static bool IsCommunicationValue(object value)
        {
            if (value == null) return false;
            var type = value.GetType();
            if (type.IsPrimitive || value is decimal || value is string) return true;
            if (!type.IsArray) return false;
            var elementType = type.GetElementType();
            return elementType != null && (elementType.IsPrimitive || elementType == typeof(decimal) || elementType == typeof(string));
        }

        private static string MapDataType(string typeName)
        {
            var name = (typeName ?? string.Empty).ToUpperInvariant();
            var array = name.Contains("ARRAY") || name.Contains("VECTOR");
            var type = name.Contains("BOOL") ? "Bool" : name.Contains("STRING") ? "String" : name.Contains("DOUBLE") ? "Double" : name.Contains("FLOAT") ? "Float" : name.Contains("INT64") || name.Contains("LONG") ? "Int64" : "Int32";
            return array && type != "String" ? type + "Array" : type;
        }

        private sealed class ImageInjectionEvidence
        {
            public string Path;
            public string InputName;
            public int SourceWidth;
            public int SourceHeight;
            public bool RuntimeReady;
            public string RuntimeType = string.Empty;
            public int RuntimeWidth;
            public int RuntimeHeight;
        }

        private ImageInjectionEvidence RunWithOptionalImage(VmProcedure procedure, VisionMasterRunConfig config)
        {
            Bitmap normalizedBitmap = null;
            ImageBaseData vmImage = null;
            ImageInjectionEvidence evidence = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(config.ImagePath))
                {
                    var imagePath = Path.GetFullPath(config.ImagePath);
                    if (!File.Exists(imagePath))
                        throw new FileNotFoundException("找不到输入图像", imagePath);
                    if (string.IsNullOrWhiteSpace(config.ImageInputName))
                        throw new InvalidOperationException("配置图像时 ImageInputName 不能为空");

                    EnsureImageInput(procedure, config.ImageInputName.Trim());
                    using (var source = new Bitmap(imagePath))
                    {
                        normalizedBitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
                        using (var graphics = Graphics.FromImage(normalizedBitmap))
                            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
                    }
                    vmImage = new ImageBaseData(normalizedBitmap);
                    procedure.ModuParams.SetInputImage_V2(config.ImageInputName.Trim(), vmImage);
                    evidence = new ImageInjectionEvidence { Path = imagePath, InputName = config.ImageInputName.Trim(), SourceWidth = normalizedBitmap.Width, SourceHeight = normalizedBitmap.Height };
                    var previous = _activeInputImage; _activeInputImage = vmImage; vmImage = null;
                    if (previous != null) previous.Dispose();
                }

                procedure.Run();
                if (evidence != null)
                {
                    try
                    {
                        var input = procedure.Inputs[evidence.InputName];
                        object runtimeValue;
                        if (input == null || !TryGetVmIoCurrentValue(input, out runtimeValue)) runtimeValue = null;
                        var runtimeImage = runtimeValue as IVmImageData;
                        evidence.RuntimeReady = input != null && input.IsReady;
                        evidence.RuntimeType = runtimeValue == null ? "<null>" : runtimeValue.GetType().FullName;
                        if (runtimeImage != null) { evidence.RuntimeWidth = runtimeImage.Width; evidence.RuntimeHeight = runtimeImage.Height; }
                    }
                    catch (Exception ex) { evidence.RuntimeType = "读取运行时输入失败：" + ex.Message; }
                }
                return evidence;
            }
            finally
            {
                if (vmImage != null)
                    vmImage.Dispose();
                if (normalizedBitmap != null)
                    normalizedBitmap.Dispose();
            }
        }

        private static void EnsureImageInput(VmProcedure procedure, string inputName)
        {
            var names = new List<string>();
            foreach (var input in procedure.ModuParams.GetAllInputNameInfo())
            {
                names.Add(input.Name);
                if (string.Equals(input.Name, inputName, StringComparison.Ordinal) &&
                    input.TypeName == IMVS_MODULE_BASE_DATA_TYPE.IMVS_BASE_TYPE_IMAGE_DATA)
                    return;
            }
            throw new InvalidOperationException(string.Format(
                "流程未发布 IMAGE 输入‘{0}’。当前输入：{1}",
                inputName,
                names.Count == 0 ? "无" : string.Join(", ", names)));
        }

        private static void ApplyVariables(VmProcedure procedure, IDictionary<string, VisionMasterVariable> variables)
        {
            if (variables == null)
                return;
            foreach (var pair in variables)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                    continue;
                switch ((pair.Value.Type ?? "String").Trim().ToLowerInvariant())
                {
                    case "int":
                        procedure.LocalVariable.SetVarInt(pair.Key, new[]
                        {
                            int.Parse(pair.Value.Value, CultureInfo.InvariantCulture)
                        });
                        break;
                    case "float":
                        procedure.LocalVariable.SetVarFloat(pair.Key, new[]
                        {
                            float.Parse(pair.Value.Value, CultureInfo.InvariantCulture)
                        });
                        break;
                    default:
                        procedure.LocalVariable.SetVarString(pair.Key, new[] { pair.Value.Value ?? string.Empty });
                        break;
                }
            }
        }

        private static bool ReadOk(VmProcedure procedure, string outputName)
        {
            if (string.IsNullOrWhiteSpace(outputName))
                return procedure.IsRunOK.GetValueOrDefault();
            var result = procedure.ModuResult.GetOutputInt(outputName.Trim());
            return result.nValueNum > 0 && result.pIntVal != null &&
                   result.pIntVal.Length > 0 && result.pIntVal[0] != 0;
        }

        public void CloseSolution()
        {
            lock (_syncRoot)
                CloseSolutionCore();
        }

        private void CloseSolutionCore()
        {
            if (VmSolution.Instance != null && !string.IsNullOrWhiteSpace(_loadedSolutionPath))
                VisionMasterRuntime.InvokeWithApplicationDirectory(delegate { VmSolution.Instance.CloseSolution(); });
            if (_activeInputImage != null) { _activeInputImage.Dispose(); _activeInputImage = null; }
            _loadedSolutionPath = string.Empty;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;
                try
                {
                    if (VmSolution.Instance != null)
                        VisionMasterRuntime.InvokeWithApplicationDirectory(delegate { VmSolution.Instance.Dispose(); });
                }
                catch
                {
                }
                if (_activeInputImage != null) { _activeInputImage.Dispose(); _activeInputImage = null; }
                _loadedSolutionPath = string.Empty;
                _disposed = true;
            }
        }
    }
}
