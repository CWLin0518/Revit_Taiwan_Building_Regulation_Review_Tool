using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingRegulationReview.Application.Candidates;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Application.Rules;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using Xunit;
using static BuildingRegulationReview.Core.Tests.Candidates.CandidateModel;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// 第79條之1 步驟 2, the parameter layer
/// (docs/regulations/article-79-1-area-exemption.md §5.1、§5.2、§6).
///
/// 無法區劃分隔 is a fact only the designer can state — 第79條之1 asks whether the part is one the
/// building's 構造 or 設備 makes impossible to subdivide, which no geometry answers — so it arrives as
/// an Area instance parameter and travels the same road 避難層通達 already travels: whitelisted field,
/// entry in <see cref="ReviewInputSources"/>, generic assembly into the 區劃's inputs. What these
/// tests hold down is everything that road must <em>not</em> pick up on the way: no rule may read it,
/// the pre-review check may not demand it (決議 9), and no <see cref="RuleCategory"/> may appear for
/// it (決議 8) — a category with no rule makes <c>Evaluate</c> answer 「規則集…沒有…規則」, which reads
/// as a broken rule set rather than as the judgement a person owes.
/// </summary>
public sealed class Article79_1ParameterTests
{
    private const string Field = "zone.cannotBeSubdivided";
    private const string Source = "面積：" + ReviewInputSources.CannotBeSubdivided;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    // --- 白名單 -------------------------------------------------------------------------------

