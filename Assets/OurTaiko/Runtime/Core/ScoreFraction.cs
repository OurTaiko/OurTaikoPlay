using System;
using System.Globalization;
using System.Numerics;

namespace OurTaiko
{
    // Authored beat fractions must survive until floor(c*T). In particular, neither repeated
    // binary additions nor an epsilon may turn six 333333-us beats into two seconds.
    internal readonly struct ScoreFraction
    {
        readonly BigInteger numerator, denominator;
        BigInteger Denominator => denominator.IsZero ? BigInteger.One : denominator;
        public bool IsInteger => numerator % Denominator == 0;
        public bool IsPositive => numerator.Sign > 0;
        public bool IsNegative => numerator.Sign < 0;
        public BigInteger Floor
        {
            get
            {
                var quotient = BigInteger.DivRem(numerator, Denominator, out var remainder);
                return remainder.Sign < 0 ? quotient - 1 : quotient;
            }
        }
        public BigInteger Ceiling => -(-this).Floor;

        public ScoreFraction(BigInteger numerator, BigInteger denominator)
        {
            if (denominator.IsZero) throw new DivideByZeroException();
            if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
            var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
            this.numerator = numerator / gcd; this.denominator = denominator / gcd;
        }

        public static implicit operator ScoreFraction(int value) => new ScoreFraction(value, 1);
        public static ScoreFraction operator -(ScoreFraction a) => new ScoreFraction(-a.numerator, a.Denominator);
        public static ScoreFraction operator +(ScoreFraction a, ScoreFraction b)
            => new ScoreFraction(a.numerator * b.Denominator + b.numerator * a.Denominator, a.Denominator * b.Denominator);
        public static ScoreFraction operator *(ScoreFraction a, ScoreFraction b)
            => new ScoreFraction(a.numerator * b.numerator, a.Denominator * b.Denominator);
        public static ScoreFraction operator /(ScoreFraction a, ScoreFraction b)
            => new ScoreFraction(a.numerator * b.Denominator, a.Denominator * b.numerator);
        public static bool operator <(ScoreFraction a, ScoreFraction b) => a.numerator * b.Denominator < b.numerator * a.Denominator;
        public static bool operator >(ScoreFraction a, ScoreFraction b) => b < a;

        // Decimal TJA values, including exponents and TjaNumber's decimal comma convention.
        // Values outside decimal's range stay with the legacy policy, rather than overflowing.
        public static bool TryParse(string text, out ScoreFraction value)
        {
            value = default;
            if (!decimal.TryParse(text.Trim().TrimEnd(',', ' ').Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out decimal number)) return false;
            int[] bits = decimal.GetBits(number);
            var magnitude = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
            value = new ScoreFraction(bits[3] < 0 ? -magnitude : magnitude, BigInteger.Pow(10, (bits[3] >> 16) & 255));
            return true;
        }
    }
}
