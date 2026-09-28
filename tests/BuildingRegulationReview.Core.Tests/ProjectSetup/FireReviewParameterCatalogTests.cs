using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildingRegulationReview.Application.Checks;
using BuildingRegulationReview.Application.ProjectSetup;
using BuildingRegulationReview.Application.Reviews;
using BuildingRegulationReview.Domain.ProjectSetup;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.ProjectSetup;

/// <summary>
/// The parameters 「一鍵建立」 creates, against the two things they have to agree with: what the review
/// actually reads (<see cref="ReviewInputSources"/>) and the shared parameter master file that holds
/// their definitions.
/// </summary>
/// <remarks>
/// The file is compared by GUID, DATATYPE and GROUP — all ASCII, so no cp950 decoder is needed (see
/// <see cref="Parameters.SharedParameterFileTests"/> for why one is deliberately not taken). The GUID
/// is what binds a project's parameter to the definition, so a GUID that drifted between the code and
/// the file would silently orphan every model that already bound the other one.
/// </remarks>
public sealed class FireReviewParameterCatalogTests
{
    /// <summary>Latin-1 maps every byte to the char of the same value, so the ASCII fields read as text.</summary>
    private static readonly Encoding Bytes = Encoding.Latin1;

    /// <summary>
    /// Every parameter the review reads has to be creatable. The catalog throws when one has no
    /// declaration, so building it at all is the assertion — this test states it so the failure says why.
    /// </summary>
    [Fact]
    public void Every_parameter_the_review_reads_can_be_created()
    {
        var created = FireReviewParameterCatalog.All.Select(x => x.Name).ToList();

        foreach (var name in ReviewInputSources.ParameterNames)
            Assert.Contains(name, created);
    }

    /// <summary>
    /// 結構材料 and 防火被覆厚度 are not read by any rule — the batch panel derives 時效 from them
    /// (第71～73條) — so nothing in <see cref="ReviewInputSources"/> would bring them along. A model
    /// without them can only have every rating typed in one Type at a time.
    /// </summary>
    [Fact]
    public void The_two_parameters_the_derivation_needs_are_created_as_well()
    {
        var material = FireReviewParameterCatalog.For(StructuralMaterialParameters.Material);
        var cover = FireReviewParameterCatalog.For(StructuralMaterialParameters.Cover);

        Assert.NotNull(material);
        Assert.NotNull(cover);
        Assert.Equal(ReviewParameterLevel.Type, material!.Level);
        Assert.Equal(ReviewParameterLevel.Type, cover!.Level);
        Assert.Equal(SharedParameterValueType.Length, cover.ValueType);
        Assert.Equal(FireReviewParameterGroup.Structural, material.Group);
    }

    /// <summary>
    /// 防火檢討_法規要求防火時效 (…000a) is in the master file but reserved for the write-back; no rule
    /// and no panel reads it, so creating it would only add a box nobody fills in.
    /// </summary>
    [Fact]
    public void The_parameter_reserved_for_the_write_back_is_not_created()
    {
        Assert.Null(FireReviewParameterCatalog.For(FireRatingParameters.Required));
    }

    /// <summary>
    /// 設計防火時效 answers two fields with different categories — 第70條 on the 主要構造 and 第79條之2
    /// 第1項 on a 管道間維修門 — and one project parameter has one category list, so the list is the
    /// union. Binding only one field's categories would leave the other field unreadable while the
    /// parameter looks present.
    /// </summary>
    [Fact]
    public void A_parameter_that_answers_two_fields_is_bound_to_both_field_s_categories()
    {
        var rating = FireReviewParameterCatalog.For(FireRatingParameters.Provided);

        Assert.NotNull(rating);
        Assert.Contains(ReviewParameterHost.Walls, rating!.Hosts);
        Assert.Contains(ReviewParameterHost.CurtainPanels, rating.Hosts);
        Assert.Contains(ReviewParameterHost.Doors, rating.Hosts);
    }

    /// <summary>
    /// The two categories a 柱 is: 建築柱 and 結構柱 are one host, and a parameter bound to only one of
    /// them reads as absent on the other's Types.
    /// </summary>
    [Fact]
    public void Every_host_a_review_source_names_is_carried_into_the_definition()
    {
        foreach (var source in ReviewInputSources.All)
        {
            var definition = FireReviewParameterCatalog.For(source.ParameterName);
            Assert.NotNull(definition);
            foreach (var host in source.Hosts) Assert.Contains(host, definition!.Hosts);
        }
    }

