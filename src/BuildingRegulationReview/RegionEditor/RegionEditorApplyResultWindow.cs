using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingRegulationReview.Application.WriteBack;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>
    /// What one write-back run did (spec 10.5). Problems come first because they are the only lines
    /// anybody has to act on, and the whole log can be saved to a file — spec 10.5 requires that a
    /// skipped element leaves a record, and a dialog the user closes is not one.
    /// </summary>
    internal sealed class RegionEditorApplyResultWindow : Window
    {
        private static readonly Brush ProblemBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));

        private readonly ApplyResult _result;

        private RegionEditorApplyResultWindow(ApplyResult result)
        {
            _result = result ?? throw new ArgumentNullException(nameof(result));

            Title = result.IsRolledBack ? "寫回已復原" : "寫回完成";
            Width = 720;
            Height = 560;
            MinWidth = 520;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(16) };
            root.Children.Add(BuildButtons());
            root.Children.Add(BuildHeader());
            root.Children.Add(BuildList());
            Content = root;
        }

        public static void Show(Window owner, ApplyResult result)
        {
            var window = new RegionEditorApplyResultWindow(result) { Owner = owner };
            window.ShowDialog();
        }

        private UIElement BuildHeader()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = _result.Summary,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.SemiBold,
                Foreground = _result.IsRolledBack ? ProblemBrush : Brushes.Black
            });

            foreach (var note in _result.Notes)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "○ " + note,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.DimGray,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }

            DockPanel.SetDock(panel, Dock.Top);
            return panel;
        }

        private UIElement BuildList()
        {
            var list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };

            foreach (var item in _result.Problems.Concat(_result.Items.Where(i => !i.IsProblem)))
            {
                list.Items.Add(new TextBlock
                {
                    Text = item.Text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = item.IsProblem ? ProblemBrush : Brushes.Black,
                    ToolTip = item.Key.ToToken()
                });
            }

            if (list.Items.Count == 0)
            {
                list.Items.Add(new TextBlock
                {
                    Text = _result.IsRolledBack ? "模型沒有任何變更。" : "沒有任何元素被更動。",
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

        private UIElement BuildButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var save = new Button
            {
                Content = "儲存日誌",
                Padding = new Thickness(14, 4, 14, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            save.Click += (_, __) => SaveLog();
            panel.Children.Add(save);

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

        private void SaveLog()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "防火區劃寫回日誌_{0:yyyyMMdd_HHmmss}.txt",
                    DateTime.Now));

            try
            {
                File.WriteAllLines(path, _result.Log, Encoding.UTF8);
                MessageBox.Show(this, "日誌已儲存到：" + Environment.NewLine + path, Title);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                MessageBox.Show(this, "無法儲存日誌：" + exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
