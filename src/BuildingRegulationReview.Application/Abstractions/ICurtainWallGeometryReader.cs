using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Common;

namespace BuildingRegulationReview.Application.Abstractions;

/// <summary>
/// What one 帷幕牆區劃交接 read covers: a package's Area Plan, plus the ratings the rules require of
/// the compartment walls and floors it will find there.
/// </summary>
/// <remarks>
/// The hosts come from outside because the reader does not judge: whether a wall is a 區劃牆 at all,
/// and whether it is there by 第79條 or by 第83條, is settled by candidate resolution and the rule
/// engine, not by reading a parameter (docs §2.5, §3.1). The walls and floors named here — in either
/// dictionary — are exactly the ones read as compartment boundaries. One named without a required
/// rating is still read; the resolver then supplies no continuous run rather than counting panels
/// against a threshold nobody set.
/// <para>
/// 第三種身分是「可能是區劃牆」（<see cref="BoundaryDoubts"/>，驗證清單 C-19 第二層）：候選解析說不出
/// 這道牆是否構成區劃邊界——跨越邊界、邊界偏離中心線、沿輪廓。它同樣被讀成區劃牆，但帶著那筆疑義，
/// 它與帷幕牆的交接處因此一律交人工覆核，而不是像確定的區劃牆一樣判符合／未符合，也不是像從前一樣
/// 整個不讀、一列都沒有。
/// </para>
/// </remarks>
public sealed class CurtainWallReadRequest
{
    public CurtainWallReadRequest(
        Guid packageId,
        string areaPlanUniqueId,
        IReadOnlyDictionary<string, double>? requiredFireRatingMinutes = null,
        IReadOnlyDictionary<string, string>? legalReferences = null,
        FireRatingUnit bareNumberUnit = FireRatingUnit.Minute,
        CurtainWallJunctionOptions? options = null,
        IReadOnlyDictionary<string, string>? boundaryDoubts = null)
    {
        if (packageId == Guid.Empty) throw new ArgumentException("Package ID cannot be empty.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(areaPlanUniqueId)) throw new ArgumentException("Area Plan UniqueId is required.", nameof(areaPlanUniqueId));

        PackageId = packageId;
        AreaPlanUniqueId = areaPlanUniqueId.Trim();
        BareNumberUnit = bareNumberUnit;
        Options = options ?? CurtainWallJunctionOptions.Default;

        var ratings = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in requiredFireRatingMinutes ?? new Dictionary<string, double>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            CompartmentWallObservation.RequireRating(pair.Value, nameof(requiredFireRatingMinutes));
            ratings[pair.Key.Trim()] = pair.Value;
        }

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in legalReferences ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
            references[pair.Key.Trim()] = pair.Value.Trim();
        }

        var doubts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in boundaryDoubts ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
            doubts[pair.Key.Trim()] = pair.Value.Trim();
        }

        RequiredFireRatingMinutes = new ReadOnlyDictionary<string, double>(ratings);
        LegalReferences = new ReadOnlyDictionary<string, string>(references);
        BoundaryDoubts = new ReadOnlyDictionary<string, string>(doubts);
    }

    public Guid PackageId { get; }
    public string AreaPlanUniqueId { get; }

    /// <summary>Required rating per host UniqueId; a host that is absent is read without one.</summary>
    public IReadOnlyDictionary<string, double> RequiredFireRatingMinutes { get; }

    /// <summary>
    /// 第79條 or 第83條 per compartment wall UniqueId. A wall that is absent is read as 第79條, the
    /// general case; 第83條 has to be said explicitly, because the evidence must let a reviewer tell
    /// the two apart (docs §2.5).
    /// </summary>
    public IReadOnlyDictionary<string, string> LegalReferences { get; }

    /// <summary>
    /// 可能是區劃牆的牆（UniqueId → 疑義種類，給審查者看的字）。讀取器照樣把它們讀成區劃牆——
    /// 條文仍由 <see cref="LegalReferences"/> 給——但帶上這筆疑義（驗證清單 C-19 第二層）。
    /// 不在這裡的區劃牆就是確定的區劃牆。
    /// </summary>
    public IReadOnlyDictionary<string, string> BoundaryDoubts { get; }

    /// <summary>Every wall and floor to read as a compartment boundary, doubtful ones included, each once.</summary>
    public IEnumerable<string> Hosts =>
        RequiredFireRatingMinutes.Keys.Concat(LegalReferences.Keys).Concat(BoundaryDoubts.Keys)
            .Distinct(StringComparer.Ordinal);

    public string? BoundaryDoubtOf(string? wallUniqueId) =>
        wallUniqueId is not null && BoundaryDoubts.TryGetValue(wallUniqueId, out var doubt) ? doubt : null;

    /// <summary>What a bare number in 防火檢討_設計防火時效 means, as the project declared it.</summary>
    public FireRatingUnit BareNumberUnit { get; }

    /// <summary>
    /// The same constants the resolver measures with (docs §4.4). The reader needs them to know how
    /// far beyond the storey a band can still reach, so a spandrel is never cut short by where the
    /// read stopped rather than by the façade.
    /// </summary>
    public CurtainWallJunctionOptions Options { get; }

    public double? RequiredRatingOf(string? hostUniqueId) =>
        hostUniqueId is not null && RequiredFireRatingMinutes.TryGetValue(hostUniqueId, out var minutes)
            ? minutes
            : (double?)null;

    public string LegalReferenceOf(string? compartmentWallUniqueId) =>
        compartmentWallUniqueId is not null && LegalReferences.TryGetValue(compartmentWallUniqueId, out var reference)
            ? reference
            : CurtainWallJunctionReferences.Article79;
}

/// <summary>
/// Reads the curtain walls, panels, grid lines, 區劃牆 and 區劃樓地板 of one package's storey
/// (docs §4.1–§4.3). Read-only by contract: it opens no transaction and must never change the model.
/// </summary>
/// <remarks>
/// It only converts — every measurement and every 人工覆核 is decided by
/// <see cref="CurtainWallJunctionResolver"/>, which the core tests cover without Revit. That split is
/// why this returns observations rather than <see cref="CurtainWallJunction"/>s.
/// </remarks>
public interface ICurtainWallGeometryReader
{
    Result<CurtainWallObservationSet> Read(CurtainWallReadRequest request);
}