    /// <summary>
    /// The whole binding plan, spelled out: this is what the button writes into someone's model, and
    /// the list a person checks it against afterwards (checklist V-22). A category that quietly
    /// appeared or vanished here changes what the review can read, so it is pinned rather than derived.
    /// </summary>
    [Theory]
    [InlineData("防火檢討_防火構造建築物", "實體參數", "專案資訊")]
    [InlineData("建築物用途類組", "實體參數", "專案資訊")]
    [InlineData("地上層數", "實體參數", "專案資訊")]
    [InlineData("防火檢討_區劃用途", "實體參數", "面積")]
    [InlineData("防火檢討_自動滅火設備", "實體參數", "面積")]
    [InlineData("防火檢討_所在樓層序", "實體參數", "面積")]
    [InlineData("防火檢討_連跨樓層數", "實體參數", "面積")]
    [InlineData("防火檢討_避難層通達", "實體參數", "面積")]
    [InlineData("防火檢討_無法區劃分隔", "實體參數", "面積")]
    [InlineData("防火檢討_室內裝修等級", "類型參數", "牆、天花板")]
    [InlineData("防火檢討_設計防火時效", "類型參數", "牆、柱、結構構架（梁）、樓板、門、帷幕嵌板")]
    [InlineData("結構材料", "類型參數", "牆、柱、結構構架（梁）、樓板、帷幕嵌板")]
    [InlineData("防火被覆厚度", "類型參數", "牆、柱、結構構架（梁）、樓板、帷幕嵌板")]
    [InlineData("防火檢討_設計防火保護", "類型參數", "門、窗、帷幕嵌板")]
    [InlineData("防火檢討_遮煙性能", "類型參數", "門、窗、帷幕嵌板")]
    [InlineData("防火檢討_嵌板種類", "類型參數", "帷幕嵌板")]
    public void The_binding_plan_is_what_it_says_it_is(string name, string level, string hosts)
    {
        var definition = FireReviewParameterCatalog.For(name);

        Assert.NotNull(definition);
        Assert.Equal(level, definition!.LevelText);
        Assert.Equal(hosts, definition.HostsText);
    }

    /// <summary>
    /// Sixteen, and no seventeenth slipping in unnoticed: the count is what the preview shows and what
    /// V-22 counts the rows against.
    /// </summary>
    [Fact]
    public void The_catalog_holds_sixteen_parameters()
    {
        Assert.Equal(16, FireReviewParameterCatalog.All.Count);
    }

    /// <summary>Two definitions cannot share a name or a GUID: either collision makes Revit reject the file.</summary>
    [Fact]
    public void Every_definition_keeps_its_own_name_and_guid()
    {
        var all = FireReviewParameterCatalog.All;

        Assert.Equal(all.Count, all.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(all.Count, all.Select(x => x.Guid).Distinct().Count());
        Assert.DoesNotContain(all, x => x.Hosts.Count == 0);
    }

    /// <summary>
    /// The GUID, the data type and the group of every created parameter, against the master file the
    /// installer reads its definitions from. A definition that only exists in the code cannot be
    /// created at all; one whose type differs would be created as the wrong kind of box.
    /// </summary>
    [Fact]
    public void Every_definition_matches_the_shared_parameter_master_file()
    {
        var file = Params();

        foreach (var definition in FireReviewParameterCatalog.All)
        {
            var key = definition.Guid.ToString("D");
            Assert.True(file.ContainsKey(key), $"共用參數主檔沒有 {definition.Name} 的 GUID {key}");

            var fields = file[key];
            Assert.Equal("PARAM", fields[0]);
            Assert.Equal(DataType(definition.ValueType), fields[3]);
            Assert.Equal(GroupId(definition.Group), fields[5]);
            Assert.Equal("1", fields[6]);   // VISIBLE
            Assert.Equal("1", fields[8]);   // USERMODIFIABLE
        }
    }

    /// <summary>
    /// The file's own GROUP lines, so the group ids asserted above are not just two magic numbers:
    /// 1 is 防火區劃檢討 and 2 is 結構. Compared as byte counts and ids only — the names are Big5 and
    /// <see cref="Parameters.SharedParameterFileTests"/> guards those.
    /// </summary>
    [Fact]
    public void The_master_file_declares_the_two_groups_the_definitions_use()
    {
        var groups = Lines()
            .Where(x => x.StartsWith("GROUP\t", StringComparison.Ordinal))
            .Select(x => x.Split('\t')[1])
            .ToList();

        Assert.Equal(new[] { "1", "2" }, groups);
    }

    private static string DataType(SharedParameterValueType valueType) => valueType switch
    {
        SharedParameterValueType.Text => "TEXT",
        SharedParameterValueType.YesNo => "YESNO",
        SharedParameterValueType.Integer => "INTEGER",
        SharedParameterValueType.Length => "LENGTH",
        _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "主檔沒有用到這個型別")
    };

    private static string GroupId(FireReviewParameterGroup group) =>
        group == FireReviewParameterGroup.Structural ? "2" : "1";

    private static IReadOnlyDictionary<string, string[]> Params() =>
        Lines()
            .Where(x => x.StartsWith("PARAM\t", StringComparison.Ordinal))
            .Select(x => x.Split('\t'))
            .ToDictionary(x => x[1], x => x, StringComparer.Ordinal);

    private static IReadOnlyList<string> Lines() =>
        Bytes.GetString(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "Assets", "fire-review-shared-params.txt")))
            .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
}
