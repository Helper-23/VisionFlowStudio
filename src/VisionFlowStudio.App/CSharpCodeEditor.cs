using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VisionFlowStudio.Scripting;

namespace VisionFlowStudio.App
{
    /// <summary>
    /// Native WPF C# editor based on the MIT licensed AvalonEdit control.
    /// Script compilation and IntelliSense remain provided by VisionFlowStudio/Roslyn.
    /// </summary>
    public sealed class CSharpCodeEditor : Grid, IDisposable
    {
        private readonly TextEditor _editor;
        private readonly FoldingManager _foldingManager;
        private readonly DispatcherTimer _foldingTimer;
        private readonly DiagnosticRenderer _diagnosticRenderer;
        private bool _disposed;

        public CSharpCodeEditor()
        {
            Background = Brushes.White;
            _editor = new TextEditor
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 14,
                Background = Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                ShowLineNumbers = true,
                WordWrap = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#")
            };
            _editor.Options.AllowScrollBelowDocument = true;
            _editor.Options.ConvertTabsToSpaces = true;
            _editor.Options.IndentationSize = 4;
            _editor.Options.EnableRectangularSelection = true;
            _editor.Options.EnableTextDragDrop = true;
            _editor.Options.ShowColumnRuler = false;
            _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(30, 80, 140, 220));
            _editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(50, 80, 140, 220)), 1);
            Children.Add(_editor);

            SearchPanel.Install(_editor.TextArea);
            _foldingManager = FoldingManager.Install(_editor.TextArea);
            _diagnosticRenderer = new DiagnosticRenderer(_editor.TextArea.TextView);
            _editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);

            _foldingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _foldingTimer.Tick += delegate { _foldingTimer.Stop(); UpdateFoldings(); };

            _editor.TextChanged += delegate
            {
                _foldingTimer.Stop();
                _foldingTimer.Start();
                CodeChanged?.Invoke(this, EventArgs.Empty);
            };
            _editor.TextArea.TextEntered += delegate(object sender, TextCompositionEventArgs args)
            {
                if (args.Text == ".") CompletionRequested?.Invoke(this, EventArgs.Empty);
                if (args.Text == "(" || args.Text == ",") SignatureHelpRequested?.Invoke(this, EventArgs.Empty);
                if (args.Text == ")") SignatureHelpCancelRequested?.Invoke(this, EventArgs.Empty);
                AutoInsertPair(args.Text);
            };
            _editor.TextArea.PreviewKeyDown += EditorPreviewKeyDown;
            _editor.TextArea.Caret.PositionChanged += delegate { CaretChanged?.Invoke(this, EventArgs.Empty); };
        }

        public event EventHandler CodeChanged;
        public event EventHandler CaretChanged;
        public event EventHandler CompletionRequested;
        public event EventHandler SignatureHelpRequested;
        public event EventHandler SignatureHelpCancelRequested;
        public event EventHandler CompileRequested;
        public event EventHandler RunRequested;
        public event EventHandler CompletionNextRequested;
        public event EventHandler CompletionPreviousRequested;
        public event EventHandler CompletionCommitRequested;
        public event EventHandler CompletionCancelRequested;

        public bool CompletionOpen { get; set; }
        public string Text
        {
            get { return _editor.Text; }
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_editor.Text, next, StringComparison.Ordinal)) _editor.Text = next;
                UpdateFoldings();
            }
        }
        public int CaretIndex
        {
            get { return _editor.CaretOffset; }
            set { _editor.CaretOffset = Math.Max(0, Math.Min(value, _editor.Document.TextLength)); }
        }
        public int LineCount { get { return Math.Max(1, _editor.Document.LineCount); } }

        public void SelectText(int start, int length)
        {
            start = Math.Max(0, Math.Min(start, _editor.Document.TextLength));
            length = Math.Max(0, Math.Min(length, _editor.Document.TextLength - start));
            _editor.Select(start, length);
        }

        public void ReplaceSelection(string value)
        {
            var start = _editor.SelectionStart;
            var length = _editor.SelectionLength;
            _editor.Document.Replace(start, length, value ?? string.Empty);
            _editor.CaretOffset = start + (value == null ? 0 : value.Length);
        }

        public void FocusEditor() { _editor.Focus(); _editor.TextArea.Focus(); }
        public void InsertAtCaret(string value)
        {
            ReplaceSelection(value);
        }

        public void GoTo(int line, int column)
        {
            line = Math.Max(1, Math.Min(line, LineCount));
            var documentLine = _editor.Document.GetLineByNumber(line);
            var offset = Math.Min(documentLine.EndOffset, documentLine.Offset + Math.Max(0, column - 1));
            _editor.CaretOffset = offset;
            _editor.ScrollTo(line, Math.Max(1, column));
            FocusEditor();
        }

        public Rect GetCaretRect()
        {
            try
            {
                var textView = _editor.TextArea.TextView;
                textView.EnsureVisualLines();
                var point = textView.GetVisualPosition(_editor.TextArea.Caret.Position, VisualYPosition.LineBottom);
                return new Rect(point.X, point.Y, 2, Math.Max(18, _editor.FontSize + 5));
            }
            catch { return new Rect(0, 0, 2, 20); }
        }

        public void SetDiagnostics(IEnumerable<ScriptDiagnostic> diagnostics)
        {
            var segments = new List<DiagnosticSegment>();
            foreach (var item in diagnostics ?? Enumerable.Empty<ScriptDiagnostic>())
            {
                if (!string.Equals(item.Severity, "Error", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Severity, "Warning", StringComparison.OrdinalIgnoreCase)) continue;
                if (item.Line <= 0 || item.Line > _editor.Document.LineCount) continue;
                var line = _editor.Document.GetLineByNumber(item.Line);
                var start = Math.Min(line.EndOffset, line.Offset + Math.Max(0, item.Column - 1));
                var length = Math.Max(1, Math.Min(Math.Max(1, line.EndOffset - start), 24));
                segments.Add(new DiagnosticSegment(start, length,
                    string.Equals(item.Severity, "Error", StringComparison.OrdinalIgnoreCase) ? Colors.Red : Colors.DarkOrange));
            }
            _diagnosticRenderer.SetSegments(segments);
        }

        private void EditorPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (CompletionOpen)
            {
                if (e.Key == Key.Down) { e.Handled = true; CompletionNextRequested?.Invoke(this, EventArgs.Empty); return; }
                if (e.Key == Key.Up) { e.Handled = true; CompletionPreviousRequested?.Invoke(this, EventArgs.Empty); return; }
                if (e.Key == Key.Enter || e.Key == Key.Tab) { e.Handled = true; CompletionCommitRequested?.Invoke(this, EventArgs.Empty); return; }
                if (e.Key == Key.Escape) { e.Handled = true; CompletionCancelRequested?.Invoke(this, EventArgs.Empty); return; }
            }
            if (e.Key == Key.F6) { e.Handled = true; CompileRequested?.Invoke(this, EventArgs.Empty); return; }
            if (e.Key == Key.F5) { e.Handled = true; RunRequested?.Invoke(this, EventArgs.Empty); return; }
            if (e.Key == Key.Escape) { SignatureHelpCancelRequested?.Invoke(this, EventArgs.Empty); return; }
            if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true; CompletionRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void AutoInsertPair(string text)
        {
            string close = null;
            if (text == "{") close = "}";
            else if (text == "(") close = ")";
            else if (text == "[") close = "]";
            else if (text == "\"") close = "\"";
            if (close == null || _editor.SelectionLength > 0) return;
            var offset = _editor.CaretOffset;
            if (text == "\"" && offset < _editor.Document.TextLength && _editor.Document.GetCharAt(offset) == '\"') return;
            _editor.Document.Insert(offset, close);
            _editor.CaretOffset = offset;
        }

        private void UpdateFoldings()
        {
            if (_disposed || _editor.Document == null) return;
            _foldingManager.UpdateFoldings(CreateFoldings(_editor.Text), -1);
        }

        private static IEnumerable<NewFolding> CreateFoldings(string code)
        {
            var result = new List<NewFolding>();
            var stack = new Stack<int>();
            var state = LexState.Code;
            for (var i = 0; i < (code ?? string.Empty).Length; i++)
            {
                var c = code[i]; var next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (state == LexState.LineComment) { if (c == '\n') state = LexState.Code; continue; }
                if (state == LexState.BlockComment) { if (c == '*' && next == '/') { state = LexState.Code; i++; } continue; }
                if (state == LexState.String) { if (c == '\\') i++; else if (c == '\"') state = LexState.Code; continue; }
                if (state == LexState.VerbatimString) { if (c == '\"' && next == '\"') i++; else if (c == '\"') state = LexState.Code; continue; }
                if (state == LexState.Character) { if (c == '\\') i++; else if (c == '\'') state = LexState.Code; continue; }
                if (c == '/' && next == '/') { state = LexState.LineComment; i++; continue; }
                if (c == '/' && next == '*') { state = LexState.BlockComment; i++; continue; }
                if (c == '@' && next == '\"') { state = LexState.VerbatimString; i++; continue; }
                if (c == '\"') { state = LexState.String; continue; }
                if (c == '\'') { state = LexState.Character; continue; }
                if (c == '{') stack.Push(i);
                else if (c == '}' && stack.Count > 0)
                {
                    var start = stack.Pop();
                    if (code.IndexOf('\n', start, i - start) >= 0) result.Add(new NewFolding(start, i + 1) { Name = "{ ... }" });
                }
            }
            return result.OrderBy(x => x.StartOffset);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _foldingTimer.Stop();
            _editor.TextArea.TextView.BackgroundRenderers.Remove(_diagnosticRenderer);
            FoldingManager.Uninstall(_foldingManager);
        }

        private enum LexState { Code, LineComment, BlockComment, String, VerbatimString, Character }

        private sealed class DiagnosticSegment : ISegment
        {
            public DiagnosticSegment(int offset, int length, Color color) { Offset = offset; Length = length; Color = color; }
            public int Offset { get; private set; }
            public int Length { get; private set; }
            public int EndOffset { get { return Offset + Length; } }
            public Color Color { get; private set; }
        }

        private sealed class DiagnosticRenderer : IBackgroundRenderer
        {
            private readonly TextView _textView;
            private IList<DiagnosticSegment> _segments = new List<DiagnosticSegment>();
            public DiagnosticRenderer(TextView textView) { _textView = textView; }
            public KnownLayer Layer { get { return KnownLayer.Selection; } }
            public void SetSegments(IList<DiagnosticSegment> segments) { _segments = segments ?? new List<DiagnosticSegment>(); _textView.InvalidateLayer(Layer); }
            public void Draw(TextView textView, DrawingContext drawingContext)
            {
                if (!textView.VisualLinesValid) return;
                foreach (var segment in _segments)
                {
                    var pen = new Pen(new SolidColorBrush(segment.Color), 1.2);
                    pen.Freeze();
                    foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                    {
                        var geometry = new StreamGeometry();
                        using (var context = geometry.Open())
                        {
                            var y = rect.Bottom - 1; var x = rect.Left; var up = true;
                            context.BeginFigure(new Point(x, y), false, false);
                            while (x < rect.Right) { x = Math.Min(rect.Right, x + 2); context.LineTo(new Point(x, y + (up ? -2 : 0)), true, false); up = !up; }
                        }
                        geometry.Freeze(); drawingContext.DrawGeometry(null, pen, geometry);
                    }
                }
            }
        }
    }
}
