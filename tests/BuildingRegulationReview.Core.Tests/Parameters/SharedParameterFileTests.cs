using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Parameters;

/// <summary>
/// The shared parameter master file, read as bytes.
///
/// Revit's <c>DefinitionFile</c> parser reads the file in the OS ANSI codepage, which on a zh-TW
/// machine is Big5/cp950. Saving it as UTF-8 does not fail — it loads, and every Chinese parameter
/// name comes through as mojibake, so the project ends up with parameters nobody can match by name.
/// An editor that "helpfully" normalises the file is all it takes, and nothing else in the build
/// would notice, so the encoding and the line endings are asserted here.
///
/// The assertions are byte-level on purpose: .NET on this target ships no cp950 decoder without
/// <c>System.Text.Encoding.CodePages</c>, and a test that had to take a dependency to check an
/// encoding would be the first thing dropped. Latin-1 is used as a byte↔char identity map so the
/// ASCII skeleton of a line can be compared as text while the Chinese fields stay raw bytes.
/// </summary>
public sealed class SharedParameterFileTests
{
    /// <summary>Latin-1 maps every byte to the char of the same value, so no information is lost.</summary>
    private static readonly Encoding Bytes = Encoding.Latin1;

    /// <summary>防火檢討_無法區劃分隔 in Big5/cp950 (第79條之1規格 §6).</summary>
    private const string CannotBeSubdividedBig5 = "a8bea4f5c0cbb0515fb54caa6bb0cfb9baa4c0b96a";

    /// <summary>The GUID §6 assigns it. The largest before that round was …0011.</summary>
    private const string CannotBeSubdividedGuid = "bcf10001-0000-4a00-9b00-000000000012";

    /// <summary>防火檢討_嵌板種類 in Big5/cp950 (帷幕牆規格 §6、決議 16).</summary>
    private const string PanelKindBig5 = "a8bea4f5c0cbb0515fb44faa4fbad8c3fe";

    /// <summary>The GUID 決議 16 assigns it — the next free number after …0012.</summary>
    private const string PanelKindGuid = "bcf10001-0000-4a00-9b00-000000000013";

    // --- 編碼與換行 ---------------------------------------------------------------------------

