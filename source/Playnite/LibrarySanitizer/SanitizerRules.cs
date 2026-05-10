using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Identifies which sanitization rule produced a particular change.
    /// Used by <see cref="SanitizationResult.AppliedRules"/> for reporting and
    /// by SanitizeAction.Signature for dismissal stability across rebuilds.
    /// </summary>
    public enum SanitizerRuleId
    {
        Whitespace,
        SmartPunctuation,
        TrademarkSymbols,
        Separator,
        Numerals,
        EditionSpillover,
        YearSuffix,
        HtmlEntities,
        AmpersandToAnd,
    }

    /// <summary>
    /// Which kind of database record a name is being sanitized for. Some rules
    /// only apply to game names (edition spillover, since Series/Company have
    /// no Version field to receive the spilled keyword).
    /// </summary>
    public enum SanitizerScope
    {
        GameName,
        SeriesName,
        CompanyName,
    }

    /// <summary>
    /// Output of <see cref="SanitizerRules.Apply"/>. Holds the proposed
    /// sanitized name plus optional spillover values and a list of rules that
    /// fired. <see cref="Changed"/> is true iff at least one of the output
    /// fields differs from the input.
    /// </summary>
    public class SanitizationResult
    {
        public string OriginalName { get; }
        public string SanitizedName { get; set; }
        /// <summary>
        /// Set by the numerals rule. The Arabic-form name destined for the
        /// game's SortingName field so list/sort behaviour stays sane after
        /// the displayed name is converted to Roman.
        /// </summary>
        public string SortingNameOverride { get; set; }
        /// <summary>
        /// Set by the edition-spillover rule. The keyword that was stripped
        /// from the name and proposed as the game's Version field.
        /// </summary>
        public string SpilledVersion { get; set; }
        public IList<SanitizerRuleId> AppliedRules { get; }

        public SanitizationResult(string original)
        {
            OriginalName = original ?? string.Empty;
            SanitizedName = OriginalName;
            AppliedRules = new List<SanitizerRuleId>();
        }

        public bool Changed =>
            !string.Equals(OriginalName, SanitizedName, StringComparison.Ordinal)
            || SortingNameOverride != null
            || SpilledVersion != null;
    }

    /// <summary>
    /// Pure normalization rules used by the Library Sanitizer engine. Each
    /// rule is a small function with no side effects; they are sequenced by
    /// <see cref="Apply"/> in a deterministic order chosen so earlier
    /// text-decoding rules run before structural rules that depend on a
    /// clean glyph stream.
    /// </summary>
    /// <remarks>
    /// Order rationale:
    /// 1. HTML entities first -- decode to real characters.
    /// 2. Smart punctuation -- normalize curly/em-dash glyphs.
    /// 3. Trademark / copyright -- strip cosmetic noise.
    /// 4. Year suffix -- strip "(2013)" before edition spillover so a tail
    ///    like "Skyrim (2013) GOTY Edition" peels in the right order.
    /// 5. Edition spillover -- relocate edition keyword into Version.
    /// 6. Separator normalization -- only after editions/years are removed
    ///    so we don't rewrite a separator that was about to be stripped.
    /// 7. Numerals -- run after edition spillover so "Skyrim Special
    ///    Edition" becomes "Skyrim" (no number to convert) rather than
    ///    "Skyrim Special V" or anything weird.
    /// 8. Ampersand -> and -- stylistic pass.
    /// 9. Whitespace -- final cleanup picks up any double-space artefacts
    ///    introduced by earlier substitutions.
    /// </remarks>
    public static class SanitizerRules
    {
        public static SanitizationResult Apply(string name, SanitizerScope scope, LibrarySanitizerSettings settings)
        {
            var result = new SanitizationResult(name);
            if (settings == null || string.IsNullOrEmpty(result.SanitizedName))
            {
                return result;
            }

            if (settings.HtmlEntitiesRule) ApplyHtmlEntities(result);
            if (settings.SmartPunctuationRule) ApplySmartPunctuation(result);
            if (settings.TrademarkSymbolsRule) ApplyTrademarkSymbols(result);
            if (settings.YearSuffixRule) ApplyYearSuffix(result);
            if (settings.EditionSpilloverRule && scope == SanitizerScope.GameName)
            {
                ApplyEditionSpillover(result, settings.EditionKeywords);
            }
            if (settings.SeparatorRule) ApplySeparatorNormalization(result);
            if (settings.NumeralsRule) ApplyNumeralsRule(result);
            if (settings.AmpersandToAndRule) ApplyAmpersandToAnd(result);
            if (settings.WhitespaceRule) ApplyWhitespace(result);

            return result;
        }

        // -------------------------------------------------------------------
        //  HTML entities
        // -------------------------------------------------------------------

        internal static void ApplyHtmlEntities(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = input
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&#39;", "'")
                .Replace("&nbsp;", " ");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.HtmlEntities);
            }
        }

        // -------------------------------------------------------------------
        //  Smart punctuation
        // -------------------------------------------------------------------

        internal static void ApplySmartPunctuation(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var sb = new StringBuilder(input.Length);
            var changed = false;
            foreach (var ch in input)
            {
                switch (ch)
                {
                    case '\u2018': // left single quotation
                    case '\u2019': // right single quotation
                    case '\u201A': // single low-9
                    case '\u201B': // single high-reversed-9
                        sb.Append('\'');
                        changed = true;
                        break;
                    case '\u201C': // left double quotation
                    case '\u201D': // right double quotation
                    case '\u201E': // double low-9
                    case '\u201F': // double high-reversed-9
                        sb.Append('"');
                        changed = true;
                        break;
                    case '\u2013': // en dash
                    case '\u2014': // em dash
                        sb.Append('-');
                        changed = true;
                        break;
                    case '\u2026': // horizontal ellipsis
                        sb.Append("...");
                        changed = true;
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            if (changed)
            {
                r.SanitizedName = sb.ToString();
                r.AppliedRules.Add(SanitizerRuleId.SmartPunctuation);
            }
        }

        // -------------------------------------------------------------------
        //  Trademark / copyright
        // -------------------------------------------------------------------

        // Bracketed forms get full token replacement; standalone glyphs are
        // dropped wherever they appear. Standalone "TM"/"R" suffixes are NOT
        // stripped -- too many legitimate titles end in two-letter words.
        private static readonly Regex TrademarkPatterns = new Regex(
            @"(\u2122|\u00AE|\u00A9|\(TM\)|\(R\)|\(C\))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        internal static void ApplyTrademarkSymbols(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = TrademarkPatterns.Replace(input, "");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.TrademarkSymbols);
            }
        }

        // -------------------------------------------------------------------
        //  Year suffix
        // -------------------------------------------------------------------

        // Trailing 4-digit year inside parens or brackets, optional whitespace.
        // Only matches at the very end of the title so "Half-Life 2 (2004)"
        // becomes "Half-Life 2" but "(2004) Episode One" stays intact.
        private static readonly Regex YearSuffix = new Regex(
            @"\s*[\(\[]\d{4}[\)\]]\s*$",
            RegexOptions.Compiled);

        internal static void ApplyYearSuffix(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = YearSuffix.Replace(input, "");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.YearSuffix);
            }
        }

        // -------------------------------------------------------------------
        //  Edition spillover
        // -------------------------------------------------------------------

        // Trailing separator left over after stripping the keyword (a colon
        // or hyphen with surrounding spaces, e.g. "Skyrim - Special Edition"
        // becomes "Skyrim - " then "Skyrim").
        private static readonly Regex TrailingSeparator = new Regex(@"[\-:\s]+$", RegexOptions.Compiled);

        internal static void ApplyEditionSpillover(SanitizationResult r, IList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0) return;
            var input = r.SanitizedName.TrimEnd();

            // Sort keywords longest-first so "Game of the Year Edition" beats
            // "Edition" and "GOTY Edition" beats "Edition" / "GOTY".
            foreach (var kw in keywords.OrderByDescending(k => (k ?? string.Empty).Length))
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                if (!input.EndsWith(kw, StringComparison.OrdinalIgnoreCase)) continue;

                var head = input.Substring(0, input.Length - kw.Length);
                head = TrailingSeparator.Replace(head, string.Empty);
                if (head.Length == 0) continue; // refuse to leave an empty title

                r.SanitizedName = head;
                r.SpilledVersion = string.IsNullOrEmpty(r.SpilledVersion)
                    ? kw.Trim()
                    : r.SpilledVersion + " " + kw.Trim();
                r.AppliedRules.Add(SanitizerRuleId.EditionSpillover);
                return;
            }
        }

        // -------------------------------------------------------------------
        //  Separator normalization
        // -------------------------------------------------------------------

        // " - " between two non-whitespace runs becomes ": ". Lookarounds
        // ensure we leave hyphenated words ("Half-Life") and trailing/leading
        // dashes alone.
        private static readonly Regex DashSeparator = new Regex(@"(?<=\S) - (?=\S)", RegexOptions.Compiled);

        internal static void ApplySeparatorNormalization(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = DashSeparator.Replace(input, ": ");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.Separator);
            }
        }

        // -------------------------------------------------------------------
        //  Ampersand -> and
        // -------------------------------------------------------------------

        private static readonly Regex AmpBetweenSpaces = new Regex(@"\s&\s", RegexOptions.Compiled);

        internal static void ApplyAmpersandToAnd(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = AmpBetweenSpaces.Replace(input, " and ");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.AmpersandToAnd);
            }
        }

        // -------------------------------------------------------------------
        //  Whitespace
        // -------------------------------------------------------------------

        private static readonly Regex MultipleWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

        internal static void ApplyWhitespace(SanitizationResult r)
        {
            var input = r.SanitizedName;
            var output = MultipleWhitespace.Replace(input.Trim(), " ");
            if (!string.Equals(input, output, StringComparison.Ordinal))
            {
                r.SanitizedName = output;
                r.AppliedRules.Add(SanitizerRuleId.Whitespace);
            }
        }

        // -------------------------------------------------------------------
        //  Numerals (Roman canonical + Arabic SortingName)
        // -------------------------------------------------------------------

        // Trailing 1-2 digit number with at least one word before it. Limits
        // to 1-30 below so "Football Manager 2024" (4 digits, won't match) and
        // "1942" (no leading word) don't get rewritten.
        private static readonly Regex TrailingArabic = new Regex(
            @"^(?<head>.+?\S)\s+(?<num>\d{1,2})\s*$",
            RegexOptions.Compiled);

        // Trailing Roman numeral. Restricted to I/V/X glyphs so we cover the
        // sequel range (1-30) without false matches on words like "MIX".
        private static readonly Regex TrailingRoman = new Regex(
            @"^(?<head>.+?\S)\s+(?<num>[IVXivx]+)\s*$",
            RegexOptions.Compiled);

        internal static void ApplyNumeralsRule(SanitizationResult r)
        {
            var input = r.SanitizedName;

            // Case A: trailing Arabic number. Convert displayed name to Roman
            // and propose Arabic-form SortingName so list-sort puts FF7 next
            // to FF6 instead of after FF IX.
            var arabic = TrailingArabic.Match(input);
            if (arabic.Success)
            {
                var head = arabic.Groups["head"].Value;
                int n;
                if (int.TryParse(arabic.Groups["num"].Value, out n) && n >= 1 && n <= 30)
                {
                    r.SanitizedName = head + " " + ToRoman(n);
                    if (string.IsNullOrEmpty(r.SortingNameOverride))
                    {
                        r.SortingNameOverride = head + " " + n;
                    }
                    r.AppliedRules.Add(SanitizerRuleId.Numerals);
                    return;
                }
            }

            // Case B: trailing Roman. Name already canonical; propose Arabic
            // SortingName if the user hasn't already set one.
            var roman = TrailingRoman.Match(input);
            if (roman.Success)
            {
                var head = roman.Groups["head"].Value;
                var n = FromRoman(roman.Groups["num"].Value);
                if (n >= 1 && n <= 30)
                {
                    if (string.IsNullOrEmpty(r.SortingNameOverride))
                    {
                        r.SortingNameOverride = head + " " + n;
                    }
                    r.AppliedRules.Add(SanitizerRuleId.Numerals);
                }
            }
        }

        internal static string ToRoman(int n)
        {
            if (n < 1 || n > 30) throw new ArgumentOutOfRangeException(nameof(n));
            var units = new[] { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };
            var tens = new[] { "", "X", "XX", "XXX" };
            return tens[n / 10] + units[n % 10];
        }

        /// <summary>
        /// Parses a Roman numeral string in the 1-30 range. Returns -1 on any
        /// parse failure (unknown glyph, out of range, or non-canonical form
        /// like "IIII" -- the round-trip equality check rejects those).
        /// </summary>
        internal static int FromRoman(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            var upper = s.ToUpperInvariant();
            for (int i = 0; i < upper.Length; i++)
            {
                if (upper[i] != 'I' && upper[i] != 'V' && upper[i] != 'X')
                {
                    return -1;
                }
            }

            int total = 0;
            int prev = 0;
            for (int i = upper.Length - 1; i >= 0; i--)
            {
                int v;
                switch (upper[i])
                {
                    case 'I': v = 1; break;
                    case 'V': v = 5; break;
                    case 'X': v = 10; break;
                    default: return -1;
                }
                if (v < prev) total -= v; else total += v;
                prev = v;
            }

            if (total < 1 || total > 30) return -1;
            // Round-trip check rejects ambiguous / non-canonical forms.
            if (!string.Equals(ToRoman(total), upper, StringComparison.Ordinal)) return -1;
            return total;
        }
    }
}
