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
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Revit.Geometry;

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
        private readonly TreeView _tree = new TreeView();
        private readonly TextBox _detail = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Microsoft JhengHei UI") };
        private readonly Button _locate = new Button { Content = "定位", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _override = new Button { Content = "人工覆寫…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _reconfirm = new Button { Content = "重新確認…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _withdraw = new Button { Content = "撤回覆寫…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _remark = new Button { Content = "重新標示檢討視圖", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };
        private readonly Button _saveLog = new Button { Content = "儲存日誌", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 0, 6, 0) };

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
            top.Children.Add(new TextBlock { Text = "前置檢查（spec 11.1）", Margin = new Thickness(0, 6, 0, 0), Foreground = MutedBrush });
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
            foreach (var button in new[] { _locate, _override, _reconfirm, _withdraw, _remark, _saveLog }) bottom.Children.Add(button);
            var close = new Button { Content = "關閉", Padding = new Thickness(10, 2, 10, 2) };
            close.Click += (_, __) => Close();
            bottom.Children.Add(close);
            root.Children.Add(bottom);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            body.Children.Add(_tree);
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);
            Grid.SetColumn(_detail, 2);
            body.Children.Add(_detail);
            root.Children.Add(body);
            Content = root;

            _rescan.Click += (_, __) => RequestScan();
            _acceptUpdate.Click += (_, __) => RequestScan();
            _start.Click += (_, __) => Start();
            _cancel.Click += (_, __) => _cancellation?.Cancel();
            _tree.SelectedItemChanged += (_, __) => ShowSelection();
            _locate.Click += (_, __) => LocateSelected();
            _override.Click += (_, __) => ChangeOverride(OverrideAction.Override);
            _reconfirm.Click += (_, __) => ChangeOverride(OverrideAction.Reconfirm);
            _withdraw.Click += (_, __) => ChangeOverride(OverrideAction.Withdraw);
            _remark.Click += (_, __) => Remark();
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
                FireReviewScan scan;
                try
                {
                    scan = FireReviewModel.Scan(application.ActiveUIDocument.Document, _packageId, accept);
                }
                catch (Exception exception)
                {
                    scan = new FireReviewScan { Failure = "讀取模型時發生錯誤：" + exception.Message };
                }
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
                _readiness.Items.Add(new TextBlock
                {
                    Text = (item.Severity == ReadinessSeverity.Blocking ? "✖ " : item.Severity == ReadinessSeverity.Warning ? "▲ " : "・ ") +
                           $"[{item.ConditionText}] {item.Message}" + (item.Fix == null ? string.Empty : "　→ " + item.Fix),
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
            if (_busy || scan?.Readiness == null || !scan.Readiness.CanRun || scan.Candidates == null || scan.Inputs == null) return;

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

            var candidates = scan.Candidates;
            Post(application =>
            {
                var document = application.ActiveUIDocument.Document;
                FireReviewOutcome outcome;
                try
                {
                    var request = new FireReviewRequest(scan.Package, scan.Readiness.RuleSet,
                        new RuleEvaluationContext(DateTime.Today, FireReviewRuleSetSource.Jurisdiction),
                        candidates, scan.Inputs, scan.Environment, scan.PreviousRun, scan.Prescan,
                        curtainWallReader: new RevitCurtainWallGeometryReader(document));
                    outcome = FireReviewRunner.Run(request, token, progress);
                }
                catch (Exception exception)
                {
                    Dispatcher.Invoke(() => Finished("檢討發生錯誤：" + exception.Message, FailBrush));
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
                    AppendLog(outcome.Log);
                    _status.Text = "寫入檢討結果並標示檢討視圖…";
                });

                FireReviewSaveResult saved;
                try
                {
                    saved = FireReviewModel.Save(document, outcome.Package, outcome.Run, candidates, null);
                }
                catch (Exception exception)
                {
                    saved = new FireReviewSaveResult { Saved = false, Error = exception.Message };
                }

                Dispatcher.Invoke(() =>
                {
                    ForgetCancellation();
                    ShowSaved(outcome, saved);
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
            _detail.Text = string.Empty;

            if (_table == null)
            {
                _verdict.Text = "尚未檢討";
                _verdict.Foreground = MutedBrush;
                _stale.Text = string.Empty;
                return;
            }

            _verdict.Text = $"總狀態：{ReviewVerdictText.Label(_table.Verdict)}　（{_table.Counts.Text}）　" +
                            $"檢討時間 {_table.Run.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}　Run {_table.RunId.ToString("D").Substring(0, 8)}";
            _verdict.Foreground = BrushOf(_table.Verdict);
            _stale.Text = _table.IsStale || _table.StaleReasons.Count > 0
                ? "需更新：" + string.Join("；", _table.StaleReasons) + "。請重新執行「開始檢討」。"
                : string.Empty;

            foreach (var section in _table.Sections)
            {
                var sectionItem = new TreeViewItem
                {
                    Header = Line($"{section.Title}　{ReviewStatusText.Label(section.Status)}　（{section.Counts.Text}）" +
                                  (section.StaleCount > 0 ? $"　需更新 {section.StaleCount}" : string.Empty),
                        BrushOf(section.Status), bold: true),
                    Tag = section,
                    IsExpanded = true
                };

                var grouping = GroupingOf(section.CheckType);
                foreach (var group in section.GroupsBy(grouping))
                {
                    var ids = new HashSet<Guid>(group.ResultIds);
                    var groupItem = new TreeViewItem
                    {
                        Header = Line($"{group.Label}　{ReviewStatusText.Label(group.Status)}　（{group.Counts.Text}）", BrushOf(group.Status)),
                        Tag = group
                    };
                    foreach (var entry in section.Entries.Where(e => ids.Contains(e.ResultId)))
                        groupItem.Items.Add(new TreeViewItem { Header = Line(EntryText(entry), BrushOf(entry.EffectiveStatus)), Tag = entry });
                    sectionItem.Items.Add(groupItem);
                }

                _tree.Items.Add(sectionItem);
            }

            if (_table.OtherEntries.Count > 0)
            {
                var others = new TreeViewItem { Header = Line("其他檢討項目", Brushes.Black, bold: true), IsExpanded = true };
                foreach (var entry in _table.OtherEntries)
                    others.Items.Add(new TreeViewItem { Header = Line(EntryText(entry), BrushOf(entry.EffectiveStatus)), Tag = entry });
                _tree.Items.Add(others);
            }

            UpdateButtons();
        }

        private static ReviewTableGrouping GroupingOf(string checkType) => checkType switch
        {
            ReviewCheckTypes.CompartmentArea => ReviewTableGrouping.Zone,
            ReviewCheckTypes.AreaExemption => ReviewTableGrouping.Zone,
            ReviewCheckTypes.FireResistance => ReviewTableGrouping.Type,
            ReviewCheckTypes.CompartmentContinuity => ReviewTableGrouping.JunctionKind,
            ReviewCheckTypes.VerticalCompartment => ReviewTableGrouping.ShaftRequirement,
            _ => ReviewTableGrouping.OpeningKind
        };

        private string EntryText(ReviewTableEntry entry)
        {
            // 第79條之1 is a 區劃 too, so it reads as the 區劃's name rather than as an empty 類別
            // （第79條之1文件 §5.1）.
            var subject = entry.CheckType == ReviewCheckTypes.CompartmentArea ||
                          entry.CheckType == ReviewCheckTypes.AreaExemption
                ? entry.ZoneName ?? entry.ZoneId
                : $"{entry.CategoryLabel}{(entry.TypeName == null ? string.Empty : "「" + entry.TypeName + "」")} {Shorten(entry.LocateUniqueIds.FirstOrDefault())}" +
                  (entry.ZoneName == null ? string.Empty : "＠" + entry.ZoneName);
            var number = CurtainWallMarkNumbers.Of(_marks, entry.ResultId);
            return $"{entry.StatusText}　{(number == null ? string.Empty : number + "　")}{subject}　{entry.Message}";
        }

        private void ShowSelection()
        {
            UpdateButtons();
            var selected = (_tree.SelectedItem as TreeViewItem)?.Tag;
            if (selected is ReviewTableEntry entry)
            {
                _detail.Text = Describe(entry);
            }
            else if (selected is ReviewTableSection section)
            {
                _detail.Text = $"{section.Title}\n狀態：{ReviewStatusText.Label(section.Status)}（總狀態 {ReviewVerdictText.Label(section.Verdict)}）\n統計：{section.Counts.Text}\n" +
                               string.Join("\n", section.Groups.Select(g => "・" + g));
            }
            else if (selected is ReviewTableGroup group)
            {
                _detail.Text = group.ToString();
            }
        }

        private string Describe(ReviewTableEntry entry)
        {
            var text = new StringBuilder();
            text.AppendLine($"項目：{ReviewTable.Title(entry.CheckType)}");
            if (CurtainWallMarkNumbers.Of(_marks, entry.ResultId) is string number)
                text.AppendLine($"檢討圖號：{number}" +
                                (entry.JunctionKind == CurtainWallJunctionKind.FloorToCurtainWall
                                    ? $"　（層間帶立面視圖名稱含「{number}」）"
                                    : "　（標註於檢討平面）"));
            if (entry.ZoneName != null || entry.ZoneId != null) text.AppendLine($"區劃：{entry.ZoneName ?? entry.ZoneId}");
            text.AppendLine($"類別：{entry.CategoryLabel}" + (entry.TypeName == null ? string.Empty : $"　Type：{entry.TypeName}"));
            text.AppendLine($"狀態：{entry.StatusText}");
            text.AppendLine($"實際值：{entry.ActualValue?.ToString() ?? "—"}");
            text.AppendLine($"規定值：{entry.RequiredValue?.ToString() ?? "—"}");
            text.AppendLine($"條文：{(string.IsNullOrWhiteSpace(entry.LegalReference) ? "—" : entry.LegalReference)}");
            text.AppendLine($"規則：{entry.RuleId}　版本 {entry.RuleVersion}　（規則集 {_table.RuleSetId} {_table.RuleSetVersion}）");
            text.AppendLine($"說明：{entry.Message}");
            text.AppendLine($"元素：{string.Join("、", entry.LocateUniqueIds)}" + (entry.IsLinked ? "（連結模型）" : string.Empty));

            if (entry.CurrentOverride is ReviewOverride current)
            {
                text.AppendLine();
                text.AppendLine(current.IsActive ? "人工覆寫（生效中）" : "人工覆寫（需重新確認，未生效）");
                text.AppendLine($"　{ReviewStatusText.Label(current.OriginalStatus)} → {ReviewStatusText.Label(current.OverriddenStatus)}");
                text.AppendLine($"　操作者：{current.OverriddenBy}　時間：{current.OverriddenAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
                text.AppendLine($"　原因：{current.Reason}");
                if (current.Comment != null) text.AppendLine($"　註解：{current.Comment}");
                if (current.StandingReason != null) text.AppendLine($"　{current.StandingReason}");
            }

            var history = _run?.Overrides.Where(o => o.ResultId == entry.ResultId && !o.IsCurrent).ToList();
            if (history != null && history.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("覆寫紀錄：");
                foreach (var old in history)
                    text.AppendLine($"　{old.OverriddenAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} {old.OverriddenBy}：{ReviewStatusText.Label(old.OverriddenStatus)}（{old.Reason}）— {old.StandingReason}");
            }

            if (entry.Result.Evidence.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("證據：");
                foreach (var item in entry.Result.Evidence.Items) text.AppendLine($"　{item.Field} = {item.Value}");
            }

            return text.ToString();
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
            _saveLog.IsEnabled = !_busy;
        }

        private static TextBlock Line(string text, Brush brush, bool bold = false) =>
            new TextBlock { Text = text, Foreground = brush, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };

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

        private static string Shorten(string uniqueId) =>
            uniqueId == null ? string.Empty : uniqueId.Length <= 12 ? uniqueId : "…" + uniqueId.Substring(uniqueId.Length - 8);
    }
}
