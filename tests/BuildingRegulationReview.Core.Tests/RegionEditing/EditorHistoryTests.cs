using System;
using System.Linq;
using BuildingRegulationReview.Application.RegionEditing;
using BuildingRegulationReview.Domain.Common;
using BuildingRegulationReview.Domain.Regions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.RegionEditing;

public class EditorHistoryTests
{
    [Fact]
    public void HasNothingToStepThroughWhenItIsNew()
    {
        var history = new EditorHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.UndoLabel);
        Assert.Null(history.RedoLabel);
        Assert.Null(history.Undo(State(0)));
        Assert.Null(history.Redo(State(0)));
    }

    [Fact]
    public void GivesBackTheStateAnEditLeftBehind()
    {
        var history = new EditorHistory();
        var before = State(1);
        history.Record(before, "建立區劃「A」");

        var entry = history.Undo(State(2));

        Assert.NotNull(entry);
        Assert.Same(before, entry!.State);
        Assert.Equal("建立區劃「A」", entry.Label);
    }

    [Fact]
    public void WalksBackAndForwardsThroughTheSameEdits()
    {
        var history = new EditorHistory();
        history.Record(State(1), "第一步");
        history.Record(State(2), "第二步");

        Assert.Equal("第二步", history.UndoLabel);
        var undone = history.Undo(State(3))!;
        Assert.Equal(2, undone.State.Zones.Count);
        Assert.Equal("第二步", history.RedoLabel);

        var redone = history.Redo(undone.State)!;
        Assert.Equal(3, redone.State.Zones.Count);
        Assert.Equal("第二步", history.UndoLabel);
    }

    [Fact]
    public void ANewEditAbandonsTheRedoBranch()
    {
        var history = new EditorHistory();
        history.Record(State(1), "第一步");
        history.Undo(State(2));
        Assert.True(history.CanRedo);

        history.Record(State(3), "另一步");

        Assert.False(history.CanRedo);
        Assert.Equal("另一步", history.UndoLabel);
    }

    [Fact]
    public void ForgetsTheOldestStepRatherThanGrowingForever()
    {
        var history = new EditorHistory();
        for (var i = 0; i <= EditorHistory.MaximumDepth + 4; i++) history.Record(State(1), "第 " + i + " 步");

        Assert.Equal(EditorHistory.MaximumDepth, history.UndoCount);
        Assert.Equal("第 5 步", history.UndoLabels.First());
    }

    [Fact]
    public void ClearingDropsBothDirections()
    {
        var history = new EditorHistory();
        history.Record(State(1), "第一步");
        history.Undo(State(2));

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RefusesAnEntryWithoutALabel()
    {
        var history = new EditorHistory();

        Assert.Throws<ArgumentException>(() => history.Record(State(1), "  "));
        Assert.Throws<ArgumentNullException>(() => history.Record(null!, "第一步"));
    }

    private static EditorState State(int zoneCount)
    {
        var zones = ZoneDraftSet.Empty;
        for (var i = 0; i < zoneCount; i++)
        {
            var added = zones.Add(new ZoneDraft(Guid.NewGuid(), "區劃 " + i, ZoneColorPalette.At(i)));
            Assert.True(added.IsSuccess);
            zones = added.Value;
        }

        return new EditorState(zones, zones.Zones.LastOrDefault()?.Id);
    }
}
