using System;
using BuildingRegulationReview.Domain.Geometry;

namespace BuildingRegulationReview.Application.RegionEditing;

// Editor screen space, in device-independent pixels with Y running down, kept apart from the model
// coordinates of Domain.Geometry so no drawing coordinate can be mistaken for a plan coordinate.
public readonly struct ScreenPoint : IEquatable<ScreenPoint>
{
    public ScreenPoint(double x, double y)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (double.IsNaN(y) || double.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(y));
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public double DistanceTo(ScreenPoint other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public bool Equals(ScreenPoint other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is ScreenPoint other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + X.GetHashCode();
            hash = (hash * 31) + Y.GetHashCode();
            return hash;
        }
    }

    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}

public readonly struct ScreenSize
{
    public ScreenSize(double width, double height)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
    }

    public double Width { get; }
    public double Height { get; }

    public override string ToString() => $"{Width:0.##} x {Height:0.##}";
}

/// <summary>An axis-aligned rectangle on screen; the shape a rubber-band selection has.</summary>
public readonly struct ScreenRect
{
    public ScreenRect(ScreenPoint first, ScreenPoint second)
    {
        Left = Math.Min(first.X, second.X);
        Top = Math.Min(first.Y, second.Y);
        Right = Math.Max(first.X, second.X);
        Bottom = Math.Max(first.Y, second.Y);
    }

    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }

    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public ScreenPoint Center => new ScreenPoint((Left + Right) / 2.0, (Top + Bottom) / 2.0);

    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    public override string ToString() => $"[{Left:0.##}, {Top:0.##} -> {Right:0.##}, {Bottom:0.##}]";
}

/// <summary>
/// What part of the plan the Editor canvas is showing and at what magnification (spec 10.3, 平移與
/// 縮放). Immutable: panning and zooming produce a new viewport, so a redraw can never see a half
/// applied transform.
/// </summary>
/// <remarks>
/// Model coordinates are decimal feet with Y up; screen coordinates are pixels with Y down, so the
/// mapping flips Y. The zoom bounds are display limits, not geometric tolerances: they only keep the
/// scale usable and are clamped rather than rejected, because a mouse wheel at the end of its range
/// should stop, not fail.
/// </remarks>
public sealed class EditorViewport
{
    public const double MinimumPixelsPerFoot = 0.01;
    public const double MaximumPixelsPerFoot = 4000.0;

    /// <summary>Kept clear around the plan when fitting, so boundary lines are not cut by the edge.</summary>
    public const double DefaultFitPaddingPixels = 24.0;

    public EditorViewport(ScreenSize size, Point2D modelCenter, double pixelsPerFoot)
    {
        if (double.IsNaN(pixelsPerFoot) || double.IsInfinity(pixelsPerFoot) || pixelsPerFoot <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelsPerFoot), "Scale must be a finite positive number.");

        Size = size;
        ModelCenter = modelCenter;
        PixelsPerFoot = Clamp(pixelsPerFoot);
    }

    public ScreenSize Size { get; }

    /// <summary>The model point drawn at the centre of the canvas.</summary>
    public Point2D ModelCenter { get; }

    public double PixelsPerFoot { get; }

    /// <summary>The plan area currently visible, for culling and for the zoom readout.</summary>
    public PlanExtent2D VisibleExtent
    {
        get
        {
            var halfWidth = Size.Width / 2.0 / PixelsPerFoot;
            var halfHeight = Size.Height / 2.0 / PixelsPerFoot;
            return new PlanExtent2D(
                new Point2D(ModelCenter.X - halfWidth, ModelCenter.Y - halfHeight),
                new Point2D(ModelCenter.X + halfWidth, ModelCenter.Y + halfHeight));
        }
    }

    public ScreenPoint ToScreen(Point2D point) => new ScreenPoint(
        ((point.X - ModelCenter.X) * PixelsPerFoot) + (Size.Width / 2.0),
        (Size.Height / 2.0) - ((point.Y - ModelCenter.Y) * PixelsPerFoot));

    public Point2D ToModel(ScreenPoint point) => new Point2D(
        ModelCenter.X + ((point.X - (Size.Width / 2.0)) / PixelsPerFoot),
        ModelCenter.Y - ((point.Y - (Size.Height / 2.0)) / PixelsPerFoot));

    public EditorViewport Resize(ScreenSize size) => new EditorViewport(size, ModelCenter, PixelsPerFoot);

    public EditorViewport PanByPixels(double deltaX, double deltaY) => new EditorViewport(
        Size,
        new Point2D(ModelCenter.X - (deltaX / PixelsPerFoot), ModelCenter.Y + (deltaY / PixelsPerFoot)),
        PixelsPerFoot);

    /// <summary>Zooms while keeping the model point under <paramref name="anchor"/> under it.</summary>
    public EditorViewport ZoomAt(ScreenPoint anchor, double factor)
    {
        if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor), "Zoom factor must be a finite positive number.");

        var scale = Clamp(PixelsPerFoot * factor);
        if (scale == PixelsPerFoot) return this;

        var held = ToModel(anchor);
        var offsetX = anchor.X - (Size.Width / 2.0);
        var offsetY = anchor.Y - (Size.Height / 2.0);
        var center = new Point2D(held.X - (offsetX / scale), held.Y + (offsetY / scale));
        return new EditorViewport(Size, center, scale);
    }

    /// <summary>Shows the whole extent, centred, with padding. A degenerate extent keeps the scale.</summary>
    public static EditorViewport FitTo(PlanExtent2D extent, ScreenSize size, double paddingPixels = DefaultFitPaddingPixels)
    {
        if (extent is null) throw new ArgumentNullException(nameof(extent));
        if (double.IsNaN(paddingPixels) || double.IsInfinity(paddingPixels) || paddingPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(paddingPixels));

        var usableWidth = Math.Max(size.Width - (2.0 * paddingPixels), 1.0);
        var usableHeight = Math.Max(size.Height - (2.0 * paddingPixels), 1.0);
        var center = new Point2D(
            (extent.Minimum.X + extent.Maximum.X) / 2.0,
            (extent.Minimum.Y + extent.Maximum.Y) / 2.0);

        var scales = new double[2];
        scales[0] = extent.WidthFeet > 0 ? usableWidth / extent.WidthFeet : MaximumPixelsPerFoot;
        scales[1] = extent.HeightFeet > 0 ? usableHeight / extent.HeightFeet : MaximumPixelsPerFoot;
        return new EditorViewport(size, center, Math.Min(scales[0], scales[1]));
    }

    private static double Clamp(double pixelsPerFoot) =>
        Math.Min(MaximumPixelsPerFoot, Math.Max(MinimumPixelsPerFoot, pixelsPerFoot));

    public override string ToString() => $"{Size} @ {PixelsPerFoot:0.###} px/ft around {ModelCenter}";
}
