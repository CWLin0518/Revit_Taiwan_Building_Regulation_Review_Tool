using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// <c>fire-review-openings-type.txt</c>, read as bytes — the file <c>load_shared_parameters</c> actually
/// binds 門、窗 and 帷幕嵌板 from. The master file next to it is the catalogue; this one is what reaches
/// a model, so a parameter missing here is a parameter nobody can fill in however well it is documented.
///
/// Read as bytes for the same reason as <see cref="SharedParameterFileTests"/>: Revit's
/// <c>DefinitionFile</c> parser reads the OS ANSI codepage, this target ships no cp950 decoder, and
/// Latin-1 is a byte↔char identity map. The line endings are LF here and CRLF in the master, which is
/// deliberate — a tool that unified the two would break one of them, so both are asserted.
/// </summary>
public sealed class OpeningsSharedParameterFileTests
{
    private static readonly Encoding Bytes = Encoding.Latin1;

    /// <summary>防火檢討_嵌板種類 in Big5/cp950 (帷幕牆規格 §6、決議 16、步驟 16c).</summary>
    private const string PanelKindBig5 = "a8bea4f5c0cbb0515fb44faa4fbad8c3fe";

    private const string PanelKindGuid = "bcf10001-0000-4a00-9b00-000000000013";

    // --- 編碼與換行 ---------------------------------------------------------------------------

    [Fact]
    public void The_file_is_ansi_and_carries_no_byte_order_mark()
    {
        var raw = Raw();

        Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "檔頭有 UTF-8 BOM");
        Assert.Contains(raw, b => b >= 0x80);
        Assert.ThrowsAny<Exception>(() => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw));
    }

    /// <summary>
    /// LF throughout, and not one CR. The master file is CRLF; keeping the difference recorded in a
    /// test is the only thing that stops a well-meaning normalisation from rewriting either of them.
    /// </summary>
    [Fact]
    public void Every_line_ends_with_a_bare_line_feed()
    {
        var raw = Raw();

        Assert.DoesNotContain(raw, b => b == (byte)'\r');
        Assert.Equal((byte)'\n', raw[raw.Length - 1]);
    }

    // --- 帷幕嵌板種類（決議 16、步驟 16c）--------------------------------------------------------

    /// <summary>
    /// 決議 16 只在主檔宣告了 防火檢討_嵌板種類；步驟 16c 把它加進這一份，否則帷幕嵌板綁不到這個參數，
    /// 而 <c>junction.panelKind</c> 是必要參數，整個檢討會在前置檢查被 BCR-PARAM-001 擋下。
    /// </summary>
    [Fact]
    public void The_panel_kind_is_bindable_from_this_file()
    {
        var fields = Param(PanelKindGuid);

        Assert.Equal(Bytes.GetString(Hex(PanelKindBig5)), fields[2]);
        Assert.Equal("TEXT", fields[3]);
        Assert.Equal(string.Empty, fields[4]);      // DATACATEGORY
        Assert.Equal("1", fields[5]);               // GROUP 1 = 防火區劃檢討
        Assert.Equal("1", fields[6]);               // VISIBLE
        Assert.Equal("1", fields[8]);               // USERMODIFIABLE
        Assert.Equal("0", fields[9]);               // HIDEWHENNOVALUE
    }

    /// <summary>
    /// Name and data type have to agree with the master's, byte for byte, for every GUID this file
    /// repeats. The GUID is what binds a project's parameter to the definition, so two files
    /// disagreeing about one silently gives a model two parameters that look like one.
    /// </summary>
    /// <remarks>
    /// DESCRIPTION is deliberately left out of the comparison: it is prose for whoever binds *this*
    /// file, and it already differs — the master's 設計防火保護 names 門窗帷幕嵌板類型 while this one
    /// says what 未勾選 means to the review. GROUP is left out too, because the id is file-local
    /// (<see cref="Every_parameter_sits_in_a_group_this_file_declares"/> covers that side).
    /// </remarks>
    [Fact]
    public void Every_definition_here_names_the_same_parameter_as_the_master_file()
    {
        var master = Lines(MasterRaw(), "\r\n").Where(IsParam)
            .ToDictionary(Guid, l => l.Split('\t'), StringComparer.Ordinal);

        foreach (var fields in Lines(Raw(), "\n").Where(IsParam).Select(l => l.Split('\t')))
        {
            Assert.True(master.ContainsKey(fields[1]), $"主檔沒有 GUID {fields[1]}");
            var theirs = master[fields[1]];

            Assert.Equal(theirs[2], fields[2]);     // NAME，逐位元組
            Assert.Equal(theirs[3], fields[3]);     // DATATYPE
            Assert.Equal(theirs[4], fields[4]);     // DATACATEGORY
            Assert.Equal(theirs[6], fields[6]);     // VISIBLE
            Assert.Equal(theirs[8], fields[8]);     // USERMODIFIABLE
            Assert.Equal(theirs[9], fields[9]);     // HIDEWHENNOVALUE
        }
    }

    /// <summary>
    /// The five the openings and the panels need: 設計防火保護、遮煙性能、嵌板種類、設計防火時效、阻熱性
    /// (帷幕牆規格 §6、垂直區劃文件 §6、決議 16、38). 結構材料 and 防火被覆厚度 are deliberately not here —
    /// they come from <c>fire-review-members-type.txt</c>, so binding this file to 門、窗 does not give a
    /// door a 結構材料 box.
    /// </summary>
    [Fact]
    public void The_file_declares_exactly_the_five_parameters_these_categories_answer_with()
    {
        var params_ = Lines(Raw(), "\n").Where(IsParam).ToList();

        Assert.Equal(5, params_.Count);
        Assert.Equal(5, params_.Select(Guid).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, params_.Select(l => l.Split('\t')[2]).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(PanelKindGuid, params_.Select(Guid));
    }

    /// <summary>
    /// Every GROUP a PARAM refers to has to be declared in the same file, or Revit rejects it. The
    /// master declares two groups and this one declares a single group, so a definition copied across
    /// can carry a group id that is not here.
    /// </summary>
    [Fact]
    public void Every_parameter_sits_in_a_group_this_file_declares()
    {
        var lines = Lines(Raw(), "\n");
        var groups = lines.Where(l => l.StartsWith("GROUP\t", StringComparison.Ordinal))
            .Select(l => l.Split('\t')[1]).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(groups);
        foreach (var line in lines.Where(IsParam))
            Assert.Contains(line.Split('\t')[5], groups);
    }

    // --- helpers ------------------------------------------------------------------------------

    private static byte[] Raw() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fire-review-openings-type.txt"));

    private static byte[] MasterRaw() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fire-review-shared-params.txt"));

    private static IReadOnlyList<string> Lines(byte[] raw, string newLine) =>
        Bytes.GetString(raw).Split(new[] { newLine }, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsParam(string line) => line.StartsWith("PARAM\t", StringComparison.Ordinal);

    private static string Guid(string line) => line.Split('\t')[1];

    private static string[] Param(string guid)
    {
        var line = Lines(Raw(), "\n")
            .SingleOrDefault(l => l.StartsWith("PARAM\t" + guid + "\t", StringComparison.Ordinal));
        Assert.NotNull(line);
        return line!.Split('\t');
    }

    private static byte[] Hex(string hex) =>
        Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
}
