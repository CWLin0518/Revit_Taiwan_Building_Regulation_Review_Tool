using System;
using System.Collections.Generic;
using System.Linq;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>
/// A compiled, type-checked rule expression. The tree only holds literals, whitelisted fields and
/// the fixed operators below; evaluating it reads <see cref="RuleFacts"/> and nothing else, so a
/// rule file can never reach code, files or the Revit model.
/// </summary>
public abstract class RuleExpressionNode
{
    private protected RuleExpressionNode(RuleValueType type, int position)
    {
        Type = type;
        Position = position;
    }

    public RuleValueType Type { get; }

    /// <summary>Where the node starts in the source text, for error messages.</summary>
    public int Position { get; }

    public abstract RuleEvaluation Evaluate(RuleFacts facts);

    /// <summary>Every whitelisted field the expression reads, in first-use order.</summary>
    public IReadOnlyList<RuleFieldDefinition> Fields
    {
        get
        {
            var fields = new List<RuleFieldDefinition>();
            CollectFields(fields);
            return fields.Distinct().ToList();
        }
    }

    internal abstract void CollectFields(List<RuleFieldDefinition> fields);

    /// <summary>A canonical, fully parenthesised form: two expressions that mean the same tree print the same.</summary>
    public abstract override string ToString();

    private protected static RuleExpressionException Mismatch(int position, string message) =>
        new(RuleExpressionErrorCode.TypeMismatch, message, position);
}

internal sealed class LiteralNode : RuleExpressionNode
{
    public LiteralNode(ReviewValue value, int position) : base(RuleValueType.Of(value), position) => Value = value;

    public ReviewValue Value { get; }

    public override RuleEvaluation Evaluate(RuleFacts facts) => RuleEvaluation.Known(Value);
    internal override void CollectFields(List<RuleFieldDefinition> fields) { }

    public override string ToString() => Value.Kind == ReviewValueKind.Text
        ? "\"" + Value.Text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
        : RuleUnits.Format(Value);
}

internal sealed class FieldNode : RuleExpressionNode
{
    public FieldNode(RuleFieldDefinition field, int position) : base(field.Type, position) => Field = field;

    public RuleFieldDefinition Field { get; }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var value = facts.Find(Field.Name);
        if (value is not null) return RuleEvaluation.Known(value);
        return RuleEvaluation.Unknown(new[] { facts.GapFor(Field.Name)! });
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields) => fields.Add(Field);
    public override string ToString() => Field.Name;
}

internal sealed class NotNode : RuleExpressionNode
{
    private readonly RuleExpressionNode _operand;

    public NotNode(RuleExpressionNode operand, int position) : base(RuleValueType.Boolean, position)
    {
        if (!operand.Type.IsBoolean) throw Mismatch(position, $"「!」只能用在是非值，這裡是{operand.Type}。");
        _operand = operand;
    }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var value = _operand.Evaluate(facts);
        return value.IsKnown ? RuleEvaluation.Known(ReviewValue.OfBoolean(!value.Value!.Flag)) : value;
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields) => _operand.CollectFields(fields);
    public override string ToString() => $"!{_operand}";
}

internal sealed class NegateNode : RuleExpressionNode
{
    private readonly RuleExpressionNode _operand;

    public NegateNode(RuleExpressionNode operand, int position) : base(Checked(operand, position), position) => _operand = operand;

    private static RuleValueType Checked(RuleExpressionNode operand, int position) =>
        operand.Type.IsQuantity ? operand.Type : throw Mismatch(position, $"「-」只能用在數值，這裡是{operand.Type}。");

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var value = _operand.Evaluate(facts);
        return value.IsKnown ? RuleEvaluation.Known(ReviewValue.Quantity(-value.Value!.Number, Type.Unit)) : value;
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields) => _operand.CollectFields(fields);
    public override string ToString() => $"-{_operand}";
}

internal enum LogicalOperator { And, Or }

/// <summary>
/// <c>&amp;&amp;</c> and <c>||</c> with three-valued logic: <c>false &amp;&amp; unknown</c> is false and
/// <c>true || unknown</c> is true, because the missing field cannot change those answers.
/// </summary>
internal sealed class LogicalNode : RuleExpressionNode
{
    private readonly LogicalOperator _operator;
    private readonly RuleExpressionNode _left;
    private readonly RuleExpressionNode _right;

