using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Xml.Linq;
using VisionFlowStudio.Core;
using VisionFlowStudio.VisionMaster;
using VisionFlowStudio.VisionPro;
using VisionFlowStudio.Halcon;
using VisionFlowStudio.Cameras;
using VisionFlowStudio.Communications;
using VisionFlowStudio.Scripting;
using VisionFlowStudio.App;

namespace VisionFlowStudio.SmokeTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--camera-only")
                    return TestCameras();
                if (args.Length > 0 && args[0] == "--halcon-reload")
                    return TestHalconReload(args);
                if (args.Length > 0 && args[0] == "--communication-only")
                    return TestCommunications();
                if (args.Length > 0 && args[0] == "--vm-image")
                    return TestVisionMasterImage(args);
                if (args.Length > 0 && args[0] == "--vp-image")
                    return TestVisionProImage(args);
                if (args.Length > 0 && args[0] == "--vm-outputs")
                    return TestVisionMasterOutputs(args);
                if (args.Length > 0 && args[0] == "--hik-connect")
                    return TestHikrobotConnect(args);
                if (args.Length > 0 && args[0] == "--script")
                    return TestCSharpScript();
                if (args.Length > 0 && args[0] == "--flow-load")
                    return TestFlowLoad(args);
                if (args.Length > 0 && args[0] == "--flow-script")
                    return TestFlowScript(args);
                if (args.Length > 0 && args[0] == "--project-roundtrip")
                    return TestProjectRoundTrip();
                if (args.Length > 0 && args[0] == "--localization")
                    return TestLocalization();
                VisionMasterRuntime.Initialize();
                using (var adapter = new VisionMasterAdapter())
                {
                    var status = adapter.GetStatus();
                    if (!status.Installed)
                        throw new InvalidOperationException(status.Message);

                    Console.WriteLine("PASS SDK: " + status.Message);
                    if (args.Length == 0)
                        return 0;

                    var path = Path.GetFullPath(args[0]);
                    var procedures = adapter.LoadSolution(path, string.Empty);
                    Console.WriteLine("PASS SOLUTION: " + Path.GetFileName(path));
                    Console.WriteLine("PROCEDURES: " + string.Join(", ", procedures));
                    if (args.Length > 1 && string.Equals(args[1], "--run", StringComparison.OrdinalIgnoreCase))
                    {
                        var result = adapter.Run(new VisionMasterRunConfig
                        {
                            SolutionPath = path,
                            ProcedureName = procedures.Count > 0 ? procedures[0] : "流程1",
                            OkOutputName = "IsOK"
                        }, new VisionContext());
                        Console.WriteLine(string.Format(
                            "RUN: {0}, {1:0.0} ms, {2}",
                            result.Status,
                            result.CostMs,
                            result.Message));
                        if (result.Status == NodeRunStatus.Error)
                            return 2;
                    }
                    adapter.CloseSolution();
                }
                if (args.Length >= 4)
                {
                    using (var visionPro = new VisionProAdapter())
                    {
                        var status = visionPro.GetStatus();
                        Console.WriteLine("PASS VISIONPRO SDK: " + status.Message);
                        var result = visionPro.Run(new VisionProRunConfig
                        {
                            ToolBlockPath = Path.GetFullPath(args[1]), ImagePath = Path.GetFullPath(args[3]),
                            ImageInputName = "InputImage", OkOutputName = "IsOK"
                        }, new VisionContext());
                        Console.WriteLine("VISIONPRO RUN: " + result.Status + " - " + result.Message);
                        if (result.Status == NodeRunStatus.Error) return 3;
                    }
                    using (var halcon = new HalconAdapter())
                    {
                        var status = halcon.GetStatus();
                        Console.WriteLine("PASS HALCON SDK: " + status.Message);
                        var result = halcon.Run(new HalconRunConfig
                        {
                            ProcedurePath = Path.GetFullPath(args[2]), OkOutputName = "IsOK"
                        }, new VisionContext());
                        Console.WriteLine("HALCON RUN: " + result.Status + " - " + result.Message);
                        if (result.Status != NodeRunStatus.Ok) return 4;
                    }
                }
                TestCameras();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static int TestCameras()
        {
            var failures = 0;
            using (var cameras = new CameraRegistry())
            {
                foreach (var vendor in cameras.Vendors)
                {
                    try { Console.WriteLine("ENUM CAMERA: " + vendor); var devices = cameras.Get(vendor).Enumerate(); Console.WriteLine(string.Format("PASS CAMERA SDK: {0}, devices={1}", vendor, devices.Count)); }
                    catch (Exception ex) { failures++; Console.WriteLine("FAIL CAMERA SDK: " + vendor + " - " + ex); }
                }
            }
            return failures == 0 ? 0 : 5;
        }

        private static int TestCSharpScript()
        {
            var context = new VisionContext();
            context.Set("VisionMaster 流程.CodeStr", "ABC123");
            var vm = new ScriptToolSnapshot { Name = "VisionMaster 流程", NodeId = "vm-1", NodeType = "VisionMasterProcedureNode", Platform = "VisionMaster" };
            vm.Inputs["ProcedureName"] = "流程1";
            vm.Outputs["CodeStr"] = "ABC123";
            var config = new ScriptNodeConfig
            {
                Code = "var code = GetNodeOutput<string>(\"VisionMaster 流程\", \"CodeStr\");\nSetOutput(\"Code\", code);\nSetOutput(\"Length\", code.Length);\nSetOutput(\"Procedure\", GetNodeInput<string>(\"VisionMaster 流程\", \"ProcedureName\"));\nSetOutput(\"ExternalType\", typeof(HslCommunication.Profinet.Siemens.SiemensS7Net).Name);",
                References = new[] { Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HslCommunication.dll") },
                Imports = new[] { "System", "System.Linq" },
                DeclaredOutputs = new[] { "Code", "Length", "Procedure", "ExternalType" }
            };
            var engine = new CSharpScriptEngine();
            var compile = engine.Compile(config);
            if (!compile.Success) throw new InvalidOperationException("Script compile failed: " + string.Join(Environment.NewLine, compile.Diagnostics));
            var globals = new ScriptGlobals(context, new[] { vm }, CancellationToken.None);
            var result = engine.RunAsync(config, globals, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Status != NodeRunStatus.Ok) throw new InvalidOperationException(result.Message);
            if (Convert.ToString(result.Outputs["Code"]) != "ABC123" || Convert.ToInt32(result.Outputs["Length"]) != 6 || Convert.ToString(result.Outputs["Procedure"]) != "流程1" || Convert.ToString(result.Outputs["ExternalType"]) != "SiemensS7Net")
                throw new InvalidOperationException("Script output mismatch.");

            var completionConfig = new ScriptNodeConfig { Code = "Context." };
            var completions = engine.GetCompletions(completionConfig, completionConfig.Code.Length);
            if (!completions.Any(x => x.DisplayText == "Data" || x.DisplayText == "Get")) throw new InvalidOperationException("Semantic completion returned no VisionContext members.");

            var invalid = engine.Compile(new ScriptNodeConfig { Code = "SetOutput(\"Bad\", missingVariable);" });
            if (invalid.Success || !invalid.Diagnostics.Any(x => x.Severity == "Error")) throw new InvalidOperationException("Compiler diagnostics were not returned.");

            var classConfig = new ScriptNodeConfig { Code = CSharpScriptEngine.DefaultClassTemplate, DeclaredOutputs = new[] { "Result", "IsOK" } };
            var classCompile = engine.Compile(classConfig); if (!classCompile.Success) throw new InvalidOperationException("Class script compile failed: " + string.Join(Environment.NewLine, classCompile.Diagnostics));
            var classResult = engine.RunAsync(classConfig, new ScriptGlobals(context, new[] { vm }, CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
            if (classResult.Status != NodeRunStatus.Ok || Convert.ToString(classResult.Outputs["Result"]) != "ABC123") throw new InvalidOperationException("Class script output mismatch: " + classResult.Message);
            var completionPosition = classConfig.Code.IndexOf("SetOutput", StringComparison.Ordinal) + 4;
            var classCompletions = engine.GetCompletions(classConfig, completionPosition);
            if (!classCompletions.Any(x => x.DisplayText == "SetOutput")) throw new InvalidOperationException("Class script completion did not expose base API.");
            var signaturePosition = classConfig.Code.IndexOf("SetOutput(", StringComparison.Ordinal) + "SetOutput(".Length;
            var hostSignatures = engine.GetSignatureHelp(classConfig, signaturePosition);
            if (hostSignatures.Signatures.Count == 0 || !hostSignatures.Signatures.Any(x => x.Contains("SetOutput"))) throw new InvalidOperationException("Host API signature help returned no overloads.");

            var halconAssembly = @"D:\Program Files\MVTec\HALCON-18.11-Progress\bin\dotnet35\halcondotnet.dll";
            var halconCode = "using System;\nusing VisionFlowStudio.Scripting;\npublic sealed class HalconScript : VisionFlowAdvancedScriptBase { public override void Run() { HTuple length; HOperatorSet.TupleLength(new HTuple(new int[] { 1, 2 }), out length); SetOutput(\"HalconType\", typeof(HTuple).FullName); SetOutput(\"HalconLength\", length.I); } }";
            var halconConfig = new ScriptNodeConfig { Code = halconCode, References = new[] { halconAssembly }, Imports = new[] { "HalconDotNet" }, DeclaredOutputs = new[] { "HalconType", "HalconLength" } };
            if (!CSharpScriptEngine.ApplyImportsToClassCode(halconCode, halconConfig.Imports).Contains("using HalconDotNet;")) throw new InvalidOperationException("Class using injection failed.");
            var halconCompile = engine.Compile(halconConfig); if (!halconCompile.Success) throw new InvalidOperationException("HALCON external DLL compile failed: " + string.Join(Environment.NewLine, halconCompile.Diagnostics));
            var halconResult = engine.RunAsync(halconConfig, new ScriptGlobals(context, new ScriptToolSnapshot[0], CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
            if (halconResult.Status != NodeRunStatus.Ok || Convert.ToString(halconResult.Outputs["HalconType"]) != "HalconDotNet.HTuple" || Convert.ToInt32(halconResult.Outputs["HalconLength"]) != 2) throw new InvalidOperationException("HALCON external DLL invocation failed: " + halconResult.Message);
            var halconCompletionCode = "using VisionFlowStudio.Scripting;\nusing HalconDotNet;\npublic sealed class CompletionScript : VisionFlowAdvancedScriptBase { public override void Run() { HOperatorSet. } }";
            var halconCompletionConfig = new ScriptNodeConfig { Code = halconCompletionCode, References = new[] { halconAssembly }, Imports = new[] { "HalconDotNet" } };
            var halconCompletions = engine.GetCompletions(halconCompletionConfig, halconCompletionCode.IndexOf("HOperatorSet.", StringComparison.Ordinal) + "HOperatorSet.".Length);
            if (halconCompletions.Count < 2000 || !halconCompletions.Any(x => x.DisplayText == "TupleLength") || !halconCompletions.Any(x => x.DisplayText == "WriteImage") || !halconCompletions.Any(x => x.DisplayText == "ZoomRegion")) throw new InvalidOperationException("HALCON completion list was truncated: " + halconCompletions.Count);
            var halconSignaturePosition = halconCode.IndexOf("TupleLength(", StringComparison.Ordinal) + "TupleLength(".Length;
            var halconSignatures = engine.GetSignatureHelp(halconConfig, halconSignaturePosition);
            if (halconSignatures.Signatures.Count == 0 || !halconSignatures.Signatures.Any(x => x.Contains("TupleLength"))) throw new InvalidOperationException("HALCON signature help returned no overloads.");
            Console.WriteLine("PASS C# SCRIPT: legacy + full class + HALCON DLL + source using + node I/O + Roslyn completion=" + halconCompletions.Count + "; signature-help=" + halconSignatures.Signatures.Count + "; diagnostics=" + invalid.Diagnostics.Count);
            return 0;
        }

        private static int TestFlowLoad(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("--flow-load <flow.json>");
            using (var visionMaster = new FakeVisionMaster())
            using (var visionPro = new FakeVisionPro())
            using (var halcon = new FakeHalcon())
            using (var cameras = new CameraRegistry())
            using (var communications = new CommunicationRegistry())
            {
                var model = new MainViewModel(visionMaster, visionPro, halcon, cameras, communications);
                model.LoadFlow(Path.GetFullPath(args[1]));
                model.AddNode("CSharpScriptNode");
                var scriptNode = model.SelectedNode; var saveWatch = System.Diagnostics.Stopwatch.StartNew(); model.SaveScriptConfig(scriptNode, model.GetScriptConfig(scriptNode)); saveWatch.Stop();
                if (saveWatch.ElapsedMilliseconds > 1000) throw new InvalidOperationException("Script editor close-save path is too slow: " + saveWatch.ElapsedMilliseconds + " ms");
                var temporary = Path.Combine(Path.GetTempPath(), "VisionFlowScriptLoad_" + Guid.NewGuid().ToString("N") + ".flow.json");
                var contextTemporary = Path.Combine(Path.GetTempPath(), "VisionFlowContext_" + Guid.NewGuid().ToString("N") + ".flow.json");
                try
                {
                    model.SaveFlow(temporary);
                    model.LoadFlow(temporary);
                    if (!model.FlowSteps.Any(x => x.NodeType == "CSharpScriptNode")) throw new InvalidOperationException("C# script node was not persisted.");

                    var savedRecipe = model.AddRecipe().Name;
                    var savedStation = model.StationName;
                    model.FlowName = "ContextRoundTrip";
                    model.AddNode("DelayNode");
                    model.SaveFlow(contextTemporary);
                    var savedDocument = FlowDocumentStore.Load(contextTemporary);
                    if (!string.Equals(savedDocument.RecipeName, savedRecipe, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(savedDocument.StationName, savedStation, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(savedDocument.FlowName, "ContextRoundTrip", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Saved flow metadata mismatch. Expected=" +
                            savedStation + "/" + savedRecipe + "/ContextRoundTrip, Actual=" +
                            savedDocument.StationName + "/" + savedDocument.RecipeName + "/" + savedDocument.FlowName);
                    var otherRecipe = model.Recipes.FirstOrDefault(x => !string.Equals(x.Name, savedRecipe, StringComparison.OrdinalIgnoreCase));
                    if (otherRecipe != null) model.ActivateRecipe(otherRecipe);
                    model.LoadFlow(contextTemporary);
                    if (!string.Equals(model.RecipeName, savedRecipe, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(model.StationName, savedStation, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(model.FlowName, "ContextRoundTrip", StringComparison.OrdinalIgnoreCase) ||
                        !model.FlowSteps.Any(x => x.NodeType == "DelayNode"))
                        throw new InvalidOperationException("Flow station/recipe context was not restored correctly. Expected=" +
                            savedStation + "/" + savedRecipe + "/ContextRoundTrip, Actual=" +
                            model.StationName + "/" + model.RecipeName + "/" + model.FlowName +
                            ", Nodes=" + string.Join(",", model.FlowSteps.Select(x => x.NodeType)));

                    Console.WriteLine("PASS FLOW LOAD: nodes=" + model.FlowSteps.Count + ", script-save=" + saveWatch.ElapsedMilliseconds + " ms");
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (File.Exists(contextTemporary)) File.Delete(contextTemporary);
                }
            }
            return 0;
        }

        private static int TestFlowScript(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("--flow-script <flow.json>");
            FlowDocument flow;
            var serializer = new DataContractJsonSerializer(typeof(FlowDocument));
            using (var stream = File.OpenRead(Path.GetFullPath(args[1])))
                flow = (FlowDocument)serializer.ReadObject(stream);
            var scriptNode = flow.Nodes.FirstOrDefault(x => x.NodeType == "CSharpScriptNode");
            if (scriptNode == null) throw new InvalidOperationException("Flow does not contain CSharpScriptNode.");

            Func<string, string> parameter = key =>
            {
                var item = scriptNode.Parameters.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                return item == null ? string.Empty : item.Value ?? string.Empty;
            };
            var config = new ScriptNodeConfig { Code = parameter("Code"), ScriptFile = parameter("ScriptFile") };
            foreach (var item in CSharpScriptEngine.ParseList(parameter("References"))) config.References.Add(item);
            foreach (var item in CSharpScriptEngine.ParseList(parameter("Imports"))) config.Imports.Add(item);
            foreach (var item in CSharpScriptEngine.ParseList(parameter("OutputNames"))) config.DeclaredOutputs.Add(item);

            var engine = new CSharpScriptEngine();
            var compile = engine.Compile(config);
            if (!compile.Success) throw new InvalidOperationException("Flow script compile failed: " + string.Join(Environment.NewLine, compile.Diagnostics));

            var context = new VisionContext();
            context.Set("VisionMaster 流程.CodeStr", "ABC123");
            var vm = new ScriptToolSnapshot { Name = "VisionMaster 流程", NodeId = "vm-1", NodeType = "VisionMasterProcedureNode", Platform = "VisionMaster" };
            vm.Outputs["CodeStr"] = "ABC123";
            var result = engine.RunAsync(config, new ScriptGlobals(context, new[] { vm }, CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
            if (result.Status != NodeRunStatus.Ok) throw new InvalidOperationException(result.Message);
            if (Convert.ToString(result.Outputs["Decode_str"]) != "ABC123" || Convert.ToBoolean(result.Outputs["IsOK"]) != true)
                throw new InvalidOperationException("Flow script output mismatch with CodeStr.");

            var emptyResult = engine.RunAsync(config, new ScriptGlobals(new VisionContext(), new[] { new ScriptToolSnapshot { Name = "VisionMaster 流程" } }, CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
            if (emptyResult.Status != NodeRunStatus.Ok) throw new InvalidOperationException("Flow script should tolerate missing VM output: " + emptyResult.Message);
            if (Convert.ToBoolean(emptyResult.Outputs["IsOK"]) != false)
                throw new InvalidOperationException("Flow script should output IsOK=false when VM output is missing.");

            Console.WriteLine("PASS FLOW SCRIPT: " + Path.GetFileName(args[1]) + ", Decode_str=" + result.Outputs["Decode_str"]);
            return 0;
        }

        private static int TestProjectRoundTrip()
        {
            const string password = "RoundTrip#2026";
            var path = Path.Combine(Path.GetTempPath(), "VisionFlowProjectRoundTrip_" + Guid.NewGuid().ToString("N") + ".vfsproj");
            try
            {
                using (var visionMaster = new FakeVisionMaster())
                using (var visionPro = new FakeVisionPro())
                using (var halcon = new FakeHalcon())
                using (var cameras = new CameraRegistry())
                using (var communications = new CommunicationRegistry())
                {
                    var model = new MainViewModel(visionMaster, visionPro, halcon, cameras, communications);
                    if (visionMaster.GetOutputsCallCount != 0)
                        throw new InvalidOperationException("MainViewModel startup performed synchronous VisionMaster output discovery.");
                    model.ProjectName = "RoundTripProject";

                    SetProjectFlow(model, "Station_01", "Model_A", "Station01_ModelA", "A_Delay_Unique");

                    var modelB = model.AddRecipe();
                    SetProjectFlow(model, "Station_01", modelB.Name, "Station01_ModelB", "B_Delay_Unique");

                    var station2 = model.AddStationForActiveRecipe();
                    SetProjectFlow(model, station2.Name, modelB.Name, "Station02_ModelB", "S2B_Delay_Unique");
                    SetProjectFlow(model, station2.Name, "Model_A", "Station02_ModelA", "S2A_Delay_Unique");
                    var firstFlow = model.StationFlows.First(x =>
                        string.Equals(x.StationName, "Station_01", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(x.RecipeName, "Model_A", StringComparison.OrdinalIgnoreCase));
                    model.ActivateStationRecipe(firstFlow);
                    model.NewStationRecipeFlow();
                    model.FlowName = "Station01_ModelA_Second";
                    model.AddNode("DelayNode");
                    model.SelectedNode.NodeName = "A2_Delay_Unique";

                    model.SaveProject(path, password);
                }

                var encrypted = File.ReadAllBytes(path);
                if (encrypted.Length < 64 || System.Text.Encoding.ASCII.GetString(encrypted, 0, 8) != "VFSENC01")
                    throw new InvalidOperationException("Project was not written as an encrypted VFS container.");
                if (System.Text.Encoding.UTF8.GetString(encrypted).Contains("RoundTripProject"))
                    throw new InvalidOperationException("Encrypted project leaked plaintext project data.");
                try { ProjectDataStore.Load(path, "wrong-password"); throw new InvalidOperationException("Wrong project password was accepted."); }
                catch (System.Security.Cryptography.CryptographicException) { }

                var settingsSerializer = new DataContractJsonSerializer(typeof(ApplicationSettings));
                using (var settingsStream = new MemoryStream())
                {
                    settingsSerializer.WriteObject(settingsStream, new ApplicationSettings { AutoSaveProject = true, AutoSaveIntervalMinutes = 17, Language = "en-US" });
                    settingsStream.Position = 0;
                    var restoredSettings = (ApplicationSettings)settingsSerializer.ReadObject(settingsStream);
                    if (restoredSettings == null || !restoredSettings.AutoSaveProject || restoredSettings.AutoSaveIntervalMinutes != 17 ||
                        !string.Equals(restoredSettings.Language, "en-US", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Application settings (including language) were not serialized correctly.");
                }

                using (var visionMaster = new FakeVisionMaster())
                using (var visionPro = new FakeVisionPro())
                using (var halcon = new FakeHalcon())
                using (var cameras = new CameraRegistry())
                using (var communications = new CommunicationRegistry())
                {
                    var restored = new MainViewModel(visionMaster, visionPro, halcon, cameras, communications);
                    restored.LoadProject(path, password);
                    if (visionMaster.GetOutputsCallCount != 0)
                        throw new InvalidOperationException("Project load performed synchronous VisionMaster output discovery.");

                    if (!string.Equals(restored.ProjectName, "RoundTripProject", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Project name was not restored.");
                    if (restored.Recipes.Count != 2 || restored.Stations.Count != 2)
                        throw new InvalidOperationException(string.Format("Project matrix size mismatch. Recipes={0}, Stations={1}", restored.Recipes.Count, restored.Stations.Count));
                    if (restored.StationFlows.Count != restored.Recipes.Count * restored.Stations.Count + 1)
                        throw new InvalidOperationException("StationFlows matrix is incomplete after project load.");
                    if (restored.ImageDocuments.Count != restored.StationFlows.Count)
                        throw new InvalidOperationException(string.Format("Image document count must match flow count. Images={0}, Flows={1}", restored.ImageDocuments.Count, restored.StationFlows.Count));
                    if (restored.ImageDocuments.Any(x => x.Key.StartsWith("CAMERA|", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("A standalone camera image document was created.");

                    AssertProjectFlow(restored, "Station_01", "Model_A", "Station01_ModelA", "A_Delay_Unique");
                    AssertProjectFlow(restored, "Station_01", "Model_B", "Station01_ModelB", "B_Delay_Unique");
                    AssertProjectFlow(restored, "Station_02", "Model_A", "Station02_ModelA", "S2A_Delay_Unique");
                    AssertProjectFlow(restored, "Station_02", "Model_B", "Station02_ModelB", "S2B_Delay_Unique");
                    AssertProjectFlow(restored, "Station_01", "Model_A", "Station01_ModelA_Second", "A2_Delay_Unique");

                    Console.WriteLine("PASS PROJECT ROUNDTRIP: recipes=2, stations=2, flows=" + restored.StationFlows.Count);
                }
                return 0;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static int TestLocalization()
        {
            LocalizationService.Initialize("en-US");
            if (!string.Equals(LocalizationService.T("文件(_F)"), "File (_F)", StringComparison.Ordinal))
                throw new InvalidOperationException("Chinese-to-English UI translation failed.");

            // A MenuItem renders its header through a template TextBlock whose Text
            // property may be empty while its Inline content contains the label. The
            // localization pass must translate the logical header without clearing
            // generated template content (the regression appeared as a blank menu).
            var testWindow = new System.Windows.Window();
            var root = new System.Windows.Controls.StackPanel();
            var menu = new System.Windows.Controls.Menu();
            var menuItem = new System.Windows.Controls.MenuItem { Header = "文件(_F)" };
            menu.Items.Add(menuItem);
            var inlineText = new System.Windows.Controls.TextBlock();
            var inlineRun = new System.Windows.Documents.Run("模板文字");
            inlineText.Inlines.Add(inlineRun);
            root.Children.Add(menu);
            root.Children.Add(inlineText);
            testWindow.Content = root;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            LocalizationService.Apply(testWindow);
            watch.Stop();
            if (!string.Equals(Convert.ToString(menuItem.Header), "File (_F)", StringComparison.Ordinal))
                throw new InvalidOperationException("Menu header localization failed.");
            if (!string.Equals(inlineRun.Text, "模板文字", StringComparison.Ordinal))
                throw new InvalidOperationException("Localization cleared a template TextBlock inline.");
            if (watch.ElapsedMilliseconds > 500)
                throw new InvalidOperationException("Localization pass is unexpectedly slow: " + watch.ElapsedMilliseconds + " ms");

            LocalizationService.SetLanguage("zh-CN");
            if (!string.Equals(LocalizationService.T("File (_F)"), "文件(_F)", StringComparison.Ordinal))
                throw new InvalidOperationException("English-to-Chinese UI translation failed.");
            var productName = "VisionPro ToolBlock";
            var identifier = "IsOK";
            var status = "OK";
            var path = @"C:\Users\CloudVision\Documents\VisionFlowStudio\VisionPrograms\VisionFlowStudio.vfsproj";
            if (!string.Equals(LocalizationService.TDynamic(productName), productName, StringComparison.Ordinal) ||
                !string.Equals(LocalizationService.TDynamic(identifier), identifier, StringComparison.Ordinal) ||
                !string.Equals(LocalizationService.TDynamic(status), status, StringComparison.Ordinal) ||
                !string.Equals(LocalizationService.TDynamic(path), path, StringComparison.Ordinal))
                throw new InvalidOperationException("Chinese display corrupted an English product name, identifier, status or path.");
            testWindow.Close();
            Console.WriteLine("PASS LOCALIZATION: zh-CN <-> en-US; runtime identifiers preserved; menu/inlines preserved; apply=" + watch.ElapsedMilliseconds + " ms");
            return 0;
        }

        private static void SetProjectFlow(MainViewModel model, string stationName, string recipeName, string flowName, string uniqueNodeName)
        {
            var flow = model.StationFlows.FirstOrDefault(x =>
                string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase));
            if (flow == null) throw new InvalidOperationException("Missing station/recipe flow before save: " + stationName + "/" + recipeName);
            model.ActivateStationRecipe(flow);
            model.FlowName = flowName;
            model.AddNode("DelayNode");
            model.SelectedNode.NodeName = uniqueNodeName;
        }

        private static void AssertProjectFlow(MainViewModel model, string stationName, string recipeName, string flowName, string uniqueNodeName)
        {
            var flow = model.StationFlows.FirstOrDefault(x =>
                string.Equals(x.StationName, stationName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RecipeName, recipeName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.FlowName, flowName, StringComparison.OrdinalIgnoreCase));
            if (flow == null) throw new InvalidOperationException("Missing station/recipe/flow after load: " + stationName + "/" + recipeName + "/" + flowName);
            if (!string.Equals(flow.FlowName, flowName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Flow name mismatch for " + stationName + "/" + recipeName + ": " + flow.FlowName);
            if (flow.Flow == null || flow.Flow.Nodes == null || !flow.Flow.Nodes.Any(x => string.Equals(x.NodeName, uniqueNodeName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Flow nodes were not restored for " + stationName + "/" + recipeName + ": " + uniqueNodeName);
            model.ActivateStationRecipe(flow);
            if (!string.Equals(model.FlowName, flowName, StringComparison.OrdinalIgnoreCase) ||
                !model.FlowSteps.Any(x => string.Equals(x.NodeName, uniqueNodeName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Loaded UI state does not match flow " + stationName + "/" + recipeName);
        }

        private sealed class FakeVisionMaster : IVisionMasterAdapter
        {
            public int GetOutputsCallCount { get; private set; }
            public VisionPlatformStatus GetStatus() { return new VisionPlatformStatus { Name = "VM", Installed = true, Message = "test" }; }
            public System.Collections.Generic.IReadOnlyList<string> LoadSolution(string path, string password) { return new[] { "流程1" }; }
            public System.Collections.Generic.IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionMasterRunConfig config) { GetOutputsCallCount++; return new VisionOutputDefinition[0]; }
            public NodeRunResult Run(VisionMasterRunConfig config, VisionContext context) { return new NodeRunResult { Status = NodeRunStatus.Ok }; }
            public void CloseSolution() { }
            public void Dispose() { }
        }
        private sealed class FakeVisionPro : IVisionProAdapter
        {
            public VisionPlatformStatus GetStatus() { return new VisionPlatformStatus { Name = "VP", Installed = true, Message = "test" }; }
            public System.Collections.Generic.IReadOnlyList<VisionOutputDefinition> GetOutputs(VisionProRunConfig config) { return new VisionOutputDefinition[0]; }
            public NodeRunResult Run(VisionProRunConfig config, VisionContext context) { return new NodeRunResult { Status = NodeRunStatus.Ok }; }
            public void Dispose() { }
        }
        private sealed class FakeHalcon : IHalconAdapter
        {
            public VisionPlatformStatus GetStatus() { return new VisionPlatformStatus { Name = "HALCON", Installed = true, Message = "test" }; }
            public System.Collections.Generic.IReadOnlyList<VisionOutputDefinition> GetOutputs(HalconRunConfig config) { return new VisionOutputDefinition[0]; }
            public NodeRunResult Run(HalconRunConfig config, VisionContext context) { return new NodeRunResult { Status = NodeRunStatus.Ok }; }
            public void ReloadProcedure(string path) { }
            public void Dispose() { }
        }

        private static int TestHikrobotConnect(string[] args)
        {
            if (args.Any(x => string.Equals(x, "--app", StringComparison.OrdinalIgnoreCase)))
            {
                VisionMasterRuntime.Initialize();
                Console.WriteLine("APP INIT: VisionMaster runtime initialized");
            }
            using (var cameras = new CameraRegistry())
            {
                if (args.Length > 2 && string.Equals(args[2], "--all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var item in cameras.EnumerateAll())
                        Console.WriteLine(string.Format("ALL: vendor={0}, name={1}, serial={2}, id={3}", item.Vendor, item.DisplayName, item.SerialNumber, item.DeviceId));
                }
                var provider = cameras.Get("Hikrobot"); var devices = provider.Enumerate();
                foreach (var item in devices) Console.WriteLine(string.Format("HIK: {0}, serial={1}, ip={2}, id={3}", item.DisplayName, item.SerialNumber, item.IpAddress, item.DeviceId));
                if (devices.Count == 0) throw new InvalidOperationException("No Hikrobot camera enumerated.");
                var serial = args.Length > 1 ? args[1] : devices[0].DeviceId;
                provider.Connect(serial); Console.WriteLine("CONNECTED: " + serial);
                var settings = provider.GetSettings(); Console.WriteLine(string.Format("SETTINGS: exposure={0}, gain={1}", settings.ExposureUs, settings.Gain));
                provider.Disconnect(); Console.WriteLine("DISCONNECTED"); return 0;
            }
        }

        private static int TestCommunications()
        {
            var hsl = Assembly.Load("HslCommunication");
            if (hsl.GetName().Version == null || hsl.GetName().Version.ToString() != "7.0.0.0")
                throw new InvalidOperationException("HslCommunication version mismatch: " + hsl.GetName().Version);
            var requiredTypes = new[]
            {
                "HslCommunication.Profinet.Siemens.SiemensS7Net",
                "HslCommunication.Profinet.Melsec.MelsecMcAsciiNet",
                "HslCommunication.ModBus.ModbusTcpNet",
                "HslCommunication.ModBus.ModbusRtu",
                "HslCommunication.Profinet.Omron.OmronFinsNet",
                "HslCommunication.Profinet.AllenBradley.AllenBradleyNet"
            };
            foreach (var name in requiredTypes)
                if (hsl.GetType(name, false) == null) throw new TypeLoadException(name);
            if (CommunicationRegistry.Protocols.Length != requiredTypes.Length)
                throw new InvalidOperationException("Communication protocol registry is incomplete.");
            Console.WriteLine("PASS HSL: " + hsl.GetName().Version + ", protocols=" + string.Join(", ", CommunicationRegistry.Protocols));
            Console.WriteLine("DATA TYPES: " + string.Join(", ", CommunicationRegistry.DataTypes));
            return 0;
        }

        private static int TestVisionMasterImage(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("--vm-image <sol> <image> [procedure] [input] [ok-output]");
            VisionMasterRuntime.Initialize();
            using (var adapter = new VisionMasterAdapter())
            {
                var solution = Path.GetFullPath(args[1]); var image = Path.GetFullPath(args[2]);
                var procedures = adapter.LoadSolution(solution, string.Empty);
                var procedure = args.Length > 3 ? args[3] : procedures.FirstOrDefault();
                var input = args.Length > 4 ? args[4] : "InputImage";
                var okOutput = args.Length > 5 ? args[5] : string.Empty;
                var result = adapter.Run(new VisionMasterRunConfig { SolutionPath = solution, ProcedureName = procedure, ImagePath = image, ImageInputName = input, OkOutputName = okOutput }, new VisionContext());
                Console.WriteLine(string.Format("VM IMAGE RUN: {0}, procedure={1}, input={2}, {3:0.0} ms - {4}", result.Status, procedure, input, result.CostMs, result.Message));
                foreach (var output in result.Outputs)
                    Console.WriteLine(string.Format("VM VALUE: key={0}, type={1}, value={2}", output.Key, output.Value == null ? "<null>" : output.Value.GetType().FullName, FormatValue(output.Value)));
                if (result.Status == NodeRunStatus.Error) throw new InvalidOperationException(result.Message);
                return 0;
            }
        }

        private static string FormatValue(object value)
        {
            var items = value as System.Collections.IEnumerable;
            if (items == null || value is string) return Convert.ToString(value);
            return string.Join(",", items.Cast<object>().Select(Convert.ToString));
        }

        private static int TestVisionMasterOutputs(string[] args)
        {
            if (args.Length < 2) throw new ArgumentException("--vm-outputs <sol> [procedure]");
            VisionMasterRuntime.Initialize();
            using (var adapter = new VisionMasterAdapter())
            {
                var solution = Path.GetFullPath(args[1]); var procedures = adapter.LoadSolution(solution, string.Empty);
                var procedure = args.Length > 2 ? args[2] : procedures.FirstOrDefault();
                var outputs = adapter.GetOutputs(new VisionMasterRunConfig { SolutionPath = solution, ProcedureName = procedure });
                foreach (var output in outputs) Console.WriteLine(output.Name + " : " + output.DataType);
                return outputs.Count == 0 ? 2 : 0;
            }
        }

        private static int TestVisionProImage(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("--vp-image <vpp> <image> [input] [ok-output]");
            using (var adapter = new VisionProAdapter())
            {
                var input = args.Length > 3 ? args[3] : "InputImage"; var okOutput = args.Length > 4 ? args[4] : "IsOK";
                var result = adapter.Run(new VisionProRunConfig { ToolBlockPath = Path.GetFullPath(args[1]), ImagePath = Path.GetFullPath(args[2]), ImageInputName = input, OkOutputName = okOutput }, new VisionContext());
                Console.WriteLine(string.Format("VP IMAGE RUN: {0}, input={1}, {2:0.0} ms - {3}", result.Status, input, result.CostMs, result.Message));
                if (result.Status == NodeRunStatus.Error) throw new InvalidOperationException(result.Message);
                return 0;
            }
        }

        private static int TestHalconReload(string[] args)
        {
            if (args.Length < 3) throw new ArgumentException("--halcon-reload <hdvp> <image>");
            var tempPath = Path.Combine(Path.GetTempPath(), "VisionFlowReload_" + Guid.NewGuid().ToString("N") + ".hdvp");
            File.Copy(Path.GetFullPath(args[1]), tempPath, true);
            try
            {
                var document = XDocument.Load(tempPath);
                var procedure = document.Descendants("procedure").First();
                var interfaceElement = procedure.Element("interface");
                var iconicInputs = interfaceElement.Element("io");
                if (iconicInputs == null) { iconicInputs = new XElement("io"); interfaceElement.AddFirst(iconicInputs); }
                var parameter = iconicInputs.Elements("par").FirstOrDefault();
                if (parameter == null) { parameter = new XElement("par", new XAttribute("name", "Image"), new XAttribute("base_type", "iconic"), new XAttribute("dimension", "0")); iconicInputs.Add(parameter); }
                parameter.SetAttributeValue("name", "Image"); document.Save(tempPath);

                using (var adapter = new HalconAdapter())
                {
                    var first = adapter.Run(new HalconRunConfig { ProcedurePath = tempPath, ImagePath = Path.GetFullPath(args[2]), ImageInputName = "Image", OkOutputName = "IsOK" }, new VisionContext());
                    if (first.Status == NodeRunStatus.Error) throw new InvalidOperationException("初次运行失败：" + first.Message);
                    parameter.SetAttributeValue("name", "InputImage"); document.Save(tempPath);
                    adapter.ReloadProcedure(tempPath);
                    var second = adapter.Run(new HalconRunConfig { ProcedurePath = tempPath, ImagePath = Path.GetFullPath(args[2]), ImageInputName = "InputImage", OkOutputName = "IsOK" }, new VisionContext());
                    if (second.Status == NodeRunStatus.Error) throw new InvalidOperationException("重载后运行失败：" + second.Message);
                    Console.WriteLine("PASS HALCON RELOAD: Image -> InputImage, " + second.Message);
                }
                return 0;
            }
            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
        }
    }
}
