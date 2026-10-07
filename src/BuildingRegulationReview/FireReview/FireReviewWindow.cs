using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UIApplication = Autodesk.Revit.UI.UIApplication;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Diagnostics;
using BuildingRegulationReview.Application.ReviewPackages;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.FireReview
{
    /// <summary>
    /// 開始檢討 (spec 11): the pre-review check, one run of the three checks with progress and a
    /// cancel button, and the review table (spec 11.7) — expand, locate, clause, manual override.
    /// </summary>
    /// <remarks>
    /// Modeless. Anything that touches the model goes through <see cref="Post"/>, which runs on
    /// Revit's API context; the checks themselves run on a worker thread over data already read
    /// (spec 15), which is what makes the cancel button live while they run.
    /// </remarks>
    internal sealed class FireReviewWindow : Window
    {
        private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        private static readonly Brush PendingBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x6E, 0x00));
        private static readonly Brush PassBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x7B, 0x34));
        private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
        private static readonly Brush HeadingBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79));
        private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));

        /// <summary>The 標籤 column of the detail pane, wide enough for 「Revit 元素編號」 at this size.</summary>
        private const double LabelColumnWidth = 118;

        private const double DetailFontSize = 13;

        private readonly Guid _packageId;
        private readonly List<ReviewLogEntry> _log = new List<ReviewLogEntry>();

        private readonly TextBlock _header = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _verdict = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _stale = new TextBlock { Foreground = PendingBrush, TextWrapping = TextWrapping.Wrap };
        private readonly ListBox _readiness = new ListBox { MaxHeight = 150, Margin = new Thickness(0, 4, 0, 4) };
        private readonly CheckBox _acceptUpdate = new CheckBox { Content = "改用目前規則版本（舊結果與人工覆寫會標示為需更新）", Visibility = Visibility.Collapsed };
        private readonly Button _rescan = new Button { Content = "重新檢查", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _start = new Button { Content = "開始檢討", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        private readonly Button _cancel = new Button { Content = "取消", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        private readonly ProgressBar _progress = new ProgressBar { Width = 180, Height = 14, Maximum = 5, Margin = new Thickness(6, 0, 6, 0) };
        private readonly TextBlock _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        private readonly TreeView _tree = new TreeView { FontSize = DetailFontSize };
        private readonly Dictionary<ReviewStatusBand, CheckBox> _bands = new Dictionary<ReviewStatusBand, CheckBox>();
        private readonly ComboBox _checkTypes = new ComboBox { MinWidth = 140, Margin = new Thickness(0, 0, 10, 2), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly CheckBox _staleOnly = new CheckBox { Content = "只看需更新", Margin = new Thickness(0, 0, 12, 2), VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBox _search = new TextBox { Width = 190, Margin = new Thickness(0, 0, 6, 2), VerticalContentAlignment = VerticalAlignment.Center };
        private readonly Button _clearFilter = new Button { Content = "清除篩選", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 10, 2) };
        private readonly TextBlock _filterSummary = new TextBlock { Foreground = MutedBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
        private readonly StackPanel _detailBody = new StackPanel { Margin = new Thickness(12, 4, 12, 12) };
        private readonly ScrollViewer _detail = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = PanelBrush
        };
        private readonly Button _locate = new Button { Content = "定位", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _override = new Button { Content = "人工覆寫…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _reconfirm = new Button { Content = "重新確認…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _withdraw = new Button { Content = "撤回覆寫…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _remark = new Button { Content = "重新標示檢討視圖", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _copy = new Button { Content = "複製明細", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _saveLog = new Button { Content = "儲存日誌", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };

        /// <summary>What 複製明細 puts on the clipboard: the detail pane exactly as it reads.</summary>
        private string _detailText = string.Empty;

        private FireReviewScan _scan;
        private ReviewRun _run;
        private ReviewRunFreshness _freshness;
        private ReviewTable _table;

        /// <summary>
        /// The 圖號 of every 未符合 帷幕牆交接 in the run that is shown. The same numbers the marks and the
        /// generated 帷幕牆立面 carry, worked out from the table the window is showing, so a row of the
        /// table and a drawing in the project browser can be matched by eye (帷幕牆規格 §7.1).
        /// </summary>
        private IReadOnlyDictionary<Guid, string> _marks = new Dictionary<Guid, string>();

        private CancellationTokenSource _cancellation;
        private bool _busy;

        /// <summary>Set while 清除篩選 resets the controls, so the tree is rebuilt once rather than per control.</summary>
        private bool _filterChanging;

        public FireReviewWindow(Guid packageId)
        {
            _packageId = packageId;
            Title = "防火區劃檢討";
            Width = 1080;
            Height = 760;
            MinWidth = 760;
            MinHeight = 520;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(12) };

            var top = new StackPanel();
            DockPanel.SetDock(top, Dock.Top);
            top.Children.Add(_header);
            top.Children.Add(_verdict);
            top.Children.Add(_stale);
            top.Children.Add(new TextBlock { Text = "開始檢討前的檢查", Margin = new Thickness(0, 6, 0, 0), Foreground = MutedBrush });
            top.Children.Add(_readiness);
            top.Children.Add(_acceptUpdate);
            var run = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            run.Children.Add(_rescan);
            run.Children.Add(_start);
            run.Children.Add(_cancel);
            run.Children.Add(_progress);
            run.Children.Add(_status);
            top.Children.Add(run);
            root.Children.Add(top);

            var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(bottom, Dock.Bottom);
            foreach (var button in new[] { _locate, _override, _reconfirm, _withdraw, _remark, _copy, _saveLog }) bottom.Children.Add(button);
            var close = new Button { Content = "關閉", Padding = new Thickness(10, 2, 10, 2) };
            close.Click += (_, __) => Close();
            bottom.Children.Add(close);
            root.Children.Add(bottom);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            // Wrapping rather than a horizontal scrollbar: a 檢討表 line names an element, a Type and a
            // 說明, which is longer than any pane is wide, and a line that runs off the right edge is a
            // line nobody reads.
            var stretch = new Style(typeof(TreeViewItem));
            stretch.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            _tree.ItemContainerStyle = stretch;
            ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
            var left = new DockPanel();
            var filter = BuildFilterBar();
            DockPanel.SetDock(filter, Dock.Top);
            left.Children.Add(filter);
            left.Children.Add(_tree);
            body.Children.Add(left);
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);
            _detail.Content = _detailBody;
            _detail.MinWidth = 320;
            Grid.SetColumn(_detail, 2);
            body.Children.Add(_detail);
            root.Children.Add(body);
            Content = root;

            _rescan.Click += (_, __) => RequestScan();
            _acceptUpdate.Click += (_, __) => RequestScan();
            _start.Click += (_, __) => Start();
            _cancel.Click += (_, __) => _cancellation?.Cancel();
            _tree.SelectedItemChanged += (_, __) => ShowSelection();
            _clearFilter.Click += (_, __) => ClearFilter();
            _locate.Click += (_, __) => LocateSelected();
            _override.Click += (_, __) => ChangeOverride(OverrideAction.Override);
            _reconfirm.Click += (_, __) => ChangeOverride(OverrideAction.Reconfirm);
            _withdraw.Click += (_, __) => ChangeOverride(OverrideAction.Withdraw);
            _remark.Click += (_, __) => Remark();
            _copy.Click += (_, __) => CopyDetail();
            _saveLog.Click += (_, __) => SaveLog();
            Closing += (_, e) =>
            {
                if (_busy && _cancellation != null) _cancellation.Cancel();
            };

            UpdateButtons();
        }

        /// <summary>Runs an action in Revit's API context.</summary>
        public Action<Action<UIApplication>> Post { get; set; }

        public void RequestScan()
        {
            if (_busy) return;
            SetBusy(true, "正在讀取模型與前置檢查…");
            var accept = _acceptUpdate.IsChecked == true;
            Post(application =>
            {
                var scan = FireReviewModel.TryScan(application.ActiveUIDocument.Document, _packageId, accept);
                Dispatcher.Invoke(() => ShowScan(scan));
            });
        }

        private void ShowScan(FireReviewScan scan)
        {
            SetBusy(false, null);
            _scan = scan;
            if (scan.Failure != null)
            {
                _status.Text = scan.Failure;
                _status.Foreground = FailBrush;
                UpdateButtons();
                return;
            }

            AppendLog(scan.Log);
            Title = "防火區劃檢討 — " + scan.PackageLabel;
            var rules = scan.RuleSet.IsSuccess ? scan.RuleSet.Value.RuleSet : null;
            _header.Text = $"工作包「{scan.PackageLabel}」　狀態：{ReviewPackageProgress.Describe(scan.Package.Status)}　" +
                           (rules == null ? "規則集：無法載入" : $"規則集：{rules.RuleSetId} {rules.Version}（{rules.Title}）");

            _readiness.Items.Clear();
            foreach (var item in scan.Readiness.Items)
            {
                var severity = item.Severity == ReadinessSeverity.Blocking ? "✖ 必須修正"
                    : item.Severity == ReadinessSeverity.Warning ? "▲ 建議確認"
                    : "・ 說明";
                _readiness.Items.Add(new TextBlock
                {
                    Text = $"{severity}（{item.ConditionText}）：{item.Message}" +
                           (item.Fix == null ? string.Empty : "　修正方式：" + item.Fix),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 1000,
                    Foreground = item.Severity == ReadinessSeverity.Blocking ? FailBrush : item.Severity == ReadinessSeverity.Warning ? PendingBrush : Brushes.Black
                });
            }

            _acceptUpdate.Visibility = scan.Readiness.NeedsRuleSetConfirmation || _acceptUpdate.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            _status.Text = scan.Readiness.Message + string.Format(CultureInfo.InvariantCulture, "（前置掃描 {0:0.0} 秒）", scan.Prescan.TotalSeconds);
            _status.Foreground = scan.Readiness.CanRun ? Brushes.Black : FailBrush;

            ShowTable(scan.PreviousRun, scan.Stored?.Freshness, scan.StoredTable);
            UpdateButtons();
        }

        /// <summary>
        /// Runs the review and stores it. It all happens in Revit's API context, like the pre-scan: the
        /// 帷幕牆區劃交接 step reads the curtain walls while the run is under way (帷幕牆規格 §8), and the
        /// Revit API may only be read there. Cancelling still works — the token is set from the UI
        /// thread and the run tests it at every safe point.
        /// </summary>
        private void Start()
        {
            var scan = _scan;
            if (_busy || !FireReviewModel.CanRun(scan)) return;

            _cancellation = new CancellationTokenSource();
            SetBusy(true, "檢討中…", cancellable: true);
            var token = _cancellation.Token;

            // Created on the UI thread, so its callbacks come back to the UI thread by themselves.
            var progress = new Progress<FireReviewProgress>(p =>
            {
                _progress.Maximum = p.Total;
                _progress.Value = p.Completed;
                _status.Text = "檢討中：" + p.Message;
            });

            Post(application =>
            {
                var result = FireReviewModel.RunAndSave(application.ActiveUIDocument.Document, scan, token, progress,
                    beforeSave: completed => Dispatcher.Invoke(() =>
                    {
                        AppendLog(completed.Log);
                        _status.Text = "寫入檢討結果並標示檢討視圖…";
                    }));

                var outcome = result.Outcome;
                if (outcome == null)
                {
                    Dispatcher.Invoke(() => Finished(result.Failure, FailBrush));
                    return;
                }

                if (!outcome.IsCompleted)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AppendLog(outcome.Log);
                        Finished(outcome.Message, outcome.Kind == FireReviewOutcomeKind.Cancelled ? PendingBrush : FailBrush);
                    });
                    return;
                }

                Dispatcher.Invoke(() =>
                {
                    ForgetCancellation();
                    ShowSaved(outcome, result.Saved);
                });
            });
        }

        private void Finished(string message, Brush brush)
        {
            ForgetCancellation();
            SetBusy(false, message, brush);
        }

        private void ForgetCancellation()
        {
            _cancellation?.Dispose();
            _cancellation = null;
        }

        private void ShowSaved(FireReviewOutcome outcome, FireReviewSaveResult saved)
        {
            AppendLog(saved.Log);
            if (!saved.Saved)
            {
                SetBusy(false, "檢討結果沒有寫入模型（已整批復原）：" + saved.Error, FailBrush);
                return;
            }

            _scan.Package = saved.Package;
            _scan.PreviousRun = saved.Run;
            _scan.Stored = null;
            ShowTable(saved.Run, null, outcome.Table);
            var reconfirm = outcome.CarryOver?.Count(OverrideCarryOverOutcome.NeedsReconfirmation) ?? 0;
            SetBusy(false, outcome.Message + (saved.Mark == null ? string.Empty : "　" + saved.Mark.Summary) +
                           (reconfirm > 0 ? $"　有 {reconfirm} 筆人工覆寫需重新確認。" : string.Empty) +
                           "　" + outcome.Performance.Summary,
                saved.Mark != null && saved.Mark.IsRolledBack ? PendingBrush : Brushes.Black);
            _header.Text = $"工作包「{_scan.PackageLabel}」　狀態：{ReviewPackageProgress.Describe(saved.Package.Status)}　規則集：{saved.Run.RuleSetId} {saved.Run.RuleSetVersion}";
        }

        private void ShowTable(ReviewRun run, ReviewRunFreshness freshness, ReviewTable table)
        {
            _run = run;
            _freshness = freshness;
            _table = table ?? (run == null ? null : ReviewTable.Build(run, freshness));
            _marks = _table == null ? new Dictionary<Guid, string>() : CurtainWallMarkNumbers.Assign(_table);
            _tree.Items.Clear();
            ShowDetail(null);

            if (_table == null)
            {
                _verdict.Text = "尚未檢討";
                _verdict.Foreground = MutedBrush;
                _stale.Text = string.Empty;
                _filterSummary.Text = string.Empty;
                return;
            }

            _verdict.Text = $"總狀態：{ReviewVerdictText.Label(_table.Verdict)}　{_table.Counts.Text}　" +
                            $"檢討時間 {_table.Run.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            _verdict.Foreground = BrushOf(_table.Verdict);
            _verdict.ToolTip = $"檢討批次 {_table.RunId:D}";
            _stale.Text = _table.IsStale || _table.StaleReasons.Count > 0
                ? "需更新：" + string.Join("；", _table.StaleReasons) + "。請重新執行「開始檢討」。"
                : string.Empty;

            RenderTree();
        }

        /// <summary>
        /// 篩選 (spec 11.7.2): the four states, one 檢討項目, 需更新 only, and free text over what the row
        /// says. A <see cref="WrapPanel"/> rather than a row, so the bar folds instead of clipping when
        /// the window is narrow or the detail pane is dragged wide.
        /// </summary>
        private UIElement BuildFilterBar()
        {
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 6, 6) };
            bar.Children.Add(new TextBlock { Text = "篩選", Foreground = MutedBrush, Margin = new Thickness(0, 0, 8, 2), VerticalAlignment = VerticalAlignment.Center });

            foreach (var band in ReviewStatusBands.All)
            {
                var box = new CheckBox
                {
                    Content = ReviewStatusBands.Label(band),
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 10, 2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                box.Click += (_, __) => RenderTree();
                _bands.Add(band, box);
                bar.Children.Add(box);
            }

            _staleOnly.Click += (_, __) => RenderTree();
            bar.Children.Add(_staleOnly);

            _checkTypes.Items.Add(new ComboBoxItem { Content = "全部項目", Tag = null });
            foreach (var checkType in ReviewTable.CheckTypes)
                _checkTypes.Items.Add(new ComboBoxItem { Content = ReviewTable.Title(checkType), Tag = checkType });
            _checkTypes.SelectedIndex = 0;
            _checkTypes.SelectionChanged += (_, __) => RenderTree();
            bar.Children.Add(_checkTypes);

            bar.Children.Add(new TextBlock { Text = "搜尋", Foreground = MutedBrush, Margin = new Thickness(0, 0, 6, 2), VerticalAlignment = VerticalAlignment.Center });
            _search.ToolTip = "可搜尋元素編號、類型名稱、區劃名稱、檢討圖號、條文、規則編號與原因說明。空白分隔的詞必須全部命中。";
            _search.TextChanged += (_, __) => RenderTree();
            bar.Children.Add(_search);
            bar.Children.Add(_clearFilter);
            bar.Children.Add(_filterSummary);
            return bar;
        }

        private ReviewTableFilter CurrentFilter() =>
            new ReviewTableFilter(
                ReviewStatusBands.All.Where(band => _bands[band].IsChecked == true),
                (_checkTypes.SelectedItem as ComboBoxItem)?.Tag as string,
                _search.Text,
                _staleOnly.IsChecked == true);

        private void ClearFilter()
        {
            _filterChanging = true;
            try
            {
                foreach (var box in _bands.Values) box.IsChecked = true;
                _staleOnly.IsChecked = false;
                _checkTypes.SelectedIndex = 0;
                _search.Text = string.Empty;
            }
            finally
            {
                _filterChanging = false;
            }
            RenderTree();
        }

        /// <summary>
        /// Draws the tree for the filter as it now stands. The statistics in every heading stay the
        /// section's and the group's own — filtering hides rows, it never re-counts the review — and a
        /// heading that shows less than it counts says so. The selected row is kept if the filter still
        /// shows it, so ticking a box does not throw away the detail being read.
        /// </summary>
        private void RenderTree()
        {
            if (_filterChanging) return;

            var selected = SelectedEntry?.ResultId;
            _tree.Items.Clear();
            if (_table == null)
            {
                _filterSummary.Text = string.Empty;
                ShowDetail(null);
                UpdateButtons();
                return;
            }

            var filter = CurrentFilter();
            var view = filter.Apply(_table, _marks);
            _filterSummary.Text = view.Summary;
            _filterSummary.Foreground = view.IsEmpty ? PendingBrush : MutedBrush;

            foreach (var sectionView in view.Sections)
            {
                // An empty row still reads 未檢討 when nothing is being filtered — that is what a package
                // with no curtain wall is. While a filter is on it is noise, so it goes.
                if (filter.IsActive && sectionView.IsEmpty) continue;

                var section = sectionView.Section;
                var sectionItem = new TreeViewItem
                {
                    Header = Line($"{section.Title}　{ReviewStatusText.Label(section.Status)}　（{section.Counts.Text}）" +
                                  (section.StaleCount > 0 ? $"　需更新 {section.StaleCount}" : string.Empty) + sectionView.ShownText,
                        BrushOf(section.Status), bold: true),
                    Tag = section,
                    IsExpanded = true
                };

                foreach (var groupView in sectionView.Groups)
                {
                    var group = groupView.Group;
                    var groupItem = new TreeViewItem
                    {
                        Header = Line($"{group.Label}　{ReviewStatusText.Label(group.Status)}　（{group.Counts.Text}）" + groupView.ShownText,
                            BrushOf(group.Status)),
                        Tag = group,
                        // Filtering is asking to see the rows, not the headings they hide behind.
                        IsExpanded = filter.IsActive
                    };
                    foreach (var entry in groupView.Entries)
                        groupItem.Items.Add(new TreeViewItem { Header = Line(EntryText(entry), BrushOf(entry.EffectiveStatus)), Tag = entry });
                    sectionItem.Items.Add(groupItem);
                }

                _tree.Items.Add(sectionItem);
            }

            if (view.OtherEntries.Count > 0)
            {
                var others = new TreeViewItem { Header = Line("其他檢討項目", Brushes.Black, bold: true), IsExpanded = true };
                foreach (var entry in view.OtherEntries)
                    others.Items.Add(new TreeViewItem { Header = Line(EntryText(entry), BrushOf(entry.EffectiveStatus)), Tag = entry });
                _tree.Items.Add(others);
            }

            if (_tree.Items.Count == 0)
                _tree.Items.Add(new TreeViewItem { Header = Line("沒有符合篩選條件的項目。請放寬篩選條件，或按「清除篩選」。", PendingBrush) });

            if (selected.HasValue) Reselect(selected.Value);
            UpdateButtons();
        }

        /// <summary>Puts the selection back on a row after a rebuild, if the filter still shows it.</summary>
        private void Reselect(Guid resultId)
        {
            foreach (TreeViewItem sectionItem in _tree.Items)
                foreach (var child in sectionItem.Items)
                {
                    var childItem = (TreeViewItem)child;
                    if (childItem.Tag is ReviewTableEntry other && other.ResultId == resultId)
                    {
                        sectionItem.IsExpanded = true;
                        childItem.IsSelected = true;
                        return;
                    }

                    foreach (TreeViewItem entryItem in childItem.Items)
                    {
                        if (!(entryItem.Tag is ReviewTableEntry entry) || entry.ResultId != resultId) continue;
                        sectionItem.IsExpanded = true;
                        childItem.IsExpanded = true;
                        entryItem.IsSelected = true;
                        return;
                    }
                }
        }

        private string EntryText(ReviewTableEntry entry) =>
            ReviewEntryReport.Headline(entry, CurtainWallMarkNumbers.Of(_marks, entry.ResultId));

        private void ShowSelection()
        {
            UpdateButtons();
            var selected = (_tree.SelectedItem as TreeViewItem)?.Tag;
            if (selected is ReviewTableEntry entry && _table != null)
                ShowDetail(ReviewEntryReport.Describe(_table, entry, CurtainWallMarkNumbers.Of(_marks, entry.ResultId)));
            else if (selected is ReviewTableSection section)
                ShowDetail(ReviewEntryReport.Describe(section));
            else if (selected is ReviewTableGroup group)
                ShowDetail(ReviewEntryReport.Describe(group));
            else
                ShowDetail(null);
        }

        /// <summary>
        /// Renders the detail pane: a heading per section, then one label and one value per line. The
        /// value is a read-only TextBox rather than a TextBlock so a Type name, a parameter's raw text
        /// or an element id can be selected and pasted into Revit without retyping it.
        /// </summary>
        private void ShowDetail(IReadOnlyList<ReviewDetailSection> sections)
        {
            _detailBody.Children.Clear();
            _detailText = sections == null ? string.Empty : ReviewEntryReport.ToText(sections);
            if (sections == null)
            {
                UpdateButtons();
                return;
            }

            foreach (var section in sections)
            {
                if (section.IsEmpty) continue;
                _detailBody.Children.Add(new TextBlock
                {
                    Text = section.Title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = DetailFontSize + 1,
                    Foreground = HeadingBrush,
                    Margin = new Thickness(0, _detailBody.Children.Count == 0 ? 8 : 14, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                });
                foreach (var line in section.Lines) _detailBody.Children.Add(DetailLine(line));
            }

            UpdateButtons();
        }

        private static UIElement DetailLine(ReviewDetailLine line)
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            row.Children.Add(new TextBlock
            {
                Text = line.Label,
                Foreground = MutedBrush,
                FontSize = DetailFontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 8, 0)
            });

            var value = new TextBox
            {
                Text = line.Value,
                IsReadOnly = true,
                IsTabStop = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                FontSize = DetailFontSize,
                FontWeight = line.Emphasis ? FontWeights.SemiBold : FontWeights.Normal,
                FontFamily = new FontFamily("Microsoft JhengHei UI")
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }

        private void CopyDetail()
        {
            if (_detailText.Length == 0)
            {
                _status.Text = "請先在左側選一個檢討項目，才有明細可以複製。";
                _status.Foreground = PendingBrush;
                return;
            }

            try
            {
                Clipboard.SetText(_detailText);
                _status.Text = "已複製此項目的明細。";
                _status.Foreground = Brushes.Black;
            }
            catch (Exception exception)
            {
                _status.Text = "無法複製到剪貼簿：" + exception.Message;
                _status.Foreground = FailBrush;
            }
        }

        private ReviewTableEntry SelectedEntry => (_tree.SelectedItem as TreeViewItem)?.Tag as ReviewTableEntry;

        private void LocateSelected()
        {
            var entry = SelectedEntry;
            if (entry == null || _busy) return;
            Post(application =>
            {
                string problem;
                try
                {
                    problem = FireReviewModel.Locate(application.ActiveUIDocument, _packageId, entry);
                }
                catch (Exception exception)
                {
                    problem = "無法定位：" + exception.Message;
                }
                Dispatcher.Invoke(() => { _status.Text = problem ?? "已在 Revit 選取並顯示此項目的元素。"; _status.Foreground = problem == null ? Brushes.Black : PendingBrush; });
            });
        }

        private void ChangeOverride(OverrideAction action)
        {
            var entry = SelectedEntry;
            var run = _run;
            var scan = _scan;
            if (entry == null || run == null || scan?.Candidates == null || _busy) return;

            var current = entry.CurrentOverride;
            var answer = FireReviewOverrideDialog.Ask(this, action, entry.ComputedStatus, current?.OverriddenStatus, current?.Reason);
            if (answer == null) return;

            var freshness = _freshness;
            SetBusy(true, "寫入人工覆寫…");
            Post(application =>
            {
                var who = FireReviewModel.OperatorName(application);
                var now = DateTime.UtcNow;
                var changed = action == OverrideAction.Override
                    ? ReviewOverrides.Apply(run, entry.ResultId, answer.Status, answer.Reason, who, now, answer.Comment, freshness)
                    : action == OverrideAction.Reconfirm
                        ? ReviewOverrides.Reconfirm(run, entry.ResultId, who, now, answer.Reason, answer.Comment, freshness)
                        : ReviewOverrides.Withdraw(run, entry.ResultId, answer.Reason, who, now);

                if (changed.IsFailure)
                {
                    Dispatcher.Invoke(() => SetBusy(false, changed.Error.Message, FailBrush));
                    return;
                }

                FireReviewSaveResult saved;
                try
                {
                    saved = FireReviewModel.Save(application.ActiveUIDocument.Document, scan.Package, changed.Value, scan.Candidates, freshness);
                }
                catch (Exception exception)
                {
                    saved = new FireReviewSaveResult { Saved = false, Error = exception.Message };
                }

                Dispatcher.Invoke(() =>
                {
                    AppendLog(saved.Log);
                    if (!saved.Saved)
                    {
                        SetBusy(false, "人工覆寫沒有寫入模型：" + saved.Error, FailBrush);
                        return;
                    }

                    _scan.PreviousRun = saved.Run;
                    ShowTable(saved.Run, RefreshFreshness(saved.Run, freshness), null);
                    SetBusy(false, "人工覆寫已記錄（" + who + "）。" + (saved.Mark == null ? string.Empty : "　" + saved.Mark.Summary), Brushes.Black);
                });
            });
        }

        /// <summary>
        /// The verdict for the run with its new audit trail, against the model as the scan read it:
        /// an override changes the audit trail, never the evidence.
        /// </summary>
        private ReviewRunFreshness RefreshFreshness(ReviewRun run, ReviewRunFreshness previous)
        {
            if (previous == null || _scan?.CurrentBaseline == null || _scan.RuleSet.IsFailure) return null;
            var rules = _scan.RuleSet.Value.RuleSet;
            return ReviewRunValidity.Evaluate(run, _scan.CurrentBaseline, rules.RuleSetId, rules.Version, _scan.Package.BoundaryRevision);
        }

        private void Remark()
        {
            var run = _run;
            var scan = _scan;
            if (run == null || scan?.Candidates == null || _busy) return;

            var freshness = _freshness;
            SetBusy(true, "重新標示檢討視圖…");
            Post(application =>
            {
                FireReviewSaveResult saved;
                try
                {
                    saved = FireReviewModel.Save(application.ActiveUIDocument.Document, scan.Package, run, scan.Candidates, freshness);
                }
                catch (Exception exception)
                {
                    saved = new FireReviewSaveResult { Saved = false, Error = exception.Message };
                }
                Dispatcher.Invoke(() =>
                {
                    AppendLog(saved.Log);
                    SetBusy(false, saved.Saved ? saved.Mark?.Summary ?? "沒有可標示的項目。" : "標示失敗（已整批復原）：" + saved.Error,
                        saved.Saved ? Brushes.Black : FailBrush);
                });
            });
        }

        private void SaveLog()
        {
            if (_log.Count == 0)
            {
                _status.Text = "目前沒有日誌可以儲存。";
                return;
            }

            var log = new ReviewLog.Builder(_packageId);
            foreach (var entry in _log) log.Add(entry);
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"防火區劃檢討日誌_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            try
            {
                File.WriteAllText(path, log.Build().ToText(), new UTF8Encoding(true));
                _status.Text = "日誌已儲存：" + path;
                _status.Foreground = Brushes.Black;
            }
            catch (Exception exception)
            {
                _status.Text = "日誌無法儲存：" + exception.Message;
                _status.Foreground = FailBrush;
            }
        }

        private void AppendLog(ReviewLog log)
        {
            if (log != null) _log.AddRange(log.Entries);
        }

        private void SetBusy(bool busy, string message, Brush brush = null, bool cancellable = false)
        {
            _busy = busy;
            if (message != null)
            {
                _status.Text = message;
                _status.Foreground = brush ?? Brushes.Black;
            }
            if (!busy) _progress.Value = 0;
            _progress.IsIndeterminate = busy && !cancellable;
            _cancel.IsEnabled = busy && cancellable;
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var entry = SelectedEntry;
            var candidates = _scan?.Candidates != null;
            _rescan.IsEnabled = !_busy;
            _acceptUpdate.IsEnabled = !_busy;
            _start.IsEnabled = !_busy && _scan?.Readiness != null && _scan.Readiness.CanRun && candidates && _scan.Inputs != null;
            _locate.IsEnabled = !_busy && entry != null && !entry.IsLinked;
            _override.IsEnabled = !_busy && entry != null && candidates && _run?.State == ReviewRunState.Completed && !entry.IsStale;
            _reconfirm.IsEnabled = !_busy && entry != null && candidates && entry.OverrideNeedsReconfirmation && !entry.IsStale;
            _withdraw.IsEnabled = !_busy && entry != null && candidates && entry.CurrentOverride != null;
            _remark.IsEnabled = !_busy && candidates && _run?.State == ReviewRunState.Completed;
            _copy.IsEnabled = _detailText.Length > 0;
            _saveLog.IsEnabled = !_busy;
        }

        private static TextBlock Line(string text, Brush brush, bool bold = false) =>
            new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 1)
            };

        private static Brush BrushOf(ReviewVerdict verdict) => verdict switch
        {
            ReviewVerdict.Fail => FailBrush,
            ReviewVerdict.Pass => PassBrush,
            ReviewVerdict.NotReviewed => MutedBrush,
            _ => PendingBrush
        };

        private static Brush BrushOf(ReviewStatus status) => status switch
        {
            ReviewStatus.Fail => FailBrush,
            ReviewStatus.Pass => PassBrush,
            ReviewStatus.NotApplicable => MutedBrush,
            ReviewStatus.NotRun => MutedBrush,
            _ => PendingBrush
        };
    }
}
