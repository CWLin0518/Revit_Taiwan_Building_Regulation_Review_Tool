using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// A rule's 依據條文 read back the way a reader of the law would want it: which 法規, which 條文,
/// which 函釋, what the clause is being used for, and whether that reading is still 暫定.
/// </summary>
/// <remarks>
/// The rule set stores one sentence per rule — for example
/// <c>建築技術規則建築設計施工編第83條第1款至第4款；內政部…函第4點（依第79條之2第3項…，暫定）</c> —
/// and that sentence mixes the citation with notes the tool's own specifications left behind
/// (「暫定」, 「解釋選擇見帷幕牆規格決議 16」). Here it is taken apart again: the citation and the
/// clause's gist go to the reader, 暫定 becomes a sentence a reader understands, and the notes that
/// point into the tool's own documents are handed back separately for the 規則來源 section.
/// </remarks>
public sealed class ReviewLegalReference
{
    /// <summary>The 法規 every rule of this tool cites, written once instead of before every 條文.</summary>
    public const string BuildingCode = "建築技術規則建築設計施工編";

    /// <summary>What a reader is told when the rule's reading of the law is not settled yet.</summary>
    public const string ProvisionalNote = "本項法規適用方式為暫定解讀，判定結果請由建築師或審查單位再確認。";

    private static readonly Regex Letter = new Regex(
        @"^(?<issuer>.*?)(?<date>\d+年\d+月\d+日)(?<number>.+?號函)(?<point>.*)$",
        RegexOptions.CultureInvariant);

    /// <summary>Notes that point into the tool's own documents rather than into the law.</summary>
    private static readonly string[] InternalMarkers = { "決議", "規格", "spec", "docs", "§" };

    private ReviewLegalReference(
        string? code, IList<string> clauses, IList<string> letters, IList<string> remarks,
        IList<string> gists, bool provisional, IList<string> internalNotes)
    {
        Code = code;
        Clauses = new ReadOnlyCollection<string>(clauses);
        Letters = new ReadOnlyCollection<string>(letters);
        Remarks = new ReadOnlyCollection<string>(remarks);
        Gists = new ReadOnlyCollection<string>(gists);
        IsProvisional = provisional;
        InternalNotes = new ReadOnlyCollection<string>(internalNotes);
    }

    /// <summary>The 法規 the clauses belong to, or null when none was named.</summary>
    public string? Code { get; }

    /// <summary>The 條文 within <see cref="Code"/>, e.g. 「第79條第1項」.</summary>
    public IReadOnlyList<string> Clauses { get; }

    /// <summary>The 函釋 cited beside the clauses.</summary>
    public IReadOnlyList<string> Letters { get; }

    /// <summary>Whatever part of the sentence is neither a clause nor a 函釋.</summary>
    public IReadOnlyList<string> Remarks { get; }

    /// <summary>What the clause is cited for — the words that stood in brackets.</summary>
    public IReadOnlyList<string> Gists { get; }

    /// <summary>Whether the rule marked its reading of the law 暫定.</summary>
    public bool IsProvisional { get; }

    /// <summary>Notes that point into the tool's own documents; they belong with the rule, not the law.</summary>
    public IReadOnlyList<string> InternalNotes { get; }

    public bool IsEmpty => Clauses.Count == 0 && Letters.Count == 0 && Remarks.Count == 0 && Gists.Count == 0;

    public static ReviewLegalReference Parse(string? text)
    {
        string? code = null;
        var clauses = new List<string>();
        var letters = new List<string>();
        var remarks = new List<string>();
        var gists = new List<string>();
        var internalNotes = new List<string>();
        var provisional = false;

        foreach (var segment in SplitTopLevel(text ?? string.Empty, '；'))
        {
            var (body, note) = TrailingNote(segment);

            foreach (var part in SplitTopLevel(note, '，'))
            {
                var gist = part;
                if (gist == "暫定")
                {
                    provisional = true;
                    continue;
                }
                if (gist.StartsWith("暫定：", StringComparison.Ordinal))
                {
                    provisional = true;
                    gist = gist.Substring(3).Trim();
                }
                if (InternalMarkers.Any(m => gist.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0))
                    internalNotes.Add(gist);
                else if (gist.Length > 0)
                    Append(gists, gist);
            }

            if (body.Length == 0) continue;

            if (body.StartsWith(BuildingCode, StringComparison.Ordinal))
            {
                code = BuildingCode;
                var clause = body.Substring(BuildingCode.Length).TrimStart('：', ' ').Trim();
                if (clause.Length > 0) clauses.Add(clause);
            }
            else if (Letter.Match(body) is { Success: true } letter)
            {
                letters.Add(Join(letter.Groups["issuer"].Value, letter.Groups["date"].Value,
                    letter.Groups["number"].Value, letter.Groups["point"].Value));
            }
            else
            {
                remarks.Add(body);
            }
        }

        // The pieces of one bracket were split at 「，」 to find 暫定 and the internal notes; what is
        // left of one bracket is still one sentence, so it reads as one.
        return new ReviewLegalReference(code, clauses, letters, remarks,
            gists.Count == 0 ? gists : new List<string> { string.Join("，", gists) }, provisional, internalNotes);
    }

    /// <summary>The lines of the 法規依據 section.</summary>
    public IReadOnlyList<ReviewDetailLine> Lines()
    {
        var lines = new List<ReviewDetailLine>();
        if (Code is not null) lines.Add(new ReviewDetailLine("法規", Code));
        if (Clauses.Count > 0) lines.Add(new ReviewDetailLine("條文", string.Join("、", Clauses), emphasis: true));
        foreach (var letter in Letters) lines.Add(new ReviewDetailLine("函釋", letter));
        foreach (var gist in Gists) lines.Add(new ReviewDetailLine("檢討重點", gist));
        foreach (var remark in Remarks) lines.Add(new ReviewDetailLine("補充說明", remark));
        if (IsProvisional) lines.Add(new ReviewDetailLine("備註", ProvisionalNote));
        return lines;
    }

    private static void Append(List<string> list, string value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    private static string Join(params string[] parts) =>
        string.Join(" ", parts.Select(p => p.Trim()).Where(p => p.Length > 0));

    /// <summary>The text before a closing 「（…）」 and what stood inside it.</summary>
    private static (string Body, string Note) TrailingNote(string segment)
    {
        if (!segment.EndsWith("）", StringComparison.Ordinal)) return (segment, string.Empty);

        var depth = 0;
        for (var i = segment.Length - 1; i >= 0; i--)
        {
            if (segment[i] == '）') depth++;
            else if (segment[i] == '（' && --depth == 0)
                return (segment.Substring(0, i).Trim(), segment.Substring(i + 1, segment.Length - i - 2).Trim());
        }
        return (segment, string.Empty);
    }

    /// <summary>The text split at <paramref name="separator"/>, but never inside 「（…）」.</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var depth = 0;
        var current = new StringBuilder();
        foreach (var c in text)
        {
            if (c == '（') depth++;
            else if (c == '）' && depth > 0) depth--;

            if (c == separator && depth == 0)
            {
                if (current.ToString().Trim() is { Length: > 0 } piece) yield return piece;
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.ToString().Trim() is { Length: > 0 } last) yield return last;
    }
}
