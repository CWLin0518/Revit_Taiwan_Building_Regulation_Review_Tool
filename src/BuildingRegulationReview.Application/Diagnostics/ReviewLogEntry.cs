using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BuildingRegulationReview.Application.Diagnostics;

/// <summary>
/// One line of the review log (spec 14). It carries everything that clause requires: 錯誤碼、階段、
/// Package ID、元素 UniqueId、使用者訊息、技術細節、處理建議與時間.
/// </summary>
/// <remarks>
/// The split between <see cref="UserText"/> and <see cref="ToLogLine"/> is the other half of spec
/// 14: the UI shows a message the user can act on, and the technical detail goes only to the local
/// log. The detail is redacted on the way in, because a Revit exception routinely quotes the path of
/// the document it came from and that clause forbids keeping 機密路徑.
/// </remarks>
public sealed class ReviewLogEntry
{
    private const string PathHidden = "（已隱藏路徑）";

    private static readonly Regex WindowsPath = new Regex(
        @"(?:[A-Za-z]:[\\/]|\\\\[^\\/\s]+[\\/])[^\s""'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PosixPath = new Regex(
        @"(?:file://)?/(?:home|Users|root|mnt|media|srv|opt|var|etc|tmp)/[^\s""'<>|]*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ReviewLogEntry(
        string code,
        ReviewStage stage,
        ReviewSeverity severity,
        Guid packageId,
        string userMessage,
        string? elementUniqueId = null,
        string? technicalDetail = null,
        string? suggestion = null,
        DateTime? timestampUtc = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A log entry needs an error code.", nameof(code));
        if (!Enum.IsDefined(typeof(ReviewStage), stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        if (!Enum.IsDefined(typeof(ReviewSeverity), severity)) throw new ArgumentOutOfRangeException(nameof(severity));
        if (string.IsNullOrWhiteSpace(userMessage)) throw new ArgumentException("A log entry needs a user message.", nameof(userMessage));

        Code = code.Trim();
        Stage = stage;
        Severity = severity;
        PackageId = packageId;
        UserMessage = userMessage.Trim();
        ElementUniqueId = Clean(elementUniqueId);
        TechnicalDetail = Redact(Clean(technicalDetail));
        Suggestion = Clean(suggestion);
        TimestampUtc = (timestampUtc ?? DateTime.UtcNow).ToUniversalTime();
    }

    public string Code { get; }
    public ReviewStage Stage { get; }
    public ReviewSeverity Severity { get; }
    public Guid PackageId { get; }

    /// <summary>The element the entry is about; null for an entry about the run as a whole.</summary>
    public string? ElementUniqueId { get; }

    /// <summary>What the UI shows.</summary>
    public string UserMessage { get; }

    /// <summary>The API message or stack, with any file path redacted. Local log only.</summary>
    public string? TechnicalDetail { get; }

    /// <summary>What the user can do about it, in Revit.</summary>
    public string? Suggestion { get; }

    public DateTime TimestampUtc { get; }

    public bool IsError => Severity == ReviewSeverity.Error;

    /// <summary>The line for the screen: no technical detail, nothing the user cannot act on.</summary>
    public string UserText => Suggestion is null
        ? UserMessage
        : UserMessage + " 建議：" + Suggestion;

    /// <summary>The line for the local log file, with every field spec 14 asks for.</summary>
    public string ToLogLine()
    {
        var line = new StringBuilder();
        line.Append(TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("Z\t");
        line.Append(SeverityText(Severity)).Append('\t');
        line.Append(Code).Append('\t');
        line.Append(StageText(Stage)).Append('\t');
        line.Append(PackageId.ToString("D", CultureInfo.InvariantCulture)).Append('\t');
        line.Append(ElementUniqueId ?? "-").Append('\t');
        line.Append(UserMessage);
        if (Suggestion is not null) line.Append("\t建議：").Append(Suggestion);
        if (TechnicalDetail is not null) line.Append("\t技術細節：").Append(TechnicalDetail);
        return line.ToString();
    }

    public static string StageText(ReviewStage stage) => stage switch
    {
        ReviewStage.Extraction => "幾何擷取",
        ReviewStage.Repair => "線網修復",
        ReviewStage.Solving => "區劃求解",
        ReviewStage.Editing => "區劃編輯",
        ReviewStage.Preview => "套用前預覽",
        ReviewStage.WriteBack => "寫回模型",
        ReviewStage.Status => "狀態更新",
        ReviewStage.Review => "開始檢討",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    public static string SeverityText(ReviewSeverity severity) => severity switch
    {
        ReviewSeverity.Info => "資訊",
        ReviewSeverity.Warning => "警告",
        ReviewSeverity.Error => "錯誤",
        _ => throw new ArgumentOutOfRangeException(nameof(severity))
    };

    /// <summary>
    /// Replaces any filesystem path with just its last segment (spec 14: 不得在錯誤紀錄中保存機密
    /// 路徑或憑證). The file name is what identifies the document to the user; the folders above it
    /// are what would leak a client name or a network share.
    /// </summary>
    internal static string? Redact(string? detail)
    {
        if (detail is null) return null;

        var redacted = WindowsPath.Replace(detail, match => LastSegment(match.Value));
        redacted = PosixPath.Replace(redacted, match => LastSegment(match.Value));
        return string.IsNullOrWhiteSpace(redacted) ? null : redacted;
    }

    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var name = index < 0 ? string.Empty : trimmed.Substring(index + 1);
        return string.IsNullOrWhiteSpace(name) ? PathHidden : PathHidden + name;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    public override string ToString() => ToLogLine();
}