    public LogicalNode(LogicalOperator op, RuleExpressionNode left, RuleExpressionNode right, int position)
        : base(RuleValueType.Boolean, position)
    {
        var symbol = op == LogicalOperator.And ? "&&" : "||";
        if (!left.Type.IsBoolean || !right.Type.IsBoolean)
            throw Mismatch(position, $"「{symbol}」兩邊都必須是是非值，這裡是{left.Type}與{right.Type}。");

        _operator = op;
        _left = left;
        _right = right;
    }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var decisive = _operator == LogicalOperator.Or;
        var left = _left.Evaluate(facts);
        if (left.IsKnown && left.Value!.Flag == decisive) return left;

        var right = _right.Evaluate(facts);
        if (right.IsKnown && right.Value!.Flag == decisive) return right;

        return RuleEvaluation.Blocking(left, right) ?? RuleEvaluation.Known(ReviewValue.OfBoolean(!decisive));
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields)
    {
        _left.CollectFields(fields);
        _right.CollectFields(fields);
    }

    public override string ToString() => $"({_left} {(_operator == LogicalOperator.And ? "&&" : "||")} {_right})";
}

public enum RuleComparator
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual
}

public static class RuleComparatorText
{
    public static string Symbol(RuleComparator comparator) => comparator switch
    {
        RuleComparator.Equal => "==",
        RuleComparator.NotEqual => "!=",
        RuleComparator.Less => "<",
        RuleComparator.LessOrEqual => "<=",
        RuleComparator.Greater => ">",
        RuleComparator.GreaterOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(comparator))
    };

    internal static bool IsEquality(RuleComparator comparator) =>
        comparator == RuleComparator.Equal || comparator == RuleComparator.NotEqual;

    /// <summary>Compares two values of the same type; quantities use <see cref="RuleUnits.AreEqual"/>.</summary>
    public static bool Holds(RuleComparator comparator, ReviewValue left, ReviewValue right)
    {
        if (left is null) throw new ArgumentNullException(nameof(left));
        if (right is null) throw new ArgumentNullException(nameof(right));

        if (left.Kind != ReviewValueKind.Quantity)
        {
            var same = left.Kind == ReviewValueKind.Text
                ? string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                : left.Flag == right.Flag;
            return comparator switch
            {
                RuleComparator.Equal => same,
                RuleComparator.NotEqual => !same,
                _ => throw new InvalidOperationException($"{comparator} cannot compare {left.Kind} values.")
            };
        }

        var equal = RuleUnits.AreEqual(left.Number, right.Number);
        return comparator switch
        {
            RuleComparator.Equal => equal,
            RuleComparator.NotEqual => !equal,
            RuleComparator.Less => !equal && left.Number < right.Number,
            RuleComparator.LessOrEqual => equal || left.Number < right.Number,
            RuleComparator.Greater => !equal && left.Number > right.Number,
            RuleComparator.GreaterOrEqual => equal || left.Number > right.Number,
            _ => throw new ArgumentOutOfRangeException(nameof(comparator))
        };
    }
}

internal sealed class ComparisonNode : RuleExpressionNode
{
    public ComparisonNode(RuleComparator comparator, RuleExpressionNode left, RuleExpressionNode right, int position)
        : base(RuleValueType.Boolean, position)
    {
        var symbol = RuleComparatorText.Symbol(comparator);
        if (left.Type != right.Type)
            throw Mismatch(position, $"「{symbol}」兩邊的型別或單位不同：{left.Type}與{right.Type}。數值請寫明單位，例如 1500 m2。");
        if (!RuleComparatorText.IsEquality(comparator) && !left.Type.IsQuantity)
            throw Mismatch(position, $"「{symbol}」只能比較數值，這裡是{left.Type}。");

        Comparator = comparator;
        Left = left;
        Right = right;
    }

    public RuleComparator Comparator { get; }
    public RuleExpressionNode Left { get; }
    public RuleExpressionNode Right { get; }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var left = Left.Evaluate(facts);
        var right = Right.Evaluate(facts);
        return RuleEvaluation.Blocking(left, right)
            ?? RuleEvaluation.Known(ReviewValue.OfBoolean(RuleComparatorText.Holds(Comparator, left.Value!, right.Value!)));
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields)
    {
        Left.CollectFields(fields);
        Right.CollectFields(fields);
    }

    public override string ToString() => $"({Left} {RuleComparatorText.Symbol(Comparator)} {Right})";
}

internal enum ArithmeticOperator { Add, Subtract, Multiply, Divide }

internal sealed class ArithmeticNode : RuleExpressionNode
{
    private readonly ArithmeticOperator _operator;
    private readonly RuleExpressionNode _left;
    private readonly RuleExpressionNode _right;

    public ArithmeticNode(ArithmeticOperator op, RuleExpressionNode left, RuleExpressionNode right, int position)
        : base(ResultType(op, left, right, position), position)
    {
        _operator = op;
        _left = left;
        _right = right;
    }

