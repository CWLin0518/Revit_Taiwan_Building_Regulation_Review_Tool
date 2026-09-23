using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>What the user decided in the preview: which plan to apply, and under which failure policy.</summary>
    internal sealed class ApplyDecision
    {
        public ApplyDecision(ApplyPlan plan, ApplyFailurePolicy policy)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Policy = policy;
        }

        /// <summary>The ordinary plan, or the rebuild plan when the user ticked 全部重建.</summary>
        public ApplyPlan Plan { get; }

        public ApplyFailurePolicy Policy { get; }
    }

    /// <summary>
    /// The difference between the 區劃 drafts and the model, shown before anything is written
    /// (spec 10.4), and the last stop before it is. It lists what would be added, updated and
    /// deleted per element kind, says in as many words that deletion only ever reaches this
    /// package's own elements, and names whatever the write-back is going to leave for a later
    /// stage instead of quietly dropping it.
    /// </summary>
    /// <remarks>
    /// The window decides nothing. It shows the plan it is given and reports the answer; the write
    /// itself happens on Revit's own thread, because the Editor is modeless.
    /// </remarks>
    internal sealed class RegionEditorPreviewWindow : Window
    {
        private static readonly Brush AddBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x7E, 0x34));
        private static readonly Brush UpdateBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x6E, 0x00));
        private static readonly Brush DeleteBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        private readonly CheckBox _rollBackOnAnyFailure = new CheckBox
        {
            Content = "遇到任何一個元素寫不進去，就整批復原",
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "不勾選時，個別失敗會被略過並寫入日誌，其餘元素照常寫入。"
        };

        private readonly CheckBox _rebuildEverything = new CheckBox
        {
            Content = "全部重建（重畫本套件所有元素，包含判定為不變的）",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            ToolTip = "模型裡的邊界線或面積被拉歪、但草稿沒有變時使用：本套件的邊界線與單線圖細部線全部重畫，"
                + "面積與標註重新定位。人工繪製或其他套件的元素仍然不會被更動。"
        };

        private readonly ApplyPlan _plan;
        private readonly ApplyPlan _rebuildPlan;

        private RegionEditorPreviewWindow(ApplyPreview preview, ApplyPlan plan, ApplyPlan rebuildPlan, bool canApply)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _rebuildPlan = rebuildPlan ?? throw new ArgumentNullException(nameof(rebuildPlan));

            Title = "套用前差異預覽";
            Width = 720;
            Height = 640;
            MinWidth = 520;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(16) };
            root.Children.Add(BuildButtons(canApply));
            root.Children.Add(BuildHeader(preview, plan));
            root.Children.Add(BuildList(preview));
            Content = root;
        }

        /// <summary>
        /// Shows the preview. Returns the decision when the user asked for it to be applied, and
        /// null when they only looked — which is also what a caller with no write-back gets.
        /// <paramref name="rebuildPlan"/> is what runs instead when the user ticks 全部重建.
        /// </summary>
        public static ApplyDecision Show(Window owner, ApplyPreview preview, ApplyPlan plan, ApplyPlan rebuildPlan, bool canApply)
        {
            var window = new RegionEditorPreviewWindow(preview, plan, rebuildPlan, canApply) { Owner = owner };
            return window.ShowDialog() == true
                ? new ApplyDecision(
                    window.ChosenPlan,
                    window._rollBackOnAnyFailure.IsChecked == true
                        ? ApplyFailurePolicy.RollBackEverything
                        : ApplyFailurePolicy.SkipAndLog)
                : null;
        }

        private ApplyPlan ChosenPlan => _rebuildEverything.IsChecked == true ? _rebuildPlan : _plan;

        private static UIElement BuildHeader(ApplyPreview preview, ApplyPlan plan)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = preview.Summary,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold
            });

            foreach (var line in preview.KindSummaries)
            {
                panel.Children.Add(Note(line, Brushes.DimGray, 4));
            }

            foreach (var line in plan.DeferredNotes)
            {
                panel.Children.Add(Note("○ " + line, Brushes.DimGray, 6));
            }

            foreach (var warning in preview.Warnings)
            {
                panel.Children.Add(Note("▲ " + warning, UpdateBrush, 6));
            }

            panel.Children.Add(Note(
                "同一次寫入還會更新本套件的面積色彩配置，並把單線圖細部線與各區劃的面積標註寫進專屬的繪圖視圖；"
                + "Revit 不允許的色彩項目會列為需人工處理，不會被當成失敗。",
                Brushes.DimGray,
                8));

            panel.Children.Add(Note(
                "刪除只會發生在這個檢討套件自己建立的元素上；人工繪製或其他套件的元素不會被更動。"
                + "寫入全程在一個交易群組內，致命錯誤會整批復原。",
                Brushes.DimGray,
                6));

            DockPanel.SetDock(panel, Dock.Top);
            return panel;
        }

        private static TextBlock Note(string text, Brush brush, double topMargin) => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = brush,
            Margin = new Thickness(0, topMargin, 0, 0)
        };

        private static UIElement BuildList(ApplyPreview preview)
        {
            var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
            foreach (var row in Rows(preview)) list.Items.Add(row);

            if (list.Items.Count == 0)
            {
                list.Items.Add(new TextBlock
                {
                    Text = "沒有任何待處理的差異。",
                    Foreground = Brushes.DimGray
                });
            }

            return new Border
            {
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                Child = list
            };
        }

        private static IEnumerable<UIElement> Rows(ApplyPreview preview)
        {
            foreach (var group in new[] { ApplyChangeKind.Add, ApplyChangeKind.Update, ApplyChangeKind.Delete })
            {
                var items = preview.Items.Where(i => i.Change == group).ToList();
                if (items.Count == 0) continue;

                yield return new TextBlock
                {
                    Text = string.Format(
                        CultureInfo.CurrentUICulture,
                        "{0}（{1} 個）",
                        ApplyPreviewItem.ChangeText(group),
                        items.Count),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = BrushFor(group),
                    Margin = new Thickness(0, 6, 0, 2)
                };

                foreach (var item in items)
                {
                    yield return new TextBlock
                    {
                        Text = "　" + item.Description,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        ToolTip = item.Key.ToToken()
                    };
                }
            }
        }

        private UIElement BuildButtons(bool canApply)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var options = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var apply = new Button
            {
                Content = "套用到模型",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            apply.Click += (_, __) => { DialogResult = true; Close(); };
            buttons.Children.Add(apply);

            var close = new Button
            {
                Content = "關閉",
                Padding = new Thickness(14, 4, 14, 4),
                IsCancel = true
            };
            close.Click += (_, __) => Close();
            buttons.Children.Add(close);

            // Re-evaluated when 全部重建 is toggled: a model already in agreement has nothing to
            // apply, but still has everything to rebuild.
            void Refresh()
            {
                var plan = ChosenPlan;

                // A model that already matches the draft has nothing to write — but it still has to
                // be able to run, because the run is what measures the Areas and lets the package
                // reach「可開始檢討」. Disabling it here left a correct model stuck in 區劃草稿 with
                // 防火區劃檢討 refusing it for boundaries that were never actually missing.
                // Areas are what the run can measure, so they are what makes the button worth
                // pressing: an empty plan whose unchanged rows are all boundary lines would run and
                // report nothing, and the tooltip would have promised a status change that cannot
                // happen.
                var verifyOnly = plan.IsEmpty && plan.UnchangedAreas.Any();

                apply.IsEnabled = canApply && (!plan.IsEmpty || verifyOnly);
                apply.Content = verifyOnly ? "確認並更新狀態" : "套用到模型";
                apply.ToolTip = verifyOnly
                    ? "模型已經與草稿一致。執行後會重新量測各區劃面積，確認無誤即讓套件進入「可開始檢討」；模型不會被更動。"
                    : plan.IsEmpty
                        ? "這個草稿沒有任何區劃可以寫入。"
                        : plan.Summary;

                // Nothing is written, so there is nothing to roll back; leaving it ticked but greyed
                // would suggest otherwise.
                if (verifyOnly) _rollBackOnAnyFailure.IsChecked = false;
                _rollBackOnAnyFailure.IsEnabled = apply.IsEnabled && !verifyOnly;
            }

            _rebuildEverything.IsEnabled = canApply && !_rebuildPlan.IsEmpty;
            _rebuildEverything.Checked += (_, __) => Refresh();
            _rebuildEverything.Unchecked += (_, __) => Refresh();
            Refresh();

            DockPanel.SetDock(buttons, Dock.Right);
            panel.Children.Add(buttons);
            options.Children.Add(_rebuildEverything);
            options.Children.Add(_rollBackOnAnyFailure);
            panel.Children.Add(options);

            DockPanel.SetDock(panel, Dock.Bottom);
            return panel;
        }

        private static Brush BrushFor(ApplyChangeKind change)
        {
            switch (change)
            {
                case ApplyChangeKind.Add: return AddBrush;
                case ApplyChangeKind.Update: return UpdateBrush;
                case ApplyChangeKind.Delete: return DeleteBrush;
                default: return Brushes.DimGray;
            }
        }
    }
}
