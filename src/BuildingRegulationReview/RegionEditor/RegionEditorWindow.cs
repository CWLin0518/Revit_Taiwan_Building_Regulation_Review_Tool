using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>
    /// The Region Editor window (spec 10.3, P2-T05): the canvas, the list of 區劃 drafts and the list
    /// of unresolved boundary problems. It is the shell around
    /// <see cref="RegionEditorSession"/> and decides nothing on its own — every command here is one
    /// session call whose result becomes the status line.
    /// </summary>
    /// <remarks>
    /// Nothing in this window writes to the model. Drafts live in memory until P2-T07 writes them
    /// back, and Undo/Redo plus the prompt for unapplied changes arrive with P2-T06.
    /// </remarks>
    internal sealed class RegionEditorWindow : Window
    {
        private readonly RegionEditorSession _session;
        private readonly RegionEditorCanvas _canvas;
        private readonly ListBox _zoneList = new ListBox { Height = 210 };
        private readonly ListBox _issueList = new ListBox { Height = 150 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _summary = new TextBlock { Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap };
        private bool _syncing;

        public RegionEditorWindow(RegionEditorSession session, string planName)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));

            Title = string.IsNullOrWhiteSpace(planName) ? "防火區劃編輯器" : "防火區劃編輯器 — " + planName;
            Width = 1180;
            Height = 780;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _canvas = new RegionEditorCanvas(_session);
            _canvas.Edited += Refresh;
            _canvas.Reported += Report;

            var root = new DockPanel();
            root.Children.Add(BuildStatusBar());
            root.Children.Add(BuildSidePanel());
            root.Children.Add(new Border
            {
                BorderBrush = Brushes.Gainsboro,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(10, 10, 0, 10),
                Child = _canvas
            });

            Content = root;
            Loaded += (_, __) =>
            {
                _canvas.Focus();
                _canvas.ZoomToFit();
                Refresh();
                Report("左鍵點選加入目前區劃，右鍵移出；拖曳框選，Ctrl+點選切換選取；滾輪縮放，中鍵或 Alt+拖曳平移，F 鍵全部顯示。");
            };
        }

        /// <summary>
        /// Asked to show source elements back in Revit. The host wires this to an ExternalEvent,
        /// because the Editor is modeless and may not touch the Revit API on its own thread.
        /// </summary>
        public Action<IReadOnlyList<SourceRef>> ShowSourceElements { get; set; }

        private UIElement BuildStatusBar()
        {
            var panel = new StackPanel { Margin = new Thickness(12, 6, 12, 10) };
            panel.Children.Add(_status);
            panel.Children.Add(_summary);
            DockPanel.SetDock(panel, Dock.Bottom);
            return panel;
        }

        private UIElement BuildSidePanel()
        {
            var panel = new StackPanel { Width = 330, Margin = new Thickness(10) };

            panel.Children.Add(Header("區劃草稿"));
            _zoneList.ItemTemplate = ZoneRow.Template();
            _zoneList.SelectionChanged += (_, __) => ActivateSelectedZone();
            panel.Children.Add(_zoneList);

            var zoneButtons = Row();
            zoneButtons.Children.Add(MakeButton("新增", CreateZone));
            zoneButtons.Children.Add(MakeButton("重新命名", RenameZone));
            zoneButtons.Children.Add(MakeButton("顏色", RecolorZone));
            zoneButtons.Children.Add(MakeButton("刪除", DeleteZone));
            panel.Children.Add(zoneButtons);

            var faceButtons = Row();
            faceButtons.Children.Add(MakeButton("加入選取的範圍", () => Apply(_session.AddSelectionToActiveZone())));
            faceButtons.Children.Add(MakeButton("移出選取的範圍", () => Apply(_session.RemoveSelectionFromZones())));
            panel.Children.Add(faceButtons);

            var viewButtons = Row();
            viewButtons.Children.Add(MakeButton("全部顯示", () => { _canvas.ZoomToFit(); }));
            viewButtons.Children.Add(MakeButton("顯示此區劃", ZoomToZone));
            viewButtons.Children.Add(MakeButton("在 Revit 中選取來源", ShowSources));
            panel.Children.Add(viewButtons);

            panel.Children.Add(Header("未閉合與修復問題"));
            _issueList.SelectionChanged += (_, __) => CenterOnSelectedIssue();
            panel.Children.Add(_issueList);

            panel.Children.Add(new TextBlock
            {
                Text = "問題清單中的每一則都可點選，畫面會移到該位置。草稿尚未寫回模型；套用與 Undo/Redo 會在後續階段提供。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 10, 0, 0)
            });

            DockPanel.SetDock(panel, Dock.Right);
            return panel;
        }

        // ---- commands -------------------------------------------------------------------------

        private void CreateZone()
        {
            var name = TextPromptWindow.Ask(this, "新增區劃", "區劃名稱", SuggestedName());
            if (name == null) return;

            var created = _session.CreateZone(name);
            if (created.IsFailure)
            {
                Report(created.Error.Message);
                return;
            }

            Report($"已建立區劃「{created.Value.Name}」，接著在畫面上點選要加入的範圍。");
            Refresh();
        }

        private void RenameZone()
        {
            var zone = SelectedZone();
            if (zone == null) return;

            var name = TextPromptWindow.Ask(this, "重新命名區劃", "區劃名稱", zone.Name);
            if (name == null) return;

            Report(Run(_session.RenameZone(zone.Id, name), $"已將區劃改名為「{name.Trim()}」。"));
        }

        private void RecolorZone()
        {
            var zone = SelectedZone();
            if (zone == null) return;

            var color = ZoneColorPickerWindow.Ask(this, zone.Color);
            if (color == null) return;

            Report(Run(_session.RecolorZone(zone.Id, color.Value), $"已將「{zone.Name}」改為 {color.Value.ToHex()}。"));
        }

        private void DeleteZone()
        {
            var zone = SelectedZone();
            if (zone == null) return;

            var confirm = MessageBox.Show(
                this,
                $"刪除區劃「{zone.Name}」？其中的 {zone.FaceCount} 個範圍會變回未指派，模型不會被更動。",
                Title,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK) return;

            var deleted = _session.DeleteZone(zone.Id);
            Report(deleted.IsSuccess ? $"已刪除區劃「{deleted.Value.Name}」。" : deleted.Error.Message);
            Refresh();
        }

        private void ZoomToZone()
        {
            var zone = SelectedZone();
            if (zone == null) return;

            var result = _session.ZoomToZone(zone.Id);
            if (result.IsFailure) Report(result.Error.Message);
            _canvas.Refresh();
        }

        private void ShowSources()
        {
            if (ShowSourceElements == null)
            {
                Report("目前沒有可用的 Revit 連線，無法選取來源元素。");
                return;
            }

            var zone = SelectedZone();
            var sources = _session.HasSelection
                ? _session.SourcesOfSelection()
                : zone == null ? Array.Empty<SourceRef>() : SourcesOfZone(zone);

            if (sources.Count == 0)
            {
                Report("請先選取範圍或區劃，再顯示來源元素。");
                return;
            }

            ShowSourceElements(sources);
            Report($"已要求在 Revit 中選取 {sources.Count} 個來源元素。");
        }

        private IReadOnlyList<SourceRef> SourcesOfZone(ZoneDraft zone)
        {
            var result = _session.SourcesOfZone(zone.Id);
            return result.IsSuccess ? result.Value : Array.Empty<SourceRef>();
        }

        private void ActivateSelectedZone()
        {
            if (_syncing) return;
            if (!(_zoneList.SelectedItem is ZoneRow row)) return;

            var zone = _session.Zones.Zone(row.ZoneId);
            if (zone == null) return;

            _session.SetActiveZone(zone.Id);
            _session.SelectZoneFaces(zone.Id);
            _canvas.Refresh();
            _summary.Text = _session.BuildView().Summary;
        }

        private void CenterOnSelectedIssue()
        {
            if (_syncing) return;
            if (!(_issueList.SelectedItem is IssueRow row)) return;

            _canvas.CenterOn(row.Location);
            Report(row.Text);
        }

        // ---- refresh --------------------------------------------------------------------------

        private void Apply(Result<ZoneMembershipChange> result)
        {
            Report(result.IsSuccess ? result.Value.Message : result.Error.Message);
            if (result.IsSuccess) Refresh();
            _canvas.Refresh();
        }

        private string Run(Result result, string success)
        {
            if (result.IsSuccess) Refresh();
            return result.IsSuccess ? success : result.Error.Message;
        }

        private void Refresh()
        {
            var view = _session.BuildView();

            _syncing = true;
            try
            {
                _zoneList.ItemsSource = view.Zones.Select(ZoneRow.From).ToList();
                _zoneList.SelectedItem = _zoneList.Items
                    .OfType<ZoneRow>()
                    .FirstOrDefault(r => r.ZoneId == _session.ActiveZoneId);

                if (_issueList.ItemsSource == null)
                {
                    _issueList.ItemsSource = view.Issues
                        .Select((issue, index) => new IssueRow(issue, view.IssueAnchors[index]))
                        .ToList();
                }
            }
            finally
            {
                _syncing = false;
            }

            _summary.Text = view.Summary;
            _canvas.Refresh();
        }

        private void Report(string message)
        {
            _status.Text = message;
            _summary.Text = _session.BuildView().Summary;
        }

        // ---- helpers --------------------------------------------------------------------------

        private ZoneDraft SelectedZone()
        {
            if (_zoneList.SelectedItem is ZoneRow row)
            {
                var zone = _session.Zones.Zone(row.ZoneId);
                if (zone != null) return zone;
            }

            Report("請先在清單中選取一個區劃。");
            return null;
        }

        private string SuggestedName() => string.Format(
            CultureInfo.CurrentUICulture, "區劃 {0}", _session.Zones.Count + 1);

        private static TextBlock Header(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 6)
        };

        private static WrapPanel Row() => new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };

        private static Button MakeButton(string text, Action action)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6) };
            button.Click += (_, __) => action();
            return button;
        }

        /// <summary>One line of the 區劃 list: colour swatch, name, face count and draft area.</summary>
        private sealed class ZoneRow
        {
            private ZoneRow(Guid zoneId, Brush swatch, string text)
            {
                ZoneId = zoneId;
                Swatch = swatch;
                Text = text;
            }

            public Guid ZoneId { get; }
            public Brush Swatch { get; }
            public string Text { get; }

            public static ZoneRow From(ZoneVisual zone)
            {
                var text = string.Format(
                    CultureInfo.CurrentUICulture,
                    "{0}（{1} 個範圍，{2}）",
                    zone.Name,
                    zone.FaceCount,
                    zone.AreaText);
                if (!zone.IsContiguous) text += string.Format(CultureInfo.CurrentUICulture, "・{0} 塊不相連", zone.ContiguousPartCount);
                if (zone.HoleCount > 0) text += string.Format(CultureInfo.CurrentUICulture, "・孔洞 {0}", zone.HoleCount);

                var brush = new SolidColorBrush(Color.FromRgb(zone.Color.Red, zone.Color.Green, zone.Color.Blue));
                brush.Freeze();
                return new ZoneRow(zone.ZoneId, brush, text);
            }

            public static DataTemplate Template()
            {
                var swatch = new FrameworkElementFactory(typeof(Border));
                swatch.SetValue(WidthProperty, 14.0);
                swatch.SetValue(HeightProperty, 14.0);
                swatch.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
                swatch.SetValue(Border.BorderBrushProperty, Brushes.Gray);
                swatch.SetValue(Border.BorderThicknessProperty, new Thickness(1));
                swatch.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
                swatch.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Swatch)));

                var label = new FrameworkElementFactory(typeof(TextBlock));
                label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Text)));
                label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

                var row = new FrameworkElementFactory(typeof(StackPanel));
                row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
                row.AppendChild(swatch);
                row.AppendChild(label);

                return new DataTemplate { VisualTree = row };
            }
        }

        /// <summary>One line of the issue list, remembering where to take the canvas.</summary>
        private sealed class IssueRow
        {
            public IssueRow(EditorIssue issue, ScreenPoint anchor)
            {
                Location = issue.Location;
                Anchor = anchor;
                Text = (issue.IsError ? "✖ " : "▲ ") + issue.Message;
            }

            public Point2D Location { get; }
            public ScreenPoint Anchor { get; }
            public string Text { get; }

            public override string ToString() => Text;
        }
    }
}
