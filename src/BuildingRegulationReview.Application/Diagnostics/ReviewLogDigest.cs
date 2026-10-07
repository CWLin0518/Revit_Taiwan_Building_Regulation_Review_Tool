using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace BuildingRegulationReview.Application.Diagnostics;

/// <summary>
/// 一份日誌的摘要：統計照全部算，列出的只有最重要的前幾筆。
/// </summary>
/// <remarks>
/// 一次檢討的日誌可以長到幾萬個字（每一筆 finding 都有一列），整包交給 MCP 的呼叫端會超過它一次
/// 能讀的上限，連「有幾個錯誤」都看不到（B-04）。所以取樣的規則寫在這裡、也只寫在這裡：
/// <list type="bullet">
/// <item><see cref="Errors"/>、<see cref="Warnings"/>、<see cref="Total"/> 永遠是全部的實情，與 limit 無關。</item>
/// <item>排序依嚴重度遞減且穩定（同嚴重度維持原順序），所以被留下的是最該看的那幾筆。</item>
/// <item><c>limit</c> 為 0 時一筆都不列，但只要原本有東西，<see cref="Truncated"/> 就是 true。</item>
/// </list>
/// 這是一個值，不是序列化：MCP 的 JSON 與以後任何輸出都讀同一份摘要，才不會各自切一套。
/// </remarks>
public sealed class ReviewLogDigest
{
    /// <summary>預設只列前 20 筆：夠代理看出問題在哪，又不會把一次回傳撐爆（B-04）。</summary>
    public const int DefaultLimit = 20;

    private ReviewLogDigest(IReadOnlyList<ReviewLogEntry> entries, int total, int errors, int warnings, bool truncated)
    {
        Entries = entries;
        Total = total;
        Errors = errors;
        Warnings = warnings;
        Truncated = truncated;
    }

    /// <summary>The entries worth listing: severity first, at most the limit asked for.</summary>
    public IReadOnlyList<ReviewLogEntry> Entries { get; }

    /// <summary>How many entries there are in all — never only the listed ones.</summary>
    public int Total { get; }

    public int Errors { get; }

    public int Warnings { get; }

    /// <summary>Whether anything was left out, so a reader knows the list is a sample.</summary>
    public bool Truncated { get; }

    public static ReviewLogDigest Of(IEnumerable<ReviewLogEntry?>? entries, int limit = DefaultLimit)
    {
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), limit, "A log limit cannot be negative.");

        var all = (entries ?? Array.Empty<ReviewLogEntry?>()).Where(e => e != null).Select(e => e!).ToList();
        var listed = all
            .OrderByDescending(e => e.Severity)
            .Take(limit)
            .ToList();

        return new ReviewLogDigest(
            new ReadOnlyCollection<ReviewLogEntry>(listed),
            all.Count,
            all.Count(e => e.Severity == ReviewSeverity.Error),
            all.Count(e => e.Severity == ReviewSeverity.Warning),
            all.Count > listed.Count);
    }
}
