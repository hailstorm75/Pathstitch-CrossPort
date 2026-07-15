using System.Globalization;

namespace Domain.App.Models;

internal static class Editor2DDimensionExpression
{
    public static bool TryEvaluate(string expression, IReadOnlyDictionary<string, double> variables, out double value)
    {
        var parser = new Parser(expression, variables);
        return parser.TryParse(out value) && parser.AtEnd && double.IsFinite(value);
    }

    private sealed class Parser(string text, IReadOnlyDictionary<string, double> variables)
    {
        private int _index;

        public bool AtEnd
        {
            get
            {
                SkipWhitespace();
                return _index == text.Length;
            }
        }

        public bool TryParse(out double value)
        {
            if (!ParseExpression(out value))
            {
                value = 0;
                return false;
            }
            return true;
        }

        private bool ParseExpression(out double value)
        {
            if (!ParseTerm(out value)) return false;
            while (true)
            {
                SkipWhitespace();
                if (!TryRead('+') && !TryRead('-')) return true;
                var op = text[_index - 1];
                if (!ParseTerm(out var right)) return false;
                value = op == '+' ? value + right : value - right;
            }
        }

        private bool ParseTerm(out double value)
        {
            if (!ParseFactor(out value)) return false;
            while (true)
            {
                SkipWhitespace();
                if (!TryRead('*') && !TryRead('/')) return true;
                var op = text[_index - 1];
                if (!ParseFactor(out var right) || (op == '/' && Math.Abs(right) <= double.Epsilon)) return false;
                value = op == '*' ? value * right : value / right;
            }
        }

        private bool ParseFactor(out double value)
        {
            SkipWhitespace();
            if (TryRead('+')) return ParseFactor(out value);
            if (TryRead('-'))
            {
                if (!ParseFactor(out value)) return false;
                value = -value;
                return true;
            }
            if (TryRead('('))
            {
                if (!ParseExpression(out value) || !TryRead(')')) return false;
                return true;
            }

            var start = _index;
            while (_index < text.Length && (char.IsLetterOrDigit(text[_index]) || text[_index] is '.' or '_')) _index++;
            if (start == _index) { value = 0; return false; }
            var token = text[start.._index];
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return variables.TryGetValue(token, out value);
        }

        private bool TryRead(char expected)
        {
            SkipWhitespace();
            if (_index >= text.Length || text[_index] != expected) return false;
            _index++;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < text.Length && char.IsWhiteSpace(text[_index])) _index++;
        }
    }
}
