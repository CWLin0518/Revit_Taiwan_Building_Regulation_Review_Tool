using System;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Domain.Geometry;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.RegionEditing;

public class EditorViewportTests
{
    private static readonly ScreenSize Canvas = new ScreenSize(800, 600);

    [Fact]
    public void DrawsTheCentreOfTheModelAtTheCentreOfTheCanvas()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(10, 20), 4);

        var screen = viewport.ToScreen(new Point2D(10, 20));

        Assert.Equal(400, screen.X, 9);
        Assert.Equal(300, screen.Y, 9);
    }

    [Fact]
    public void FlipsYSoTheModelPointsUpAndTheCanvasPointsDown()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 4);

        Assert.Equal(300 - 40, viewport.ToScreen(new Point2D(0, 10)).Y, 9);
        Assert.Equal(400 + 40, viewport.ToScreen(new Point2D(10, 0)).X, 9);
    }

    [Fact]
    public void ConvertsBackToTheSameModelPoint()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(-3.5, 7.25), 13.75);
        var point = new Point2D(11.5, -4.25);

        var round = viewport.ToModel(viewport.ToScreen(point));

        Assert.Equal(point.X, round.X, 9);
        Assert.Equal(point.Y, round.Y, 9);
    }

    [Fact]
    public void KeepsTheModelPointUnderTheCursorWhileZooming()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 4);
        var cursor = new ScreenPoint(120, 500);
        var held = viewport.ToModel(cursor);

        var zoomed = viewport.ZoomAt(cursor, 2.5);

        Assert.Equal(10.0, zoomed.PixelsPerFoot, 9);
        Assert.Equal(held.X, zoomed.ToModel(cursor).X, 9);
        Assert.Equal(held.Y, zoomed.ToModel(cursor).Y, 9);
    }

    [Fact]
    public void PansByWholePixels()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 5);

        var panned = viewport.PanByPixels(50, -25);

        Assert.Equal(-10.0, panned.ModelCenter.X, 9);
        Assert.Equal(-5.0, panned.ModelCenter.Y, 9);
        Assert.Equal(viewport.PixelsPerFoot, panned.PixelsPerFoot, 9);
    }

    [Fact]
    public void StopsZoomingAtTheEndsOfItsRangeInsteadOfFailing()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 1);

        Assert.Equal(EditorViewport.MaximumPixelsPerFoot, viewport.ZoomAt(new ScreenPoint(1, 1), 1.0e9).PixelsPerFoot, 9);
        Assert.Equal(EditorViewport.MinimumPixelsPerFoot, viewport.ZoomAt(new ScreenPoint(1, 1), 1.0e-9).PixelsPerFoot, 9);
    }

    [Fact]
    public void LeavesTheViewportAloneWhenZoomIsAlreadyAtItsLimit()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), EditorViewport.MaximumPixelsPerFoot);

        Assert.Same(viewport, viewport.ZoomAt(new ScreenPoint(1, 1), 2));
    }

    [Fact]
    public void FitsThePlanInsideTheCanvasWithPadding()
    {
        var extent = new PlanExtent2D(new Point2D(0, 0), new Point2D(100, 50));

        var viewport = EditorViewport.FitTo(extent, Canvas, 20);

        // 760 px of usable width over 100 ft, against 560 px of usable height over 50 ft: width binds.
        Assert.Equal(7.6, viewport.PixelsPerFoot, 9);
        Assert.Equal(50.0, viewport.ModelCenter.X, 9);
        Assert.Equal(25.0, viewport.ModelCenter.Y, 9);
        Assert.True(viewport.VisibleExtent.Contains(extent.Minimum));
        Assert.True(viewport.VisibleExtent.Contains(extent.Maximum));
    }

    [Fact]
    public void FitsAPlanThatIsTallerThanItIsWide()
    {
        var viewport = EditorViewport.FitTo(new PlanExtent2D(new Point2D(0, 0), new Point2D(10, 100)), Canvas, 0);

        Assert.Equal(6.0, viewport.PixelsPerFoot, 9);
    }

    [Fact]
    public void FallsBackToTheLargestScaleForAnExtentWithNoSize()
    {
        var viewport = EditorViewport.FitTo(new PlanExtent2D(new Point2D(5, 5), new Point2D(5, 5)), Canvas);

        Assert.Equal(EditorViewport.MaximumPixelsPerFoot, viewport.PixelsPerFoot, 9);
        Assert.Equal(5.0, viewport.ModelCenter.X, 9);
    }

    [Fact]
    public void ReportsWhatIsVisible()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 10);

        var visible = viewport.VisibleExtent;

        Assert.Equal(-40.0, visible.Minimum.X, 9);
        Assert.Equal(40.0, visible.Maximum.X, 9);
        Assert.Equal(-30.0, visible.Minimum.Y, 9);
        Assert.Equal(30.0, visible.Maximum.Y, 9);
    }

    [Fact]
    public void KeepsTheScaleAndCentreWhenTheWindowIsResized()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(3, 4), 7);

        var resized = viewport.Resize(new ScreenSize(1024, 768));

        Assert.Equal(7.0, resized.PixelsPerFoot, 9);
        Assert.Equal(3.0, resized.ModelCenter.X, 9);
        Assert.Equal(1024.0, resized.Size.Width, 9);
    }

    [Fact]
    public void RectangleNormalizesWhicheverCornerTheDragStartedFrom()
    {
        var rect = new ScreenRect(new ScreenPoint(300, 200), new ScreenPoint(100, 50));

        Assert.Equal(100, rect.Left, 9);
        Assert.Equal(50, rect.Top, 9);
        Assert.Equal(200, rect.Width, 9);
        Assert.Equal(150, rect.Height, 9);
        Assert.True(rect.Contains(new ScreenPoint(150, 60)));
        Assert.False(rect.Contains(new ScreenPoint(150, 300)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void RefusesAnImpossibleScale(double pixelsPerFoot)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EditorViewport(Canvas, new Point2D(0, 0), pixelsPerFoot));
    }

    [Fact]
    public void RefusesACanvasWithNoArea()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenSize(0, 600));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenSize(800, -1));
    }

    [Fact]
    public void RefusesAZoomFactorThatIsNotPositive()
    {
        var viewport = new EditorViewport(Canvas, new Point2D(0, 0), 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => viewport.ZoomAt(new ScreenPoint(0, 0), 0));
    }
}
