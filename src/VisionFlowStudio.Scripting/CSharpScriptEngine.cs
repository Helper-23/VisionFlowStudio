using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using VisionFlowStudio.Core;

namespace VisionFlowStudio.Scripting
{
    public sealed class ScriptNodeConfig
    {
        public string Code { get; set; } = string.Empty;
        public string ScriptFile { get; set; } = string.Empty;
        public IList<string> References { get; set; } = new List<string>();
        public IList<string> Imports { get; set; } = new List<string>();
        public IList<string> DeclaredOutputs { get; set; } = new List<string>();
    }

    public sealed class ScriptToolSnapshot
    {
        public string Name { get; set; }
        public string NodeId { get; set; }
        public string NodeType { get; set; }
        public string Platform { get; set; }
        public IDictionary<string, object> Inputs { get; private set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, object> Outputs { get; private set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public T Input<T>(string name, T fallback = default(T)) { return ConvertValue(Inputs, name, fallback); }
        public T Output<T>(string name, T fallback = default(T)) { return ConvertValue(Outputs, name, fallback); }
        private static T ConvertValue<T>(IDictionary<string, object> values, string name, T fallback)
        {
            object value;
            if (!values.TryGetValue(name ?? string.Empty, out value) || value == null) return fallback;
            if (value is T) return (T)value;
            try { return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture); } catch { return fallback; }
        }
    }

