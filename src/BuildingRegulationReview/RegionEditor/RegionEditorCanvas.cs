using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Regions;

namespace BuildingRegulationReview.RegionEditor
{
    /// <summary>
    /// The Region Editor canvas (spec 10.3): it draws whatever
    /// <see cref="RegionEditorSession.BuildView"/> hands it and turns mouse gestures into session
    /// calls. Every rule about what a gesture means lives in the session, so this class holds no
    /// geometry and no policy of its own — only pens, brushes and event plumbing.
    /// </summary>
    internal sealed class RegionEditorCanvas : FrameworkElement
    {
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA));
        private static readonly Brush UnassignedFill = new SolidColorBrush(Color.FromArgb(0x18, 0x60, 0x60, 0x60));
        private static readonly Brush LabelBackground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        private static readonly Pen BoundaryPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)), 1.0));
        private static readonly Pen ActiveZonePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x5F, 0xB4)), 2.5));
        private static readonly Pen SelectionPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 2.5));
        private static readonly Pen BandPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), 1.0) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) });
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
        private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        private static readonly Typeface LabelFace = new Typeface("Microsoft JhengHei UI");

        private readonly RegionEditorSession _session;
        private Point _dragStart;
        private Point _dragCurrent;
        private bool _banding;
        private bool _panning;
        private Point _panAnchor;

        public RegionEditorCanvas(RegionEditorSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Focusable = true;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
        }

        /// <summary>Raised whenever an edit changed the drafts, so the panels can refresh.</summary>
        public event Action Edited;

        /// <summary>Raised with the line for the status bar after every gesture.</summary>
        public event Action<string> Reported;

        public void Refresh() => InvalidateVisual();

        public void ZoomToFit()
        {
            _session.ZoomToFit();
            InvalidateVisual();
        }

        /// <summary>Brings a model point to the middle of the canvas without changing the scale.</summary>
        public void CenterOn(Point2D modelPoint)
        {
            var target = _session.Viewport.ToScreen(modelPoint);
            _session.PanByPixels((ActualWidth / 2.0) - target.X, (ActualHeight / 2.0) - target.Y);
            InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            if (info.NewSize.Width > 0 && info.NewSize.Height > 0)
                _session.Resize(new ScreenSize(info.NewSize.Width, info.NewSize.Height));
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;

            context.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var view = _session.BuildView();
            foreach (var face in view.Faces) DrawFace(context, face);
            foreach (var zone in view.Zones.Where(z => z.LabelAnchor.HasValue)) DrawZoneLabel(context, zone);
            for (var i = 0; i < view.Issues.Count; i++) DrawIssue(context, view.Issues[i], view.IssueAnchors[i]);

            if (_banding)
            {
                context.DrawRectangle(null, BandPen, new Rect(_dragStart, _dragCurrent));
            }
        }

        private void DrawFace(DrawingContext context, FaceVisual face)
        {
            var geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var stream = geometry.Open())
            {
                foreach (var ring in face.Rings)
                {
                    if (ring.Count < 3) continue;
                    stream.BeginFigure(ToPoint(ring[0]), true, true);
                    stream.PolyLineTo(ring.Skip(1).Select(ToPoint).ToList(), true, false);
                }
            }

            geometry.Freeze();

            var fill = face.Fill.HasValue ? FillBrush(face.Fill.Value) : UnassignedFill;
            var pen = face.IsSelected ? SelectionPen : face.IsInActiveZone ? ActiveZonePen : BoundaryPen;
            context.DrawGeometry(fill, pen, geometry);
        }

        private void DrawZoneLabel(DrawingContext context, ZoneVisual zone)
        {
            // The label sits on its own light plate rather than on the fill, so one text colour
            // stays readable over every zone colour.
            var text = Text(zone.Label, 12.5, Brushes.Black);
            var anchor = ToPoint(zone.LabelAnchor.Value);
            var origin = new Point(anchor.X - (text.Width / 2.0), anchor.Y - (text.Height / 2.0));
            context.DrawRectangle(
                LabelBackground,
                null,
                new Rect(origin.X - 5, origin.Y - 3, text.Width + 10, text.Height + 6));
            context.DrawText(text, origin);
        }

        private void DrawIssue(DrawingContext context, EditorIssue issue, ScreenPoint at)
        {
            var brush = issue.IsError ? ErrorBrush : WarningBrush;
            var center = ToPoint(at);
            context.DrawEllipse(brush, null, center, 5, 5);
            context.DrawEllipse(null, new Pen(Brushes.White, 1.5), center, 5, 5);
        }

        // ---- gestures -------------------------------------------------------------------------

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            _session.ZoomByWheel(ToScreen(e.GetPosition(this)), e.Delta / 120);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
            {
                _panning = true;
                _panAnchor = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.ScrollAll;
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                _dragStart = e.GetPosition(this);
                _dragCurrent = _dragStart;
                CaptureMouse();
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var position = e.GetPosition(this);

            if (_panning)
            {
                _session.PanByPixels(position.X - _panAnchor.X, position.Y - _panAnchor.Y);
                _panAnchor = position;
                InvalidateVisual();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed && IsMouseCaptured)
            {
                _dragCurrent = position;
                _banding = Distance(_dragStart, position) > RegionEditorSession.ClickThresholdPixels;
                if (_banding) InvalidateVisual();
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            var position = e.GetPosition(this);

            if (_panning && (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left))
            {
                _panning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                ReleaseMouseCapture();
                if (_banding)
                {
                    _banding = false;
                    var selected = _session.SelectInBox(ToScreen(_dragStart), ToScreen(position), PickMode());
                    Report($"已選取 {selected.Count} 個範圍。");
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    var selected = _session.SelectAt(ToScreen(position), SelectionMode.Toggle);
                    Report($"已選取 {selected.Count} 個範圍。");
                }
                else
                {
                    Apply(_session.AddFaceAt(ToScreen(position)));
                }

                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                Apply(_session.RemoveFaceAt(ToScreen(position)));
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.Key)
            {
                case Key.F:
                    ZoomToFit();
                    break;
                case Key.Escape:
                    _session.ClearSelection();
                    InvalidateVisual();
                    break;
                case Key.A when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                    _session.SelectAll();
                    Report($"已選取 {_session.SelectedFaceIds.Count} 個範圍。");
                    InvalidateVisual();
                    break;
                case Key.Enter:
                    Apply(_session.AddSelectionToActiveZone());
                    InvalidateVisual();
                    break;
                case Key.Delete:
                    Apply(_session.RemoveSelectionFromZones());
                    InvalidateVisual();
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        private void Apply(Result<ZoneMembershipChange> result)
        {
            if (result.IsFailure)
            {
                Report(result.Error.Message);
                return;
            }

            Report(result.Value.Message);
            Edited?.Invoke();
        }

        private void Report(string message) => Reported?.Invoke(message);

        private SelectionMode PickMode() =>
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? SelectionMode.Add : SelectionMode.Replace;

        private FormattedText Text(string text, double size, Brush brush) => new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            LabelFace,
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        private static Brush FillBrush(ZoneColor color)
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x80, color.Red, color.Green, color.Blue));
            brush.Freeze();
            return brush;
        }

        private static Pen Frozen(Pen pen)
        {
            pen.Freeze();
            return pen;
        }

        private static Point ToPoint(ScreenPoint point) => new Point(point.X, point.Y);

        private static ScreenPoint ToScreen(Point point) => new ScreenPoint(point.X, point.Y);

        private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }
}
