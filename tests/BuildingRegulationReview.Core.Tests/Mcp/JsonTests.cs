using BuildingRegulationReview.Mcp.Json;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Mcp;

public sealed class JsonTests
{
    [Fact]
    public void Parse_ReadsEveryKindOfValue()
    {
        var value = JsonValue.Parse("{\"a\":1,\"b\":-2.5e1,\"c\":\"x\",\"d\":true,\"e\":null,\"f\":[1,{\"g\":false}]}");

        var obj = Assert.IsType<JsonObject>(value);
        Assert.Equal(1, obj["a"]!.AsNumber());
        Assert.Equal(-25, obj["b"]!.AsNumber());
        Assert.Equal("x", obj["c"]!.AsString());
        Assert.True(obj["d"]!.AsBoolean());
        Assert.True(obj["e"]!.IsNull);
        var array = obj["f"]!.AsArray()!;
        Assert.Equal(2, array.Count);
        Assert.False(array[1].AsObject()!["g"]!.AsBoolean());
    }

    [Fact]
    public void Parse_DecodesEscapesAndSurrogatePairs()
    {
        var value = JsonValue.Parse("\"防火\\n\\\"區劃\\\"\\u0041\\ud83d\\ude00\"");

        Assert.Equal("防火\n\"區劃\"A\U0001F600", value.AsString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[1,]")]
    [InlineData("{\"a\":1,}")]
    [InlineData("01")]
    [InlineData("NaN")]
    [InlineData("'x'")]
    [InlineData("{\"a\":1} x")]
    [InlineData("\"tab\there\"")]
    public void Parse_RefusesWhatIsNotStrictJson(string text)
    {
        Assert.Throws<JsonParseException>(() => JsonValue.Parse(text));
    }

    [Fact]
    public void Parse_RefusesNestingDeeperThanTheLimit()
    {
        var deep = new string('[', JsonParser.MaxDepth + 2) + new string(']', JsonParser.MaxDepth + 2);

        Assert.Throws<JsonParseException>(() => JsonValue.Parse(deep));
    }

    [Fact]
    public void Write_KeepsMemberOrderAndWritesNonAsciiAsIs()
    {
        var obj = new JsonObject { ["z"] = 1, ["a"] = "區劃", ["m"] = (string?)null };
        obj["z"] = 2;

        Assert.Equal("{\"z\":2,\"a\":\"區劃\",\"m\":null}", obj.ToString());
    }

    [Fact]
    public void Write_EscapesControlCharactersAndQuotes()
    {
        JsonValue value = "a\"b\\c\n\u0001";

        Assert.Equal("\"a\\\"b\\\\c\\n\\u0001\"", value.ToString());
    }

    [Theory]
    [InlineData(12345.0, "12345")]
    [InlineData(-0.0, "0")]
    [InlineData(0.25, "0.25")]
    [InlineData(1e20, "1E+20")]
    public void Write_FormatsNumbersWithoutNeedlessFractions(double number, string expected)
    {
        Assert.Equal(expected, ((JsonValue)number).ToString());
    }

    [Fact]
    public void ImplicitConversions_MapNullsAndNonFiniteNumbersToJsonNull()
    {
        Assert.True(((JsonValue)(int?)null).IsNull);
        Assert.True(((JsonValue)(bool?)null).IsNull);
        Assert.True(((JsonValue)double.NaN).IsNull);
        Assert.True(((JsonValue)double.PositiveInfinity).IsNull);
    }

    [Fact]
    public void RoundTrip_PreservesTheDocument()
    {
        const string text = "{\"jsonrpc\":\"2.0\",\"id\":7,\"params\":{\"list\":[1,2.5,\"三\",true,null],\"nested\":{}}}";

        Assert.Equal(text, JsonValue.Parse(text).ToString());
    }
}
