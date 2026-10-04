using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// Batch-edits every parameter the fire review reads: the Type parameters of 牆／柱／樑／樓板 and
    /// the openings' 防火保護, the 區劃 facts on each Area, and the Project Information facts.
    /// </summary>
    /// <remarks>
    /// Nothing reaches the model until 寫入模型, and then all three tabs go in one transaction: a
    /// half-applied set of inputs would produce a review nobody asked for. 套用推定值 only fills the
    /// editable column, so the derived numbers can be checked against their clause first. The panel
    /// never writes 防火檢討_法規要求防火時效 — that belongs to the rules' write-back (spec 11.5
    /// step 4) — and never writes an Area's extent, which is what the review measures (spec 11.4).
    /// </remarks>
    public partial class FireReviewParameterPanelWindow : Window
    {
        private readonly ObservableCollection<FireReviewTypeRowViewModel> _rows =
            new ObservableCollection<FireReviewTypeRowViewModel>();

        private readonly ObservableCollection<FireReviewZoneRowViewModel> _zones =
            new ObservableCollection<FireReviewZoneRowViewModel>();

        private readonly FireReviewProjectViewModel _project;
        private FloorNumbering _floors;

        internal FireReviewParameterPanelWindow(FireReviewParameterSet set, string viewName)
        {
            InitializeComponent();
            if (set == null) throw new ArgumentNullException(nameof(set));

            foreach (var row in set.Types.Rows) _rows.Add(new FireReviewTypeRowViewModel(row));
            Grid.ItemsSource = _rows;

            _floors = set.Floors;
            foreach (var zone in set.Zones)
                _zones.Add(new FireReviewZoneRowViewModel(zone, _floors.For(zone.LevelId)));
            ZoneGrid.ItemsSource = _zones;

            if (!_floors.IsEmpty)
            {
                FloorsBox.Visibility = Visibility.Visible;
                FloorsText.Text = string.Join("\n", _floors.Warnings);
            }
            DeriveFloorsButton.IsEnabled = !_floors.IsEmpty;

            if (set.Project != null)
            {
                _project = new FireReviewProjectViewModel(set.Project);
                ProjectPanel.DataContext = _project;
                _project.PropertyChanged += (_, e) =>
                {
                    ShowProjectWarnings();
                    if (e.PropertyName == nameof(FireReviewProjectViewModel.BuildingUse)) PushBuildingUse();
                };
                ShowProjectWarnings();
                PushBuildingUse();
            }
            else
            {
                ProjectPanel.IsEnabled = false;
                ProjectMissingBox.Visibility = Visibility.Visible;
                ProjectMissingText.Text = "這個文件沒有可讀取的專案資訊。";
            }

            ScopeText.Text = string.IsNullOrWhiteSpace(viewName)
                ? $"構件類型取自整個專案，共 {_rows.Count} 個類型；區劃 {_zones.Count} 個。"
                : $"構件類型取自視圖「{viewName}」，共 {_rows.Count} 個類型；區劃 {_zones.Count} 個（取自整個專案）。";

            if (set.Types.Warnings.Count > 0)
            {
                WarningBox.Visibility = Visibility.Visible;
                WarningText.Text = string.Join("\n", set.Types.Warnings);
            }

            UpdateStatus();
        }

        /// <summary>The edits the user accepted; empty until 寫入模型 is pressed.</summary>
        internal IReadOnlyList<FireReviewParameterEdit> Edits { get; private set; } =
            new List<FireReviewParameterEdit>();

        /// <summary>Set by the command, which owns the transaction.</summary>
        internal Action<IReadOnlyList<FireReviewParameterEdit>> Write { get; set; }

        internal void ReportWritten(string summary) => StatusText.Text = summary;

        private void ShowProjectWarnings()
        {
            FireResistiveWarningText.Text = _project.FireResistiveWarning;

            var missing = _project.MissingParameters;
            ProjectMissingBox.Visibility = missing.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProjectMissingText.Text = missing.Length > 0
                ? missing + "。請先在「管理 > 專案參數 > 加入 > 共用參數」把它們綁到「專案資訊」，否則這一頁的變更寫不進去。"
                : "";
        }

        private void ApplyAll_OnClick(object sender, RoutedEventArgs e) => Apply(_rows);

        private void ApplySelected_OnClick(object sender, RoutedEventArgs e)
        {
            var selected = Grid.SelectedItems.OfType<FireReviewTypeRowViewModel>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "請先在「構件類型」分頁選取要套用的列。", Title);
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
            // 篩選條件與「結構材料」欄亮不亮是同一個問題，所以用同一個判斷：主要構造全部算，帷幕嵌板
            // 要先宣告實心。用 SupportsDerivation 會把梁漏掉——梁推不出時效，但仍然要填材料。
            var selected = Grid.SelectedItems.OfType<FireReviewTypeRowViewModel>()
                .Where(r => r.CarriesMaterial).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "請先在「構件類型」分頁選取要設定的牆、柱、梁、樓板，或已宣告為實心的帷幕嵌板。", Title);
                return;
            }

            var chooser = new MaterialChooserWindow(selected.Count) { Owner = this };
            if (chooser.ShowDialog() != true) return;

            foreach (var row in selected) row.Material = chooser.Material;
            Grid.Items.Refresh();
            StatusText.Text = $"已將 {selected.Count} 列的結構材料設為 " +
                              (chooser.Material.Length == 0 ? "（清除）" : chooser.Material) + "。尚未寫入模型。";
        }

        /// <summary>
        /// Hands every zone the 用途類組 the 專案資訊 tab currently holds, so the limit each row
        /// shows accounts for 第83條's Ｈ－２組 proviso as soon as that box changes. Display only —
        /// the value is written from the project row, once, and never from a zone.
        /// </summary>
        private void PushBuildingUse()
        {
            if (_project == null) return;
            foreach (var zone in _zones) zone.BuildingUse = _project.BuildingUse;
        }

        private void FillZones_OnClick(object sender, RoutedEventArgs e)
        {
            CommitEdit();
            var selected = ZoneGrid.SelectedItems.OfType<FireReviewZoneRowViewModel>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "請先在「區劃」分頁選取要設定的區劃。", Title);
                return;
            }

            var chooser = new ZoneFillWindow(selected.Count) { Owner = this };
            if (chooser.ShowDialog() != true) return;

            foreach (var zone in selected)
            {
                if (chooser.Use != null) zone.Use = chooser.Use;
                if (chooser.Sprinklered != null) zone.Sprinklered = chooser.Sprinklered;
                if (chooser.FloorNumber != null) zone.FloorNumber = chooser.FloorNumber;
                if (chooser.LinksRefugeFloor != null) zone.LinksRefugeFloor = chooser.LinksRefugeFloor;
                if (chooser.CannotBeSubdivided != null) zone.CannotBeSubdivided = chooser.CannotBeSubdivided;
            }

            ZoneGrid.Items.Refresh();
            StatusText.Text = $"已設定 {selected.Count} 個區劃。尚未寫入模型。";
        }

        /// <summary>
        /// Fills every zone's 所在樓層序 from the levels, and 地上層數 with the count that goes with
        /// it. Both are written to the boxes, not to the model: 第70條 divides one by the other, so a
        /// storey number accepted without the matching count would compute a position from the top
        /// that is nobody's answer.
        /// </summary>
        private void DeriveFloors_OnClick(object sender, RoutedEventArgs e)
        {
            CommitEdit();

            if (_floors.IsEmpty)
            {
                MessageBox.Show(this, "模型中沒有樓層可以判斷樓層序。", Title);
                return;
            }

            var filled = 0;
            var unknown = new List<string>();
            foreach (var zone in _zones)
            {
                if (zone.DerivedFloorNumber.HasValue) { zone.ApplyDerivedFloorNumber(); filled++; }
                else unknown.Add(zone.DisplayName);
            }

            ZoneGrid.Items.Refresh();

            if (_project != null) _project.FloorsAboveGround = _floors.FloorsAboveGround.ToString(CultureInfo.InvariantCulture);

            var missing = unknown.Count == 0
                ? ""
                : $"；{unknown.Count} 個區劃讀不到所屬樓層（{string.Join("、", unknown.Take(3))}{(unknown.Count > 3 ? "…" : "")}）";

            StatusText.Text = $"已依樓層填入 {filled} 個區劃的樓層序，地上層數帶入 {_floors.FloorsAboveGround}{missing}。" +
                              " 請核對上方的推定說明後再寫入模型。";
        }

        private void Write_OnClick(object sender, RoutedEventArgs e)
        {
            CommitEdit();

            var typeEdits = _rows.SelectMany(r => r.Edits()).ToList();
            var zoneEdits = _zones.SelectMany(z => z.Edits()).ToList();
            var projectEdits = _project == null
                ? new List<FireReviewParameterEdit>()
                : _project.Edits().ToList();

            var edits = typeEdits.Concat(zoneEdits).Concat(projectEdits).ToList();
            if (edits.Count == 0)
            {
                MessageBox.Show(this, "沒有任何變更需要寫入。", Title);
                return;
            }

            var lines = new List<string>();
            if (typeEdits.Count > 0)
            {
                var types = _rows.Count(r => r.IsDirty);
                var instances = _rows.Where(r => r.IsDirty).Sum(r => r.Source.ProjectInstanceCount);
                lines.Add($"・構件類型：{typeEdits.Count} 個值，影響 {types} 個類型、專案中共 {instances} 個實體");

                // 提案是工具猜的，留著不動也會被寫入（決議 16、D3），所以在確認視窗裡點名它有幾列——
                // 使用者為了別的欄位按下寫入時，不該順手替自己宣告了嵌板種類卻不知道。
                var proposed = _rows.Count(r => r.PanelKindIsProposed);
                if (proposed > 0)
                    lines.Add($"　其中 {proposed} 個帷幕嵌板類型的「嵌板種類」是工具由材料提案、您未修改的值");
            }

            if (zoneEdits.Count > 0) lines.Add($"・區劃：{zoneEdits.Count} 個值，影響 {_zones.Count(z => z.IsDirty)} 個區劃");
            if (projectEdits.Count > 0) lines.Add($"・專案資訊：{projectEdits.Count} 個值");

            var warning = typeEdits.Count > 0
                ? "\n\n構件類型是類型參數，變更會套用到專案中所有同類型的實體，不只目前視圖。"
                : "";

            var confirm = MessageBox.Show(this,
                "將寫入以下變更：\n\n" + string.Join("\n", lines) + warning + "\n\n要繼續嗎？",
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
            foreach (var grid in new[] { Grid, ZoneGrid })
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void UpdateStatus()
        {
            var parts = new List<string> { $"可推定 {_rows.Count(r => r.CanApplyDerived)} 列" };

            // 嵌板種類排在材料之前，因為它決定那一列到底要不要填材料。提案過但還沒寫入的列照樣算在
            // 這裡：提案不是宣告，不按下「寫入模型」CW-O 仍然答資料不足（決議 16）。
            var kinds = _rows.Count(r => r.AwaitsPanelKind);
            if (kinds > 0) parts.Add($"待宣告嵌板種類 {kinds} 列");

            var awaiting = _rows.Count(r => r.Derivation.Kind == FireRatingDerivationKind.MaterialMissing);
            if (awaiting > 0) parts.Add($"待填結構材料 {awaiting} 列");

            var cover = _rows.Count(r => r.Derivation.Kind == FireRatingDerivationKind.CoverMissing);
            if (cover > 0) parts.Add($"待填 SC 被覆厚度 {cover} 列");

            var sprinklers = _zones.Count(z => string.IsNullOrEmpty(z.Sprinklered));
            if (sprinklers > 0) parts.Add($"待填滅火設備 {sprinklers} 個區劃");

            var floors = _zones.Count(z => string.IsNullOrEmpty(z.FloorNumber));
            if (floors > 0) parts.Add($"待填樓層序 {floors} 個區劃");

            // Only 挑空 are counted: 第79條之2第3項 is written for nothing else, so an empty box on any
            // other 區劃 is not something to chase (垂直區劃規格 §6、決議 27).
            var atria = _zones.Count(z => z.IsAtrium &&
                                          string.IsNullOrEmpty(z.LinksRefugeFloor));
            if (atria > 0) parts.Add($"待填挑空免除事實 {atria} 個區劃");

            // Same reasoning for 第79條之1: only the six uses it names need 無法區劃分隔, so a blank box
            // anywhere else is not something to chase (第79條之1規格 §6、決議 9).
            var subdivision = _zones.Count(z => z.IsArticle79_1Use && string.IsNullOrEmpty(z.CannotBeSubdivided));
            if (subdivision > 0) parts.Add($"待填無法區劃分隔 {subdivision} 個區劃");

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
            panel.Children.Add(PanelButtons.Build(this));
            Content = panel;
        }

        /// <summary>The chosen code (RC／SRC／SC), or an empty string to clear the parameter.</summary>
        public string Material => _choice.SelectedIndex <= 0
            ? string.Empty
            : StructuralMaterialText.Code(StructuralMaterialText.All[_choice.SelectedIndex - 1]);
    }

    /// <summary>
    /// Asks what to stamp onto the selected 區劃. A field left as 「不變更」 is not written, so the
    /// same dialog can set only the sprinklers, only the storey, or both.
    /// </summary>
    internal sealed class ZoneFillWindow : Window
    {
        private const string Unchanged = "（不變更）";

        private readonly ComboBox _use = new ComboBox
        {
            Margin = new Thickness(0, 4, 0, 12),
            MinWidth = 220,
            IsEditable = true,
            IsTextSearchEnabled = false
        };

        private readonly ComboBox _sprinklered = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 220 };
        private readonly TextBox _floorNumber = new TextBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 220 };
        private readonly ComboBox _linksRefugeFloor = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 220 };
        private readonly ComboBox _cannotBeSubdivided = new ComboBox { Margin = new Thickness(0, 4, 0, 12), MinWidth = 220 };

        public ZoneFillWindow(int rowCount)
        {
            Title = "區劃批次填入";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // 用途的清單只列會改變判定的字樣——它們也正是一次要設給好幾個樓梯間、管道間，或好幾間
            // 教室的那種。其餘用途仍可自行輸入，因為這是可編輯的下拉。兩段分開的理由與格子裡那一欄
            // 相同（第79條之1規格 §7.3）：上面五個免掉面積上限，下面六個上限照樣適用。
            _use.Items.Add(Unchanged);
            _use.Items.Add(Heading(FireReviewZoneRowViewModel.VerticalCompartmentHeading));
            foreach (var use in ZoneUses.VerticalCompartments) _use.Items.Add(use);
            _use.Items.Add(Heading(FireReviewZoneRowViewModel.Article79_1Heading));
            foreach (var use in ZoneUses.Article79_1Uses) _use.Items.Add(use);
            _use.SelectedIndex = 0;

            _sprinklered.Items.Add(Unchanged);
            _sprinklered.Items.Add(FireReviewEditableRow.YesText);
            _sprinklered.Items.Add(FireReviewEditableRow.NoText);
            _sprinklered.SelectedIndex = 0;

            _linksRefugeFloor.Items.Add(Unchanged);
            _linksRefugeFloor.Items.Add(FireReviewEditableRow.YesText);
            _linksRefugeFloor.Items.Add(FireReviewEditableRow.NoText);
            _linksRefugeFloor.SelectedIndex = 0;

            _cannotBeSubdivided.Items.Add(Unchanged);
            _cannotBeSubdivided.Items.Add(FireReviewEditableRow.YesText);
            _cannotBeSubdivided.Items.Add(FireReviewEditableRow.NoText);
            _cannotBeSubdivided.SelectedIndex = 0;

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock { Text = $"把選取的 {rowCount} 個區劃設為：", FontWeight = FontWeights.SemiBold });
            // 與格子裡那一欄同一句提示（共用 UseHintText，不各寫一份）：這個欄位大部分區劃不必填，
            // 需要填的只有清單上那兩段，而近似字不會比中也不會有警告。
            panel.Children.Add(new TextBlock
            {
                Text = "區劃用途（一般區劃不必填，留空即可；也可自行輸入）",
                Margin = new Thickness(0, 12, 0, 0),
                ToolTip = FireReviewZoneRowViewModel.UseHintText
            });
            _use.ToolTip = FireReviewZoneRowViewModel.UseHintText;
            ToolTipService.SetShowDuration(_use, 60000);
            panel.Children.Add(_use);
            panel.Children.Add(new TextBlock { Text = "自動滅火設備" });
            panel.Children.Add(_sprinklered);
            panel.Children.Add(new TextBlock { Text = "所在樓層序（留白代表不變更）" });
            panel.Children.Add(_floorNumber);
            panel.Children.Add(new TextBlock
            {
                Text = "以下一項只有挑空需要填（第79條之2第3項第一款；連跨樓層數與連通面積由各樓層區劃推得）",
                Margin = new Thickness(0, 6, 0, 0),
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock { Text = "避難層通達其直上層或直下層", Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(_linksRefugeFloor);
            panel.Children.Add(new TextBlock
            {
                Text = "以下一項只有第79條之1 的六個用途需要填",
                Margin = new Thickness(0, 6, 0, 0),
                FontWeight = FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "無法區劃分隔（第79條之1；面積上限仍適用，只會多一筆待人工確認的判定）",
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320
            });
            panel.Children.Add(_cannotBeSubdivided);
            panel.Children.Add(PanelButtons.Build(this));
            Content = panel;
        }

        /// <summary>
        /// A dropdown row that names the article the words below it belong to. A disabled
        /// <see cref="ComboBoxItem"/> is its own container, so it stays unselectable — the words are
        /// grouped without the heading ever being mistaken for a 區劃用途.
        /// </summary>
        private static ComboBoxItem Heading(string text) => new ComboBoxItem
        {
            Content = text,
            IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        };

        /// <summary>
        /// 區劃用途, or null to leave each zone as it is. There is no 「清除」: a blank box is 不變更,
        /// because batch-clearing the use of several 區劃 at once is not something anyone asked for
        /// and it is the one value that decides whether a 區劃 is reviewed at all.
        /// </summary>
        public string Use => string.IsNullOrWhiteSpace(_use.Text) || _use.Text == Unchanged ? null : _use.Text.Trim();

        /// <summary>是／否 as the grid spells it, or null to leave each zone as it is.</summary>
        public string Sprinklered => _sprinklered.SelectedIndex <= 0 ? null : (string)_sprinklered.SelectedItem;

        public string FloorNumber => string.IsNullOrWhiteSpace(_floorNumber.Text) ? null : _floorNumber.Text.Trim();

        public string LinksRefugeFloor => _linksRefugeFloor.SelectedIndex <= 0 ? null : (string)_linksRefugeFloor.SelectedItem;

        /// <summary>防火檢討_無法區劃分隔 as the grid spells it, or null to leave each zone as it is.</summary>
        public string CannotBeSubdivided =>
            _cannotBeSubdivided.SelectedIndex <= 0 ? null : (string)_cannotBeSubdivided.SelectedItem;
    }

    /// <summary>The 確定／取消 pair both little dialogs end with.</summary>
    internal static class PanelButtons
    {
        public static UIElement Build(Window window)
        {
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var ok = new Button { Content = "確定", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsDefault = true };
            ok.Click += (_, __) => { window.DialogResult = true; window.Close(); };

            var cancel = new Button
            {
                Content = "取消",
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 5, 10, 5),
                IsCancel = true
            };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            return buttons;
        }
    }
}
