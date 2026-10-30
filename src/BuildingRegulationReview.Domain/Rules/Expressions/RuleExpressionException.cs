using System;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>Stable codes for why an expression was refused when a rule set was compiled.</summary>
public static class RuleExpressionErrorCode
{
    /// <summary>The text is not valid DSL: a stray character, an unclosed string, a missing operand.</summary>
    public const string Syntax = "expression-syntax";

    /// <summary>A name that is not on the field whitelist, or a call, which the DSL does not have.</summary>
    public const string UnknownField = "unknown-field";

    /// <summary>A whitelisted field used by a rule category that cannot supply it (e.g. an opening field in an area rule).</summary>
    public const string FieldNotAvailable = "field-not-available";

    /// <summary>Operands of the wrong kind or unit: text compared with a number, m2 added to minutes.</summary>
    public const string TypeMismatch = "type-mismatch";

    /// <summary>A required value that is not <c>field op value</c>, or a condition that is not true/false.</summary>
    public const string InvalidForm = "invalid-form";

    /// <summary>Longer or more deeply nested than the engine agrees to read.</summary>
    public const string TooComplex = "too-complex";
}

/// <summary>
/// A rule expression that cannot be compiled. Raised only while a rule set is loaded, never while a
/// subject is evaluated, so a broken rule is stopped before it can produce a verdict.
/// </summary>
public sealed class RuleExpressionException : Exception
{
    public RuleExpressionException(string code, string message, int position)
        : base(message)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Position = position;
    }

    public string Code { get; }

    /// <summary>Zero-based character offset in the expression source where the problem was found.</summary>
    public int Position { get; }
}
