using System.Globalization;

namespace Domain.App.Models;

internal static class Editor2DDimensionExpression
{
    public static IReadOnlySet<string> ReferencedVariables(string expression)
        => TryGetReferencedVariables(expression, out var variables, out _) ? variables : new HashSet<string>();

    public static bool TryGetReferencedVariables(
        string expression,
        out IReadOnlySet<string> variables,
        out string error)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Lexer.TryTokenize(expression ?? string.Empty, out var tokens, out error))
        {
            variables = referenced;
            return false;
        }

        var parser = new Parser(tokens, name =>
        {
            referenced.Add(name);
            return (true, 1.0);
        }, validateArithmetic: false);
        if (!parser.TryParse(out _, out error))
        {
            variables = referenced;
            return false;
        }

        variables = referenced;
        return true;
    }

    public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, double> variables, out double value)
        => TryEvaluate(expression, variables, out value, out _);

    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, double> variables,
        out double value,
        out string error)
    {
        if (!Lexer.TryTokenize(expression ?? string.Empty, out var tokens, out error))
        {
            value = 0.0;
            return false;
        }

        var parser = new Parser(tokens, name =>
            variables.TryGetValue(name, out var resolved)
                ? (true, resolved)
                : (false, 0.0), validateArithmetic: true);
        return parser.TryParse(out value, out error);
    }

    private enum TokenKind
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        Caret,
        LeftParenthesis,
        RightParenthesis,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, double Number, int Position);

    private static class Lexer
    {
        public static bool TryTokenize(string text, out IReadOnlyList<Token> tokens, out string error)
        {
            var result = new List<Token>();
            var index = 0;
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }

                var position = index;
                var current = text[index];
                var kind = current switch
                {
                    '+' => TokenKind.Plus,
                    '-' => TokenKind.Minus,
                    '*' => TokenKind.Star,
                    '/' => TokenKind.Slash,
                    '^' => TokenKind.Caret,
                    '(' => TokenKind.LeftParenthesis,
                    ')' => TokenKind.RightParenthesis,
                    _ => (TokenKind?)null,
                };
                if (kind is { } punctuation)
                {
                    result.Add(new Token(punctuation, current.ToString(), 0.0, position));
                    index++;
                    continue;
                }

                if (char.IsDigit(current) || current == '.')
                {
                    if (!TryReadNumber(text, ref index, out var number, out error))
                    {
                        tokens = [];
                        return false;
                    }

                    var unitStart = index;
                    while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
                    var suffixStart = index;
                    if (index < text.Length && text[index] == '"')
                    {
                        index++;
                        number *= 25.4;
                    }
                    else
                    {
                        while (index < text.Length && char.IsLetter(text[index])) index++;
                        var suffix = text[suffixStart..index];
                        if (suffix.Length == 0)
                            index = unitStart;
                        else if (TryGetUnitScale(suffix, out var scale))
                            number *= scale;
                        else
                        {
                            tokens = [];
                            error = $"Unsupported unit '{suffix}' at position {suffixStart + 1}.";
                            return false;
                        }
                    }

                    if (!double.IsFinite(number))
                    {
                        tokens = [];
                        error = $"Number at position {position + 1} is outside the supported range.";
                        return false;
                    }
                    result.Add(new Token(TokenKind.Number, text[position..unitStart], number, position));
                    continue;
                }

                if (char.IsLetter(current) || current == '_')
                {
                    index++;
                    while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_')) index++;
                    result.Add(new Token(TokenKind.Identifier, text[position..index], 0.0, position));
                    continue;
                }

                tokens = [];
                error = $"Unexpected character '{current}' at position {position + 1}.";
                return false;
            }

            result.Add(new Token(TokenKind.End, string.Empty, 0.0, text.Length));
            tokens = result;
            error = string.Empty;
            return true;
        }

        private static bool TryReadNumber(string text, ref int index, out double value, out string error)
        {
            var start = index;
            var digitsBefore = 0;
            while (index < text.Length && char.IsDigit(text[index])) { index++; digitsBefore++; }
            var digitsAfter = 0;
            if (index < text.Length && text[index] == '.')
            {
                index++;
                while (index < text.Length && char.IsDigit(text[index])) { index++; digitsAfter++; }
            }
            if (digitsBefore + digitsAfter == 0)
            {
                value = 0.0;
                error = $"Invalid number at position {start + 1}.";
                return false;
            }

            if (index < text.Length && text[index] is 'e' or 'E')
            {
                index++;
                if (index < text.Length && text[index] is '+' or '-') index++;
                var exponentStart = index;
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (index == exponentStart)
                {
                    value = 0.0;
                    error = $"Scientific notation at position {start + 1} needs an exponent.";
                    return false;
                }
            }

            if (!double.TryParse(text[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                error = $"Invalid number at position {start + 1}.";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }

    private sealed class Parser(
        IReadOnlyList<Token> tokens,
        Func<string, (bool Found, double Value)> resolveVariable,
        bool validateArithmetic)
    {
        private int _index;

        public bool TryParse(out double value, out string error)
        {
            if (Current.Kind == TokenKind.End)
            {
                value = 0.0;
                error = "Expression is empty.";
                return false;
            }
            if (!ParseExpression(out value, out error)) return false;
            if (Current.Kind != TokenKind.End)
            {
                error = $"Unexpected token '{Current.Text}' at position {Current.Position + 1}.";
                value = 0.0;
                return false;
            }
            if (validateArithmetic && !double.IsFinite(value))
            {
                error = "Expression result is outside the supported range.";
                value = 0.0;
                return false;
            }
            return true;
        }

        private bool ParseExpression(out double value, out string error)
        {
            if (!ParseTerm(out value, out error)) return false;
            while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var operation = Advance().Kind;
                if (!ParseTerm(out var right, out error)) return false;
                value = operation == TokenKind.Plus ? value + right : value - right;
                if (!CheckFinite(value, out error)) return false;
            }
            error = string.Empty;
            return true;
        }

        private bool ParseTerm(out double value, out string error)
        {
            if (!ParsePower(out value, out error)) return false;
            while (Current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var operation = Advance().Kind;
                if (!ParsePower(out var right, out error)) return false;
                if (validateArithmetic && operation == TokenKind.Slash && Math.Abs(right) < 1e-12)
                {
                    value = 0.0;
                    error = "Division by zero.";
                    return false;
                }
                value = operation == TokenKind.Star ? value * right : validateArithmetic ? value / right : 1.0;
                if (!CheckFinite(value, out error)) return false;
            }
            error = string.Empty;
            return true;
        }

        private bool ParsePower(out double value, out string error)
        {
            if (!ParseUnary(out value, out error)) return false;
            if (Current.Kind == TokenKind.Caret)
            {
                Advance();
                if (!ParsePower(out var exponent, out error)) return false;
                value = validateArithmetic ? Math.Pow(value, exponent) : 1.0;
                if (!CheckFinite(value, out error, "Power has no finite real result.")) return false;
            }
            error = string.Empty;
            return true;
        }

        private bool ParseUnary(out double value, out string error)
        {
            if (Current.Kind == TokenKind.Plus)
            {
                Advance();
                return ParseUnary(out value, out error);
            }
            if (Current.Kind == TokenKind.Minus)
            {
                Advance();
                if (!ParseUnary(out value, out error)) return false;
                value = -value;
                return CheckFinite(value, out error);
            }
            return ParseAtom(out value, out error);
        }

        private bool ParseAtom(out double value, out string error)
        {
            if (Current.Kind == TokenKind.Number)
            {
                value = Advance().Number;
                error = string.Empty;
                return true;
            }
            if (Current.Kind == TokenKind.LeftParenthesis)
            {
                Advance();
                if (!ParseExpression(out value, out error)) return false;
                if (Current.Kind != TokenKind.RightParenthesis)
                {
                    value = 0.0;
                    error = $"Missing ')' before position {Current.Position + 1}.";
                    return false;
                }
                Advance();
                return true;
            }
            if (Current.Kind == TokenKind.Identifier)
            {
                var identifier = Advance();
                if (identifier.Text == "sqrt")
                {
                    if (Current.Kind != TokenKind.LeftParenthesis)
                    {
                        value = 0.0;
                        error = "sqrt requires parentheses.";
                        return false;
                    }
                    Advance();
                    if (!ParseExpression(out var argument, out error)) { value = 0.0; return false; }
                    if (Current.Kind != TokenKind.RightParenthesis)
                    {
                        value = 0.0;
                        error = $"Missing ')' after sqrt argument at position {Current.Position + 1}.";
                        return false;
                    }
                    Advance();
                    if (validateArithmetic && argument < 0.0)
                    {
                        value = 0.0;
                        error = "sqrt requires a non-negative value.";
                        return false;
                    }
                    value = validateArithmetic ? Math.Sqrt(argument) : 1.0;
                    return CheckFinite(value, out error);
                }
                if (Current.Kind == TokenKind.LeftParenthesis)
                {
                    value = 0.0;
                    error = $"Unknown function '{identifier.Text}'.";
                    return false;
                }
                if (TryGetUnitScale(identifier.Text, out _))
                {
                    value = 0.0;
                    error = $"Unit '{identifier.Text}' must follow a number.";
                    return false;
                }
                var resolved = resolveVariable(identifier.Text);
                if (!resolved.Found)
                {
                    value = 0.0;
                    error = $"Unknown variable '{identifier.Text}'.";
                    return false;
                }
                value = resolved.Value;
                return CheckFinite(value, out error, $"Variable '{identifier.Text}' is not finite.");
            }

            value = 0.0;
            error = Current.Kind == TokenKind.End
                ? "Unexpected end of expression."
                : $"Unexpected token '{Current.Text}' at position {Current.Position + 1}.";
            return false;
        }

        private bool CheckFinite(double value, out string error, string? message = null)
        {
            if (!validateArithmetic || double.IsFinite(value))
            {
                error = string.Empty;
                return true;
            }
            error = message ?? "Expression result is outside the supported range.";
            return false;
        }

        private Token Current => tokens[Math.Min(_index, tokens.Count - 1)];

        private Token Advance() => tokens[_index++];
    }

    private static bool TryGetUnitScale(string suffix, out double scale)
    {
        scale = suffix.ToLowerInvariant() switch
        {
            "mm" => 1.0,
            "cm" => 10.0,
            "m" => 1000.0,
            "in" or "inch" or "inches" => 25.4,
            _ => 0.0,
        };
        return scale > 0.0;
    }
}
