using System;

namespace BuildingRegulationReview.Application.WriteBack;

/// <summary>
/// Something the tool decided not to do and is handing to the user instead (spec 10.5 item 3:
/// 若 API 版本不支援完整編輯，套用預先配置方案並列出需人工處理項).
/// </summary>
/// <remarks>
/// A manual item is not a failure. It is the honest answer in the two cases that keep coming up in
/// the Color Scheme: Revit will not let the tool do something from the API, or the drafts are
/// ambiguous and picking one reading would be a guess. Either way the run carries on and the user
/// is told what is left, in terms of what to click — an item that only says "不支援" is a dead end.
/// </remarks>
public sealed class ManualAction
{
    public ManualAction(string subject, string reason, string suggestion)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A manual item needs a subject.", nameof(subject));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A manual item needs a reason.", nameof(reason));
        if (string.IsNullOrWhiteSpace(suggestion)) throw new ArgumentException("A manual item needs a suggestion.", nameof(suggestion));

        Subject = subject.Trim();
        Reason = reason.Trim();
        Suggestion = suggestion.Trim();
    }

    /// <summary>What it is about: a colour entry, the scheme itself, the Drafting View.</summary>
    public string Subject { get; }

    /// <summary>Why the tool did not do it.</summary>
    public string Reason { get; }

    /// <summary>What the user can do about it, in Revit.</summary>
    public string Suggestion { get; }

    public string Text => "需人工處理：" + Subject + "——" + Reason + "。建議：" + Suggestion;

    public override string ToString() => Text;
}