    /// <summary>
    /// Not UTF-8, and not marked as anything. A BOM would be handed to Revit's ANSI parser as three
    /// stray characters at the head of the first line; valid UTF-8 would mean someone re-encoded the
    /// file and every Chinese name in it is now wrong.
    /// </summary>
    [Fact]
    public void The_file_is_ansi_and_carries_no_byte_order_mark()
    {
        var raw = Raw();

        Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, "檔頭有 UTF-8 BOM");
        Assert.Contains(raw, b => b >= 0x80);
        Assert.ThrowsAny<Exception>(() => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw));
    }

    /// <summary>
    /// CRLF throughout. The openings file next to it is LF and that difference is deliberate, so a
    /// tool that unified the two would take this one with it.
    /// </summary>
    [Fact]
    public void Every_line_ends_with_crlf()
    {
        var raw = Raw();

        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == (byte)'\n') Assert.True(i > 0 && raw[i - 1] == (byte)'\r', $"第 {i} 個位元組是沒有 CR 的 LF");
            if (raw[i] == (byte)'\r') Assert.True(i + 1 < raw.Length && raw[i + 1] == (byte)'\n', $"第 {i} 個位元組是沒有 LF 的 CR");
        }

        Assert.EndsWith("\r\n", Bytes.GetString(raw), StringComparison.Ordinal);
    }

    // --- 第79條之1 的參數 ---------------------------------------------------------------------

    /// <summary>
    /// §6: a YESNO instance parameter in the 防火區劃檢討 group, with the GUID the spec fixes. The GUID
    /// is what binds a project's parameter to this definition, so a changed one silently orphans every
    /// model that already bound the old one.
    /// </summary>
    [Fact]
    public void The_declaration_parameter_is_declared_as_the_spec_fixes_it()
    {
        var fields = Param(CannotBeSubdividedGuid);

        Assert.Equal("PARAM", fields[0]);
        Assert.Equal(CannotBeSubdividedGuid, fields[1]);
        Assert.Equal(Bytes.GetString(Hex(CannotBeSubdividedBig5)), fields[2]);
        Assert.Equal("YESNO", fields[3]);
        Assert.Equal(string.Empty, fields[4]);      // DATACATEGORY
        Assert.Equal("1", fields[5]);               // GROUP 1 = 防火區劃檢討
        Assert.Equal("1", fields[6]);               // VISIBLE
        Assert.Equal("1", fields[8]);               // USERMODIFIABLE
        Assert.Equal("0", fields[9]);               // HIDEWHENNOVALUE
    }

    /// <summary>
    /// The description is what the person binding the parameter reads, and 非必要參數 is the part that
    /// has to survive: a project with none of the six 區劃用途 should not bind it at all (決議 9).
    /// Compared as bytes, so a re-encoding is caught here too.
    /// </summary>
    [Fact]
    public void The_description_says_who_needs_to_fill_it_in()
    {
        var description = Param(CannotBeSubdividedGuid)[7];

        foreach (var fragment in new[] { "第79條之1", "觀眾席", "生產線", "教室", "體育館", "零售市場", "停車空間", "非必要參數" })
            Assert.Contains(Big5Of(fragment), description, StringComparison.Ordinal);
    }

    // --- 帷幕嵌板種類（決議 16）------------------------------------------------------------------

    /// <summary>
    /// A TEXT Type parameter in the 防火區劃檢討 group. TEXT rather than YESNO because 未宣告 has to stay
    /// apart from both kinds: a Yes/No box always reads as one of them, which is exactly the collapse
    /// 決議 16 exists to prevent.
    /// </summary>
    [Fact]
    public void The_panel_kind_parameter_is_declared_as_resolution_16_fixes_it()
    {
        var fields = Param(PanelKindGuid);

        Assert.Equal("PARAM", fields[0]);
        Assert.Equal(PanelKindGuid, fields[1]);
        Assert.Equal(Bytes.GetString(Hex(PanelKindBig5)), fields[2]);
        Assert.Equal("TEXT", fields[3]);
        Assert.Equal(string.Empty, fields[4]);      // DATACATEGORY
        Assert.Equal("1", fields[5]);               // GROUP 1 = 防火區劃檢討
        Assert.Equal("1", fields[6]);               // VISIBLE
        Assert.Equal("1", fields[8]);               // USERMODIFIABLE
        Assert.Equal("0", fields[9]);               // HIDEWHENNOVALUE
    }

    /// <summary>
    /// The description has to name both values and say which question each one answers — the person
    /// binding the parameter is the one who then fills it in, and a wrong kind sends the panel down
    /// the other rule (決議 16). 非必要參數 survives here for the same reason as 第79條之1's: a project
    /// with no 帷幕牆 should not bind it at all.
    /// </summary>
    [Fact]
    public void The_panel_kind_description_names_both_kinds_and_the_parameter_each_one_answers_with()
    {
        var description = Param(PanelKindGuid)[7];

        foreach (var fragment in new[]
                 { "帷幕嵌板種類", "實心", "玻璃", "防火檢討_設計防火時效", "防火檢討_設計防火保護", "非必要參數" })
            Assert.Contains(Big5Of(fragment), description, StringComparison.Ordinal);
    }

    /// <summary>
    /// One definition per GUID and per name, and nothing lost: 決議 16 adds one line to the sixteen
    /// that were already there, 垂直區劃規格決議 35 retired 防火檢討_連跨樓層數 (…0010), and 決議 38 added
    /// 防火檢討_阻熱性 (…0016). A duplicate
    /// GUID makes Revit reject the whole file.
    /// </summary>
    [Fact]
    public void Every_parameter_keeps_its_own_guid_and_name()
    {
        var params_ = Lines().Where(l => l.StartsWith("PARAM\t", StringComparison.Ordinal))
            .Select(l => l.Split('\t')).ToList();

        Assert.Equal(17, params_.Count);
        Assert.Equal(params_.Count, params_.Select(f => f[1]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(params_.Count, params_.Select(f => f[2]).Distinct(StringComparer.Ordinal).Count());
    }

    // --- helpers ------------------------------------------------------------------------------

    private static byte[] Raw() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "fire-review-shared-params.txt"));

    /// <summary>The file's lines, each char standing for one byte of the original.</summary>
    private static IReadOnlyList<string> Lines() =>
        Bytes.GetString(Raw()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

    private static string[] Param(string guid)
    {
        var line = Lines().SingleOrDefault(l => l.StartsWith("PARAM\t" + guid + "\t", StringComparison.Ordinal));
        Assert.NotNull(line);
        return line!.Split('\t');
    }

    /// <summary>
    /// A fragment as Big5 bytes, read back one byte per char so it can be searched for in a line.
    /// Written out rather than computed, because this target has no cp950 encoder; each entry is the
    /// fragment as <c>[Text.Encoding]::GetEncoding(950).GetBytes(…)</c> gives it.
    /// </summary>
    private static string Big5Of(string text) => Bytes.GetString(Hex(text switch
    {
        "第79條之1" => "b2c43739b1f8a4a731",
        "觀眾席" => "c65bb2b3ae75",
        "生產線" => "a5cdb2a3bd75",
        "教室" => "b1d0abc7",
        "體育館" => "c5e9a87cc05d",
        "零售市場" => "b973b0e2a5abb3f5",
        "停車空間" => "b0b1a8aeaac5b6a1",
        "非必要參數" => "ab44a5b2ad6eb0d1bcc6",
        "帷幕嵌板種類" => "b163b9f5b44faa4fbad8c3fe",
        "實心" => "b9eaa4df",
        "玻璃" => "acc1bcfe",
        "防火檢討_設計防火時效" => "a8bea4f5c0cbb0515fb35dad70a8bea4f5aec9aec4",
        "防火檢討_設計防火保護" => "a8bea4f5c0cbb0515fb35dad70a8bea4f5ab4fc540",
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "沒有這個片語的 Big5 位元組")
    }));

    private static byte[] Hex(string hex) =>
        Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
}
