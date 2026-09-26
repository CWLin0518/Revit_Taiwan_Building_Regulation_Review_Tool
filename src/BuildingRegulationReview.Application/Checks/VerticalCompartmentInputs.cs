using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BuildingRegulationReview.Application.Parameters;

namespace BuildingRegulationReview.Application.Checks;

/// <summary>
/// The parameter a 防火設備's 遮煙性能 is read from (docs/regulations/vertical-compartment.md §6). The
/// review reads it and never writes it. It is a Yes/No parameter on the 門／窗／帷幕嵌板 <em>Type</em>,
/// like <see cref="FireProtectionParameters.Provided"/> — but a separate parameter from it on
/// purpose: 設計防火保護 answers「是不是防火門窗等防火設備」, this one answers「這個防火設備有沒有通過
/// 遮煙試驗（第1條第45款）」, and 第79條之2第1項 asks a 昇降機道's 防火設備 both at once.
/// </summary>
/// <remarks>
/// 遮煙性能 is a test result, so nothing in the model derives it: like 阻熱性 it can only be declared
/// by the designer. An unticked box is 否 — the same reading <see cref="ProvidedFireProtection"/> is
/// given, because Revit shows an unticked and a never-touched checkbox alike.
/// </remarks>
public static class SmokeProtectionParameters
{
    public const string Provided = "防火檢討_遮煙性能";
}

/// <summary>
/// The requirements 第79條之2第1項 adds on top of ordinary 區劃分隔, and the value each one puts in
/// <c>shaft.requirement</c> (docs/regulations/vertical-compartment.md §3).
/// </summary>
/// <remarks>
/// <para>
/// 第1項本文's 「一小時以上防火時效之牆壁、防火門窗等防火設備」 is <em>not</em> here: a 垂直區劃 is a
/// 區劃 like any other, so its boundary walls are already required to reach 60 min by
/// <c>tw-bcr-79-wall-rating</c> and its openings are already required to be 防火設備 by
/// <c>tw-bcr-79-opening</c>, neither of which cares which article created the 區劃. Only what those
/// rules cannot express is a rule here — the same line 第83條第5款's 阻熱性 was left on
/// (docs/regulations/curtain-wall-fire-compartment.md §5.3).
/// </para>
/// <para>
/// One 管道間 維修門 owes two of these at once (時效 and 遮煙性能), and a rule's
/// <c>requiredValue</c> is a single comparison, so the subject of this category is one
/// (設備, 要求) pair rather than one element. That is also what keeps the rules mutually
/// exclusive: every subject matches exactly one of them.
/// </para>
/// </remarks>
public enum VerticalCompartmentRequirement
{
    /// <summary>第1項第2句：昇降機道裝設之防火設備應具有遮煙性能。</summary>
    HoistwaySmokeSeal,

    /// <summary>第1項第3句前段：管道間之維修門應具有一小時以上防火時效。</summary>
    ShaftDoorRating,

    /// <summary>第1項第3句後段：管道間之維修門應具有遮煙性能。</summary>
    ShaftDoorSmokeSeal
}

public static class VerticalCompartmentRequirements
{
    public static IReadOnlyList<VerticalCompartmentRequirement> All { get; } =
        new ReadOnlyCollection<VerticalCompartmentRequirement>(new[]
        {
            VerticalCompartmentRequirement.HoistwaySmokeSeal,
            VerticalCompartmentRequirement.ShaftDoorRating,
            VerticalCompartmentRequirement.ShaftDoorSmokeSeal
        });

    /// <summary>The text the rules compare <c>shaft.requirement</c> against.</summary>
    public static string RuleText(VerticalCompartmentRequirement requirement) => requirement switch
    {
        VerticalCompartmentRequirement.HoistwaySmokeSeal => "HoistwaySmokeSeal",
        VerticalCompartmentRequirement.ShaftDoorRating => "ShaftDoorRating",
        VerticalCompartmentRequirement.ShaftDoorSmokeSeal => "ShaftDoorSmokeSeal",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement))
    };

    /// <summary>The row of the 檢討表 this requirement is counted in.</summary>
    public static string Label(VerticalCompartmentRequirement requirement) => requirement switch
    {
        VerticalCompartmentRequirement.HoistwaySmokeSeal => "昇降機道防火設備遮煙性能",
        VerticalCompartmentRequirement.ShaftDoorRating => "管道間維修門防火時效",
        VerticalCompartmentRequirement.ShaftDoorSmokeSeal => "管道間維修門遮煙性能",
        _ => throw new ArgumentOutOfRangeException(nameof(requirement))
    };

    /// <summary>
    /// The 區劃用途 a requirement belongs to — the <c>zone.use</c> spelling from
    /// <see cref="ZoneUses"/>, so the vocabulary the panel offers is the vocabulary that decides
    /// which requirements a 區劃's 防火設備 are held to.
    /// </summary>
    public static string UseOf(VerticalCompartmentRequirement requirement) => requirement switch
    {
        VerticalCompartmentRequirement.HoistwaySmokeSeal => ZoneUses.ElevatorShaft,
        VerticalCompartmentRequirement.ShaftDoorRating => ZoneUses.Shaft,
        VerticalCompartmentRequirement.ShaftDoorSmokeSeal => ZoneUses.Shaft,
        _ => throw new ArgumentOutOfRangeException(nameof(requirement))
    };

    /// <summary>The requirements the 防火設備 of a 區劃 with this 用途 are held to; empty for the rest.</summary>
    public static IReadOnlyList<VerticalCompartmentRequirement> ForUse(string? use)
    {
        var trimmed = use?.Trim();
        return trimmed is null
            ? Array.Empty<VerticalCompartmentRequirement>()
            : All.Where(x => string.Equals(UseOf(x), trimmed, StringComparison.Ordinal)).ToList();
    }
}
