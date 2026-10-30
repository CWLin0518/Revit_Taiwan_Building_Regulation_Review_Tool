using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>
    /// Sets one 區劃 draft's 防火檢討_區劃用途 (第79條之2第1項的垂直區劃、第79條之1 的六個用字，或自填).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three answers are offered as three separate things, not as one text box, because a blank
    /// box cannot mean both 不變更 and 一般區劃 and the difference decides whether the user's Areas
    /// keep what they carry. 不變更 leaves every Area exactly as it is — the only safe answer for a
    /// 區劃 whose parts were given different uses by hand — while 一般區劃 is the explicit clear, and
    /// the window says in as many words which Areas that would empty.
    /// </para>
    /// <para>
    /// The list is the same <see cref="ZoneUses"/> the 批次設定面板 offers, in the same two sections
    /// and for the same reason: a rule compares the text 逐字, so a plausible-looking 「電梯井」 would
    /// be judged against the area limit instead of reported as a typo.
    /// </para>
    /// </remarks>
    internal sealed class ZoneUsePickerWindow : Window
    {
        private readonly RadioButton _keep;
        private readonly RadioButton _clear;
        private readonly RadioButton _pick;
        private readonly ComboBox _uses;

        public ZoneUsePickerWindow(string zoneName, string current, int areaCount)
        {
            Title = "區劃用途";
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = string.Format("區劃「{0}」的區劃用途", zoneName),
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = current is null
                    ? "目前：各個面積填了不一致的用途，或這個模型還沒有 防火檢討_區劃用途 參數。"
                    : current.Length == 0 ? "目前：一般區劃（未填用途）。" : "目前：" + current + "。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });

            _keep = new RadioButton
            {
                Content = "不變更（各個面積維持現在的用途）",
                IsChecked = current is null,
                Margin = new Thickness(0, 0, 0, 6)
            };

            _pick = new RadioButton
            {
                Content = "設為下列用途（這個區劃的每個面積都會寫成同一個）",
                IsChecked = !string.IsNullOrEmpty(current),
                Margin = new Thickness(0, 0, 0, 4)
            };

            _uses = new ComboBox { IsEditable = true, Margin = new Thickness(20, 0, 0, 10), Padding = new Thickness(4) };
            foreach (var entry in Choices()) _uses.Items.Add(entry);
            _uses.Text = string.IsNullOrEmpty(current) ? ZoneUses.Shaft : current;

            _clear = new RadioButton
            {
                Content = string.Format(
                    "設為一般區劃（清除用途{0}）",
                    areaCount > 1 ? string.Format("，{0} 個面積都會被清空", areaCount) : string.Empty),
                IsChecked = current is not null && current.Length == 0
            };

            panel.Children.Add(_keep);
            panel.Children.Add(_pick);
            panel.Children.Add(_uses);
            panel.Children.Add(_clear);

            panel.Children.Add(new TextBlock
            {
                Text = "垂直區劃（第79條之2第1項）不受第79條、第83條的區劃面積規定拘束，改依該項單獨區劃分隔。" +
                       "挑空與其連通區劃之間的那條線，只有在第79條之2第3項的免除成立時才會被檢討判為區劃內部線；" +
                       "設定用途不會刪線，也不會更動模型的其他部分。",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancel = new Button { Content = "取消", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
            var ok = new Button { Content = "確定", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
            ok.Click += Confirm;
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            Content = panel;
        }

        /// <summary>
        /// What was chosen: null for 不變更, empty for 一般區劃, otherwise the use. Only meaningful
        /// once <see cref="Window.ShowDialog"/> returned true.
        /// </summary>
        public string Value { get; private set; }

        /// <summary>True when the user asked to leave every Area alone, which writes nothing at all.</summary>
        public bool KeepsCurrent { get; private set; }

        private void Confirm(object sender, RoutedEventArgs e)
        {
            if (_keep.IsChecked == true)
            {
                Value = null;
                KeepsCurrent = true;
                DialogResult = true;
                return;
            }

            if (_clear.IsChecked == true)
            {
                Value = string.Empty;
                DialogResult = true;
                return;
            }

            var picked = (_uses.Text ?? string.Empty).Trim();
            if (picked.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "請選一個用途，或改選「設為一般區劃」來清除它。留白不等於清除。",
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Value = picked;
            DialogResult = true;
        }

        /// <summary>The two sections the 批次設定面板 offers, with their headings as plain separators.</summary>
        private static IEnumerable<string> Choices()
        {
            foreach (var use in ZoneUses.VerticalCompartments) yield return use;
            foreach (var use in ZoneUses.Article79_1Uses) yield return use;
        }

        /// <summary>
        /// Asks, and reports both the answer and whether it was 不變更 — which the caller cannot tell
        /// from a null return alone, because null is also what a cancelled dialog gives back.
        /// </summary>
        public static bool Ask(Window owner, string zoneName, string current, int areaCount, out string value)
        {
            var window = new ZoneUsePickerWindow(zoneName, current, areaCount) { Owner = owner };
            if (window.ShowDialog() != true)
            {
                value = null;
                return false;
            }

            value = window.Value;
            return true;
        }
    }
}
