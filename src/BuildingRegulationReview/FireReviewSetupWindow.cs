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
        private readonly TextBlock _savedHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.DarkSlateBlue,
            Visibility = Visibility.Collapsed
        };
        private readonly TextBlock _historyWarning = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Visibility = Visibility.Collapsed
        };
        private readonly ReviewPackageSetupHistory _history;

        /// <summary>
        /// 目前三個控制項上放的是哪一組套件鍵帶回來的值。視窗每次重新取得焦點都會刷新面積配置，
        /// 連帶重新觸發選取變更，所以沒有這個記錄就會一再把使用者手改的選擇蓋回存檔值。
        /// </summary>
        private string _appliedKey;

        /// <summary>使用者自己動過那三個控制項之後，就不再自動帶回任何存檔值（只在提示裡說明）。</summary>
        private bool _userEdited;

        /// <summary>自己正在寫入那三個控制項：此時的變更事件不算使用者編輯。</summary>
        private bool _applying;

        public IReadOnlyList<ReviewPackageSetupSelection> Selections { get; private set; }
        public System.Action OpenAreaComputations { get; set; }
        public System.Action RefreshAreaSchemes { get; set; }
        public System.Action CreateAreaPlans { get; set; }
        public IReadOnlyList<ReviewPackageSetupSelection> PendingSelections { get; private set; }

        /// <param name="history">
        /// What each existing package last saved, so the two optional dropdowns and the crop
        /// checkbox open on it instead of on this window's own defaults (驗證清單 C-13). Leave it
        /// out and the window behaves as if the document had nothing saved.
        /// </param>
        /// <param name="historyWarning">
        /// Why <paramref name="history"/> is incomplete, shown in red above the hint. Reading the
        /// saved options must never stop the window opening, but it must not fail silently either.
        /// </param>
        public FireReviewSetupWindow(
            RevitReviewSetupCatalog catalog,
            ReviewPackageSetupHistory history = null,
            string historyWarning = null)
        {
            _history = history ?? ReviewPackageSetupHistory.Empty;
            if (!string.IsNullOrWhiteSpace(historyWarning))
            {
                _historyWarning.Text = historyWarning;
                _historyWarning.Visibility = Visibility.Visible;
            }
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
            computations.Click += (_, __) => OpenAreaComputations?.Invoke();
            DockPanel.SetDock(computations, Dock.Right);
            schemeRow.Children.Add(computations);
            var refresh = new Button { Content = "更新清單", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
            refresh.Click += (_, __) => RefreshAreaSchemes?.Invoke();
            DockPanel.SetDock(refresh, Dock.Right);
            schemeRow.Children.Add(refresh);
            _scheme.MinWidth = 0;
            schemeRow.Children.Add(_scheme);
            panel.Children.Add(schemeRow);
            AddField(panel, "Area Plan 視圖樣板（選填）", _template); AddField(panel, "Scope Box（選填）", _scopeBox);
            _copyCrop.Margin = new Thickness(0, 8, 0, 8); panel.Children.Add(_copyCrop);
            panel.Children.Add(_historyWarning);
            panel.Children.Add(_savedHint);
            panel.Children.Add(new TextBlock { Text = "需要新面積配置時，點選 Area Computation；關閉 Revit 設定視窗後可直接回到此處。", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray });
            var ok = new Button { Content = "建立 Area Plan", Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, __) => Accept(); panel.Children.Add(ok);
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // 掛在初始選取之後，免得建構期間就跑一次。這兩個事件讀的是「樓層平面＋面積配置」這個
            // 套件鍵，改的是另外三個控制項；那三個控制項的變更事件只會設旗標與改提示文字，不會回頭
            // 改選取，所以不會互相觸發成循環。
            _floors.SelectionChanged += (_, __) => ShowSavedOptions();
            _scheme.SelectionChanged += (_, __) => ShowSavedOptions();
            _template.SelectionChanged += (_, __) => NoteUserEdit();
            _scopeBox.SelectionChanged += (_, __) => NoteUserEdit();
            _copyCrop.Checked += (_, __) => NoteUserEdit();
            _copyCrop.Unchecked += (_, __) => NoteUserEdit();
            ShowSavedOptions();
        }

        /// <summary>
        /// 使用者自己動了視圖樣板／Scope Box／裁切勾選。從這一刻起不再自動帶回存檔值——否則只要
        /// 視窗重新取得焦點（按「Area Computation…」「更新清單」，或點一下 Revit 主視窗再點回來）
        /// 就會把他剛改的選擇蓋掉，那正是 C-13 要修的那種「靜默換掉使用者的選擇」，只是方向相反。
        /// 存檔值不會因此消失，提示文字仍然會把它寫出來。
        /// </summary>
        private void NoteUserEdit()
        {
            if (_applying) return;
            _userEdited = true;
            ShowSavedOptions();
        }

        /// <summary>
        /// 驗證清單 C-13：把這個套件上次存的視圖樣板、Scope Box 與裁切設定帶回畫面上。附錄 9.5 的
        /// 「刪除後修復」走的就是這個視窗，視窗不讀回來的話，Apply 會無條件把視窗當下的選擇存回去，
        /// 把使用者原本的樣板與 Scope Box 靜默換成「（不指定）」。
        /// </summary>
        /// <remarks>
        /// 三種情形分開處理，而且**只有一種**會動那三個控制項：沒存過就什麼都不說；存過的幾組互相
        /// 衝突就只把衝突寫出來（套用其中一組會把其他樓層原本的設定換成別人的值）；只有存過的那一
        /// 組（或幾組內容一致）、使用者還沒手動改過、而且這一組鍵還沒套用過時，才真的帶回。
        /// </remarks>
        private void ShowSavedOptions()
        {
            var scheme = _scheme.SelectedItem as RevitSetupOption;
            var preselection = ReviewPackageSetupPreselection.For(
                _floors.SelectedItems.Cast<RevitSetupOption>()
                    .Select(floor => new ReviewPackageSetupFloorPlan(floor.UniqueId, floor.Name)),
                scheme?.UniqueId,
                _history);

            if (preselection.Kind == ReviewPackageSetupPreselectionKind.Nothing)
            {
                // 沒存過的組合不動那三個控制項：使用者剛才看到、也許剛調過的值留在畫面上，比靜默
                // 跳回預設值誠實；提示收起來就代表「這一組沒有存過的設定」。
                _savedHint.Visibility = Visibility.Collapsed;
                return;
            }

            if (preselection.Kind == ReviewPackageSetupPreselectionKind.Conflict)
            {
                _savedHint.Text = DescribeConflict(preselection);
                _savedHint.Visibility = Visibility.Visible;
                return;
            }

            var saved = preselection.Agreed;
            // 鍵相同代表這一組的值已經在那三個控制項上了（使用者沒動過的話），不必再寫一次；
            // 這就是焦點來回一趟不會把手改的選擇蓋掉的地方。
            if (!_userEdited && preselection.Key != _appliedKey)
            {
                ApplySaved(saved);
                _appliedKey = preselection.Key;
            }
            _savedHint.Text = _userEdited
                ? Names(preselection) + "上次儲存的設定是：" + Describe(saved)
                    + "。畫面上是你自己調過的選擇，所以沒有自動帶回；要沿用原設定請照這一行選回來。"
                : "已帶回" + Names(preselection) + "上次儲存的設定：" + Describe(saved)
                    + "。修復被刪掉的 Area Plan 時保持原樣，就會沿用原本的設定。" + MissingNote(saved);
            _savedHint.Visibility = Visibility.Visible;
        }

        /// <summary>把存檔值寫上那三個控制項，期間的變更事件不算使用者編輯。</summary>
        private void ApplySaved(ReviewPackageSetupSelection saved)
        {
            _applying = true;
            try
            {
                _template.SelectedItem = Resolve(_template, saved.AreaPlanTemplateUniqueId);
                _scopeBox.SelectedItem = Resolve(_scopeBox, saved.ScopeBoxUniqueId);
                _copyCrop.IsChecked = saved.CopyCropSettings;
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>
        /// 選取的樓層平面存過兩組以上、而且內容不同時的說法。這裡刻意逐一點名每個樓層與它各自存的
        /// 值：按下「建立 Area Plan」時，畫面上這一組會套到**全部**選取的樓層，所以挑其中一組預選
        /// 等於把別人原本的設定換掉——那是 C-13 的病換個方向再犯一次。
        /// </summary>
        private string DescribeConflict(ReviewPackageSetupPreselection preselection)
        {
            var rows = preselection.Saved
                .Select(entry => "「" + entry.FloorPlanName + "」：" + Describe(entry.Saved));
            return "所選樓層平面存過不同的設定（" + string.Join("；", rows)
                + "），所以這裡不自動帶回。請一次修復一個樓層平面才能沿用各自的原設定；"
                + "若就這樣按「建立 Area Plan」，畫面上目前這一組會套用到全部選取的樓層。";
        }

        private string Describe(ReviewPackageSetupSelection saved) =>
            "視圖樣板＝" + OptionName(_template, saved.AreaPlanTemplateUniqueId)
            + "、Scope Box＝" + OptionName(_scopeBox, saved.ScopeBoxUniqueId)
            + (saved.CopyCropSettings ? "、複製裁切設定" : "、不複製裁切設定");

        private static string Names(ReviewPackageSetupPreselection preselection) =>
            string.Join("、", preselection.Saved.Select(entry => "「" + entry.FloorPlanName + "」"));

        /// <summary>存過的樣板或 Scope Box 後來被刪掉時，明說退回了什麼，而不是靜默換掉。</summary>
        private string MissingNote(ReviewPackageSetupSelection saved)
        {
            var missing = new List<string>();
            if (Find(_template, saved.AreaPlanTemplateUniqueId) == null) missing.Add("視圖樣板");
            if (Find(_scopeBox, saved.ScopeBoxUniqueId) == null) missing.Add("Scope Box");
            return missing.Count == 0
                ? ""
                : "（原本存的" + string.Join("與", missing) + "已不在專案中，已退回「（不指定）」。）";
        }

        /// <summary>清單裡 UniqueId 對得上的那一項，對不上就回 null（例如存過的樣板後來被刪掉）。</summary>
        private static RevitSetupOption Find(ComboBox combo, string uniqueId) =>
            ((IEnumerable<RevitSetupOption>)combo.ItemsSource).FirstOrDefault(x => x.UniqueId == (uniqueId ?? ""));

        /// <summary>對得上的那一項，對不上時退回第 0 項「（不指定）」。</summary>
        private static RevitSetupOption Resolve(ComboBox combo, string uniqueId) =>
            Find(combo, uniqueId) ?? ((IEnumerable<RevitSetupOption>)combo.ItemsSource).First();

        /// <summary>存檔值在清單裡的顯示名稱；已被刪掉的就說它不在了，不要假裝是「（不指定）」。</summary>
        private static string OptionName(ComboBox combo, string uniqueId) =>
            Find(combo, uniqueId)?.Name ?? "（已不在專案中）";

        private void Accept()
        {
            var floors = _floors.SelectedItems.Cast<RevitSetupOption>().ToList();
            if (floors.Count == 0) { MessageBox.Show(this, "請至少選取一個樓層平面。", Title); return; }
            var scheme = (RevitSetupOption)_scheme.SelectedItem;
            if (scheme == null) { MessageBox.Show(this, "請先用 Area Computation 建立面積配置。", Title); return; }
            var template = (RevitSetupOption)_template.SelectedItem; var scope = (RevitSetupOption)_scopeBox.SelectedItem;
            Selections = floors.Select(floor => new ReviewPackageSetupSelection(floor.UniqueId, floor.RelatedUniqueId, scheme.UniqueId,
                EmptyToNull(template.UniqueId), _copyCrop.IsChecked == true, EmptyToNull(scope.UniqueId))).ToList();
            PendingSelections = Selections;
            CreateAreaPlans?.Invoke();
        }

        public void RefreshSchemes(RevitReviewSetupCatalog catalog)
        {
            var selectedId = (_scheme.SelectedItem as RevitSetupOption)?.UniqueId;
            _scheme.ItemsSource = catalog.AreaSchemes;
            _scheme.SelectedItem = catalog.AreaSchemes.FirstOrDefault(x => x.UniqueId == selectedId)
                ?? catalog.AreaSchemes.LastOrDefault();
        }

        private static ComboBox NewCombo() => new ComboBox { DisplayMemberPath = "Name", MinWidth = 360 };
        private static void AddField(Panel panel, string label, Control control) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) }); panel.Children.Add(control); }
        private static List<RevitSetupOption> WithNone(IEnumerable<RevitSetupOption> source) { var result = new List<RevitSetupOption> { new RevitSetupOption("", "（不指定）") }; result.AddRange(source); return result; }
        private static string EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
