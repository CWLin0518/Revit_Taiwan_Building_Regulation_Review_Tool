using System.Globalization;
using System.Windows;
using System.Windows.Controls;
namespace BuildingRegulationReview
{
    internal sealed class Article164OptionsWindow : Window
    {
        private readonly TextBox _width = new TextBox { Text = "8" };
        private readonly CheckBox _open = new CheckBox { Content = "道路對側為永久性空地", Margin = new Thickness(0, 10, 0, 0) };
        public double RoadWidthMeters { get; private set; }
        public bool HasPermanentOpenSpace => _open.IsChecked == true;
        public Article164OptionsWindow()
        {
            Title = "第164條參數"; Width = 340; Height = 190; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var p = new StackPanel { Margin = new Thickness(18) };
            p.Children.Add(new TextBlock { Text = "面前道路寬度 Sw（公尺）" }); p.Children.Add(_width); p.Children.Add(_open);
            var ok = new Button { Content = "開始檢討", Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(8) };
            ok.Click += (_, __) => Accept(); p.Children.Add(ok); Content = p;
        }
        private void Accept()
        {
            if (!double.TryParse(_width.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) || v <= 0) { MessageBox.Show("請輸入大於零的道路寬度。", Title); return; }
            RoadWidthMeters = v; DialogResult = true;
        }
    }
}