    public class ScriptGlobals
    {
        private readonly Dictionary<string, ScriptToolSnapshot> _tools;
        public ScriptGlobals(VisionContext context, IEnumerable<ScriptToolSnapshot> tools, CancellationToken cancellationToken)
        {
            Context = context ?? throw new ArgumentNullException("context");
            CancellationToken = cancellationToken;
            _tools = (tools ?? Enumerable.Empty<ScriptToolSnapshot>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Last(), StringComparer.OrdinalIgnoreCase);
        }
        public VisionContext Context { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public IDictionary<string, object> Data { get { return Context.Data; } }
        public IDictionary<string, object> Outputs { get; private set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        public IEnumerable<ScriptToolSnapshot> Tools { get { return _tools.Values; } }
        public object Get(string key) { return Context.GetValue(key); }
        public T Get<T>(string key, T fallback = default(T)) { return Context.Get<T>(key, fallback); }
        public ScriptToolSnapshot Tool(string nodeName)
        {
            ScriptToolSnapshot value; return _tools.TryGetValue(nodeName ?? string.Empty, out value) ? value : null;
        }
        public T GetNodeOutput<T>(string nodeName, string outputName, T fallback = default(T)) { var tool = Tool(nodeName); return tool == null ? fallback : tool.Output(outputName, fallback); }
        public T GetNodeInput<T>(string nodeName, string inputName, T fallback = default(T)) { var tool = Tool(nodeName); return tool == null ? fallback : tool.Input(inputName, fallback); }
        public void SetOutput(string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("输出名称不能为空", "name");
            Outputs[name.Trim()] = value;
        }
        public void ThrowIfCancellationRequested() { CancellationToken.ThrowIfCancellationRequested(); }
    }

    public abstract class VisionFlowAdvancedScriptBase
    {
        private ScriptGlobals _runtime;
        internal void Attach(ScriptGlobals runtime) { _runtime = runtime ?? throw new ArgumentNullException("runtime"); }
        protected VisionContext Context { get { return Runtime.Context; } }
        protected CancellationToken CancellationToken { get { return Runtime.CancellationToken; } }
        protected IDictionary<string, object> Data { get { return Runtime.Data; } }
        protected IDictionary<string, object> Outputs { get { return Runtime.Outputs; } }
        protected IEnumerable<ScriptToolSnapshot> Tools { get { return Runtime.Tools; } }
        protected object Get(string key) { return Runtime.Get(key); }
        protected T Get<T>(string key, T fallback = default(T)) { return Runtime.Get(key, fallback); }
        protected ScriptToolSnapshot Tool(string nodeName) { return Runtime.Tool(nodeName); }
        protected T GetNodeOutput<T>(string nodeName, string outputName, T fallback = default(T)) { return Runtime.GetNodeOutput(nodeName, outputName, fallback); }
        protected T GetNodeInput<T>(string nodeName, string inputName, T fallback = default(T)) { return Runtime.GetNodeInput(nodeName, inputName, fallback); }
        protected void SetOutput(string name, object value) { Runtime.SetOutput(name, value); }
        protected void ThrowIfCancellationRequested() { Runtime.ThrowIfCancellationRequested(); }
        protected ScriptGlobals Runtime { get { return _runtime ?? throw new InvalidOperationException("脚本运行上下文尚未初始化"); } }
        public virtual void Initialize() { }
        public abstract void Run();
    }

    public sealed class ScriptDiagnostic
    {
        public string Severity { get; set; }
        public string Id { get; set; }
        public string Message { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public override string ToString() { return string.Format("{0}({1},{2}) {3}: {4}", Severity, Line, Column, Id, Message); }
    }

    public sealed class ScriptCompileResult
    {
        public bool Success { get { return Diagnostics.All(x => !string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase)); } }
        public IList<ScriptDiagnostic> Diagnostics { get; private set; } = new List<ScriptDiagnostic>();
    }

    public sealed class ScriptCompletionItem
    {
        public string DisplayText { get; set; }
        public string InsertText { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
        public int ReplacementStart { get; set; }
        public int ReplacementLength { get; set; }
        public override string ToString() { return DisplayText; }
    }

    public sealed class ScriptSignatureHelp
    {
        public int ActiveParameter { get; set; }
        public IList<string> Signatures { get; private set; } = new List<string>();
    }

    public sealed class CSharpScriptEngine
    {
        public const string DefaultClassTemplate =
@"using System;
using System.Collections.Generic;
using System.Linq;
using VisionFlowStudio.Core;
using VisionFlowStudio.Scripting;

public sealed class VisionFlowAdvancedScript : VisionFlowAdvancedScriptBase
{
    public override void Initialize()
    {
        // 可在这里初始化仅供本脚本使用的对象。
    }

    public override void Run()
    {
        // 可读取流程中任意节点的输入和输出。
        var value = GetNodeOutput<string>(""VisionMaster 流程"", ""CodeStr"", string.Empty);

        SetOutput(""Result"", value);
        SetOutput(""IsOK"", !string.IsNullOrEmpty(value));
    }
}";
        private static readonly string[] DefaultImports = { "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks", "VisionFlowStudio.Core", "VisionFlowStudio.Scripting" };
        private static readonly string[] Keywords = { "var", "new", "if", "else", "for", "foreach", "while", "switch", "case", "return", "true", "false", "null", "string", "int", "double", "bool", "object", "async", "await", "try", "catch", "finally", "throw", "typeof", "nameof" };
        private readonly ConcurrentDictionary<string, ScriptRunner<object>> _cache = new ConcurrentDictionary<string, ScriptRunner<object>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, Type> _classCache = new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);
        private static readonly object DependencyPathSync = new object();
        private static readonly HashSet<string> RegisteredDependencyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ScriptCompileResult Compile(ScriptNodeConfig config)
        {
            LoadExternalAssemblies(config);
            if (IsClassCode(config)) return CompileClass(config);
            var script = CreateScript(config);
            var diagnostics = script.Compile();
            var result = new ScriptCompileResult();
            foreach (var diagnostic in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Hidden)) result.Diagnostics.Add(ToDiagnostic(diagnostic));
            if (result.Success) _cache[CreateCacheKey(config)] = script.CreateDelegate();
            return result;
        }

        public async Task<NodeRunResult> RunAsync(ScriptNodeConfig config, ScriptGlobals globals, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                LoadExternalAssemblies(config);
                if (IsClassCode(config)) return await RunClassAsync(config, globals, cancellationToken, watch).ConfigureAwait(false);
                var key = CreateCacheKey(config); ScriptRunner<object> runner;
                if (!_cache.TryGetValue(key, out runner))
                {
                    var script = CreateScript(config); var diagnostics = script.Compile();
                    var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
                    if (errors.Length > 0) return ErrorResult(watch, string.Join(Environment.NewLine, errors.Select(x => ToDiagnostic(x).ToString())));
                    runner = script.CreateDelegate(); _cache[key] = runner;
                }
                var returnValue = await runner(globals, cancellationToken).ConfigureAwait(false);
                if (returnValue != null && !globals.Outputs.ContainsKey("ReturnValue")) globals.SetOutput("ReturnValue", returnValue);
                watch.Stop();
                return new NodeRunResult { Status = NodeRunStatus.Ok, Message = string.Format("C# 脚本执行完成，输出 {0} 项", globals.Outputs.Count), CostMs = watch.Elapsed.TotalMilliseconds, Outputs = new Dictionary<string, object>(globals.Outputs, StringComparer.OrdinalIgnoreCase) };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return ErrorResult(watch, FormatException(ex)); }
        }

