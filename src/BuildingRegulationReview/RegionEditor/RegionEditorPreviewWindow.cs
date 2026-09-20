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
    /// <summary>What the user decided in the preview: apply, and under which failure policy.</summary>
    internal sealed class ApplyDecision
    {
        public ApplyDecision(ApplyFailurePolicy policy) => Policy = policy;

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

        private RegionEditorPreviewWindow(ApplyPreview preview, ApplyPlan plan, bool canApply)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            Title = "套用前差異預覽";
            Width = 720;
            Height = 640;
            MinWidth = 520;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(16) };
            root.Children.Add(BuildButtons(plan, canApply));
            root.Children.Add(BuildHeader(preview, plan));
            root.Children.Add(BuildList(preview));
            Content = root;
        }

        /// <summary>
        /// Shows the preview. Returns the decision when the user asked for it to be applied, and
        /// null when they only looked — which is also what a caller with no write-back gets.
        /// </summary>
        public static ApplyDecision Show(Window owner, ApplyPreview preview, ApplyPlan plan, bool canApply)
        {
            var window = new RegionEditorPreviewWindow(preview, plan, canApply) { Owner = owner };
            return window.ShowDialog() == true
                ? new ApplyDecision(window._rollBackOnAnyFailure.IsChecked == true
                    ? ApplyFailurePolicy.RollBackEverything
                    : ApplyFailurePolicy.SkipAndLog)
                : null;
        }

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
                "同一次寫入還會更新本套件的面積色彩配置，並把單線圖細部線寫進專屬的繪圖視圖；"
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

        private UIElement BuildButtons(ApplyPlan plan, bool canApply)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };

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
                IsDefault = true,
                IsEnabled = canApply && !plan.IsEmpty,
                ToolTip = plan.IsEmpty ? "模型已經與草稿一致，沒有需要寫入的變更。" : plan.Summary
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

            _rollBackOnAnyFailure.IsEnabled = apply.IsEnabled;
            DockPanel.SetDock(buttons, Dock.Right);
            panel.Children.Add(buttons);
            panel.Children.Add(_rollBackOnAnyFailure);

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
