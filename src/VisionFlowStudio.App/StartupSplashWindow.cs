using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VisionFlowStudio.App
{
    public sealed class StartupSplashWindow : Window
    {
        private readonly TranslateTransform _lightTransform = new TranslateTransform(-160, 0);
        private readonly ScaleTransform _markTransform = new ScaleTransform(1, 1);
        private bool _closing;

        public StartupSplashWindow()
        {
            Width = 520; Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = true; UseLayoutRounding = true; SnapsToDevicePixels = true;

            var shell = new Border
            {
                CornerRadius = new CornerRadius(18), BorderBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(247, 251, 255)),
                Padding = new Thickness(30), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = 0.18, Color = Color.FromRgb(30, 64, 175) }
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = new Border
            {
                Width = 52, Height = 52, CornerRadius = new CornerRadius(15),
                Background = new LinearGradientBrush(Color.FromRgb(14, 165, 233), Color.FromRgb(37, 99, 235), 45),
                RenderTransform = _markTransform, RenderTransformOrigin = new Point(0.5, 0.5),
                Child = new TextBlock { Text = "VFS", Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            heading.Children.Add(mark);
            var words = new StackPanel { Margin = new Thickness(15, 1, 0, 0) };
            words.Children.Add(new TextBlock { Text = "VisionFlow Studio", FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(15, 42, 68)) });
            words.Children.Add(new TextBlock { Text = "工业视觉流程平台正在初始化…", FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(68, 103, 137)), Margin = new Thickness(1, 4, 0, 0) });
            heading.Children.Add(words); Grid.SetRow(heading, 0); grid.Children.Add(heading);

            var stage = new Grid { Height = 86, Margin = new Thickness(0, 20, 0, 15), ClipToBounds = true };
            var rail = new Border { Height = 2, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(Color.FromRgb(203, 224, 244)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 18, 0) };
            stage.Children.Add(rail);
            var dots = new Canvas { Height = 70, VerticalAlignment = VerticalAlignment.Center };
            for (var i = 0; i < 5; i++)
            {
                var dot = new Ellipse { Width = i == 2 ? 15 : 11, Height = i == 2 ? 15 : 11, Fill = new SolidColorBrush(i == 2 ? Color.FromRgb(14, 165, 233) : Color.FromRgb(125, 190, 235)) };
                Canvas.SetLeft(dot, 30 + i * 100); Canvas.SetTop(dot, i == 2 ? 27 : 29);
                dots.Children.Add(dot);
                var pulse = new DoubleAnimation(0.38, 1.0, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(i * 130), EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
                dot.BeginAnimation(OpacityProperty, pulse);
            }
            stage.Children.Add(dots);
            var light = new Rectangle
            {
                Width = 150, Height = 72, RadiusX = 36, RadiusY = 36,
                Fill = new LinearGradientBrush(Color.FromArgb(0, 56, 189, 248), Color.FromArgb(95, 56, 189, 248), 0),
                RenderTransform = _lightTransform, Opacity = 0.7
            };
            stage.Children.Add(light); Grid.SetRow(stage, 1); grid.Children.Add(stage);

            var footer = new Grid();
            footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var status = new TextBlock { Text = "正在准备工作区", Foreground = new SolidColorBrush(Color.FromRgb(71, 105, 138)), FontSize = 12 };
            var indicator = new StackPanel { Orientation = Orientation.Horizontal };
            for (var i = 0; i < 3; i++) indicator.Children.Add(new Ellipse { Width = 5, Height = 5, Margin = new Thickness(3, 0, 0, 0), Fill = new SolidColorBrush(Color.FromRgb(14, 165, 233)), Opacity = 0.7 });
            Grid.SetColumn(indicator, 1); footer.Children.Add(status); footer.Children.Add(indicator); Grid.SetRow(footer, 2); grid.Children.Add(footer);
            shell.Child = grid; Content = shell;
            Loaded += delegate { BeginAnimations(); };
        }

        private void BeginAnimations()
        {
            _lightTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-170, 540, TimeSpan.FromSeconds(1.45)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            var pulse = new DoubleAnimation(0.975, 1.025, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            _markTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            _markTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        public void CloseAnimated()
        {
            if (_closing) return; _closing = true;
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += delegate { Close(); };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
