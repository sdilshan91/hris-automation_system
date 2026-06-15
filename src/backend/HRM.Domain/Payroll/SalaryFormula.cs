using System.Globalization;
using System.Text;

namespace HRM.Domain.Payroll;

/// <summary>
/// A safe, self-contained arithmetic expression evaluator for formula-based salary components
/// (US-PAY-001 FR-4/BR-6). Supports ONLY a restricted, side-effect-free grammar:
/// numbers, named variables (component codes), the binary operators <c>+ - * /</c>, unary minus,
/// and parentheses. There are NO function calls, member access, indexing, or any construct that could
/// execute arbitrary code — the parser rejects anything outside the grammar, so "arbitrary code
/// execution is not permitted" (§10) holds by construction.
///
/// <para>
/// Chosen over an external evaluator (e.g. NCalc): adding NCalc tripped a NuGet version-conflict
/// (its transitive Microsoft.Extensions.* 9.x clashes with the pinned EFCore.NamingConventions 9.x
/// ceiling vs Identity 10.x). The formula needs here are simple arithmetic over named variables, so a
/// focused recursive-descent parser is smaller, dependency-free, fully unit-testable, and safe by
/// construction. See the payroll module note.
/// </para>
///
/// Grammar (recursive descent):
///   expr   := term (('+' | '-') term)*
///   term   := factor (('*' | '/') factor)*
///   factor := '-' factor | '(' expr ')' | number | identifier
/// Identifiers are matched case-insensitively against the supplied variable map.
/// </summary>
public static class SalaryFormula
{
    /// <summary>
    /// Validates that <paramref name="expression"/> parses under the safe grammar.
    /// Returns null on success, or a human-readable error message.
    /// </summary>
    public static string? Validate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "Formula expression is empty.";

        try
        {
            var tokens = Tokenize(expression);
            var parser = new Parser(tokens);
            parser.ParseExpression();
            parser.ExpectEnd();
            return null;
        }
        catch (FormulaException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Returns the distinct set of variable (identifier) names referenced by the expression,
    /// preserving original casing of first occurrence. Used for circular-reference detection (BR-6).
    /// Throws <see cref="FormulaException"/> if the expression is syntactically invalid.
    /// </summary>
    public static IReadOnlyCollection<string> ReferencedVariables(string expression)
    {
        var tokens = Tokenize(expression);
        var parser = new Parser(tokens);
        parser.ParseExpression();
        parser.ExpectEnd();
        return parser.Identifiers;
    }

    /// <summary>
    /// Evaluates the expression against the supplied variable values (case-insensitive variable lookup).
    /// Throws <see cref="FormulaException"/> on syntax errors, unknown variables, or division by zero.
    /// </summary>
    public static decimal Evaluate(string expression, IReadOnlyDictionary<string, decimal> variables)
    {
        var tokens = Tokenize(expression);
        var parser = new Parser(tokens, variables);
        var value = parser.ParseExpression();
        parser.ExpectEnd();
        return value;
    }

    // ── Tokenizer ────────────────────────────────────────────────────

    private enum TokenType { Number, Identifier, Plus, Minus, Star, Slash, LParen, RParen, End }

    private readonly record struct Token(TokenType Type, string Text, decimal Number);

    private static List<Token> Tokenize(string expr)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < expr.Length)
        {
            var c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '+': tokens.Add(new Token(TokenType.Plus, "+", 0)); i++; continue;
                case '-': tokens.Add(new Token(TokenType.Minus, "-", 0)); i++; continue;
                case '*': tokens.Add(new Token(TokenType.Star, "*", 0)); i++; continue;
                case '/': tokens.Add(new Token(TokenType.Slash, "/", 0)); i++; continue;
                case '(': tokens.Add(new Token(TokenType.LParen, "(", 0)); i++; continue;
                case ')': tokens.Add(new Token(TokenType.RParen, ")", 0)); i++; continue;
            }

            if (char.IsDigit(c) || c == '.')
            {
                var start = i;
                var seenDot = false;
                while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.'))
                {
                    if (expr[i] == '.')
                    {
                        if (seenDot) throw new FormulaException($"Malformed number near position {start}.");
                        seenDot = true;
                    }
                    i++;
                }
                var text = expr[start..i];
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var num))
                    throw new FormulaException($"Invalid number '{text}'.");
                tokens.Add(new Token(TokenType.Number, text, num));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                    i++;
                tokens.Add(new Token(TokenType.Identifier, expr[start..i], 0));
                continue;
            }

            throw new FormulaException($"Unexpected character '{c}' at position {i}.");
        }

        tokens.Add(new Token(TokenType.End, string.Empty, 0));
        return tokens;
    }

    // ── Parser / evaluator ───────────────────────────────────────────

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly IReadOnlyDictionary<string, decimal>? _variables;
        private int _pos;

        private readonly Dictionary<string, string> _identifiers = new(StringComparer.OrdinalIgnoreCase);

        public Parser(List<Token> tokens, IReadOnlyDictionary<string, decimal>? variables = null)
        {
            _tokens = tokens;
            _variables = variables;
        }

        /// <summary>Distinct identifiers seen, original casing of first occurrence.</summary>
        public IReadOnlyCollection<string> Identifiers => _identifiers.Values;

        private Token Current => _tokens[_pos];

        public void ExpectEnd()
        {
            if (Current.Type != TokenType.End)
                throw new FormulaException($"Unexpected token '{Current.Text}'.");
        }

        public decimal ParseExpression()
        {
            var value = ParseTerm();
            while (Current.Type is TokenType.Plus or TokenType.Minus)
            {
                var op = Current.Type;
                _pos++;
                var rhs = ParseTerm();
                value = op == TokenType.Plus ? value + rhs : value - rhs;
            }
            return value;
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (Current.Type is TokenType.Star or TokenType.Slash)
            {
                var op = Current.Type;
                _pos++;
                var rhs = ParseFactor();
                if (op == TokenType.Star)
                {
                    value *= rhs;
                }
                else
                {
                    if (rhs == 0m) throw new FormulaException("Division by zero in formula.");
                    value /= rhs;
                }
            }
            return value;
        }

        private decimal ParseFactor()
        {
            switch (Current.Type)
            {
                case TokenType.Minus:
                    _pos++;
                    return -ParseFactor();

                case TokenType.LParen:
                {
                    _pos++;
                    var value = ParseExpression();
                    if (Current.Type != TokenType.RParen)
                        throw new FormulaException("Expected ')'.");
                    _pos++;
                    return value;
                }

                case TokenType.Number:
                {
                    var n = Current.Number;
                    _pos++;
                    return n;
                }

                case TokenType.Identifier:
                {
                    var name = Current.Text;
                    _identifiers.TryAdd(name, name);
                    _pos++;
                    if (_variables is null)
                        return 0m; // validation / variable-extraction pass: value irrelevant.
                    if (!_variables.TryGetValue(name, out var v))
                        throw new FormulaException($"Unknown variable '{name}' in formula.");
                    return v;
                }

                case TokenType.End:
                    throw new FormulaException("Unexpected end of formula.");

                default:
                    throw new FormulaException($"Unexpected token '{Current.Text}'.");
            }
        }
    }

    /// <summary>Raised when a formula cannot be tokenized, parsed, or evaluated under the safe grammar.</summary>
    public sealed class FormulaException : Exception
    {
        public FormulaException(string message) : base(message) { }
    }
}