    private static string Symbol(ArithmeticOperator op) => op switch
    {
        ArithmeticOperator.Add => "+",
        ArithmeticOperator.Subtract => "-",
        ArithmeticOperator.Multiply => "*",
        _ => "/"
    };

    private static RuleValueType ResultType(ArithmeticOperator op, RuleExpressionNode left, RuleExpressionNode right, int position)
    {
        var symbol = Symbol(op);
        if (!left.Type.IsQuantity || !right.Type.IsQuantity)
            throw Mismatch(position, $"「{symbol}」只能用在數值，這裡是{left.Type}與{right.Type}。");

        ReviewUnit? unit = op switch
        {
            ArithmeticOperator.Add or ArithmeticOperator.Subtract => left.Type.Unit == right.Type.Unit ? left.Type.Unit : null,
            ArithmeticOperator.Multiply => RuleUnits.Multiply(left.Type.Unit, right.Type.Unit),
            _ => RuleUnits.Divide(left.Type.Unit, right.Type.Unit)
        };

        return unit is { } known
            ? RuleValueType.Quantity(known)
            : throw Mismatch(position, $"{left.Type} 與 {right.Type} 不能做「{symbol}」運算，單位不相容。");
    }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var left = _left.Evaluate(facts);
        var right = _right.Evaluate(facts);
        var blocked = RuleEvaluation.Blocking(left, right);
        if (blocked is not null) return blocked;

        var a = left.Value!.Number;
        var b = right.Value!.Number;
        if (_operator == ArithmeticOperator.Divide && b == 0)
            return RuleEvaluation.Failed($"運算「{this}」除以零。");

        var result = _operator switch
        {
            ArithmeticOperator.Add => a + b,
            ArithmeticOperator.Subtract => a - b,
            ArithmeticOperator.Multiply => a * b,
            _ => a / b
        };

        return double.IsNaN(result) || double.IsInfinity(result)
            ? RuleEvaluation.Failed($"運算「{this}」的結果超出數值範圍。")
            : RuleEvaluation.Known(ReviewValue.Quantity(result, Type.Unit));
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields)
    {
        _left.CollectFields(fields);
        _right.CollectFields(fields);
    }

    public override string ToString() => $"({_left} {Symbol(_operator)} {_right})";
}

/// <summary>
/// <c>condition ? a : b</c>. An unknown condition still yields a value when both branches agree, since
/// the missing field cannot change the answer.
/// </summary>
internal sealed class ConditionalNode : RuleExpressionNode
{
    private readonly RuleExpressionNode _condition;
    private readonly RuleExpressionNode _whenTrue;
    private readonly RuleExpressionNode _whenFalse;

    public ConditionalNode(RuleExpressionNode condition, RuleExpressionNode whenTrue, RuleExpressionNode whenFalse, int position)
        : base(Checked(condition, whenTrue, whenFalse, position), position)
    {
        _condition = condition;
        _whenTrue = whenTrue;
        _whenFalse = whenFalse;
    }

    private static RuleValueType Checked(RuleExpressionNode condition, RuleExpressionNode whenTrue, RuleExpressionNode whenFalse, int position)
    {
        if (!condition.Type.IsBoolean)
            throw Mismatch(condition.Position, $"「? :」的條件必須是是非值，這裡是{condition.Type}。");
        if (whenTrue.Type != whenFalse.Type)
            throw Mismatch(position, $"「? :」兩個分支的型別或單位不同：{whenTrue.Type}與{whenFalse.Type}。");
        return whenTrue.Type;
    }

    public override RuleEvaluation Evaluate(RuleFacts facts)
    {
        var condition = _condition.Evaluate(facts);
        if (condition.IsKnown) return condition.Value!.Flag ? _whenTrue.Evaluate(facts) : _whenFalse.Evaluate(facts);
        if (condition.IsFailed) return condition;

        var whenTrue = _whenTrue.Evaluate(facts);
        var whenFalse = _whenFalse.Evaluate(facts);
        if (whenTrue.IsKnown && whenFalse.IsKnown && RuleComparatorText.Holds(RuleComparator.Equal, whenTrue.Value!, whenFalse.Value!))
            return whenTrue;

        // The branch that will be taken is unknown, so even a failing branch only makes the answer unknown.
        return RuleEvaluation.Unknown(condition.Gaps.Concat(whenTrue.Gaps).Concat(whenFalse.Gaps));
    }

    internal override void CollectFields(List<RuleFieldDefinition> fields)
    {
        _condition.CollectFields(fields);
        _whenTrue.CollectFields(fields);
        _whenFalse.CollectFields(fields);
    }

    public override string ToString() => $"({_condition} ? {_whenTrue} : {_whenFalse})";
}