        public IReadOnlyList<ScriptCompletionItem> GetCompletions(ScriptNodeConfig config, int position)
        {
            try
            {
                var items = GetWorkspaceCompletions(config, position);
                if (items.Count > 0) return items;
            }
            catch
            {
                // Keep editing usable even when a third-party assembly cannot be loaded by Roslyn.
            }
            return GetSemanticFallbackCompletions(config, position);
        }

        private IReadOnlyList<ScriptCompletionItem> GetWorkspaceCompletions(ScriptNodeConfig config, int position)
        {
            var classMode = IsClassCode(config);
            var originalCode = LoadCode(config); var code = classMode ? ApplyImportsToClassCode(originalCode, config == null ? null : config.Imports) : originalCode;
            if (classMode && code.Length != originalCode.Length) position += code.Length - originalCode.Length;
            position = Math.Max(0, Math.Min(position, code.Length));
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, kind: classMode ? SourceCodeKind.Regular : SourceCodeKind.Script);
            var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: classMode ? null : GetImports(config));
            using (var workspace = new AdhocWorkspace())
            {
                var project = workspace.AddProject(ProjectInfo.Create(
                    ProjectId.CreateNewId(), VersionStamp.Create(), "VisionFlowIntelliSense", "VisionFlowIntelliSense", LanguageNames.CSharp,
                    compilationOptions: compilationOptions, parseOptions: parseOptions, metadataReferences: GetMetadataReferences(config)));
                var document = workspace.AddDocument(project.Id, classMode ? "AdvancedScript.cs" : "AdvancedScript.csx", SourceText.From(code, Encoding.UTF8));
                var service = CompletionService.GetService(document);
                if (service == null) return Array.Empty<ScriptCompletionItem>();
                var completion = service.GetCompletionsAsync(document, position).GetAwaiter().GetResult();
                if (completion == null) return Array.Empty<ScriptCompletionItem>();
                var offset = classMode ? code.Length - originalCode.Length : 0;
                return completion.ItemsList
                    .GroupBy(x => x.DisplayText, StringComparer.Ordinal)
                    .Select(x => x.First())
                    .Select(x => new ScriptCompletionItem
                    {
                        DisplayText = x.DisplayText,
                        InsertText = x.DisplayText,
                        Kind = x.Tags.FirstOrDefault() ?? "Symbol",
                        Description = string.IsNullOrWhiteSpace(x.InlineDescription) ? x.DisplayText : x.DisplayText + "  " + x.InlineDescription,
                        ReplacementStart = Math.Max(0, completion.Span.Start - offset),
                        ReplacementLength = completion.Span.Length
                    })
                    .OrderBy(x => x.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .Take(10000)
                    .ToArray();
            }
        }

