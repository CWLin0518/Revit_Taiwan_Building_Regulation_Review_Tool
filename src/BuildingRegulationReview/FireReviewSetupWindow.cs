using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Revit.ProjectSetup;

namespace BuildingRegulationReview
{
    internal sealed class FireReviewSetupWindow : Window
    {
        private readonly ComboBox _floor = NewCombo();
        private readonly ComboBox _scheme = NewCombo();
        private readonly ComboBox _template = NewCombo();
        private readonly ComboBox _scopeBox = NewCombo();
        private readonly CheckBox _copyCrop = new CheckBox { Content = "從來源平面複製裁切設定", IsChecked = true };

        public ReviewPackageSetupSelection Selection { get; private set; }

        public FireReviewSetupWindow(RevitReviewSetupCatalog catalog)
        {
            Title = "防火區劃檢討設定"; Width = 440; Height = 410; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _floor.ItemsSource = catalog.FloorPlans; _scheme.ItemsSource = catalog.AreaSchemes;
            _template.ItemsSource = WithNone(catalog.AreaPlanTemplates); _scopeBox.ItemsSource = WithNone(catalog.ScopeBoxes);
            _floor.SelectedIndex = 0; _scheme.SelectedIndex = 0; _template.SelectedIndex = 0; _scopeBox.SelectedIndex = 0;
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "建立檢討套件", FontSize = 20, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "此步驟只儲存設定，不會建立 Area Plan。", Margin = new Thickness(0, 4, 0, 16), Foreground = System.Windows.Media.Brushes.DimGray });
            AddField(panel, "來源樓層平面", _floor); AddField(panel, "面積配置", _scheme);
            AddField(panel, "Area Plan 視圖樣板（選填）", _template); AddField(panel, "Scope Box（選填）", _scopeBox);
            _copyCrop.Margin = new Thickness(0, 8, 0, 8); panel.Children.Add(_copyCrop);
            panel.Children.Add(new TextBlock { Text = "需要新面積配置時，請先使用 Revit 的「面積配置」命令建立。", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray });
            var ok = new Button { Content = "儲存設定", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, __) => Accept(); panel.Children.Add(ok); Content = panel;
        }

        private void Accept()
        {
            var floor = (RevitSetupOption)_floor.SelectedItem; var scheme = (RevitSetupOption)_scheme.SelectedItem;
            var template = (RevitSetupOption)_template.SelectedItem; var scope = (RevitSetupOption)_scopeBox.SelectedItem;
            Selection = new ReviewPackageSetupSelection(floor.UniqueId, floor.RelatedUniqueId, scheme.UniqueId,
                EmptyToNull(template.UniqueId), _copyCrop.IsChecked == true, EmptyToNull(scope.UniqueId));
            DialogResult = true;
        }

        private static ComboBox NewCombo() => new ComboBox { DisplayMemberPath = "Name", MinWidth = 360 };
        private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) }); panel.Children.Add(control); }
        private static List<RevitSetupOption> WithNone(IEnumerable<RevitSetupOption> source) { var result = new List<RevitSetupOption> { new RevitSetupOption("", "（不指定）") }; result.AddRange(source); return result; }
        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
