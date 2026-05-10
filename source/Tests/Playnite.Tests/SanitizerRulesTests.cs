using NUnit.Framework;
using Playnite;
using Playnite.LibrarySanitizer;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Playnite.Tests
{
    [TestFixture]
    public class SanitizerRulesTests
    {
        private static LibrarySanitizerSettings Defaults()
        {
            // The factory's defaults turn most rules on; we override the few
            // that ship off (year suffix, ampersand) per-test instead.
            return new LibrarySanitizerSettings();
        }

        private static LibrarySanitizerSettings AllOff()
        {
            var s = Defaults();
            s.WhitespaceRule = false;
            s.SmartPunctuationRule = false;
            s.TrademarkSymbolsRule = false;
            s.SeparatorRule = false;
            s.NumeralsRule = false;
            s.EditionSpilloverRule = false;
            s.YearSuffixRule = false;
            s.HtmlEntitiesRule = false;
            s.AmpersandToAndRule = false;
            return s;
        }

        // ---- Whitespace ----------------------------------------------------

        [Test]
        public void Whitespace_TrimsAndCollapses()
        {
            var s = AllOff();
            s.WhitespaceRule = true;
            var r = SanitizerRules.Apply("  Hades   II  ", SanitizerScope.GameName, s);
            Assert.AreEqual("Hades II", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.Whitespace);
        }

        [Test]
        public void Whitespace_DisabledLeavesPaddingAlone()
        {
            var s = AllOff();
            var r = SanitizerRules.Apply("  Hades  ", SanitizerScope.GameName, s);
            Assert.AreEqual("  Hades  ", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        // ---- Smart punctuation --------------------------------------------

        [Test]
        public void SmartPunctuation_NormalizesQuotesAndDashes()
        {
            var s = AllOff();
            s.SmartPunctuationRule = true;
            var r = SanitizerRules.Apply("Tony Hawk\u2019s Pro Skater \u2014 1+2", SanitizerScope.GameName, s);
            Assert.AreEqual("Tony Hawk's Pro Skater - 1+2", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.SmartPunctuation);
        }

        [Test]
        public void SmartPunctuation_LeavesAsciiAlone()
        {
            var s = AllOff();
            s.SmartPunctuationRule = true;
            var r = SanitizerRules.Apply("Tony Hawk's Pro Skater - 1+2", SanitizerScope.GameName, s);
            Assert.AreEqual("Tony Hawk's Pro Skater - 1+2", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        // ---- Trademark symbols --------------------------------------------

        [Test]
        public void TrademarkSymbols_StripsUnicodeAndBracketedForms()
        {
            var s = AllOff();
            s.TrademarkSymbolsRule = true;
            s.WhitespaceRule = true;
            var r = SanitizerRules.Apply("Halo\u00AE Infinite\u2122 (TM) (R) (C)", SanitizerScope.GameName, s);
            Assert.AreEqual("Halo Infinite", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.TrademarkSymbols);
        }

        [Test]
        public void TrademarkSymbols_LeavesStandaloneTMandRAlone()
        {
            // We don't want to strip "TM" from titles like "TMNT", or "R" from
            // initials. Only bracketed and unicode forms get removed.
            var s = AllOff();
            s.TrademarkSymbolsRule = true;
            var r = SanitizerRules.Apply("TMNT R-Type", SanitizerScope.GameName, s);
            Assert.AreEqual("TMNT R-Type", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        // ---- Year suffix --------------------------------------------------

        [Test]
        public void YearSuffix_StripsParensWhenEnabled()
        {
            var s = AllOff();
            s.YearSuffixRule = true;
            var r = SanitizerRules.Apply("Half-Life 2 (2004)", SanitizerScope.GameName, s);
            Assert.AreEqual("Half-Life 2", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.YearSuffix);
        }

        [Test]
        public void YearSuffix_StripsBracketsWhenEnabled()
        {
            var s = AllOff();
            s.YearSuffixRule = true;
            var r = SanitizerRules.Apply("Doom [1993]", SanitizerScope.GameName, s);
            Assert.AreEqual("Doom", r.SanitizedName);
        }

        [Test]
        public void YearSuffix_LeavesEmbeddedYearAlone()
        {
            var s = AllOff();
            s.YearSuffixRule = true;
            var r = SanitizerRules.Apply("Football Manager 2024", SanitizerScope.GameName, s);
            Assert.AreEqual("Football Manager 2024", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        [Test]
        public void YearSuffix_OffByDefault()
        {
            var s = Defaults();
            var r = SanitizerRules.Apply("Doom (1993)", SanitizerScope.GameName, s);
            Assert.AreEqual("Doom (1993)", r.SanitizedName);
        }

        // ---- Edition spillover --------------------------------------------

        [Test]
        public void EditionSpillover_MovesEditionToVersion()
        {
            var s = AllOff();
            s.EditionSpilloverRule = true;
            var r = SanitizerRules.Apply("Skyrim Special Edition", SanitizerScope.GameName, s);
            Assert.AreEqual("Skyrim", r.SanitizedName);
            Assert.AreEqual("Special Edition", r.SpilledVersion);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.EditionSpillover);
        }

        [Test]
        public void EditionSpillover_PrefersLongestKeyword()
        {
            var s = AllOff();
            s.EditionSpilloverRule = true;
            var r = SanitizerRules.Apply("Fallout: Game of the Year Edition", SanitizerScope.GameName, s);
            Assert.AreEqual("Fallout", r.SanitizedName);
            Assert.AreEqual("Game of the Year Edition", r.SpilledVersion);
        }

        [Test]
        public void EditionSpillover_StripsTrailingSeparator()
        {
            var s = AllOff();
            s.EditionSpilloverRule = true;
            var r = SanitizerRules.Apply("Skyrim - Special Edition", SanitizerScope.GameName, s);
            Assert.AreEqual("Skyrim", r.SanitizedName);
        }

        [Test]
        public void EditionSpillover_RefusesEmptyHead()
        {
            var s = AllOff();
            s.EditionSpilloverRule = true;
            var r = SanitizerRules.Apply("Special Edition", SanitizerScope.GameName, s);
            // Removing the keyword would leave nothing; rule must back off.
            Assert.AreEqual("Special Edition", r.SanitizedName);
            Assert.IsNull(r.SpilledVersion);
        }

        [Test]
        public void EditionSpillover_OnlyAppliesToGameName()
        {
            var s = AllOff();
            s.EditionSpilloverRule = true;
            var r = SanitizerRules.Apply("Series Special Edition", SanitizerScope.SeriesName, s);
            Assert.AreEqual("Series Special Edition", r.SanitizedName);
            Assert.IsNull(r.SpilledVersion);
        }

        // ---- Separator normalization --------------------------------------

        [Test]
        public void Separator_RewritesSpaceDashSpaceToColon()
        {
            var s = AllOff();
            s.SeparatorRule = true;
            var r = SanitizerRules.Apply("Half-Life - Blue Shift", SanitizerScope.GameName, s);
            Assert.AreEqual("Half-Life: Blue Shift", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.Separator);
        }

        [Test]
        public void Separator_LeavesHyphenatedWordsAlone()
        {
            var s = AllOff();
            s.SeparatorRule = true;
            var r = SanitizerRules.Apply("Half-Life", SanitizerScope.GameName, s);
            Assert.AreEqual("Half-Life", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        // ---- Numerals -----------------------------------------------------

        [Test]
        public void Numerals_TrailingArabic_BecomesRomanWithArabicSorting()
        {
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Final Fantasy 7", SanitizerScope.GameName, s);
            Assert.AreEqual("Final Fantasy VII", r.SanitizedName);
            Assert.AreEqual("Final Fantasy 7", r.SortingNameOverride);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.Numerals);
        }

        [Test]
        public void Numerals_TrailingRoman_FillsArabicSortingNameOnly()
        {
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Final Fantasy VII", SanitizerScope.GameName, s);
            Assert.AreEqual("Final Fantasy VII", r.SanitizedName);
            Assert.AreEqual("Final Fantasy 7", r.SortingNameOverride);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.Numerals);
        }

        [Test]
        public void Numerals_DoubleDigitSequel()
        {
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Final Fantasy 14", SanitizerScope.GameName, s);
            Assert.AreEqual("Final Fantasy XIV", r.SanitizedName);
            Assert.AreEqual("Final Fantasy 14", r.SortingNameOverride);
        }

        [Test]
        public void Numerals_LeavesYearTitleAlone()
        {
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Football Manager 2024", SanitizerScope.GameName, s);
            Assert.AreEqual("Football Manager 2024", r.SanitizedName);
            Assert.IsNull(r.SortingNameOverride);
        }

        [Test]
        public void Numerals_LeavesStandaloneNumberAlone()
        {
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("1942", SanitizerScope.GameName, s);
            Assert.AreEqual("1942", r.SanitizedName);
            Assert.IsNull(r.SortingNameOverride);
        }

        [Test]
        public void Numerals_LeavesMidStringNumberAlone()
        {
            // Number must be at the very end of the title.
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Kingdom Hearts 358/2 Days", SanitizerScope.GameName, s);
            Assert.AreEqual("Kingdom Hearts 358/2 Days", r.SanitizedName);
        }

        [Test]
        public void Numerals_RejectsNonCanonicalRoman()
        {
            // "IIII" is not a valid Roman numeral (should be IV) -- skip.
            var s = AllOff();
            s.NumeralsRule = true;
            var r = SanitizerRules.Apply("Fake Title IIII", SanitizerScope.GameName, s);
            Assert.AreEqual("Fake Title IIII", r.SanitizedName);
            Assert.IsNull(r.SortingNameOverride);
        }

        [TestCase(1, "I")]
        [TestCase(3, "III")]
        [TestCase(4, "IV")]
        [TestCase(9, "IX")]
        [TestCase(10, "X")]
        [TestCase(14, "XIV")]
        [TestCase(20, "XX")]
        [TestCase(29, "XXIX")]
        [TestCase(30, "XXX")]
        public void Numerals_RomanRoundTrip(int value, string roman)
        {
            Assert.AreEqual(roman, SanitizerRules.ToRoman(value));
            Assert.AreEqual(value, SanitizerRules.FromRoman(roman));
        }

        // ---- Ampersand ----------------------------------------------------

        [Test]
        public void Ampersand_OffByDefault()
        {
            var s = Defaults();
            var r = SanitizerRules.Apply("Mario & Luigi", SanitizerScope.GameName, s);
            Assert.AreEqual("Mario & Luigi", r.SanitizedName);
        }

        [Test]
        public void Ampersand_RewritesWhenEnabled()
        {
            var s = AllOff();
            s.AmpersandToAndRule = true;
            var r = SanitizerRules.Apply("Mario & Luigi", SanitizerScope.GameName, s);
            Assert.AreEqual("Mario and Luigi", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.AmpersandToAnd);
        }

        [Test]
        public void Ampersand_LeavesEmbeddedAmpAlone()
        {
            // No surrounding spaces -- something like a hash, AT&T, etc.
            var s = AllOff();
            s.AmpersandToAndRule = true;
            var r = SanitizerRules.Apply("AT&T Demo", SanitizerScope.GameName, s);
            Assert.AreEqual("AT&T Demo", r.SanitizedName);
            Assert.IsFalse(r.Changed);
        }

        // ---- HTML entities ------------------------------------------------

        [Test]
        public void HtmlEntities_DecodesCommonEntities()
        {
            var s = AllOff();
            s.HtmlEntitiesRule = true;
            var r = SanitizerRules.Apply("Sid Meier&#39;s Civilization &amp; Friends", SanitizerScope.GameName, s);
            Assert.AreEqual("Sid Meier's Civilization & Friends", r.SanitizedName);
            CollectionAssert.Contains(r.AppliedRules, SanitizerRuleId.HtmlEntities);
        }

        // ---- End-to-end orchestration -------------------------------------

        [Test]
        public void Apply_DefaultsLeaveCleanTitleUnchanged()
        {
            var r = SanitizerRules.Apply("Disco Elysium", SanitizerScope.GameName, Defaults());
            Assert.IsFalse(r.Changed, "Clean title should produce no proposals.");
        }

        [Test]
        public void Apply_ChainedRules_EditionThenNumerals()
        {
            // "Fallout 4: Game of the Year Edition" -> edition spills out,
            // then the numerals rule sees the head ending in "4".
            var r = SanitizerRules.Apply("Fallout 4: Game of the Year Edition", SanitizerScope.GameName, Defaults());
            // Edition spillover removes ": Game of the Year Edition" leaving "Fallout 4",
            // then numerals converts to Roman + Arabic sorting.
            Assert.AreEqual("Fallout IV", r.SanitizedName);
            Assert.AreEqual("Fallout 4", r.SortingNameOverride);
            Assert.AreEqual("Game of the Year Edition", r.SpilledVersion);
        }

        [Test]
        public void Apply_NullSettings_ReturnsUnchanged()
        {
            var r = SanitizerRules.Apply("Anything", SanitizerScope.GameName, null);
            Assert.IsFalse(r.Changed);
            Assert.AreEqual("Anything", r.SanitizedName);
        }

        [Test]
        public void Apply_EmptyName_ReturnsEmptyUnchanged()
        {
            var r = SanitizerRules.Apply("", SanitizerScope.GameName, Defaults());
            Assert.IsFalse(r.Changed);
            Assert.AreEqual("", r.SanitizedName);
        }
    }
}