    /// <summary>
    /// §5.2: a Boolean open to every category, exactly like <c>zone.linksRefugeFloor</c>. The
    /// categories matter although no rule reads the field: the assembler applies the whole
    /// <c>zone.*</c> set to every subject under review, and a field outside the whitelist throws
    /// there rather than being quietly dropped.
    /// </summary>
    [Fact]
    public void The_declaration_is_a_whitelisted_zone_field_open_to_every_category()
    {
        var definition = RuleFieldCatalog.Default.Find(Field);

        Assert.NotNull(definition);
        Assert.True(definition!.Type.IsBoolean);
        Assert.Equal(Enum.GetValues<RuleCategory>().OrderBy(x => x), definition.AvailableIn.OrderBy(x => x));
        Assert.Contains("第79條之1", definition.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// 決議 9: 非必要參數. <see cref="ReviewInputSources.NeededBy"/> is rule-driven, so leaving the field
    /// out of every rule is what keeps it out of the pre-review check — a project with none of the six
    /// 區劃用途 must still be able to start a review without binding it. Asserted on the shipped rule
    /// set, because that is the one a real review runs.
    /// </summary>
    [Fact]
    public void No_rule_needs_the_declaration()
    {
        var shipped = Shipped();

        Assert.DoesNotContain(ReviewInputSources.FieldsUsedBy(shipped), f => f.Name == Field);
        Assert.DoesNotContain(ReviewInputSources.NeededBy(shipped), s => s.Field == Field);
        Assert.DoesNotContain(ReviewInputSources.NeededBy(shipped),
            s => s.ParameterName == ReviewInputSources.CannotBeSubdivided);

        // The two 挑空 facts are out for the same reason, and this is the company they keep.
        foreach (var other in new[] { "zone.spannedFloors", "zone.linksRefugeFloor" })
            Assert.DoesNotContain(ReviewInputSources.NeededBy(shipped), s => s.Field == other);
    }

    // --- 參數來源 -----------------------------------------------------------------------------

    /// <summary>§6: an instance parameter on Areas, read by the adapter because the field names it.</summary>
    [Fact]
    public void The_declaration_has_a_parameter_of_its_own()
    {
        var source = ReviewInputSources.For(Field);

        Assert.NotNull(source);
        Assert.Equal("防火檢討_無法區劃分隔", source!.ParameterName);
        Assert.Equal(ReviewInputSources.CannotBeSubdivided, source.ParameterName);
        Assert.Equal(ReviewParameterLevel.Instance, source.Level);
        Assert.Equal(new[] { ReviewParameterHost.Areas }, source.Hosts);
        Assert.Contains(ReviewInputSources.CannotBeSubdivided, ReviewInputSources.ParameterNames);
    }

    // --- 讀進區劃輸入 -------------------------------------------------------------------------

    /// <summary>
    /// The generic assembly carries it from the snapshot to the 區劃's inputs with no code of its own,
    /// which is what lets 步驟 3's check simply read it off the zone.
    /// </summary>
    [Fact]
    public void The_declaration_reaches_the_zone_inputs()
    {
        var input = Assemble(("area-a", 1));

        Assert.NotNull(input);
        Assert.Equal(ReviewValue.OfBoolean(true), input!.Value);
        Assert.Equal(Source, input.Source);
    }

    /// <summary>
    /// Revit shows an unticked box and a never-touched box the same way, so a bound-but-unticked
    /// parameter is 否 — which settles 第79條之1 as 不適用 rather than leaving it 資料不足. That is the
    /// strict direction, and it is the same reading <c>防火檢討_避難層通達</c> gets (§9 的已知限制).
    /// </summary>
    [Fact]
    public void An_unticked_box_is_no_rather_than_nothing_stated()
    {
        var input = Assemble(("area-a", 0));

        Assert.NotNull(input);
        Assert.Equal(ReviewValue.OfBoolean(false), input!.Value);
    }

    /// <summary>
    /// Not bound at all is the one case that is 資料不足: nothing is supplied, the field is missing, and
    /// <see cref="Article79_1Exemption"/> answers 缺無法區劃分隔. The tool may not declare on the
    /// designer's behalf that an 觀眾席 cannot be subdivided (§6).
    /// </summary>
    [Fact]
    public void An_unbound_parameter_supplies_nothing()
    {
        Assert.Null(Assemble());
        Assert.True(Article79_1Exemption.For(true, "A-1", Application.Parameters.ZoneUses.Auditorium, null).IsUndecided);
    }

    /// <summary>
    /// One 區劃 is one set of facts. When a 區劃 is drawn as several Areas and they disagree, picking
    /// one would be the tool deciding whether the part can be subdivided, so the input is
    /// <see cref="ReviewInput.Unreadable"/> and the exemption stays undecided — the same path
    /// <c>防火檢討_避難層通達</c> takes.
    /// </summary>
    [Fact]
    public void Areas_of_one_zone_that_disagree_are_unreadable()
    {
        var input = Assemble(("area-a1", 1), ("area-a2", 0));

        Assert.NotNull(input);
        Assert.True(input!.IsUnreadable);
        Assert.Contains("2 個面積填寫不一致", input.UnreadableReason!, StringComparison.Ordinal);
        Assert.Equal(Source, input.Source);
    }

    /// <summary>And when they agree, the several Areas read as the one fact they state.</summary>
    [Fact]
    public void Areas_of_one_zone_that_agree_read_as_one_fact()
    {
        var input = Assemble(("area-a1", 1), ("area-a2", 1));

        Assert.NotNull(input);
        Assert.False(input!.IsUnreadable);
        Assert.Equal(ReviewValue.OfBoolean(true), input.Value);
    }

    // --- 檢討類型 -----------------------------------------------------------------------------

    /// <summary>
    /// 決議 8 and 8a: the review table gains a row type, the engine gains nothing. The name is
    /// <c>AreaExemption</c> rather than <c>Article79_1</c> so 第79條之2第3項's 挑空免除 could move under
    /// the same type later — this round does not move it.
    /// </summary>
    [Fact]
    public void The_review_gains_a_check_type_but_no_rule_category()
    {
        Assert.Equal("AreaExemption", ReviewCheckTypes.AreaExemption);
        Assert.Equal(5, Enum.GetValues<RuleCategory>().Length);
        Assert.DoesNotContain("AreaExemption", Enum.GetNames<RuleCategory>(), StringComparer.Ordinal);

        var existing = new[]
        {
            ReviewCheckTypes.CompartmentArea, ReviewCheckTypes.FireResistance, ReviewCheckTypes.OpeningProtection,
            ReviewCheckTypes.CompartmentContinuity, ReviewCheckTypes.VerticalCompartment
        };
        Assert.DoesNotContain(ReviewCheckTypes.AreaExemption, existing, StringComparer.Ordinal);
    }

    // --- helpers ------------------------------------------------------------------------------

    /// <summary>
    /// The 無法區劃分隔 input assembled for 區劃 A, whose Areas are the ones named, or null when nothing
    /// was supplied. Naming no Area leaves the parameter unbound.
    /// </summary>
    private static ReviewInput? Assemble(params (string AreaUniqueId, int YesNo)[] areas)
    {
        var parts = areas.Length == 0 ? new[] { "area-a" } : areas.Select(a => a.AreaUniqueId).ToArray();
        var set = CandidateResolver.Resolve(new CandidateObservationSet(PackageId, "level-1F", "1F",
            new[] { new ZoneObservation(ZoneA, "A 區", parts.Select((uid, i) =>
                new ZonePartObservation(uid, new[] { Rect(i * 12, 0, i * 12 + 10, 10) }, null)).ToList()) },
            Array.Empty<MemberObservation>(), Array.Empty<OpeningObservation>()));

        var elements = areas.ToDictionary(
            a => a.AreaUniqueId,
            a => (IReadOnlyDictionary<string, ParameterReading>)new Dictionary<string, ParameterReading>
            {
                [ReviewInputSources.CannotBeSubdivided] = ParameterReading.OfYesNo(a.YesNo)
            },
            StringComparer.Ordinal);

        var snapshot = new ReviewParameterSnapshot(
            new[] { new KeyValuePair<string, ReviewParameterHost>(ReviewInputSources.CannotBeSubdivided, ReviewParameterHost.Areas) },
            null,
            elements);

        return ReviewInputAssembler.Assemble(set, snapshot).Area.ForZone(ZoneA)
            .FirstOrDefault(i => i.Field == Field);
    }

    private static CompiledRuleSet Shipped()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Rules", "BuiltIn", "fire-review-rules.json");
        var loaded = RuleSetCompiler.Load(JsonSerializer.Deserialize<RuleSetDocument>(File.ReadAllText(path), Json));
        Assert.True(loaded.IsSuccess, loaded.IsSuccess ? string.Empty : loaded.Error.ToString());
        return loaded.Value;
    }
}
