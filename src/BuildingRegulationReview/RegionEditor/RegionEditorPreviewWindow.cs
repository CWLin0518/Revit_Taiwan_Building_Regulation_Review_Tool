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
    /// <summary>
    /// The difference between the 區劃 drafts and the model, shown before anything is written
    /// (spec 10.4). It lists what would be added, updated and deleted per element kind, and says in
    /// as many words that deletion only ever reaches this package's own elements.
    /// </summary>
    /// <remarks>
    /// Read-only: the window reports, it does not apply. The 套用 command arrives with P2-T07, which
    /// is also what makes the 刪除 rows anything other than empty, because nothing has been written
    /// to the model yet.
    /// </remarks>
    internal sealed class RegionEditorPreviewWindow : Window
    {
        private static readonly Brush AddBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x7E, 0x34));
        private static readonly Brush UpdateBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x6E, 0x00));
        private static readonly Brush DeleteBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        public RegionEditorPreviewWindow(ApplyPreview preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));

            Title = "套用前差異預覽";
            Width = 720;
            Height = 620;
            MinWidth = 520;
            MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(16) };
            root.Children.Add(BuildButtons());
            root.Children.Add(BuildHeader(preview));
            root.Children.Add(BuildList(preview));
            Content = root;
        }

        public static void Show(Window owner, ApplyPreview preview)
        {
            var window = new RegionEditorPreviewWindow(preview) { Owner = owner };
            window.ShowDialog();
        }

        private static UIElement BuildHeader(ApplyPreview preview)
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
                panel.Children.Add(new TextBlock
                {
                    Text = line,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }

            foreach (var warning in preview.Warnings)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "▲ " + warning,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = UpdateBrush,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            panel.Children.Add(new TextBlock
            {
                Text = "刪除只會發生在這個檢討套件自己建立的元素上；人工繪製或其他套件的元素不會被更動。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 8, 0, 0)
            });

            DockPanel.SetDock(panel, Dock.Top);
            return panel;
        }

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

        private UIElement BuildButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var close = new Button
            {
                Content = "關閉",
                Padding = new Thickness(14, 4, 14, 4),
                IsDefault = true,
                IsCancel = true
            };
            close.Click += (_, __) => Close();
            panel.Children.Add(close);

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
