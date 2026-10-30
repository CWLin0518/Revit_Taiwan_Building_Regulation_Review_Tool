using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BuildingRegulationReview.Domain.Reviews;

namespace BuildingRegulationReview.Domain.Rules.Expressions;

/// <summary>
/// A requirement compiled from a rule's <c>requiredValue</c>: <c>actualField comparator required</c>,
/// e.g. <c>zone.area &lt;= (zone.sprinklered ? 3000 m2 : 1500 m2)</c>. The left side names where the
/// actual value comes from; the right side is what the engine computes as the required value.
/// </summary>
public sealed class RuleRequirement
{
    internal RuleRequirement(RuleFieldDefinition actual, RuleComparator comparator, RuleExpressionNode required)
    {
        Actual = actual;
        Comparator = comparator;
        Required = required;
    }

    public RuleFieldDefinition Actual { get; }
    public RuleComparator Comparator { get; }
    public RuleExpressionNode Required { get; }

    public override string ToString() => $"{Actual.Name} {RuleComparatorText.Symbol(Comparator)} {Required}";
}

/// <summary>
/// The hand-written parser of the restricted rule DSL (spec 11.2). It knows literals, whitelisted
/// fields, <c>! - * / + - &lt; &lt;= &gt; &gt;= == != &amp;&amp; || ? :</c> and parentheses — nothing else.
/// There is no function call, assignment, member access, indexing or statement, so no rule text can
/// run code; anything unrecognised is refused with a position.
/// </summary>
/// <remarks>
/// Grammar, lowest precedence first:
/// <code>
/// conditional := or ('?' conditional ':' conditional)?
/// or          := and ('||' and)*
/// and         := equality ('&amp;&amp;' equality)*
/// equality    := relational (('==' | '!=') relational)?
/// relational  := additive (('&lt;' | '&lt;=' | '&gt;' | '&gt;=') additive)?
/// additive    := term (('+' | '-') term)*
/// term        := unary (('*' | '/') unary)*
/// unary       := ('!' | '-') unary | primary
/// primary     := number unit? | "text" | true | false | scope.field | '(' conditional ')'
/// </code>
/// Comparisons do not chain (<c>a &lt; b &lt; c</c> is refused) and a number compared with a quantity
/// must spell its unit: <c>zone.area &lt;= 1500</c> is a type error, <c>zone.area &lt;= 1500 m2</c> is not.
/// </remarks>
public static class RuleExpressionParser
{
    public const int MaxLength = 1000;
    public const int MaxDepth = 32;

    /// <summary>Compiles a true/false expression such as <c>appliesWhen</c> or an exemption.</summary>
    public static RuleExpressionNode ParseCondition(string source, RuleFieldCatalog catalog, RuleCategory category)
    {
        var node = Parse(source, catalog, category);
        if (!node.Type.IsBoolean)
            throw new RuleExpressionException(RuleExpressionErrorCode.InvalidForm,
                $"條件必須得到是非值（true／false），這裡得到{node.Type}。", 0);
        return node;
    }

    /// <summary>Compiles a <c>requiredValue</c>, which must be <c>field comparator expression</c>.</summary>
    public static RuleRequirement ParseRequirement(string source, RuleFieldCatalog catalog, RuleCategory category)
    {
        var node = Parse(source, catalog, category);
        if (node is not ComparisonNode { Left: FieldNode actual } comparison)
            throw new RuleExpressionException(RuleExpressionErrorCode.InvalidForm,
                "要求值必須寫成「實際值欄位 比較運算子 要求值」，例如 element.providedFireRating >= 60 min。", 0);

        foreach (var field in comparison.Right.Fields)
            if (ReferenceEquals(field, actual.Field))
                throw new RuleExpressionException(RuleExpressionErrorCode.InvalidForm,
                    $"要求值不可再引用實際值欄位 {actual.Field.Name}。", comparison.Right.Position);

        return new RuleRequirement(actual.Field, comparison.Comparator, comparison.Right);
    }

