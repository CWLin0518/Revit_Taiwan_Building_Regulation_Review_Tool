using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Revit.ProjectSetup;

namespace BuildingRegulationReview
{
    internal sealed class FireReviewSetupWindow : Window
    {
        private readonly ListBox _floors = new ListBox { DisplayMemberPath = "Name", SelectionMode = SelectionMode.Extended, MinHeight = 100, MaxHeight = 180 };
        private readonly ComboBox _scheme = NewCombo();
        private readonly ComboBox _template = NewCombo();
        private readonly ComboBox _scopeBox = NewCombo();
        private readonly CheckBox _copyCrop = new CheckBox { Content = "從來源平面複製裁切設定", IsChecked = true };

        public IReadOnlyList<ReviewPackageSetupSelection> Selections { get; private set; }
        public bool OpenAreaComputations { get; private set; }

        public FireReviewSetupWindow(RevitReviewSetupCatalog catalog)
        {
            Title = "防火區劃檢討設定"; Width = 560; SizeToContent = SizeToContent.Height;
            MinHeight = 430; MaxHeight = SystemParameters.WorkArea.Height * 0.9;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _floors.ItemsSource = catalog.FloorPlans; _scheme.ItemsSource = catalog.AreaSchemes;
            _template.ItemsSource = WithNone(catalog.AreaPlanTemplates); _scopeBox.ItemsSource = WithNone(catalog.ScopeBoxes);
            if (catalog.FloorPlans.Count > 0) _floors.SelectedIndex = 0;
            _scheme.SelectedIndex = 0; _template.SelectedIndex = 0; _scopeBox.SelectedIndex = 0;
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = "建立檢討套件", FontSize = 20, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "儲存設定並建立或重用受管理 Area Plan。", Margin = new Thickness(0, 4, 0, 16), Foreground = System.Windows.Media.Brushes.DimGray });
            panel.Children.Add(new TextBlock { Text = "來源樓層平面（Ctrl／Shift 可複選）", Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(_floors);
            panel.Children.Add(new TextBlock { Text = "面積配置", Margin = new Thickness(0, 8, 0, 4) });
            var schemeRow = new DockPanel();
            var computations = new Button { Content = "Area Computation…", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
            computations.Click += (_, __) => { OpenAreaComputations = true; DialogResult = false; };
            DockPanel.SetDock(computations, Dock.Right);
            schemeRow.Children.Add(computations);
            _scheme.MinWidth = 0;
            schemeRow.Children.Add(_scheme);
            panel.Children.Add(schemeRow);
            AddField(panel, "Area Plan 視圖樣板（選填）", _template); AddField(panel, "Scope Box（選填）", _scopeBox);
            _copyCrop.Margin = new Thickness(0, 8, 0, 8); panel.Children.Add(_copyCrop);
            panel.Children.Add(new TextBlock { Text = "需要新面積配置時，點選 Area Computation，在 Revit 中建立後重新開啟此設定。", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray });
            var ok = new Button { Content = "建立 Area Plan", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, __) => Accept(); panel.Children.Add(ok);
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void Accept()
        {
            var floors = _floors.SelectedItems.Cast<RevitSetupOption>().ToList();
            if (floors.Count == 0) { MessageBox.Show(this, "請至少選取一個樓層平面。", Title); return; }
            var scheme = (RevitSetupOption)_scheme.SelectedItem;
            if (scheme == null) { MessageBox.Show(this, "請先用 Area Computation 建立面積配置，再重新開啟此設定。", Title); return; }
            var template = (RevitSetupOption)_template.SelectedItem; var scope = (RevitSetupOption)_scopeBox.SelectedItem;
            Selections = floors.Select(floor => new ReviewPackageSetupSelection(floor.UniqueId, floor.RelatedUniqueId, scheme.UniqueId,
                EmptyToNull(template.UniqueId), _copyCrop.IsChecked == true, EmptyToNull(scope.UniqueId))).ToList();
            DialogResult = true;
        }

        private static ComboBox NewCombo() => new ComboBox { DisplayMemberPath = "Name", MinWidth = 360 };
        private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) }); panel.Children.Add(control); }
        private static List<RevitSetupOption> WithNone(IEnumerable<RevitSetupOption> source) { var result = new List<RevitSetupOption> { new RevitSetupOption("", "（不指定）") }; result.AddRange(source); return result; }
        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
