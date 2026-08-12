using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace VisionFlowStudio.App
{
    public sealed class ImageDocumentWindow : Window
    {
        public ImageDocumentWindow(ImageViewDocumentViewModel document)
        {
            DataContext = document;
            Title = document == null ? "图像窗口" : document.Title;
            Width = 900;
            Height = 680;
            MinWidth = 420;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(11, 17, 32));

            SetBinding(TitleProperty, new Binding("Title") { StringFormat = "图像窗口 - {0}" });

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var title = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            header.Child = title;
            root.Children.Add(header);

            var viewer = new VisionImageViewer();
            viewer.SetBinding(VisionImageViewer.SourceProperty, new Binding("Source"));
            Grid.SetRow(viewer, 1);
            root.Children.Add(viewer);

            var footer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var state = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            state.SetBinding(TextBlock.TextProperty, new Binding("ResultState"));
            footerGrid.Children.Add(state);

            var message = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            message.SetBinding(TextBlock.TextProperty, new Binding("ResultMessage"));
            Grid.SetColumn(message, 1);
            footerGrid.Children.Add(message);

            var cycle = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            cycle.SetBinding(TextBlock.TextProperty, new Binding("CycleTime"));
            Grid.SetColumn(cycle, 2);
            footerGrid.Children.Add(cycle);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }
    }
}