        private IReadOnlyList<ScriptCompletionItem> GetSemanticFallbackCompletions(ScriptNodeConfig config, int position)
        {
            var classMode = IsClassCode(config);
            var originalCode = LoadCode(config); var code = classMode ? ApplyImportsToClassCode(originalCode, config == null ? null : config.Imports) : originalCode;
            if (classMode && code.Length != originalCode.Length) position += code.Length - originalCode.Length;
            position = Math.Max(0, Math.Min(position, code.Length));
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp7_3, kind: classMode ? SourceCodeKind.Regular : SourceCodeKind.Script));
            var compilation = classMode
                ? CSharpCompilation.Create("VisionFlowClassCompletion", new[] { tree }, GetMetadataReferences(config), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                : CSharpCompilation.CreateScriptCompilation("VisionFlowScriptCompletion", tree, GetMetadataReferences(config), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: GetImports(config)), globalsType: typeof(ScriptGlobals));
            var model = compilation.GetSemanticModel(tree, true); var root = tree.GetRoot();
            var scan = position; while (scan > 0 && (char.IsLetterOrDigit(code[scan - 1]) || code[scan - 1] == '_')) scan--;
            var prefix = code.Substring(scan, position - scan); IEnumerable<ISymbol> symbols;
            var token = root.FindToken(Math.Max(0, position - 1));
            var member = token.Parent == null ? null : token.Parent.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().FirstOrDefault(x => x.SpanStart <= position && x.OperatorToken.Span.End <= position);
            if (member != null)
            {
                var type = model.GetTypeInfo(member.Expression).Type;
                symbols = type == null ? Enumerable.Empty<ISymbol>() : type.GetMembers();
            }
            else symbols = model.LookupSymbols(position).Concat(typeof(ScriptGlobals).GetMembers(BindingFlags.Instance | BindingFlags.Public).Select(x => (ISymbol)null).Where(x => false));
            var items = new List<ScriptCompletionItem>();
            foreach (var symbol in symbols.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name) && (x.DeclaredAccessibility == Accessibility.Public || x.DeclaredAccessibility == Accessibility.Protected || x.DeclaredAccessibility == Accessibility.ProtectedOrInternal)).GroupBy(x => x.Name).Select(x => x.First()))
            {
                if (prefix.Length > 0 && !symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new ScriptCompletionItem { DisplayText = symbol.Name, InsertText = symbol.Name, Kind = symbol.Kind.ToString(), Description = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), ReplacementStart = scan, ReplacementLength = position - scan });
            }
            if (member == null)
            {
                var apiType = classMode ? typeof(VisionFlowAdvancedScriptBase) : typeof(ScriptGlobals);
                foreach (var method in apiType.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(x => x.MemberType == MemberTypes.Method || x.MemberType == MemberTypes.Property))
                    if ((prefix.Length == 0 || method.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) && !items.Any(x => x.DisplayText == method.Name)) items.Add(new ScriptCompletionItem { DisplayText = method.Name, InsertText = method.Name, Kind = method.MemberType.ToString(), Description = method.ToString(), ReplacementStart = scan, ReplacementLength = position - scan });
                foreach (var keyword in Keywords.Where(x => prefix.Length == 0 || x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    if (!items.Any(x => x.DisplayText == keyword)) items.Add(new ScriptCompletionItem { DisplayText = keyword, InsertText = keyword, Kind = "Keyword", Description = "C# 关键字", ReplacementStart = scan, ReplacementLength = position - scan });
            }
            return items.OrderBy(x => x.DisplayText, StringComparer.OrdinalIgnoreCase).Take(5000).ToArray();
        }

        public ScriptSignatureHelp GetSignatureHelp(ScriptNodeConfig config, int position)
        {
            var classMode = IsClassCode(config);
            var originalCode = LoadCode(config); var code = classMode ? ApplyImportsToClassCode(originalCode, config == null ? null : config.Imports) : originalCode;
            if (classMode && code.Length != originalCode.Length) position += code.Length - originalCode.Length;
            position = Math.Max(0, Math.Min(position, code.Length));
            var tree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.CSharp7_3, kind: classMode ? SourceCodeKind.Regular : SourceCodeKind.Script));
            var compilation = classMode
                ? CSharpCompilation.Create("VisionFlowSignatureHelp", new[] { tree }, GetMetadataReferences(config), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                : CSharpCompilation.CreateScriptCompilation("VisionFlowSignatureHelp", tree, GetMetadataReferences(config), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, usings: GetImports(config)), globalsType: typeof(ScriptGlobals));
            var root = tree.GetRoot(); var model = compilation.GetSemanticModel(tree, true);
            var token = root.FindToken(Math.Max(0, position - 1));
            var arguments = token.Parent == null ? null : token.Parent.AncestorsAndSelf().OfType<ArgumentListSyntax>()
                .FirstOrDefault(x => x.OpenParenToken.SpanStart < position && (x.CloseParenToken.IsMissing || position <= x.CloseParenToken.Span.End));
            var invocation = arguments == null ? null : arguments.Parent as InvocationExpressionSyntax;
            if (invocation == null) return new ScriptSignatureHelp();
            var methods = model.GetMemberGroup(invocation.Expression).OfType<IMethodSymbol>().ToList();
            if (methods.Count == 0)
            {
                var info = model.GetSymbolInfo(invocation.Expression);
                methods.AddRange(info.CandidateSymbols.OfType<IMethodSymbol>());
                var symbol = info.Symbol as IMethodSymbol; if (symbol != null) methods.Add(symbol);
            }
            var result = new ScriptSignatureHelp
            {
                ActiveParameter = arguments.Arguments.GetSeparators().Count(x => x.SpanStart < position)
            };
            foreach (var signature in methods
                .Select(x => x.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .Take(100)) result.Signatures.Add(signature);
            return result;
        }

        public static bool IsClassCode(ScriptNodeConfig config) { return LoadCode(config).IndexOf(nameof(VisionFlowAdvancedScriptBase), StringComparison.Ordinal) >= 0; }
        public static string WrapStatementsInClass(string statements)
        {
            var body = string.Join(Environment.NewLine, (statements ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(x => "        " + x));
            return "using System;" + Environment.NewLine + "using System.Collections.Generic;" + Environment.NewLine + "using System.Linq;" + Environment.NewLine + "using VisionFlowStudio.Core;" + Environment.NewLine + "using VisionFlowStudio.Scripting;" + Environment.NewLine + Environment.NewLine
                + "public sealed class VisionFlowAdvancedScript : VisionFlowAdvancedScriptBase" + Environment.NewLine + "{" + Environment.NewLine
                + "    public override void Initialize()" + Environment.NewLine + "    {" + Environment.NewLine + "    }" + Environment.NewLine + Environment.NewLine
                + "    public override void Run()" + Environment.NewLine + "    {" + Environment.NewLine + body + Environment.NewLine + "    }" + Environment.NewLine + "}";
        }
        public static string ApplyImportsToClassCode(string code, IEnumerable<string> imports)
        {
            code = code ?? string.Empty; if (code.IndexOf(nameof(VisionFlowAdvancedScriptBase), StringComparison.Ordinal) < 0) return code;
            var normalized = (imports ?? Enumerable.Empty<string>()).Select(NormalizeImport).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            if (normalized.Length == 0) return code;
            var existing = new HashSet<string>(Regex.Matches(code, @"(?m)^\s*using\s+([\w\.]+)\s*;").Cast<Match>().Select(x => x.Groups[1].Value), StringComparer.Ordinal);
            var missing = normalized.Where(x => !existing.Contains(x)).ToArray(); if (missing.Length == 0) return code;
            var lines = string.Join(Environment.NewLine, missing.Select(x => "using " + x + ";")) + Environment.NewLine;
            var matches = Regex.Matches(code, @"(?m)^\s*using\s+[\w\.]+\s*;\s*(?:\r?\n)?");
            if (matches.Count == 0) return lines + code;
            var last = matches[matches.Count - 1]; return code.Insert(last.Index + last.Length, lines);
        }
        private static string NormalizeImport(string value)
        {
            var text = (value ?? string.Empty).Trim(); if (text.StartsWith("using ", StringComparison.Ordinal)) text = text.Substring(6).Trim(); return text.TrimEnd(';').Trim();
        }

        public static IList<string> ParseList(string value) { return (value ?? string.Empty).Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        public static string LoadCode(ScriptNodeConfig config)
        {
            if (config != null && !string.IsNullOrWhiteSpace(config.ScriptFile) && File.Exists(config.ScriptFile)) return File.ReadAllText(config.ScriptFile, Encoding.UTF8);
            return config == null ? string.Empty : config.Code ?? string.Empty;
        }

        private static Microsoft.CodeAnalysis.Scripting.Script<object> CreateScript(ScriptNodeConfig config)
        {
            var options = ScriptOptions.Default.WithReferences(GetMetadataReferences(config)).WithImports(GetImports(config)).WithEmitDebugInformation(true).WithFilePath(config == null ? string.Empty : config.ScriptFile ?? string.Empty);
            return CSharpScript.Create<object>(LoadCode(config), options, typeof(ScriptGlobals));
        }
        private static CSharpCompilation CreateClassCompilation(ScriptNodeConfig config)
        {
            var source = ApplyImportsToClassCode(LoadCode(config), config == null ? null : config.Imports);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3, kind: SourceCodeKind.Regular), config == null ? string.Empty : config.ScriptFile ?? string.Empty, Encoding.UTF8);
            var suppressed = new Dictionary<string, ReportDiagnostic> { { "CS1701", ReportDiagnostic.Suppress }, { "CS1702", ReportDiagnostic.Suppress } };
            return CSharpCompilation.Create("VisionFlowAdvancedScript_" + Guid.NewGuid().ToString("N"), new[] { tree }, GetMetadataReferences(config), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug, allowUnsafe: true, specificDiagnosticOptions: suppressed));
        }
        private static ScriptCompileResult CompileClass(ScriptNodeConfig config)
        {
            var result = new ScriptCompileResult();
            foreach (var diagnostic in CreateClassCompilation(config).GetDiagnostics().Where(x => x.Severity != DiagnosticSeverity.Hidden)) result.Diagnostics.Add(ToDiagnostic(diagnostic));
            if (result.Success && !ContainsAdvancedScriptClass(config)) result.Diagnostics.Add(new ScriptDiagnostic { Severity = "Error", Id = "VFS001", Message = "必须定义一个继承 VisionFlowAdvancedScriptBase 的非抽象类", Line = 1, Column = 1 });
            return result;
        }
        private async Task<NodeRunResult> RunClassAsync(ScriptNodeConfig config, ScriptGlobals globals, CancellationToken cancellationToken, Stopwatch watch)
        {
            var key = CreateCacheKey(config); Type scriptType;
            if (!_classCache.TryGetValue(key, out scriptType))
            {
                var compilation = CreateClassCompilation(config); var diagnostics = compilation.GetDiagnostics();
                var errors = diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length > 0) return ErrorResult(watch, string.Join(Environment.NewLine, errors.Select(x => ToDiagnostic(x).ToString())));
                using (var stream = new MemoryStream())
                {
                    var emit = compilation.Emit(stream);
                    if (!emit.Success) return ErrorResult(watch, string.Join(Environment.NewLine, emit.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Select(x => ToDiagnostic(x).ToString())));
                    var assembly = Assembly.Load(stream.ToArray());
                    scriptType = assembly.GetTypes().FirstOrDefault(x => !x.IsAbstract && typeof(VisionFlowAdvancedScriptBase).IsAssignableFrom(x));
                    if (scriptType == null) return ErrorResult(watch, "必须定义一个继承 VisionFlowAdvancedScriptBase 的非抽象类");
                    _classCache[key] = scriptType;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (globals == null) return ErrorResult(watch, "C# 高级脚本运行上下文为空，请检查脚本节点创建的 ScriptGlobals。");
            VisionFlowAdvancedScriptBase instance;
            try
            {
                instance = (VisionFlowAdvancedScriptBase)Activator.CreateInstance(scriptType);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ErrorResult(watch, "C# 高级脚本构造失败：" + FormatException(ex));
            }
            if (instance == null) return ErrorResult(watch, "C# 高级脚本构造失败：Activator.CreateInstance 返回空实例。");
            try
            {
                instance.Attach(globals);
                instance.Initialize();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ErrorResult(watch, "C# 高级脚本初始化失败：" + FormatException(ex));
            }
            try
            {
                instance.Run();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ErrorResult(watch, "C# 高级脚本运行失败：" + FormatException(ex));
            }
            await Task.CompletedTask.ConfigureAwait(false); cancellationToken.ThrowIfCancellationRequested(); watch.Stop();
            return new NodeRunResult { Status = NodeRunStatus.Ok, Message = string.Format("C# 高级脚本执行完成，输出 {0} 项", globals.Outputs.Count), CostMs = watch.Elapsed.TotalMilliseconds, Outputs = new Dictionary<string, object>(globals.Outputs, StringComparer.OrdinalIgnoreCase) };
        }
        private static bool ContainsAdvancedScriptClass(ScriptNodeConfig config)
        {
            var tree = CSharpSyntaxTree.ParseText(ApplyImportsToClassCode(LoadCode(config), config == null ? null : config.Imports), new CSharpParseOptions(LanguageVersion.CSharp7_3));
            return tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Any(x => x.BaseList != null && x.BaseList.Types.Any(t => t.Type.ToString().EndsWith(nameof(VisionFlowAdvancedScriptBase), StringComparison.Ordinal)));
        }
        private static IEnumerable<string> GetImports(ScriptNodeConfig config) { return DefaultImports.Concat(config == null ? Enumerable.Empty<string>() : config.Imports ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal); }
        private static IEnumerable<MetadataReference> GetMetadataReferences(ScriptNodeConfig config)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) { try { if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location)) paths.Add(assembly.Location); } catch { } }
            foreach (var type in new[] { typeof(object), typeof(Enumerable), typeof(Task), typeof(VisionContext), typeof(ScriptGlobals) }) paths.Add(type.Assembly.Location);
            if (config != null) foreach (var path in config.References ?? Enumerable.Empty<string>()) if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) paths.Add(Path.GetFullPath(path));
            return paths.Select(path => MetadataReference.CreateFromFile(path));
        }
        private static void LoadExternalAssemblies(ScriptNodeConfig config)
        {
            if (config == null) return;
            foreach (var reference in config.References ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(reference)) continue;
                var path = Path.GetFullPath(reference);
                if (!File.Exists(path)) throw new FileNotFoundException("脚本引用的 DLL 不存在", path);
                RegisterDependencySearchPaths(path);
                var assemblyName = AssemblyName.GetAssemblyName(path);
                var loaded = AppDomain.CurrentDomain.GetAssemblies().Any(x => AssemblyName.ReferenceMatchesDefinition(x.GetName(), assemblyName));
                if (!loaded) Assembly.LoadFrom(path);
            }
        }
        private static void RegisterDependencySearchPaths(string assemblyPath)
        {
            var directory = Path.GetDirectoryName(assemblyPath); if (string.IsNullOrWhiteSpace(directory)) return;
            var parent = Directory.GetParent(directory); var grandParent = parent == null ? null : parent.Parent;
            var candidates = new List<string> { directory };
            foreach (var root in new[] { directory, parent == null ? null : parent.FullName, grandParent == null ? null : grandParent.FullName })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                candidates.Add(Path.Combine(root, "x64")); candidates.Add(Path.Combine(root, "win-x64")); candidates.Add(Path.Combine(root, "win64")); candidates.Add(Path.Combine(root, "x64-win64"));
                candidates.Add(Path.Combine(root, "native")); candidates.Add(Path.Combine(root, "runtimes", "win-x64", "native")); candidates.Add(Path.Combine(root, "bin", "x64")); candidates.Add(Path.Combine(root, "bin", "x64-win64"));
            }
            lock (DependencyPathSync)
            {
                var additions = candidates.Where(Directory.Exists).Select(Path.GetFullPath).Where(x => RegisteredDependencyPaths.Add(x)).ToArray(); if (additions.Length == 0) return;
                var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? string.Empty;
                Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator.ToString(), additions.Concat(new[] { current })), EnvironmentVariableTarget.Process);
            }
        }
        private static string CreateCacheKey(ScriptNodeConfig config)
        {
            var text = LoadCode(config) + "\n#R\n" + string.Join("\n", (config == null ? Enumerable.Empty<string>() : config.References ?? Enumerable.Empty<string>()).Select(x => x + "|" + (File.Exists(x) ? File.GetLastWriteTimeUtc(x).Ticks.ToString(CultureInfo.InvariantCulture) : "0"))) + "\n#U\n" + string.Join("\n", GetImports(config));
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        private static ScriptDiagnostic ToDiagnostic(Diagnostic diagnostic)
        {
            var line = 0; var column = 0;
            if (diagnostic.Location != Location.None && diagnostic.Location.IsInSource) { var position = diagnostic.Location.GetLineSpan().StartLinePosition; line = position.Line + 1; column = position.Character + 1; }
            return new ScriptDiagnostic { Severity = diagnostic.Severity.ToString(), Id = diagnostic.Id, Message = diagnostic.GetMessage(CultureInfo.CurrentCulture), Line = line, Column = column };
        }
        private static NodeRunResult ErrorResult(Stopwatch watch, string message) { watch.Stop(); return new NodeRunResult { Status = NodeRunStatus.Error, Message = message, CostMs = watch.Elapsed.TotalMilliseconds }; }
        private static string FormatException(Exception exception)
        {
            var current = exception; while (current.InnerException != null) current = current.InnerException;
            return current.GetType().Name + ": " + current.Message + (string.IsNullOrWhiteSpace(current.StackTrace) ? string.Empty : Environment.NewLine + current.StackTrace);
        }
    }
}
