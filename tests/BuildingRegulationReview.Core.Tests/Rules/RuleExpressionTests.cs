using System;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;
using BuildingRegulationReview.Domain.Rules;
using BuildingRegulationReview.Domain.Rules.Expressions;
using Xunit;

namespace BuildingRegulationReview.Core.Tests.Rules;

public sealed class RuleExpressionTests
{
    private static readonly RuleFieldCatalog Catalog = RuleFieldCatalog.Default;

    private static RuleExpressionNode Condition(string source, RuleCategory category = RuleCategory.CompartmentArea) =>
        RuleExpressionParser.ParseCondition(source, Catalog, category);

    private static RuleRequirement Requirement(string source, RuleCategory category = RuleCategory.CompartmentArea) =>
        RuleExpressionParser.ParseRequirement(source, Catalog, category);

    private static RuleExpressionException Refused(Action parse, string code)
    {
        var error = Assert.Throws<RuleExpressionException>(parse);
        Assert.Equal(code, error.Code);
        return error;
    }

    private static RuleFacts Facts() => new RuleFacts(Catalog);

    [Fact]
    public void Operators_bind_by_precedence_and_print_canonically()
    {
        Assert.Equal(
            "(building.fireResistiveConstruction || (zone.sprinklered && !building.fireResistiveConstruction))",
            Condition("building.fireResistiveConstruction || zone.sprinklered && !building.fireResistiveConstruction").ToString());

        Assert.Equal(
            "(zone.area <= ((1000 m2 + (200 m2 * 2)) - 100 m2))",
            Condition("zone.area <= 1000 m2 + 200 m2 * 2 - 100 m2").ToString());

        // Whitespace and redundant parentheses do not change the tree.
        Assert.Equal(Condition("zone.area<=(1500 m2)").ToString(), Condition("  zone.area   <=  1500 m2 ").ToString());
    }

    [Fact]
    public void Unit_literals_are_converted_to_one_canonical_unit_per_dimension()
    {
        AssertQuantity(90, ReviewUnit.Minute, "element.providedFireRating >= 1.5 h", RuleCategory.FireResistance);
        AssertQuantity(120, ReviewUnit.Minute, "element.providedFireRating >= 2 hr", RuleCategory.FireResistance);
        AssertQuantity(1500, ReviewUnit.SquareMeter, "zone.area <= 1500 ㎡");
        AssertQuantity(31, ReviewUnit.Meter, "building.height <= 3100 cm");
        AssertQuantity(0.25, ReviewUnit.Meter, "building.height >= 250 mm");

        static void AssertQuantity(double number, ReviewUnit unit, string source, RuleCategory category = RuleCategory.CompartmentArea)
        {
            var value = RuleExpressionParser.ParseRequirement(source, Catalog, category).Required.Evaluate(new RuleFacts(Catalog)).Value!;
            Assert.Equal(unit, value.Unit);
            Assert.Equal(number, value.Number, 9);
        }
    }

    [Fact]
    public void Units_must_be_written_and_must_match()
    {
        var bare = Refused(() => Condition("zone.area <= 1500"), RuleExpressionErrorCode.TypeMismatch);
        Assert.Contains("單位", bare.Message);

        Refused(() => Condition("element.providedFireRating >= 60 m2", RuleCategory.FireResistance), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.area <= 1500 m2 + 60 min"), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.area <= 1500 kg"), RuleExpressionErrorCode.Syntax);

