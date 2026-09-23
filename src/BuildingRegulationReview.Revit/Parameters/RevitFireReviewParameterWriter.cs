using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Autodesk.Revit.DB;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Revit.Parameters;

/// <summary>Why one edit did not reach the model.</summary>
public sealed class FireReviewWriteFailure
{
    internal FireReviewWriteFailure(FireReviewParameterEdit edit, string typeName, string reason)
    {
        Edit = edit;
        TypeName = typeName;
        Reason = reason;
    }

    public FireReviewParameterEdit Edit { get; }
    public string TypeName { get; }
    public string Reason { get; }

    public override string ToString() => $"{TypeName}：{Edit.ParameterName} — {Reason}";
}

/// <summary>What one batch write did.</summary>
public sealed class FireReviewWriteResult
{
    internal FireReviewWriteResult(int written, int unchanged, IEnumerable<FireReviewWriteFailure> failures, string? error = null)
    {
        Written = written;
        Unchanged = unchanged;
        Failures = new ReadOnlyCollection<FireReviewWriteFailure>(failures.ToList());
        Error = error;
    }

    /// <summary>Parameters whose value actually changed.</summary>
    public int Written { get; }

    /// <summary>Edits that already held the value asked for.</summary>
    public int Unchanged { get; }

    public IReadOnlyList<FireReviewWriteFailure> Failures { get; }

    /// <summary>Set when the whole transaction was rolled back; nothing was written.</summary>
    public string? Error { get; }

    public bool Committed => Error is null;

    public string Summary => Error is not null
        ? $"未寫入任何參數：{Error}"
        : $"已寫入 {Written} 個參數值" +
          (Unchanged > 0 ? $"，{Unchanged} 個與原值相同未變更" : string.Empty) +
          (Failures.Count > 0 ? $"，{Failures.Count} 個未能寫入" : string.Empty) + "。";
}

/// <summary>
/// Writes the batch panel's edits in one transaction: either every edit lands or none does, so a
/// failure halfway through cannot leave the model half-updated (spec 13.2).
/// </summary>
/// <remarks>
/// An edit addresses one element by UniqueId — a Type for 防火時效／結構材料／防火被覆厚度, an
/// instance for 設計防火保護, which is an instance parameter. A Type edit reaches every instance of
/// that Type in the project; the caller is responsible for having told the user so. The writer only
/// refuses 防火檢討_法規要求防火時效, which belongs to the rules' write-back (spec 11.5 step 4).
/// </remarks>
public sealed class RevitFireReviewParameterWriter
{
    private readonly Document _document;

    public RevitFireReviewParameterWriter(Document document) =>
        _document = document ?? throw new ArgumentNullException(nameof(document));

    public FireReviewWriteResult Apply(IEnumerable<FireReviewParameterEdit> edits, string transactionName = "批次設定防火檢討參數")
    {
        if (edits is null) throw new ArgumentNullException(nameof(edits));

        var pending = edits.Where(e => e is not null).ToList();
        if (pending.Count == 0) return new FireReviewWriteResult(0, 0, Array.Empty<FireReviewWriteFailure>());

        var written = 0;
        var unchanged = 0;
        var failures = new List<FireReviewWriteFailure>();

        using (var transaction = new Transaction(_document, transactionName))
        {
            try
            {
                transaction.Start();

                foreach (var edit in pending)
                {
                    var element = _document.GetElement(edit.ElementUniqueId);
                    if (element is null)
                    {
                        failures.Add(new FireReviewWriteFailure(edit, edit.ElementUniqueId, "在模型中找不到這個元素，可能已被刪除。"));
                        continue;
                    }

                    var parameter = element.LookupParameter(edit.ParameterName);
                    if (parameter is null)
                    {
                        failures.Add(new FireReviewWriteFailure(edit, element.Name,
                            $"這個元素沒有參數 {edit.ParameterName}，請先在「管理 > 專案參數」把它綁定到這個類別與層級。"));
                        continue;
                    }

                    if (parameter.IsReadOnly)
                    {
                        failures.Add(new FireReviewWriteFailure(edit, element.Name, $"參數 {edit.ParameterName} 是唯讀的。"));
                        continue;
                    }

                    switch (Write(parameter, edit))
                    {
                        case WriteOutcome.Written: written++; break;
                        case WriteOutcome.Unchanged: unchanged++; break;
                        default:
                            failures.Add(new FireReviewWriteFailure(edit, element.Name,
                                $"參數 {edit.ParameterName} 的資料型別（{parameter.StorageType}）無法接受這個值。"));
                            break;
                    }
                }

                transaction.Commit();
            }
            catch (Exception exception) when (exception is Autodesk.Revit.Exceptions.ApplicationException ||
                                              exception is InvalidOperationException)
            {
                if (transaction.HasStarted()) transaction.RollBack();
                return new FireReviewWriteResult(0, 0, Array.Empty<FireReviewWriteFailure>(), exception.Message);
            }
        }

        return new FireReviewWriteResult(written, unchanged, failures);
    }

    private enum WriteOutcome { Written, Unchanged, Rejected }

    private static WriteOutcome Write(Parameter parameter, FireReviewParameterEdit edit)
    {
        if (edit.IsLength)
        {
            if (parameter.StorageType != StorageType.Double) return WriteOutcome.Rejected;
            if (edit.LengthMeters is not double meters)
                return parameter.HasValue && parameter.Set(0.0) ? WriteOutcome.Written : WriteOutcome.Unchanged;

            var feet = UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);
            if (parameter.HasValue && Math.Abs(parameter.AsDouble() - feet) < 1e-9) return WriteOutcome.Unchanged;
            return parameter.Set(feet) ? WriteOutcome.Written : WriteOutcome.Rejected;
        }

        var text = edit.Text ?? string.Empty;
        switch (parameter.StorageType)
        {
            case StorageType.String:
                if (string.Equals(parameter.AsString() ?? string.Empty, text, StringComparison.Ordinal)) return WriteOutcome.Unchanged;
                return parameter.Set(text) ? WriteOutcome.Written : WriteOutcome.Rejected;
            case StorageType.Integer:
                if (!int.TryParse(text, out var number)) return WriteOutcome.Rejected;
                if (parameter.HasValue && parameter.AsInteger() == number) return WriteOutcome.Unchanged;
                return parameter.Set(number) ? WriteOutcome.Written : WriteOutcome.Rejected;
            default:
                return WriteOutcome.Rejected;
        }
    }
}
