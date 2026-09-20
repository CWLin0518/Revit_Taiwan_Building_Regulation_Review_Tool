using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>Asks for one line of text: the name of a 區劃 draft when it is created or renamed.</summary>
    internal sealed class TextPromptWindow : Window
    {
        private readonly TextBox _input;

        public TextPromptWindow(string title, string prompt, string value)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _input = new TextBox { Text = value ?? string.Empty, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(4) };
            _input.SelectAll();

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var cancel = new Button { Content = "取消", Padding = new Thickness(12, 4, 12, 4), IsCancel = true };
            var ok = new Button { Content = "確定", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
            ok.Click += (_, __) => { Value = _input.Text; DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            Content = panel;
            Loaded += (_, __) => { _input.Focus(); _input.SelectAll(); };
        }

        public string Value { get; private set; }

        public static string Ask(Window owner, string title, string prompt, string value)
        {
            var window = new TextPromptWindow(title, prompt, value) { Owner = owner };
            return window.ShowDialog() == true ? window.Value : null;
        }
    }

    /// <summary>
    /// Picks a 區劃 colour from the shared palette (spec 10.3, 改色). The palette is the one the
    /// Editor hands out automatically, so a colour chosen by hand stays in the same family as the
    /// rest and reaches the Area Color Scheme unchanged in P2-T08.
    /// </summary>
    internal sealed class ZoneColorPickerWindow : Window
    {
        public ZoneColorPickerWindow(ZoneColor current)
        {
            Title = "區劃顏色";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Selected = current;

            var grid = new UniformGrid { Columns = 5, Margin = new Thickness(16) };
            foreach (var color in ZoneColorPalette.Colors)
            {
                var swatch = new Button
                {
                    Width = 56,
                    Height = 40,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)),
                    BorderBrush = color == current ? Brushes.Black : Brushes.Gainsboro,
                    BorderThickness = new Thickness(color == current ? 3 : 1),
                    ToolTip = color.ToHex(),
                    Tag = color
                };
                swatch.Click += (sender, __) =>
                {
                    Selected = (ZoneColor)((Button)sender).Tag;
                    DialogResult = true;
                };
                grid.Children.Add(swatch);
            }

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "選擇區劃顏色", Margin = new Thickness(16, 16, 16, 0), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(grid);
            Content = panel;
        }

        public ZoneColor Selected { get; private set; }

        public static ZoneColor? Ask(Window owner, ZoneColor current)
        {
            var window = new ZoneColorPickerWindow(current) { Owner = owner };
            return window.ShowDialog() == true ? window.Selected : (ZoneColor?)null;
        }
    }

    /// <summary>Picks which review package to edit when the document holds more than one.</summary>
    internal sealed class PackagePickerWindow : Window
    {
        private readonly ListBox _list = new ListBox { DisplayMemberPath = "Label", MinHeight = 140, Margin = new Thickness(0, 8, 0, 0) };

        public PackagePickerWindow(System.Collections.Generic.IEnumerable<PackageChoice> choices)
        {
            Title = "選擇檢討套件";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _list.ItemsSource = choices.ToList();
            _list.SelectedIndex = 0;
            _list.MouseDoubleClick += (_, __) => Accept();

            var panel = new StackPanel { Margin = new Thickness(18) };
            panel.Children.Add(new TextBlock { Text = "要編輯哪一個 Area Plan 的防火區劃？", FontWeight = FontWeights.SemiBold });
            panel.Children.Add(_list);

            var ok = new Button
            {
                Content = "開啟編輯器",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 14, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };
            ok.Click += (_, __) => Accept();
            panel.Children.Add(ok);
            Content = panel;
        }

        public PackageChoice Selected { get; private set; }

        private void Accept()
        {
            Selected = _list.SelectedItem as PackageChoice;
            if (Selected != null) DialogResult = true;
        }
    }

    internal sealed class PackageChoice
    {
        public PackageChoice(Guid packageId, string areaPlanUniqueId, string label, string draftingViewUniqueId = null)
        {
            PackageId = packageId;
            AreaPlanUniqueId = areaPlanUniqueId;
            Label = label;
            DraftingViewUniqueId = draftingViewUniqueId;
        }

        public Guid PackageId { get; }
        public string AreaPlanUniqueId { get; }
        public string Label { get; }

        /// <summary>Where the 單線圖 copies live, when the package already has one; null until P2-T08.</summary>
        public string DraftingViewUniqueId { get; }
    }
}
