using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace VisionFlowStudio.App
{
    public partial class VisionImageViewer : UserControl
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(BitmapSource), typeof(VisionImageViewer), new PropertyMetadata(null, OnSourceChanged));
        private bool _dragging; private bool _fitMode = true; private Point _dragStart; private double _offsetX; private double _offsetY; private double _startX; private double _startY;
        public BitmapSource Source { get { return (BitmapSource)GetValue(SourceProperty); } set { SetValue(SourceProperty, value); } }
        public VisionImageViewer() { InitializeComponent(); }
        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var viewer = (VisionImageViewer)d;
            var source = e.NewValue as BitmapSource;

            // WPF normally lays out a bitmap in device-independent units based on
            // its embedded DPI. Industrial images often carry non-96 DPI metadata,
            // while our zoom math intentionally uses pixel dimensions. Giving the
            // image an explicit pixel-sized layout keeps fitting and cursor sampling
            // correct regardless of the file's DPI metadata.
            viewer.ImageElement.Width = source == null ? double.NaN : source.PixelWidth;
            viewer.ImageElement.Height = source == null ? double.NaN : source.PixelHeight;
            viewer.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(viewer.Fit));
        }
        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Source != null && _fitMode)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(Fit));
        }
        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Source == null) return; _fitMode = false; var cursor = e.GetPosition(Viewport); var old = Scale.ScaleX; var next = Math.Max(0.02, Math.Min(64, old * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            var imageX = (cursor.X - _offsetX) / old; var imageY = (cursor.Y - _offsetY) / old; Scale.ScaleX = Scale.ScaleY = next; SetOffset(cursor.X - imageX * next, cursor.Y - imageY * next); UpdateInfo(cursor); e.Handled = true;
        }
        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (Source == null) return; if (e.ClickCount == 2) { Fit(); e.Handled = true; return; } _fitMode = false; _dragging = true; _dragStart = e.GetPosition(Viewport); _startX = _offsetX; _startY = _offsetY; Viewport.CaptureMouse(); }
        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { _dragging = false; Viewport.ReleaseMouseCapture(); }
        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            var current = e.GetPosition(Viewport); if (_dragging) SetOffset(_startX + current.X - _dragStart.X, _startY + current.Y - _dragStart.Y);
            UpdateInfo(current);
        }
        private void Viewport_MouseLeave(object sender, MouseEventArgs e) { if (!_dragging) UpdateInfo(null); }
        private void Fit_Click(object sender, RoutedEventArgs e) { Fit(); }
        private void Actual_Click(object sender, RoutedEventArgs e) { if (Source == null) return; _fitMode = false; Scale.ScaleX = Scale.ScaleY = 1; SetOffset((Viewport.ActualWidth - Source.PixelWidth) / 2, (Viewport.ActualHeight - Source.PixelHeight) / 2); UpdateInfo(null); }
        private void Fit()
        {
            if (Source == null || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
            _fitMode = true; var scale = Math.Min(Viewport.ActualWidth / Source.PixelWidth, Viewport.ActualHeight / Source.PixelHeight); Scale.ScaleX = Scale.ScaleY = Math.Max(0.02, scale); SetOffset((Viewport.ActualWidth - Source.PixelWidth * Scale.ScaleX) / 2, (Viewport.ActualHeight - Source.PixelHeight * Scale.ScaleY) / 2); UpdateInfo(null);
        }
        private void UpdateInfo(Point? point)
        {
            var prefix = string.Format("Zoom: {0:0.0}%", Scale.ScaleX * 100); if (Source == null || point == null) { PixelInfo.Text = "X: --  Y: --  Gray: --  " + prefix; return; }
            var x = (int)Math.Floor((point.Value.X - _offsetX) / Scale.ScaleX); var y = (int)Math.Floor((point.Value.Y - _offsetY) / Scale.ScaleY);
            if (x < 0 || y < 0 || x >= Source.PixelWidth || y >= Source.PixelHeight) { PixelInfo.Text = "X: --  Y: --  Gray: --  " + prefix; return; }
            try
            {
                var bpp = Source.Format.BitsPerPixel; var bytes = Math.Max(1, (bpp + 7) / 8); var pixel = new byte[bytes]; Source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, bytes, 0);
                var gray = bytes >= 3 ? (int)Math.Round(pixel[2] * 0.299 + pixel[1] * 0.587 + pixel[0] * 0.114) : pixel[0]; PixelInfo.Text = string.Format("X: {0}  Y: {1}  Gray: {2}  {3}", x, y, gray, prefix);
            }
            catch { PixelInfo.Text = string.Format("X: {0}  Y: {1}  Gray: --  {2}", x, y, prefix); }
        }

        private void SetOffset(double x, double y)
        {
            _offsetX = x; _offsetY = y; Canvas.SetLeft(ImageElement, x); Canvas.SetTop(ImageElement, y);
        }
    }
}
