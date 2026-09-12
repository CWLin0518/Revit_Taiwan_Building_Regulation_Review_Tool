using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview
{
    internal sealed class Article164TitleBlockWindow : Window
    {
        private const double MinTextSizeMillimeters = 1.5;
        private const double MaxTextSizeMillimeters = 6.0;
        private const double DefaultTextSizeMillimeters = 2.5;

        private readonly ListBox _list = new ListBox { Margin = new Thickness(0, 8, 0, 0), Height = 220 };
        private readonly TextBox _textSize = new TextBox { Text = DefaultTextSizeMillimeters.ToString(CultureInfo.CurrentCulture) };
        private readonly Slider _textSizeSlider = new Slider
        {
            Minimum = MinTextSizeMillimeters, Maximum = MaxTextSizeMillimeters, Value = DefaultTextSizeMillimeters,
            TickFrequency = 0.5, IsSnapToTickEnabled = false, Margin = new Thickness(0, 6, 0, 10)
        };
        private bool _syncing;

        public ElementId SelectedTitleBlockTypeId { get; private set; } = ElementId.InvalidElementId;
        public double TextSizeMillimeters { get; private set; } = DefaultTextSizeMillimeters;

        public Article164TitleBlockWindow(Document document)
        {
            Title = "選擇圖框（Title Block）";
            Width = 380; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var titleBlocks = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                .ToList();

            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(new TextBlock { Text = "選擇要用於本張圖紙的圖框類型：", TextWrapping = TextWrapping.Wrap });

            foreach (var titleBlock in titleBlocks)
                _list.Items.Add(new ListBoxItem { Content = $"{titleBlock.FamilyName} - {titleBlock.Name}", Tag = titleBlock.Id });
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            root.Children.Add(_list);

            root.Children.Add(new TextBlock
            {
                Text = $"圖例文字大小（毫米，{MinTextSizeMillimeters:0.#}~{MaxTextSizeMillimeters:0.#}）",
                Margin = new Thickness(0, 14, 0, 0)
            });
            root.Children.Add(_textSize);
            root.Children.Add(_textSizeSlider);

            _textSizeSlider.ValueChanged += (_, e) =>
            {
                if (_syncing) return;
                _syncing = true;
                _textSize.Text = e.NewValue.ToString("0.#", CultureInfo.CurrentCulture);
                _syncing = false;
            };
            _textSize.TextChanged += (_, __) =>
            {
                if (_syncing) return;
                if (!double.TryParse(_textSize.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v)) return;
                _syncing = true;
                _textSizeSlider.Value = System.Math.Max(MinTextSizeMillimeters, System.Math.Min(MaxTextSizeMillimeters, v));
                _syncing = false;
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "取消", Padding = new Thickness(10, 4, 10, 4) };
            cancel.Click += (_, __) => { DialogResult = false; };
            var ok = new Button { Content = "確定", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, __) => Accept();
            buttons.Children.Add(cancel); buttons.Children.Add(ok);
            root.Children.Add(buttons);

            Content = root;
        }

        private void Accept()
        {
            if (_list.Items.Count == 0)
            {
                MessageBox.Show("專案裡沒有任何圖框（Title Block）類型，請先載入圖框族群後再試一次。", Title);
                return;
            }
            if (!(_list.SelectedItem is ListBoxItem item) || !(item.Tag is ElementId id))
            {
                MessageBox.Show("請先選擇一個圖框類型。", Title);
                return;
            }
            if (!double.TryParse(_textSize.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var textSize)
                || textSize < MinTextSizeMillimeters || textSize > MaxTextSizeMillimeters)
            {
                MessageBox.Show($"請輸入 {MinTextSizeMillimeters:0.#}~{MaxTextSizeMillimeters:0.#} 毫米的圖例文字大小。", Title);
                return;
            }
            SelectedTitleBlockTypeId = id;
            TextSizeMillimeters = textSize;
            DialogResult = true;
        }
    }
}
