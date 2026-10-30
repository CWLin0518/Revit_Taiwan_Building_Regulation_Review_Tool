using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Domain.Geometry;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Application.Reviews;

/// <summary>
/// The project-wide settings a run was computed under that the candidate set does not carry (spec
/// 13.1: 來源視圖／Level、Area Scheme、專案 Phase／Design Option、幾何容差). The adapter supplies them
/// by name; any change to any of them invalidates every result of the run.
/// </summary>
public sealed class ReviewEnvironment
{
    public const string SourceViewUniqueId = "sourceView";
    public const string LevelUniqueId = "level";
    public const string AreaSchemeUniqueId = "areaScheme";
    public const string Phase = "phase";
    public const string DesignOption = "designOption";
    public const string GeometryTolerance = "geometryTolerance";
    public const string ProjectUnits = "projectUnits";

    public static readonly ReviewEnvironment Empty = new ReviewEnvironment(null);

    private static readonly IReadOnlyDictionary<string, string> Labels = new ReadOnlyDictionary<string, string>(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { SourceViewUniqueId, "來源視圖" },
            { LevelUniqueId, "樓層" },
            { AreaSchemeUniqueId, "Area Scheme" },
            { Phase, "專案 Phase" },
            { DesignOption, "Design Option" },
            { GeometryTolerance, "幾何容差" },
            { ProjectUnits, "專案單位" }
        });

    public ReviewEnvironment(IEnumerable<KeyValuePair<string, string?>>? facts)
    {
        var list = (facts ?? Array.Empty<KeyValuePair<string, string?>>()).ToList();
        if (list.Any(x => string.IsNullOrWhiteSpace(x.Key)))
            throw new ArgumentException("An environment fact needs a name.", nameof(facts));
        var duplicate = list.GroupBy(x => x.Key.Trim(), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Environment fact '{duplicate.Key}' is given more than once.", nameof(facts));

        // An absent value is still a value: a Design Option that was "none" and became "Option 2" is a change.
        Facts = new ReadOnlyDictionary<string, string>(list.ToDictionary(
            x => x.Key.Trim(), x => x.Value?.Trim() ?? string.Empty, StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, string> Facts { get; }

    public static ReviewEnvironment Of(params (string Name, string? Value)[] facts) =>
        new ReviewEnvironment(facts.Select(x => new KeyValuePair<string, string?>(x.Name, x.Value)));

    /// <summary>The Chinese name of a fact for messages; unknown names are shown as they are.</summary>
    public static string Label(string name) => Labels.TryGetValue(name, out var label) ? label : name;
}

/// <summary>Keys under which a baseline stores its subjects.</summary>
public static class ReviewBaselineKeys
{
    private const string ZonePrefix = "zone:";

    /// <summary>A zone is keyed by its Zone ID, prefixed so it can never collide with a Revit UniqueId.</summary>
    public static string Zone(string zoneId) => ZonePrefix + zoneId;

    public static string Zone(Guid zoneId) => Zone(zoneId.ToString("D"));

    /// <summary>The keys a result depends on: each subject it names and the zone it belongs to.</summary>
    public static IReadOnlyList<string> Of(ReviewResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        var keys = result.SubjectUniqueIds.ToList();
        if (result.ZoneId is not null) keys.Add(Zone(result.ZoneId));
        return new ReadOnlyCollection<string>(keys.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    public static bool IsZone(string key) => key.StartsWith(ZonePrefix, StringComparison.Ordinal);
}

/// <summary>
/// Turns what a run read — candidate set, check inputs, environment — into the evidence baseline the
/// run is stored with (spec 13.1). Every subject's fingerprint covers exactly what could change its
/// verdict: its geometry, Type, relation to each zone and every design value read for
/// it — 設計防火時效, 設計防火保護 and 遮煙性能 alike; a zone's
/// covers its Areas, their outlines and the zone inputs. Everything shared by all results goes into
/// the context fingerprint.
/// </summary>
/// <remarks>
/// Lengths and areas are rounded to a micrometre (as <see cref="CandidateSet.Signature"/> does), so
/// reading the same model twice produces the same baseline and only a real edit moves it. The rule
/// set version is deliberately not part of the context: <see cref="ReviewRunValidity"/> compares it
/// on its own, so the user is told "規則版本更新" rather than "環境變更".
/// </remarks>
public static class ReviewBaselineBuilder
{
    /// <summary>
    /// The baseline of one run's inputs. Every caller — the run itself and the pre-scan that judges a
    /// stored run against the model — must build it from the same assembly, or the two fingerprints
    /// differ for no reason and every result reads 需更新.
    /// </summary>
    public static ReviewBaseline Build(CandidateSet set, ReviewEnvironment? environment, ReviewInputAssembly inputs)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));
        return Build(set, environment, inputs.Area, inputs.Rating, inputs.Protection, inputs.PanelRatings,
            inputs.VerticalCompartment);
    }

    public static ReviewBaseline Build(
        CandidateSet set,
        ReviewEnvironment? environment = null,
        CompartmentAreaInputs? areaInputs = null,
        FireResistanceInputs? ratingInputs = null,
        OpeningProtectionInputs? protectionInputs = null,
        IEnumerable<TypeFireRating>? curtainPanelRatings = null,
        VerticalCompartmentInputs? verticalCompartmentInputs = null)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        var env = environment ?? ReviewEnvironment.Empty;
        var panelRatings = curtainPanelRatings?.ToDictionary(x => x.TypeUniqueId, StringComparer.Ordinal);
        var contexts = new (string Name, CompartmentAreaInputs? Inputs)[]
        {
            ("area", areaInputs),
            ("rating", ratingInputs?.Context),
            ("protection", protectionInputs?.Context),
            ("shaft", verticalCompartmentInputs?.Context)
        };

        var context = new StringBuilder();
        context.Append("package|").Append(set.PackageId.ToString("D")).Append('|').Append(set.LevelUniqueId).Append('\n');
        foreach (var fact in env.Facts.OrderBy(x => x.Key, StringComparer.Ordinal))
            context.Append("env|").Append(fact.Key).Append('=').Append(fact.Value).Append('\n');
        foreach (var (name, inputs) in contexts)
            AppendInputs(context, name + ".building", inputs?.Building);

        var subjects = new SortedDictionary<string, StringBuilder>(StringComparer.Ordinal);
        StringBuilder Subject(string key)
        {
            if (!subjects.TryGetValue(key, out var text)) subjects[key] = text = new StringBuilder();
            return text;
        }

        foreach (var zone in set.Zones)
        {
            var text = Subject(ReviewBaselineKeys.Zone(zone.ZoneId));
            text.Append("zone|").Append(zone.Name).Append("|problems=").Append(string.Join(",", zone.Problems)).Append('\n');
            foreach (var part in zone.Parts)
            {
                var line = PartLine(part);
                text.Append(line);
                Subject(part.AreaUniqueId).Append(line);
            }

            foreach (var (name, inputs) in contexts)
                AppendInputs(text, name + ".zone", inputs?.ForZone(zone.ZoneId));
        }

        foreach (var member in set.Members)
        {
            var o = member.Observation;
            var text = Subject(o.Source.ElementUniqueId);
            text.Append("member|").Append(o.Category).Append('|').Append(o.Source)
                .Append("|type=").Append(o.TypeUniqueId ?? "-").Append('|').Append(o.TypeName ?? "-")
                .Append("|w=").Append(Length(o.WidthFeet))
                .Append("|structural=").Append(o.IsStructural?.ToString() ?? "-")
                .Append("|curtain=").Append(o.IsCurtainWall)
                // 室內外判定改變就換了一套適用規則，所以它進基準：既有的人工覆寫必須重新確認，
                // 不能讓一個針對外牆下的結論留在一道已改判為室內的牆上（帷幕牆規格 §4.8）。
                // 連同宣告與理由一起記：Function 改了、或區劃補建了，都是判斷依據的變動。
                .Append("|exposure=").Append(member.CurtainWallExposureText)
                .Append('|').Append(member.CurtainWallExposure?.Reason.ToString() ?? "-")
                .Append('|').Append(o.CurtainWallFunction).Append('\n');
            AppendPoints(text, "centerline", o.Centerline);
            foreach (var ring in o.Outlines) AppendPoints(text, "outline", ring);
            AppendRelations(text, member.Relations);
            if (ratingInputs is not null) text.Append("rating|").Append(Rating(ratingInputs.ForType(o.TypeUniqueId))).Append('\n');
        }

        foreach (var opening in set.Openings)
        {
            var o = opening.Observation;
            var text = Subject(o.Source.ElementUniqueId);
            text.Append("opening|").Append(o.Category).Append('|').Append(o.Source)
                .Append("|host=").Append(o.HostUniqueId ?? "-")
                .Append("|hostResolved=").Append(opening.HasResolvedHost)
                .Append("|hostCurtain=").Append(opening.Host?.IsCurtainWall == true)
                // Host 的 Function 與這片嵌板的宣告種類都進基準：兩者都會改變這個開口走哪一條規則。
                .Append("|hostFunction=").Append(opening.Host?.CurtainWallFunction.ToString() ?? "-")
                .Append("|panelKind=").Append(o.PanelKind?.ToString() ?? "-")
                .Append("|type=").Append(o.TypeUniqueId ?? "-").Append('|').Append(o.TypeName ?? "-")
                .Append("|at=").Append(Point(o.Location))
                .Append("|w=").Append(Length(o.WidthFeet)).Append("|h=").Append(Length(o.HeightFeet)).Append('\n');
            AppendRelations(text, opening.Relations);
            if (protectionInputs is not null)
            {
                text.Append("protection|").Append(Protection(protectionInputs.For(o))).Append('\n');

                // 第79條第1項之阻熱性 (決議 38): ticking it must make a stored run 需更新 like 防火保護 does.
                var insulation = protectionInputs.InsulationFor(o);
                text.Append("insulation|").Append(insulation.Kind).Append('|').Append(insulation.RawText ?? "-")
                    .Append('|').Append(insulation.Reason ?? "-").Append('\n');
            }

            // 第79條之2 reads 遮煙性能 on every opening Type and 設計防火時效 on a 門's, neither of which
            // any other input covers — a 維修門 is not a member, so <c>rating|</c> above never sees it
            // (垂直區劃規格 §9 第9項). Without this line, editing 防火檢討_遮煙性能 would leave the stored
            // run reading 有效 (spec 13.1).
            if (verticalCompartmentInputs is not null)
                text.Append("shaftDevice|").Append(Device(verticalCompartmentInputs.ForType(o.TypeUniqueId))).Append('\n');

            // A 帷幕嵌板's 設計防火時效 decides the 90 cm 交接帶 (帷幕牆規格 §4), so a panel whose Type
            // rating changed is a changed subject and the junction results that named it go stale.
            if (panelRatings is not null && o.Category == CandidateCategory.CurtainPanel)
            {
                text.Append("panelRating|")
                    .Append(o.TypeUniqueId is not null && panelRatings.TryGetValue(o.TypeUniqueId, out var panel) ? Rating(panel) : "-")
                    .Append('\n');
            }
        }

        foreach (var ambiguity in set.Ambiguities)
        {
            var line = "ambiguity|" + ambiguity.Kind + "|" + (ambiguity.ZoneId?.ToString("D") ?? "-") + "\n";
            if (ambiguity.SubjectUniqueIds.Count == 0) context.Append(line);
            foreach (var subject in ambiguity.SubjectUniqueIds) Subject(subject).Append(line);
        }

        return new ReviewBaseline(
            Digest(context.ToString()),
            subjects.Select(x => new KeyValuePair<string, string>(x.Key, Digest(x.Value.ToString()))));
    }

    /// <summary>
    /// One digest of everything a result depended on: the run's context and the fingerprint of each
    /// key the result names. Two results with the same digest were computed from the same evidence.
    /// Null when the baseline recorded nothing.
    /// </summary>
    public static string? DependencyFingerprint(ReviewBaseline baseline, ReviewResult result)
    {
        if (baseline is null) throw new ArgumentNullException(nameof(baseline));
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (!baseline.IsRecorded) return null;

        var text = new StringBuilder();
        text.Append("context|").Append(baseline.ContextFingerprint).Append('\n');
        foreach (var key in ReviewBaselineKeys.Of(result))
            text.Append(key).Append('=').Append(baseline.FingerprintOf(key) ?? "-").Append('\n');
        return Digest(text.ToString());
    }

    internal static string Digest(string text)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        var hex = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }

    private static string PartLine(CandidateZonePart part)
    {
        var text = new StringBuilder();
        text.Append("area|").Append(part.AreaUniqueId)
            .Append("|revit=").Append(Number(part.RevitAreaSquareMeters))
            .Append("|at=").Append(Point(part.Observation.Placement)).Append('\n');
        foreach (var loop in part.Observation.BoundaryLoops) AppendPoints(text, "loop", loop);
        return text.ToString();
    }

    private static void AppendRelations(StringBuilder text, IEnumerable<ZoneRelation> relations)
    {
        foreach (var relation in relations)
        {
            text.Append("relation|").Append(relation.ZoneId.ToString("D")).Append('|').Append(relation.Kind)
                .Append('|').Append(relation.Ambiguity?.ToString() ?? "-");
            foreach (var (field, feet) in relation.Measurements.Present())
                text.Append('|').Append(field).Append('=').Append(Length(feet));
            text.Append('\n');
        }
    }

    private static void AppendInputs(StringBuilder text, string scope, IEnumerable<ReviewInput>? inputs)
    {
        foreach (var input in (inputs ?? Array.Empty<ReviewInput>()).OrderBy(x => x.Field, StringComparer.Ordinal))
        {
            text.Append("input|").Append(scope).Append('|').Append(input.Field).Append('=')
                .Append(input.IsUnreadable ? "unreadable:" + input.UnreadableReason : Value(input.Value!))
                .Append("|source=").Append(input.Source ?? "-").Append('\n');
        }
    }

    private static void AppendPoints(StringBuilder text, string name, IEnumerable<Point2D> points)
    {
        text.Append(name).Append('|').Append(string.Join(";", points.Select(p => Point(p)))).Append('\n');
    }

    private static string Rating(TypeFireRating? rating) => rating is null
        ? "-"
        : $"{rating.Rating.Kind}|{Number(rating.Rating.Minutes)}|{rating.Rating.RawText ?? "-"}|{rating.Rating.Reason ?? "-"}|{rating.Source}";

    private static string Protection(OpeningFireProtection? value) => value is null
        ? "-"
        : $"{value.Scope}|{value.UniqueId}|{value.Protection.Kind}|{value.Protection.RawText ?? "-"}|{value.Protection.Reason ?? "-"}|{value.Source}";

    /// <summary>What a 防火設備 Type declared for 第79條之2: 遮煙性能 and — for a 門 — 設計防火時效.</summary>
    private static string Device(ShaftDeviceProperties? value) => value is null
        ? "-"
        : $"{value.SmokeProtection.Kind}|{value.SmokeProtection.RawText ?? "-"}|{value.SmokeProtection.Reason ?? "-"}|" +
          $"{value.FireRating.Kind}|{Number(value.FireRating.Minutes)}|{value.FireRating.RawText ?? "-"}|{value.FireRating.Reason ?? "-"}";

    private static string Value(ReviewValue value) => value.Kind switch
    {
        ReviewValueKind.Quantity => "q:" + Number(value.Number) + value.Unit,
        ReviewValueKind.Text => "t:" + value.Text,
        _ => "b:" + value.Flag
    };

    private static string Point(Point2D? point) =>
        point is Point2D p ? Length(p.X) + "," + Length(p.Y) : "-";

    private static string Length(double? feet) => Number(feet is double f ? PlanUnits.FeetToMeters(f) : (double?)null);

    private static string Number(double? value) =>
        value is double v ? Math.Round(v, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture) : "-";
}