        // m × m is an area and m2 ÷ m a length; a plain number scales any unit.
        Assert.True(Condition("zone.area <= building.height * building.height").Type.IsBoolean);
        Assert.True(Condition("building.height >= zone.area / 10 m").Type.IsBoolean);
        Assert.True(Condition("zone.area <= 1500 m2 * 2").Type.IsBoolean);
        Refused(() => Condition("zone.area <= zone.area * zone.area"), RuleExpressionErrorCode.TypeMismatch);
    }

    [Fact]
    public void Types_are_checked_when_the_expression_compiles()
    {
        Refused(() => Condition("zone.use < \"辦公\""), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.use == 1"), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.sprinklered && zone.use"), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("!zone.use == \"a\""), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("-zone.sprinklered"), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.area <= (zone.sprinklered ? 3000 m2 : 60 min)"), RuleExpressionErrorCode.TypeMismatch);
        Refused(() => Condition("zone.area <= (zone.use ? 3000 m2 : 1500 m2)"), RuleExpressionErrorCode.TypeMismatch);
    }

    [Fact]
    public void Conditions_must_be_true_or_false()
    {
        Refused(() => Condition("zone.area"), RuleExpressionErrorCode.InvalidForm);
        Refused(() => Condition("\"是\""), RuleExpressionErrorCode.InvalidForm);
        Assert.True(Condition("true").Type.IsBoolean);
    }

    [Theory]
    [InlineData("System.IO.File.Delete(\"C:/x\")", RuleExpressionErrorCode.UnknownField)]
    [InlineData("eval(\"1\")", RuleExpressionErrorCode.UnknownField)]
    [InlineData("typeof(zone.area)", RuleExpressionErrorCode.UnknownField)]
    [InlineData("new System.Object() == 1", RuleExpressionErrorCode.UnknownField)]
    [InlineData("zone.area.ToString == \"1\"", RuleExpressionErrorCode.UnknownField)]
    [InlineData("zone.area = 1 m2", RuleExpressionErrorCode.Syntax)]
    [InlineData("zone.sprinklered; true", RuleExpressionErrorCode.Syntax)]
    [InlineData("zone.use == 'a'", RuleExpressionErrorCode.Syntax)]
    [InlineData("zone.sprinklered & true", RuleExpressionErrorCode.Syntax)]
    [InlineData("zone.use == \"a\" + \"b\"", RuleExpressionErrorCode.TypeMismatch)]
    [InlineData("zone.flags[0]", RuleExpressionErrorCode.Syntax)]
    [InlineData("${zone.area}", RuleExpressionErrorCode.Syntax)]
    public void Nothing_but_whitelisted_fields_and_fixed_operators_is_accepted(string source, string code)
    {
        Refused(() => Condition(source), code);
    }

    [Fact]
    public void Fields_must_be_whitelisted_and_available_to_the_rule_category()
    {
        var unknown = Refused(() => Condition("zone.height > 3 m"), RuleExpressionErrorCode.UnknownField);
        Assert.Equal(0, unknown.Position);

        Refused(() => Condition("area <= 1500 m2"), RuleExpressionErrorCode.UnknownField);
        Refused(() => Condition("Zone.Area <= 1500 m2"), RuleExpressionErrorCode.UnknownField);

        var misplaced = Refused(() => Condition("opening.isHosted == true", RuleCategory.CompartmentArea), RuleExpressionErrorCode.FieldNotAvailable);
        Assert.Contains("opening.isHosted", misplaced.Message);
        Refused(() => Condition("element.isStructural", RuleCategory.OpeningProtection), RuleExpressionErrorCode.FieldNotAvailable);

        // zone.* and building.* describe the compartment and are open to every category.
        Assert.True(Condition("zone.sprinklered", RuleCategory.OpeningProtection).Type.IsBoolean);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("zone.use == \"未結束", 12)]
    [InlineData("(zone.sprinklered", 17)]
    [InlineData("zone.sprinklered)", 16)]
    [InlineData("zone.sprinklered true", 17)]
    [InlineData("zone.area <=", 12)]
    [InlineData("zone.sprinklered ? 1 m2", 23)]
    [InlineData("zone.area < 1 m2 < 2 m2", 17)]
    [InlineData("zone.sprinklered == true == false", 25)]
    [InlineData("zone.area <= 1. m2", 15)]
    [InlineData("zone.use == \"a\\n\"", 14)]
    public void Malformed_text_is_refused_with_its_position(string source, int position)
    {
        var error = Assert.Throws<RuleExpressionException>(() => Condition(source));
        Assert.Equal(RuleExpressionErrorCode.Syntax, error.Code);
        Assert.Equal(position, error.Position);
    }

    [Fact]
    public void Overlong_or_overnested_expressions_are_refused()
    {
        var longText = "zone.sprinklered" + string.Concat(Enumerable.Repeat(" || zone.sprinklered", 60));
        Refused(() => Condition(longText), RuleExpressionErrorCode.TooComplex);

        var deep = new string('(', RuleExpressionParser.MaxDepth + 1) + "true" + new string(')', RuleExpressionParser.MaxDepth + 1);
        Refused(() => Condition(deep), RuleExpressionErrorCode.TooComplex);

        var negations = new string('!', RuleExpressionParser.MaxDepth + 1) + "true";
        Refused(() => Condition(negations), RuleExpressionErrorCode.TooComplex);

        var fits = new string('(', RuleExpressionParser.MaxDepth - 1) + "true" + new string(')', RuleExpressionParser.MaxDepth - 1);
        Assert.True(Condition(fits).Type.IsBoolean);
    }

    [Fact]
    public void Text_literals_keep_their_content_and_escapes()
    {
        var node = Condition("zone.use == \"樓梯間 \\\"A\\\" \\\\\"");
        var facts = Facts().Set("zone.use", "樓梯間 \"A\" \\");

        Assert.True(node.Evaluate(facts).IsTrue);
        Assert.Equal("(zone.use == \"樓梯間 \\\"A\\\" \\\\\")", node.ToString());
    }

    [Fact]
    public void A_requirement_is_actual_field_comparator_required_value()
    {
        var requirement = Requirement("zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)");

        Assert.Equal("zone.area", requirement.Actual.Name);
        Assert.Equal(RuleComparator.LessOrEqual, requirement.Comparator);
        Assert.Equal("(zone.sprinklered ? 3000 m2 : 1500 m2)", requirement.Required.ToString());
        Assert.Equal(new[] { "zone.sprinklered" }, requirement.Required.Fields.Select(x => x.Name));

        Refused(() => Requirement("1500 m2"), RuleExpressionErrorCode.InvalidForm);
        Refused(() => Requirement("1500 m2 >= zone.area"), RuleExpressionErrorCode.InvalidForm);
        Refused(() => Requirement("zone.area <= 1500 m2 && zone.sprinklered"), RuleExpressionErrorCode.InvalidForm);
        Refused(() => Requirement("zone.area <= zone.area + 1 m2"), RuleExpressionErrorCode.InvalidForm);
        Refused(() => Requirement("(zone.area) + 1 m2 <= 1500 m2"), RuleExpressionErrorCode.InvalidForm);
    }

    [Fact]
    public void Missing_fields_make_a_value_unknown_instead_of_false()
    {
        var node = Condition("zone.sprinklered == true");

        var unknown = node.Evaluate(Facts());
        Assert.True(unknown.IsUnknown);
        Assert.False(unknown.IsFalse);
        Assert.Equal(new[] { new RuleFactGap("zone.sprinklered", RuleFactGapKind.Missing) }, unknown.Gaps);

        var unreadable = node.Evaluate(Facts().MarkUnreadable("zone.sprinklered", "值為「有」"));
        Assert.Equal(RuleFactGapKind.Unreadable, unreadable.Gaps.Single().Kind);
        Assert.Contains("值為「有」", unreadable.Gaps.Single().ToString());
    }

    [Fact]
    public void Logic_uses_three_values_so_a_missing_field_only_matters_when_it_could_change_the_answer()
    {
        var facts = Facts().Set("building.fireResistiveConstruction", false);

        Assert.True(Condition("building.fireResistiveConstruction && zone.sprinklered").Evaluate(facts).IsFalse);
        Assert.True(Condition("zone.sprinklered && building.fireResistiveConstruction").Evaluate(facts).IsFalse);
        Assert.True(Condition("!building.fireResistiveConstruction || zone.sprinklered").Evaluate(facts).IsTrue);
        Assert.True(Condition("zone.sprinklered || !building.fireResistiveConstruction").Evaluate(facts).IsTrue);

        var unknown = Condition("!building.fireResistiveConstruction && zone.sprinklered").Evaluate(facts);
        Assert.True(unknown.IsUnknown);
        Assert.Equal("zone.sprinklered", unknown.Gaps.Single().Field);
    }

    [Fact]
    public void A_conditional_with_an_unknown_condition_is_known_only_when_both_branches_agree()
    {
        var agree = Requirement("zone.area <= (zone.sprinklered ? 1500 m2 : 1500.0 m2)").Required.Evaluate(Facts());
        Assert.Equal(ReviewValue.Quantity(1500, ReviewUnit.SquareMeter), agree.Value);

        var differ = Requirement("zone.area <= (zone.sprinklered ? 3000 m2 : 1500 m2)").Required.Evaluate(Facts());
        Assert.True(differ.IsUnknown);
        Assert.Equal("zone.sprinklered", differ.Gaps.Single().Field);
    }

    [Fact]
    public void Computation_errors_are_failures_not_values()
    {
        var node = Requirement("zone.area <= 3000 m2 / building.floorsAboveGround").Required;

        var failed = node.Evaluate(Facts().Set("building.floorsAboveGround", 0, ReviewUnit.None));
        Assert.True(failed.IsFailed);
        Assert.Contains("除以零", failed.Failure);

        Assert.Equal(ReviewValue.Quantity(1000, ReviewUnit.SquareMeter),
            node.Evaluate(Facts().Set("building.floorsAboveGround", 3, ReviewUnit.None)).Value);
    }

    [Fact]
    public void Comparisons_treat_conversion_noise_as_equal()
    {
        var atLimit = ReviewValue.Quantity(1500.0000000001, ReviewUnit.SquareMeter);
        var limit = ReviewValue.Quantity(1500, ReviewUnit.SquareMeter);

        Assert.True(RuleComparatorText.Holds(RuleComparator.LessOrEqual, atLimit, limit));
        Assert.False(RuleComparatorText.Holds(RuleComparator.Less, atLimit, limit));
        Assert.True(RuleComparatorText.Holds(RuleComparator.Equal, atLimit, limit));
        Assert.False(RuleComparatorText.Holds(RuleComparator.LessOrEqual, ReviewValue.Quantity(1500.01, ReviewUnit.SquareMeter), limit));
        Assert.True(RuleComparatorText.Holds(RuleComparator.GreaterOrEqual, ReviewValue.Quantity(59.99999999999, ReviewUnit.Minute), ReviewValue.Quantity(60, ReviewUnit.Minute)));
    }

    [Fact]
    public void Facts_only_accept_whitelisted_fields_in_their_declared_type_and_unit()
    {
        Assert.Throws<ArgumentException>(() => Facts().Set("zone.height", 3, ReviewUnit.Meter));
        Assert.Throws<ArgumentException>(() => Facts().Set("zone.area", 1500, ReviewUnit.None));
        Assert.Throws<ArgumentException>(() => Facts().Set("element.providedFireRating", 1, ReviewUnit.Count));
        Assert.Throws<ArgumentException>(() => Facts().Set("zone.sprinklered", "是"));
        Assert.Throws<ArgumentException>(() => Facts().MarkUnreadable("nope.field", "x"));

        var facts = Facts().MarkUnreadable("zone.area", "文字").Set("zone.area", 10, ReviewUnit.SquareMeter);
        Assert.Null(facts.GapFor("zone.area"));
        Assert.Equal(new[] { "zone.area" }, facts.KnownFields);
    }

    [Fact]
    public void The_default_catalog_holds_well_formed_unique_fields()
    {
        Assert.All(Catalog.Fields, x => Assert.Contains('.', x.Name));
        Assert.Equal(RuleValueType.Quantity(ReviewUnit.SquareMeter), Catalog.Find("zone.area")!.Type);
        Assert.Equal(RuleValueType.Quantity(ReviewUnit.Minute), Catalog.Find("element.providedFireRating")!.Type);
        Assert.Equal(new[] { RuleCategory.OpeningProtection }, Catalog.Find("opening.providedFireProtection")!.AvailableIn);

        Assert.Throws<ArgumentException>(() => new RuleFieldDefinition("area", RuleValueType.Text, "", new[] { RuleCategory.CompartmentArea }));
        Assert.Throws<ArgumentException>(() => new RuleFieldDefinition("zone.a-b", RuleValueType.Text, "", new[] { RuleCategory.CompartmentArea }));
        Assert.Throws<ArgumentException>(() => new RuleFieldDefinition("zone.a", RuleValueType.Text, "", Array.Empty<RuleCategory>()));
        Assert.Throws<ArgumentException>(() => new RuleFieldCatalog(new[]
        {
            new RuleFieldDefinition("zone.a", RuleValueType.Text, "", new[] { RuleCategory.CompartmentArea }),
            new RuleFieldDefinition("zone.a", RuleValueType.Boolean, "", new[] { RuleCategory.CompartmentArea })
        }));
    }
}
