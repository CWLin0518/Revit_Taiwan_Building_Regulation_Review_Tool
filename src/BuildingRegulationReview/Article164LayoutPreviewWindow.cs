using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BuildingRegulationReview
{
    internal sealed class Article164LayoutPreviewWindow : Window
    {
        private const double MaxCanvasPixels = 640;

        private readonly Canvas _canvas;
        private readonly Border _planBox;
        private readonly Border _legendBox;
        private readonly double _pixelsPerFoot;
        private readonly double _paperHeightFeet;

        public (double X, double Y) PlanCenter { get; private set; }
        public (double X, double Y) LegendCenter { get; private set; }

        public Article164LayoutPreviewWindow(double paperWidthFeet, double paperHeightFeet,
            double planWidthFeet, double planHeightFeet, double legendWidthFeet, double legendHeightFeet)
        {
            Title = "圖說排版預覽";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            _pixelsPerFoot = MaxCanvasPixels / Math.Max(paperWidthFeet, paperHeightFeet);
            _paperHeightFeet = paperHeightFeet;
            var canvasWidth = paperWidthFeet * _pixelsPerFoot;
            var canvasHeight = paperHeightFeet * _pixelsPerFoot;

            var root = new DockPanel { Margin = new Thickness(16) };

            var header = new TextBlock
            {
                Text = "拖曳下方兩個色塊，安排平面圖與圖例在圖紙上的位置，完成後按「確定」。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(header, Dock.Top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = "取消", Padding = new Thickness(10, 4, 10, 4) };
            cancel.Click += (_, __) => { DialogResult = false; };
            var ok = new Button { Content = "確定", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, __) => Accept();
            buttons.Children.Add(cancel); buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);

            _canvas = new Canvas { Width = canvasWidth, Height = canvasHeight, Background = Brushes.White, ClipToBounds = true };
            var canvasHost = new Border
            {
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(1),
                Child = _canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };

            _planBox = CreateDraggableBox("平面圖", planWidthFeet * _pixelsPerFoot, planHeightFeet * _pixelsPerFoot, Brushes.LightBlue);
            _legendBox = CreateDraggableBox("圖例", legendWidthFeet * _pixelsPerFoot, legendHeightFeet * _pixelsPerFoot, Brushes.LightGoldenrodYellow);
            _canvas.Children.Add(_planBox);
            _canvas.Children.Add(_legendBox);

            Canvas.SetLeft(_planBox, canvasWidth * 0.03);
            Canvas.SetTop(_planBox, Math.Max(0, (canvasHeight - _planBox.Height) / 2));
            Canvas.SetLeft(_legendBox, Math.Max(0, canvasWidth - _legendBox.Width - canvasWidth * 0.03));
            Canvas.SetTop(_legendBox, Math.Max(0, (canvasHeight - _legendBox.Height) / 2));

            EnableDrag(_planBox);
            EnableDrag(_legendBox);

            root.Children.Add(header);
            root.Children.Add(buttons);
            root.Children.Add(canvasHost);
            Content = root;

            Width = canvasWidth + 64;
            Height = canvasHeight + 130;
        }

        private static Border CreateDraggableBox(string label, double width, double height, Brush background) => new Border
        {
            Width = Math.Max(24, width), Height = Math.Max(24, height),
            Background = background, BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(1.5),
            Cursor = Cursors.SizeAll,
            Child = new TextBlock
            {
                Text = label, TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            }
        };

        private void EnableDrag(Border box)
        {
            Point? dragStart = null;
            box.MouseLeftButtonDown += (s, e) => { dragStart = e.GetPosition(_canvas); box.CaptureMouse(); e.Handled = true; };
            box.MouseMove += (s, e) =>
            {
                if (dragStart == null || e.LeftButton != MouseButtonState.Pressed) return;
                var pos = e.GetPosition(_canvas);
                var newLeft = Clamp(Canvas.GetLeft(box) + (pos.X - dragStart.Value.X), 0, _canvas.Width - box.Width);
                var newTop = Clamp(Canvas.GetTop(box) + (pos.Y - dragStart.Value.Y), 0, _canvas.Height - box.Height);
                Canvas.SetLeft(box, newLeft);
                Canvas.SetTop(box, newTop);
                dragStart = pos;
            };
            box.MouseLeftButtonUp += (s, e) => { dragStart = null; box.ReleaseMouseCapture(); };
        }

        private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

        private void Accept()
        {
            PlanCenter = ToFeetCenter(_planBox);
            LegendCenter = ToFeetCenter(_legendBox);
            DialogResult = true;
        }

        private (double X, double Y) ToFeetCenter(Border box)
        {
            var centerXPixels = Canvas.GetLeft(box) + box.Width / 2;
            var centerYPixels = Canvas.GetTop(box) + box.Height / 2;
            return (centerXPixels / _pixelsPerFoot, _paperHeightFeet - centerYPixels / _pixelsPerFoot);
        }
    }
}
