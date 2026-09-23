using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// Batch-edits the fire review's Type parameters for everything one view shows: 結構材料,
    /// 防火被覆厚度, 設計防火時效 and, for openings, 設計防火保護.
    /// </summary>
    /// <remarks>
    /// Nothing reaches the model until 寫入模型: 套用推定值 only fills the editable column, so the
    /// derived numbers can be reviewed against their clause first. The panel never writes
    /// 防火檢討_法規要求防火時效 — that belongs to the rules' write-back (spec 11.5 step 4).
    /// </remarks>
    public partial class FireReviewParameterPanelWindow : Window
    {
        private readonly ObservableCollection<FireReviewTypeRowViewModel> _rows =
            new ObservableCollection<FireReviewTypeRowViewModel>();

        internal FireReviewParameterPanelWindow(FireReviewTypeTable table, string viewName)
        {
            InitializeComponent();
            if (table == null) throw new ArgumentNullException(nameof(table));

            foreach (var row in table.Rows) _rows.Add(new FireReviewTypeRowViewModel(row));
            Grid.ItemsSource = _rows;

            ScopeText.Text = string.IsNullOrWhiteSpace(viewName)
                ? $"整個專案，共 {_rows.Count} 個類型。"
                : $"自視圖「{viewName}」收集，共 {_rows.Count} 個類型。";

            if (table.Warnings.Count > 0)
            {
                WarningBox.Visibility = Visibility.Visible;
                WarningText.Text = string.Join("\n", table.Warnings);
            }

            UpdateStatus();
        }

        /// <summary>The edits the user accepted; empty until 寫入模型 is pressed.</summary>
        internal IReadOnlyList<FireReviewParameterEdit> Edits { get; private set; } =
            new List<FireReviewParameterEdit>();

        /// <summary>Set by the command after it has written, so the panel can show what happened.</summary>
        internal Action<IReadOnlyList<FireReviewParameterEdit>> Write { get; set; }

        /// <summary>Called by the command with the outcome of <see cref="Write"/>.</summary>
        internal void ReportWritten(string summary) => StatusText.Text = summary;

        private void ApplyAll_OnClick(object sender, RoutedEventArgs e) => Apply(_rows);

        private void ApplySelected_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = Grid.SelectedItems.OfType<FireReviewTypeRowViewModel>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "請先選取要套用的列。", Title);
                return;
            }

            Apply(selected);
        }

        private void Apply(IEnumerable<FireReviewTypeRowViewModel> rows)
        {
            CommitEdit();
            var applied = 0;
            var blocked = new List<FireReviewTypeRowViewModel>();
            foreach (var row in rows)
            {
                if (row.CanApplyDerived) { row.ApplyDerived(); applied++; }
                else if (row.SupportsDerivation) blocked.Add(row);
            }

            Grid.Items.Refresh();
            var reasons = blocked
                .GroupBy(r => r.Derivation.Kind)
                .Select(g => $"{Reason(g.Key)} {g.Count()} 列")
                .ToList();

            StatusText.Text = $"已套用推定值到 {applied} 列" +
                              (reasons.Count > 0 ? $"；未推定：{string.Join("、", reasons)}。" : "。") +
                              " 尚未寫入模型。";
        }

        private static string Reason(FireRatingDerivationKind kind)
        {
            switch (kind)
            {
                case FireRatingDerivationKind.MaterialMissing: return "未填結構材料";
                case FireRatingDerivationKind.CoverMissing: return "SC 未填被覆厚度";
                case FireRatingDerivationKind.DimensionMissing: return "讀不到斷面尺寸";
                case FireRatingDerivationKind.NotRated: return "未達門檻無時效";
                default: return "不適用";
            }
        }

        private void SetMaterial_OnClick(object sender, RoutedEventArgs e)
        {
            CommitEdit();
            var selected = Grid.SelectedItems.OfType<FireReviewTypeRowViewModel>()
                .Where(r => r.SupportsDerivation).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "請先選取要設定的牆、柱或樓板類型。", Title);
                return;
            }

            var chooser = new MaterialChooserWindow(selected.Count) { Owner = this };
            if (chooser.ShowDialog() != true) return;

            foreach (var row in selected) row.Material = chooser.Material;
            Grid.Items.Refresh();
            StatusText.Text = $"已將 {selected.Count} 列的結構材料設為 " +
                              (chooser.Material.Length == 0 ? "（清除）" : chooser.Material) + "。尚未寫入模型。";
        }

        private void Write_OnClick(object sender, RoutedEventArgs e)
        {
            CommitEdit();
            var edits = _rows.SelectMany(r => r.Edits()).ToList();
            if (edits.Count == 0)
            {
                MessageBox.Show(this, "沒有任何變更需要寫入。", Title);
                return;
            }

            var types = _rows.Count(r => r.IsDirty);
            var instances = _rows.Where(r => r.IsDirty).Sum(r => r.Source.ProjectInstanceCount);
            var confirm = MessageBox.Show(this,
                $"將寫入 {edits.Count} 個參數值，影響 {types} 個類型、專案中共 {instances} 個實體。\n\n" +
                "這些是類型參數，變更會套用到專案中所有同類型的實體，不只目前視圖。要繼續嗎？",
                Title, MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            Edits = edits;
            if (Write != null) Write(edits);
            else { DialogResult = true; Close(); }
        }

        private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

        /// <summary>Pushes the cell being typed into the view-model before anything reads it.</summary>
        private void CommitEdit()
        {
            Grid.CommitEdit(DataGridEditingUnit.Cell, true);
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void UpdateStatus()
        {
            var derivable = _rows.Count(r => r.CanApplyDerived);
            var awaiting = _rows.Count(r => r.Derivation.Kind == FireRatingDerivationKind.MaterialMissing);
            var cover = _rows.Count(r => r.Derivation.Kind == FireRatingDerivationKind.CoverMissing);

            var parts = new List<string> { $"可推定 {derivable} 列" };
            if (awaiting > 0) parts.Add($"待填結構材料 {awaiting} 列");
            if (cover > 0) parts.Add($"待填 SC 被覆厚度 {cover} 列");
            StatusText.Text = string.Join("；", parts) + "。";
        }
    }

    /// <summary>Asks which 結構材料 to stamp onto the selected rows.</summary>
    internal sealed class MaterialChooserWindow : Window
    {
        private readonly ComboBox _choice = new ComboBox { Margin = new Thickness(0, 8, 0, 0), MinWidth = 220 };

        public MaterialChooserWindow(int rowCount)
        {
            Title = "設定結構材料";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _choice.Items.Add("（清除）");
            foreach (var material in StructuralMaterialText.All)
                _choice.Items.Add(StructuralMaterialText.Label(material));
            _choice.SelectedIndex = 1;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = $"把選取的 {rowCount} 個類型設為：" });
            panel.Children.Add(_choice);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };
            var ok = new Button { Content = "確定", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsDefault = true };
            ok.Click += (_, __) => { DialogResult = true; Close(); };
            var cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5), IsCancel = true };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            Content = panel;
        }

        /// <summary>The chosen code (RC／SRC／SC), or an empty string to clear the parameter.</summary>
        public string Material => _choice.SelectedIndex <= 0
            ? string.Empty
            : StructuralMaterialText.Code(StructuralMaterialText.All[_choice.SelectedIndex - 1]);
    }
}
