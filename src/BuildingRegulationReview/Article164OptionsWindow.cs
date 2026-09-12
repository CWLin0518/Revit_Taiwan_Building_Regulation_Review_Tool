using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace BuildingRegulationReview
{
    internal sealed class Article164OptionsWindow : Window
    {
        private const double MinWidthMeters = 1.0;
        private const double MaxWidthMeters = 40.0;

        private readonly TextBox _width = new TextBox { Text = "8" };
        private readonly Slider _widthWholeSlider = new Slider
        {
            Minimum = 0, Maximum = MaxWidthMeters, Value = 8,
            TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0)
        };
        private readonly Slider _widthFractionSlider = new Slider
        {
            Minimum = 0, Maximum = 0.9, Value = 0,
            TickFrequency = 0.1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0)
        };
        private readonly CheckBox _open = new CheckBox { Content = "道路對側為永久性空地", Margin = new Thickness(0, 14, 0, 0) };
        private bool _syncing;

        public double RoadWidthMeters { get; private set; }
        public bool HasPermanentOpenSpace => _open.IsChecked == true;

        public Article164OptionsWindow()
        {
            Title = "第164條參數"; Width = 340; Height = 300; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var p = new StackPanel { Margin = new Thickness(18) };
            p.Children.Add(new TextBlock { Text = $"面前道路寬度 Sw（公尺，{MinWidthMeters:0.#}~{MaxWidthMeters:0.#}）" });
            p.Children.Add(_width);
            p.Children.Add(new TextBlock { Text = "整數（公尺）", Margin = new Thickness(0, 10, 0, 0) });
            p.Children.Add(_widthWholeSlider);
            p.Children.Add(new TextBlock { Text = "小數（0.1 公尺）", Margin = new Thickness(0, 6, 0, 0) });
            p.Children.Add(_widthFractionSlider);
            p.Children.Add(_open);

            _widthWholeSlider.ValueChanged += (_, __) => UpdateTextFromSliders();
            _widthFractionSlider.ValueChanged += (_, __) => UpdateTextFromSliders();
            _width.TextChanged += (_, __) =>
            {
                if (_syncing) return;
                if (!double.TryParse(_width.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return;
                v = System.Math.Max(0, System.Math.Min(MaxWidthMeters, v));
                var whole = System.Math.Floor(v);
                var fraction = System.Math.Round(v - whole, 1);
                if (fraction >= 1.0) { whole += 1; fraction = 0; }
                _syncing = true;
                _widthWholeSlider.Value = whole;
                _widthFractionSlider.Value = fraction;
                _syncing = false;
            };

            var ok = new Button { Content = "開始檢討", Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(8) };
            ok.Click += (_, __) => Accept(); p.Children.Add(ok); Content = p;
        }

        private void UpdateTextFromSliders()
        {
            if (_syncing) return;
            _syncing = true;
            var combined = System.Math.Round(_widthWholeSlider.Value + _widthFractionSlider.Value, 1);
            _width.Text = combined.ToString("0.0", CultureInfo.CurrentCulture);
            _syncing = false;
        }

        private void Accept()
        {
            if (!double.TryParse(_width.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) || v < MinWidthMeters || v > MaxWidthMeters)
            { MessageBox.Show($"請輸入 {MinWidthMeters:0.#}~{MaxWidthMeters:0.#} 公尺的道路寬度。", Title); return; }
            RoadWidthMeters = v; DialogResult = true;
        }
    }
}
