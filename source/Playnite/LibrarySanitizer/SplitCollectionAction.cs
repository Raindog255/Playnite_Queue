using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Why a particular game was flagged as a collection that ought to be
    /// split. Surfaced on the action card so the user can quickly judge
    /// whether the heuristic was right.
    /// </summary>
    public enum SplitCollectionReason
    {
        Keyword,
        NumericSequence,
        AmpersandPair,
        SlashPair,
        RomanRange,
    }

    /// <summary>
    /// Mutable wrapper around a proposed child title so the split-collection
    /// card's TextBoxes can use a two-way binding. WPF rejects
    /// <c>{Binding Mode=TwoWay}</c> with no Path, so the card binds to
    /// <see cref="Name"/> instead of the bare string.
    /// </summary>
    public class EditableChildName : ObservableObject
    {
        private string name;
        public string Name
        {
            get => name;
            set { name = value; OnPropertyChanged(); }
        }

        public EditableChildName()
        {
        }

        public EditableChildName(string name)
        {
            this.name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// Proposes splitting a single Game record (a bundle, trilogy, etc.) into
    /// per-title child records. The detector populates <see cref="ProposedChildNames"/>
    /// with a best-guess starter list which the user is expected to edit
    /// inside the card. <see cref="Apply"/> creates a new Game per non-empty
    /// proposed name (cloning shared metadata) and marks the original as
    /// <see cref="Game.Hidden"/> so it disappears from the library view
    /// without destroying its history.
    /// </summary>
    public class SplitCollectionAction : SanitizerAction
    {
        public Guid SourceGameId { get; }
        public string SourceGameName { get; }
        public SplitCollectionReason Reason { get; }

        // Auto-growing list of editable child names. Always carries one
        // trailing blank entry; whenever the user types into that blank,
        // EnsureTrailingBlank appends a fresh one so they can keep adding
        // titles when the detector under-counted. Apply filters blanks
        // out so the trailing entry never produces an extra child record.
        public ObservableCollection<EditableChildName> ProposedChildNames { get; private set; }

        private readonly Game sourceRef;

        public SplitCollectionAction(Game source, SplitCollectionReason reason, IEnumerable<string> proposedChildren)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            sourceRef = source;
            SourceGameId = source.Id;
            SourceGameName = source.Name;
            Reason = reason;
            ProposedChildNames = new ObservableCollection<EditableChildName>();
            foreach (var name in proposedChildren ?? Enumerable.Empty<string>())
            {
                AddChild(new EditableChildName(name));
            }
            EnsureTrailingBlank();
        }

        /// <summary>
        /// Replaces the entire proposed-children list. Used by tests that
        /// want to drive Apply with an explicit roster; production code
        /// should mutate <see cref="ProposedChildNames"/> in place so the
        /// auto-grow subscriptions stay attached.
        /// </summary>
        public void SetProposedChildren(IEnumerable<EditableChildName> children)
        {
            foreach (var existing in ProposedChildNames)
            {
                existing.PropertyChanged -= Child_PropertyChanged;
            }
            ProposedChildNames.Clear();
            foreach (var c in children ?? Enumerable.Empty<EditableChildName>())
            {
                if (c != null)
                {
                    AddChild(c);
                }
            }
            EnsureTrailingBlank();
        }

        private void AddChild(EditableChildName entry)
        {
            entry.PropertyChanged += Child_PropertyChanged;
            ProposedChildNames.Add(entry);
        }

        private void Child_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(EditableChildName.Name)) return;
            EnsureTrailingBlank();
        }

        private void EnsureTrailingBlank()
        {
            var last = ProposedChildNames.LastOrDefault();
            if (last == null || !string.IsNullOrWhiteSpace(last.Name))
            {
                AddChild(new EditableChildName(string.Empty));
            }
        }

        public override SanitizerActionType Type => SanitizerActionType.SplitCollection;

        public override string DisplayTitle => SourceGameName;

        public override Game GetTargetGame() => sourceRef;

        public override string Signature
        {
            get
            {
                // Signature keys off the source id and the detector reason
                // only -- the user-edited child list shouldn't change which
                // dismissal entry this matches against.
                return "Split|" + SourceGameId + "|" + Reason;
            }
        }

        public override void Apply(IGameDatabaseMain database)
        {
            if (sourceRef == null) return;
            var children = (ProposedChildNames ?? Enumerable.Empty<EditableChildName>())
                .Select(c => (c?.Name ?? string.Empty).Trim())
                .Where(n => n.Length > 0)
                .ToList();
            if (children.Count == 0) return;

            foreach (var name in children)
            {
                var clone = CloneSharedMetadata(sourceRef, name);
                database.Games.Add(clone);
            }
            sourceRef.Hidden = true;
            database.Games.Update(sourceRef);
        }

        // Copies just the descriptive metadata (genre/series/dev/etc) and
        // visual assets. Per-record fields (playtime, install state, scripts,
        // play count, last activity) are intentionally NOT cloned -- each new
        // child starts with a fresh play history.
        internal static Game CloneSharedMetadata(Game src, string newName)
        {
            return new Game
            {
                Id = Guid.NewGuid(),
                Name = newName,
                Description = src.Description,
                ReleaseDate = src.ReleaseDate,
                AcquiredDate = src.AcquiredDate,
                CompletionStatusId = src.CompletionStatusId,
                CategoryIds = src.CategoryIds == null ? null : new List<Guid>(src.CategoryIds),
                GenreIds = src.GenreIds == null ? null : new List<Guid>(src.GenreIds),
                TagIds = src.TagIds == null ? null : new List<Guid>(src.TagIds),
                SeriesIds = src.SeriesIds == null ? null : new List<Guid>(src.SeriesIds),
                DeveloperIds = src.DeveloperIds == null ? null : new List<Guid>(src.DeveloperIds),
                PublisherIds = src.PublisherIds == null ? null : new List<Guid>(src.PublisherIds),
                AgeRatingIds = src.AgeRatingIds == null ? null : new List<Guid>(src.AgeRatingIds),
                RegionIds = src.RegionIds == null ? null : new List<Guid>(src.RegionIds),
                PlatformIds = src.PlatformIds == null ? null : new List<Guid>(src.PlatformIds),
                FeatureIds = src.FeatureIds == null ? null : new List<Guid>(src.FeatureIds),
                SourceId = src.SourceId,
                Icon = src.Icon,
                CoverImage = src.CoverImage,
                BackgroundImage = src.BackgroundImage,
                Mobile = src.Mobile,
                Free = src.Free,
                OnHold = src.OnHold,
            };
        }
    }

    /// <summary>
    /// Pattern detector for <see cref="SplitCollectionAction"/>. Pure regex
    /// work; no DB access. Returns null when nothing fires.
    /// </summary>
    internal static class CollectionDetector
    {
        // "Title 1+2" / "Title 1 + 2" / "Title 1, 2" / "Title 1 & 2" / "Title 1 / 2"
        private static readonly Regex NumericSequence = new Regex(
            @"^(?<head>.+?\S)\s+(?<a>\d{1,2})\s*[\+,/&]\s*(?<b>\d{1,2})(?:\s*[\+,/&]\s*(?<c>\d{1,2}))?\s*$",
            RegexOptions.Compiled);

        // "Final Fantasy I & II" / "Final Fantasy I + II"
        private static readonly Regex RomanRange = new Regex(
            @"^(?<head>.+?\S)\s+(?<a>[IVX]+)\s*[\+&\-/]\s*(?<b>[IVX]+)\s*$",
            RegexOptions.Compiled);

        // Two distinct titles separated by " & " or " + ", e.g. "Portal & Portal 2".
        private static readonly Regex AmpersandPair = new Regex(
            @"^(?<a>.+?\S)\s+(?:&|\+|and)\s+(?<b>\S.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "Title One / Title Two".
        private static readonly Regex SlashPair = new Regex(
            @"^(?<a>.+?\S)\s+/\s+(?<b>\S.+?)\s*$",
            RegexOptions.Compiled);

        public static SplitCollectionAction TryDetect(Game game, LibrarySanitizerSettings settings)
        {
            if (game == null || string.IsNullOrEmpty(game.Name)) return null;

            // Keyword detection runs first: a title like "Mass Effect Trilogy"
            // is unambiguous and shouldn't fall through to numeric/roman
            // detection on an embedded "3".
            var trimmed = game.Name.Trim();
            if (settings.CollectionKeywords != null)
            {
                foreach (var kw in settings.CollectionKeywords.OrderByDescending(k => (k ?? string.Empty).Length))
                {
                    if (string.IsNullOrWhiteSpace(kw)) continue;
                    if (TrailingKeywordMatch(trimmed, kw))
                    {
                        var head = trimmed.Substring(0, trimmed.Length - kw.Length).TrimEnd(' ', ':', '-', ',');
                        if (head.Length == 0) continue;
                        var children = ProposeChildrenFromKeyword(head, kw);
                        return new SplitCollectionAction(game, SplitCollectionReason.Keyword, children);
                    }
                }
            }

            if (!settings.DetectMultiTitlePatterns) return null;

            var num = NumericSequence.Match(trimmed);
            if (num.Success)
            {
                var head = num.Groups["head"].Value;
                var children = new List<string> { head };
                children.Add(head + " " + num.Groups["a"].Value);
                children.Add(head + " " + num.Groups["b"].Value);
                if (num.Groups["c"].Success && num.Groups["c"].Length > 0)
                {
                    children.Add(head + " " + num.Groups["c"].Value);
                }
                // Drop a leading "head" duplicate when the first number was 1
                // and the head is the canonical first-game name (e.g.
                // "Portal 1+2" -> "Portal", "Portal 2", not three entries).
                if (children[1].EndsWith(" 1"))
                {
                    children.RemoveAt(1);
                }
                return new SplitCollectionAction(game, SplitCollectionReason.NumericSequence, children);
            }

            var roman = RomanRange.Match(trimmed);
            if (roman.Success)
            {
                var head = roman.Groups["head"].Value;
                var a = SanitizerRules.FromRoman(roman.Groups["a"].Value);
                var b = SanitizerRules.FromRoman(roman.Groups["b"].Value);
                if (a > 0 && b > a && (b - a) <= 5)
                {
                    var children = new List<string>();
                    for (int i = a; i <= b; i++)
                    {
                        children.Add(head + " " + SanitizerRules.ToRoman(i));
                    }
                    return new SplitCollectionAction(game, SplitCollectionReason.RomanRange, children);
                }
            }

            // Slash pair takes precedence over the more permissive ampersand
            // pair so "Title One / Title Two" never gets caught by a stray
            // " and " inside one of the halves.
            var slash = SlashPair.Match(trimmed);
            if (slash.Success)
            {
                var children = new List<string> { slash.Groups["a"].Value.Trim(), slash.Groups["b"].Value.Trim() };
                return new SplitCollectionAction(game, SplitCollectionReason.SlashPair, children);
            }

            var amp = AmpersandPair.Match(trimmed);
            if (amp.Success)
            {
                var a = amp.Groups["a"].Value.Trim();
                var b = amp.Groups["b"].Value.Trim();
                // Avoid catching trivial things like "Tom & Jerry" (two short
                // words) -- require either side to be at least 4 characters.
                if (a.Length >= 4 && b.Length >= 4)
                {
                    return new SplitCollectionAction(game, SplitCollectionReason.AmpersandPair, new[] { a, b });
                }
            }

            return null;
        }

        private static bool TrailingKeywordMatch(string title, string keyword)
        {
            if (title.Length <= keyword.Length) return false;
            if (!title.EndsWith(keyword, StringComparison.OrdinalIgnoreCase)) return false;
            // Require a separator before the keyword so "Pacific Connection"
            // doesn't count as ending in "Connection"-style false positives
            // for keywords that share their suffix with a real word.
            var c = title[title.Length - keyword.Length - 1];
            return char.IsWhiteSpace(c) || c == ':' || c == '-' || c == ',';
        }

        // For pure-keyword matches we don't know how many children there are,
        // so propose 2 placeholder slots for the user to fill in. "Trilogy"
        // bumps that to 3; "Anthology"/"Compilation"/"Saga"/"Bundle"/"Pack"
        // get 2 by default.
        internal static IList<string> ProposeChildrenFromKeyword(string head, string keyword)
        {
            var lc = keyword.ToLowerInvariant();
            int count = 2;
            if (lc.Contains("trilogy")) count = 3;
            var list = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                list.Add(head + " " + i);
            }
            return list;
        }
    }
}
