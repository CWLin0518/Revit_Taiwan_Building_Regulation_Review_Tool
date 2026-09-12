using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;

namespace BuildingRegulationReview
{
    internal sealed class Article164ElementFilterWindow : Window
    {
        private readonly List<(CheckBox CheckBox, ElementId TypeId)> _rows = new List<(CheckBox, ElementId)>();
        public HashSet<ElementId> ExcludedTypeIds { get; } = new HashSet<ElementId>();

        public Article164ElementFilterWindow(IEnumerable<Article164ElementTypeGroup> typeGroups, string elementSource)
        {
            Title = "選擇要納入第164條檢討的牆／樓板類型";
            Width = 420; Height = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = new DockPanel { Margin = new Thickness(16) };

            var header = new TextBlock
            {
                Text = $"以下為透過{elementSource}取得的牆／樓板類型，預設全部勾選。取消勾選可將該類型排除於本次計算之外。",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(header, Dock.Top);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Button { Content = "取消", Padding = new Thickness(10, 4, 10, 4) };
            cancel.Click += (_, __) => { DialogResult = false; };
            var ok = new Button { Content = "開始檢討", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, __) => Accept();
            buttons.Children.Add(cancel); buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);

            var toggle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var selectAll = new Button { Content = "全選", Padding = new Thickness(8, 2, 8, 2) };
            var selectNone = new Button { Content = "全不選", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
            selectAll.Click += (_, __) => SetAll(true);
            selectNone.Click += (_, __) => SetAll(false);
            toggle.Children.Add(selectAll); toggle.Children.Add(selectNone);
            DockPanel.SetDock(toggle, Dock.Top);

            var list = new StackPanel();
            foreach (var group in typeGroups.OrderBy(g => g.Category).ThenBy(g => g.TypeName))
            {
                var checkBox = new CheckBox
                {
                    Content = $"[{group.Category}] {group.TypeName}（{group.Count} 個）",
                    IsChecked = true,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                list.Children.Add(checkBox);
                _rows.Add((checkBox, group.TypeId));
            }
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

            root.Children.Add(header);
            root.Children.Add(buttons);
            root.Children.Add(toggle);
            root.Children.Add(scroll);
            Content = root;
        }

        private void SetAll(bool isChecked)
        {
            foreach (var row in _rows) row.CheckBox.IsChecked = isChecked;
        }

        private void Accept()
        {
            ExcludedTypeIds.Clear();
            foreach (var row in _rows)
                if (row.CheckBox.IsChecked != true) ExcludedTypeIds.Add(row.TypeId);
            DialogResult = true;
        }
    }

    internal sealed class Article164ElementTypeGroup
    {
        public string Category { get; set; }
        public string TypeName { get; set; }
        public ElementId TypeId { get; set; }
        public int Count { get; set; }
    }
}