    private static RuleExpressionNode Parse(string source, RuleFieldCatalog catalog, RuleCategory category)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (string.IsNullOrWhiteSpace(source))
            throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, "運算式是空的。", 0);
        if (source.Length > MaxLength)
            throw new RuleExpressionException(RuleExpressionErrorCode.TooComplex, $"運算式超過 {MaxLength} 個字元。", MaxLength);

        var parser = new Parser(Lexer.Tokenize(source), catalog, category);
        return parser.ParseAll();
    }

    private enum TokenKind { Number, Text, Identifier, Operator, End }

    private readonly struct Token
    {
        public Token(TokenKind kind, string text, int position, double number = 0)
        {
            Kind = kind;
            Text = text;
            Position = position;
            Number = number;
        }

        public TokenKind Kind { get; }
        public string Text { get; }
        public int Position { get; }
        public double Number { get; }

        public bool Is(string op) => Kind == TokenKind.Operator && Text == op;
        public string Describe() => Kind == TokenKind.End ? "運算式結尾" : $"「{Text}」";
    }

    private static class Lexer
    {
        private static readonly string[] Operators = { "==", "!=", "<=", ">=", "&&", "||", "<", ">", "!", "+", "-", "*", "/", "?", ":", "(", ")" };

        public static List<Token> Tokenize(string source)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < source.Length)
            {
                var c = source[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c)) { tokens.Add(ReadNumber(source, ref i)); continue; }
                if (c == '"') { tokens.Add(ReadText(source, ref i)); continue; }
                if (c == '㎡') { tokens.Add(new Token(TokenKind.Identifier, "㎡", i)); i++; continue; }
                if (char.IsLetter(c) || c == '_') { tokens.Add(ReadIdentifier(source, ref i)); continue; }

                var op = Array.Find(Operators, x => string.CompareOrdinal(source, i, x, 0, x.Length) == 0);
                if (op is not null)
                {
                    tokens.Add(new Token(TokenKind.Operator, op, i));
                    i += op.Length;
                    continue;
                }

                var hint = c switch
                {
                    '=' => "；相等請用「==」",
                    '&' => "；「且」請用「&&」",
                    '|' => "；「或」請用「||」",
                    '\'' => "；文字請用雙引號",
                    _ => string.Empty
                };
                throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, $"不允許的字元「{c}」{hint}。", i);
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, source.Length));
            return tokens;
        }

        private static Token ReadNumber(string source, ref int i)
        {
            var start = i;
            while (i < source.Length && char.IsDigit(source[i])) i++;
            if (i < source.Length && source[i] == '.')
            {
                i++;
                if (i >= source.Length || !char.IsDigit(source[i]))
                    throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, "小數點後面必須有數字。", i);
                while (i < source.Length && char.IsDigit(source[i])) i++;
            }

            var text = source.Substring(start, i - start);
            var number = double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (double.IsInfinity(number))
                throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, $"數字「{text}」太大。", start);
            return new Token(TokenKind.Number, text, start, number);
        }

        private static Token ReadText(string source, ref int i)
        {
            var start = i++;
            var text = new StringBuilder();
            while (i < source.Length && source[i] != '"')
            {
                if (source[i] == '\\')
                {
                    if (i + 1 >= source.Length || (source[i + 1] != '"' && source[i + 1] != '\\'))
                        throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, "文字裡只能用「\\\"」與「\\\\」兩種跳脫。", i);
                    i++;
                }

                text.Append(source[i++]);
            }

            if (i >= source.Length)
                throw new RuleExpressionException(RuleExpressionErrorCode.Syntax, "文字缺少結尾的雙引號。", start);
            i++;
            return new Token(TokenKind.Text, text.ToString(), start);
        }

        private static Token ReadIdentifier(string source, ref int i)
        {
            var start = i;
            while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.')) i++;
            return new Token(TokenKind.Identifier, source.Substring(start, i - start), start);
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly RuleFieldCatalog _catalog;
        private readonly RuleCategory _category;
        private int _index;
        private int _depth;

        public Parser(List<Token> tokens, RuleFieldCatalog catalog, RuleCategory category)
        {
            _tokens = tokens;
            _catalog = catalog;
            _category = category;
        }

        private Token Current => _tokens[_index];

        public RuleExpressionNode ParseAll()
        {
            var node = ParseConditional();
            if (Current.Kind != TokenKind.End)
                throw Syntax(Current, $"多出了{Current.Describe()}；前面的運算式已經結束。");
            return node;
        }

        private RuleExpressionNode ParseConditional()
        {
            Enter();
            var condition = ParseOr();
            if (Current.Is("?"))
            {
                var question = Next();
                var whenTrue = ParseConditional();
                Expect(":", "「? :」缺少「:」。");
                var whenFalse = ParseConditional();
                condition = new ConditionalNode(condition, whenTrue, whenFalse, question.Position);
            }

            _depth--;
            return condition;
        }

        private RuleExpressionNode ParseOr()
        {
            var left = ParseAnd();
            while (Current.Is("||"))
            {
                var op = Next();
                left = new LogicalNode(LogicalOperator.Or, left, ParseAnd(), op.Position);
            }

            return left;
        }

        private RuleExpressionNode ParseAnd()
        {
            var left = ParseEquality();
            while (Current.Is("&&"))
            {
                var op = Next();
                left = new LogicalNode(LogicalOperator.And, left, ParseEquality(), op.Position);
            }

            return left;
        }

        private RuleExpressionNode ParseEquality()
        {
            var left = ParseRelational();
            if (!Current.Is("==") && !Current.Is("!=")) return left;

            var op = Next();
            var node = new ComparisonNode(op.Text == "==" ? RuleComparator.Equal : RuleComparator.NotEqual, left, ParseRelational(), op.Position);
            if (Current.Is("==") || Current.Is("!="))
                throw Syntax(Current, "比較不能連寫；請用「&&」連接兩個比較。");
            return node;
        }

        private RuleExpressionNode ParseRelational()
        {
            var left = ParseAdditive();
            var comparator = Current.Text switch
            {
                "<" => RuleComparator.Less,
                "<=" => RuleComparator.LessOrEqual,
                ">" => RuleComparator.Greater,
                ">=" => RuleComparator.GreaterOrEqual,
                _ => (RuleComparator?)null
            };
            if (Current.Kind != TokenKind.Operator || comparator is null) return left;

            var op = Next();
            var node = new ComparisonNode(comparator.Value, left, ParseAdditive(), op.Position);
            if (Current.Is("<") || Current.Is("<=") || Current.Is(">") || Current.Is(">="))
                throw Syntax(Current, "比較不能連寫；請用「&&」連接兩個比較。");
            return node;
        }

        private RuleExpressionNode ParseAdditive()
        {
            var left = ParseTerm();
            while (Current.Is("+") || Current.Is("-"))
            {
                var op = Next();
                left = new ArithmeticNode(op.Text == "+" ? ArithmeticOperator.Add : ArithmeticOperator.Subtract, left, ParseTerm(), op.Position);
            }

            return left;
        }

        private RuleExpressionNode ParseTerm()
        {
            var left = ParseUnary();
            while (Current.Is("*") || Current.Is("/"))
            {
                var op = Next();
                left = new ArithmeticNode(op.Text == "*" ? ArithmeticOperator.Multiply : ArithmeticOperator.Divide, left, ParseUnary(), op.Position);
            }

            return left;
        }

        private RuleExpressionNode ParseUnary()
        {
            if (!Current.Is("!") && !Current.Is("-")) return ParsePrimary();

            Enter();
            var op = Next();
            var operand = ParseUnary();
            _depth--;
            return op.Text == "!" ? new NotNode(operand, op.Position) : new NegateNode(operand, op.Position);
        }

        private RuleExpressionNode ParsePrimary()
        {
            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Number:
                    Next();
                    return ParseQuantity(token);

                case TokenKind.Text:
                    Next();
                    return new LiteralNode(ReviewValue.OfText(token.Text), token.Position);

                case TokenKind.Identifier:
                    Next();
                    return ParseName(token);

                case TokenKind.Operator when token.Is("("):
                    Next();
                    var inner = ParseConditional();
                    Expect(")", "缺少對應的「)」。");
                    return inner;

                default:
                    throw Syntax(token, $"這裡需要一個值，卻是{token.Describe()}。");
            }
        }

        private RuleExpressionNode ParseQuantity(Token number)
        {
            if (Current.Kind == TokenKind.Identifier && !Current.Text.Contains("."))
            {
                if (!RuleUnits.TryGetSuffix(Current.Text, out var unit, out var factor))
                    throw Syntax(Current, $"不認得的單位「{Current.Text}」；可用單位：{string.Join("、", RuleUnits.LiteralSuffixes)}。");
                Next();
                return new LiteralNode(ReviewValue.Quantity(number.Number * factor, unit), number.Position);
            }

            return new LiteralNode(ReviewValue.Quantity(number.Number, ReviewUnit.None), number.Position);
        }

        private RuleExpressionNode ParseName(Token name)
        {
            if (name.Text == "true" || name.Text == "false")
                return new LiteralNode(ReviewValue.OfBoolean(name.Text == "true"), name.Position);

            if (Current.Is("("))
                throw new RuleExpressionException(RuleExpressionErrorCode.UnknownField,
                    $"不支援函式呼叫「{name.Text}(...)」；規則只能讀取白名單欄位。", name.Position);

            var field = _catalog.Find(name.Text);
            if (field is null)
                throw new RuleExpressionException(RuleExpressionErrorCode.UnknownField,
                    $"「{name.Text}」不是白名單欄位。", name.Position);
            if (!field.IsAvailableIn(_category))
                throw new RuleExpressionException(RuleExpressionErrorCode.FieldNotAvailable,
                    $"欄位 {field.Name} 不適用於 {_category} 規則。", name.Position);

            return new FieldNode(field, name.Position);
        }

        private Token Next()
        {
            var token = Current;
            if (token.Kind != TokenKind.End) _index++;
            return token;
        }

        private void Expect(string op, string message)
        {
            if (!Current.Is(op)) throw Syntax(Current, message);
            Next();
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
                throw new RuleExpressionException(RuleExpressionErrorCode.TooComplex, $"運算式巢狀超過 {MaxDepth} 層。", Current.Position);
        }

        private static RuleExpressionException Syntax(Token at, string message) =>
            new(RuleExpressionErrorCode.Syntax, message, at.Position);
    }
}
