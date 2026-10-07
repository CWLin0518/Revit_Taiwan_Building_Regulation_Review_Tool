using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Application.Diagnostics;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Diagnostics;

/// <summary>
/// B-04: 一次檢討的日誌可以上百筆、每筆好幾百字，整包回傳會超過 MCP 呼叫端一次能讀的上限。摘要的
/// 規則只有一份：統計照全部算，列出的只有最重要的前幾筆，而且被截斷時說出來。
/// </summary>
public sealed class ReviewLogDigestTests
{
    private static readonly Guid PackageId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ReviewLogEntry Entry(ReviewSeverity severity, string message) =>
        new(ReviewErrorCode.ReviewCompleted, ReviewStage.Review, severity, PackageId, message);

    private static IReadOnlyList<ReviewLogEntry> Many(int infos, int warnings = 0, int errors = 0) =>
        Enumerable.Range(0, errors).Select(i => Entry(ReviewSeverity.Error, "錯誤 " + i))
            .Concat(Enumerable.Range(0, warnings).Select(i => Entry(ReviewSeverity.Warning, "警告 " + i)))
            .Concat(Enumerable.Range(0, infos).Select(i => Entry(ReviewSeverity.Info, "訊息 " + i)))
            .ToList();

    [Fact]
    public void TheCountsAreAlwaysAboutEveryEntryNotOnlyTheListedOnes()
    {
        var digest = ReviewLogDigest.Of(Many(infos: 90, warnings: 8, errors: 3), limit: 5);

        Assert.Equal(101, digest.Total);
        Assert.Equal(3, digest.Errors);
        Assert.Equal(8, digest.Warnings);
        Assert.Equal(5, digest.Entries.Count);
        Assert.True(digest.Truncated);
    }

    /// <summary>嚴重度遞減，同嚴重度維持原順序——留下的是最該看的那幾筆。</summary>
    [Fact]
    public void TheWorstEntriesSurviveTheCutInTheOrderTheyWereLogged()
    {
        var digest = ReviewLogDigest.Of(Many(infos: 20, warnings: 2, errors: 1), limit: 4);

        Assert.Equal(
            new[] { ReviewSeverity.Error, ReviewSeverity.Warning, ReviewSeverity.Warning, ReviewSeverity.Info },
            digest.Entries.Select(e => e.Severity));
        Assert.Equal(new[] { "錯誤 0", "警告 0", "警告 1", "訊息 0" }, digest.Entries.Select(e => e.UserMessage));
    }

    /// <summary>limit 0 ＝ 只要統計，不要 entries；但日誌不是空的，所以 truncated 仍然是 true。</summary>
    [Fact]
    public void ALimitOfZeroKeepsTheCountsAndStillSaysSomethingWasLeftOut()
    {
        var digest = ReviewLogDigest.Of(Many(infos: 2, warnings: 1), limit: 0);

        Assert.Empty(digest.Entries);
        Assert.Equal(3, digest.Total);
        Assert.Equal(1, digest.Warnings);
        Assert.True(digest.Truncated);
    }

    [Fact]
    public void NothingLeftOutMeansNotTruncated()
    {
        Assert.False(ReviewLogDigest.Of(Many(infos: 3), limit: 3).Truncated);
        Assert.False(ReviewLogDigest.Of(Many(infos: 3), limit: 99).Truncated);

        var empty = ReviewLogDigest.Of(Array.Empty<ReviewLogEntry>(), limit: 0);
        Assert.False(empty.Truncated);
        Assert.Equal(0, empty.Total);
    }

    [Fact]
    public void AnEmptyOrMissingLogIsADigestOfNothing()
    {
        foreach (var digest in new[] { ReviewLogDigest.Of(null), ReviewLogDigest.Of(new ReviewLogEntry?[] { null, null }) })
        {
            Assert.Empty(digest.Entries);
            Assert.Equal(0, digest.Total);
            Assert.Equal(0, digest.Errors);
            Assert.Equal(0, digest.Warnings);
            Assert.False(digest.Truncated);
        }
    }

    [Fact]
    public void TheDefaultLimitIsSmallEnoughForAnAgentToRead()
    {
        Assert.Equal(20, ReviewLogDigest.DefaultLimit);
        Assert.Equal(20, ReviewLogDigest.Of(Many(infos: 101)).Entries.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewLogDigest.Of(Many(infos: 1), limit: -1));
    }
}
