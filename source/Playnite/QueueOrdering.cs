using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite
{
    /// <summary>
    /// Pure ordering pipeline that turns the full Game collection into the visible
    /// Queue list according to <see cref="QueueSettings"/>. The engine is intentionally
    /// free of GameDatabase coupling: every per-status decision is made via Guids that
    /// the user picks once in the queue settings panel.
    /// </summary>
    public static class QueueOrdering
    {
        /// <summary>
        /// Builds the ordered queue. The result is what the Queue view should show:
        /// always-visible Playing games are prepended, and the rest of the queue is
        /// truncated so the total length never exceeds
        /// <see cref="QueueSettings.VisibleCount"/>. Playing games consume slots —
        /// once enough games are Playing to fill the cap, no upcoming queue items
        /// are added. If more than <c>VisibleCount</c> games are Playing the result
        /// will exceed the cap (they are still always shown).
        /// </summary>
        public static IList<Game> BuildQueue(IEnumerable<Game> allGames, QueueSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var games = allGames?.Where(g => g != null).ToList() ?? new List<Game>();
            var rules = settings.PriorityRules?.ToList() ?? new List<QueuePriorityRule>();

            var playingGames = settings.PlayingStatusId.HasValue
                ? games.Where(g => g.CompletionStatusId == settings.PlayingStatusId.Value).ToList()
                : new List<Game>();

            var playingIds = new HashSet<Guid>(playingGames.Select(g => g.Id));

            var eligible = games
                .Where(g => !playingIds.Contains(g.Id) && IsEligible(g, settings))
                .ToList();

            // 2. Primary date sort, ascending. Nulls (and ReleaseDate.Empty) sort last.
            // 3. Priority rules layered on top via a stable composite-key sort.
            // The secondary date (the "other" date) is the tiebreaker within the
            // same primary date — older first, nulls last — so import-time noise
            // on AcquiredDate doesn't reshuffle games that legitimately share a day.
            // We capture the original index so any later rebucketing can preserve
            // order among ties without re-running the date sort.
            var ordered = eligible
                .Select((g, i) => new RankedGame(
                    g,
                    RuleRank(g, rules),
                    DateKey(g, settings.DateSource),
                    SecondaryDateKey(g, settings.DateSource),
                    i))
                .OrderBy(r => r.RuleRank)
                .ThenBy(r => r.DateKey)
                .ThenBy(r => r.SecondaryDateKey)
                .ThenBy(r => r.OriginalIndex)
                .ToList();

            // 4. Buffers: any game whose attribute matches a buffer's push-back set
            // gets moved to the bottom of its current priority tier. We do this by
            // re-sorting with PushBack as a sub-key under RuleRank, so ordering
            // between tiers (priority rules) is preserved while pushed-back games
            // sink within their own tier.
            var pushBackIds = ComputePushBackSet(games, playingGames, settings);
            if (pushBackIds.Count > 0)
            {
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (pushBackIds.Contains(ordered[i].Game.Id))
                    {
                        ordered[i].PushBack = true;
                    }
                }

                ordered = ordered
                    .OrderBy(r => r.RuleRank)
                    .ThenBy(r => r.PushBack ? 1 : 0)
                    .ThenBy(r => r.DateKey)
                    .ThenBy(r => r.SecondaryDateKey)
                    .ThenBy(r => r.OriginalIndex)
                    .ToList();
            }

            var orderedGames = ordered.Select(r => r.Game).ToList();

            // 5. Grouping replacement (Playing games are exempt — they're separate).
            var grouped = ApplyGrouping(orderedGames, settings.GroupByDeveloper, settings.GroupBySeries);

            // 6. Take top N — but reserve a slot for every Playing game first.
            // Playing tiles are always shown, so they consume the visible cap.
            // (When more games are Playing than VisibleCount, the queue portion
            // is empty and Playing tiles overflow on their own — they're never
            // hidden.)
            var visibleCount = Math.Max(0, settings.VisibleCount);
            var slotsForQueue = Math.Max(0, visibleCount - playingGames.Count);
            var takeN = grouped.Take(slotsForQueue).ToList();

            // 7. Prepend Playing games (sorted by the same priority + date pipeline,
            // but never grouped or pushed back; they always show).
            var playingOrdered = playingGames
                .Select((g, i) => new RankedGame(
                    g,
                    RuleRank(g, rules),
                    DateKey(g, settings.DateSource),
                    SecondaryDateKey(g, settings.DateSource),
                    i))
                .OrderBy(r => r.RuleRank)
                .ThenBy(r => r.DateKey)
                .ThenBy(r => r.SecondaryDateKey)
                .ThenBy(r => r.OriginalIndex)
                .Select(r => r.Game)
                .ToList();

            var result = new List<Game>(playingOrdered.Count + takeN.Count);
            result.AddRange(playingOrdered);
            result.AddRange(takeN);
            return result;
        }

        internal static bool IsEligible(Game game, QueueSettings settings)
        {
            if (game.Hidden || game.OnHold)
            {
                return false;
            }

            if (settings.TerminalStatusIds != null
                && settings.TerminalStatusIds.Count > 0
                && settings.TerminalStatusIds.Contains(game.CompletionStatusId))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the date used for primary sorting. Null / Empty values resolve to
        /// <see cref="DateTime.MaxValue"/> so missing-data games sink to the bottom
        /// without disappearing.
        /// </summary>
        internal static DateTime DateKey(Game game, QueueDateSource source)
        {
            switch (source)
            {
                case QueueDateSource.ReleaseDate:
                    var rd = game.ReleaseDate;
                    if (rd.HasValue && rd.Value.Year != 0)
                    {
                        return rd.Value.Date;
                    }

                    return DateTime.MaxValue;

                case QueueDateSource.AcquiredDate:
                    // Strip the time component so games "acquired the same day" don't
                    // sort by import-time noise from whichever library plugin ran first.
                    return game.AcquiredDate?.Date ?? DateTime.MaxValue;

                default:
                    return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// Tiebreaker date used when two games share the primary <see cref="DateKey"/>.
        /// Returns the OTHER date — older first — so a queue ordered by acquisition
        /// breaks ties by older release, and a queue ordered by release breaks ties
        /// by older acquisition. Null / Empty values resolve to <see cref="DateTime.MaxValue"/>.
        /// </summary>
        internal static DateTime SecondaryDateKey(Game game, QueueDateSource primarySource)
        {
            switch (primarySource)
            {
                case QueueDateSource.AcquiredDate:
                    var rd = game.ReleaseDate;
                    if (rd.HasValue && rd.Value.Year != 0)
                    {
                        return rd.Value.Date;
                    }

                    return DateTime.MaxValue;

                case QueueDateSource.ReleaseDate:
                    return game.AcquiredDate?.Date ?? DateTime.MaxValue;

                default:
                    return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// Index of the first matching rule, or <paramref name="rules"/>.Count if no rule
        /// matches. Lower rank = higher queue priority.
        /// </summary>
        internal static int RuleRank(Game game, IReadOnlyList<QueuePriorityRule> rules)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (RuleMatches(rules[i], game))
                {
                    return i;
                }
            }

            return rules.Count;
        }

        internal static bool RuleMatches(QueuePriorityRule rule, Game game)
        {
            if (rule == null)
            {
                return false;
            }

            switch (rule.Field)
            {
                case QueuePriorityField.Series:
                    return rule.ValueId.HasValue && ContainsId(game.SeriesIds, rule.ValueId.Value);
                case QueuePriorityField.Developer:
                    return rule.ValueId.HasValue && ContainsId(game.DeveloperIds, rule.ValueId.Value);
                case QueuePriorityField.Library:
                    return rule.ValueId.HasValue && game.PluginId == rule.ValueId.Value;
                case QueuePriorityField.Style:
                    return rule.ValueId.HasValue && ContainsId(game.CategoryIds, rule.ValueId.Value);
                case QueuePriorityField.Genre:
                    return rule.ValueId.HasValue && ContainsId(game.GenreIds, rule.ValueId.Value);
                case QueuePriorityField.Free:
                    return rule.BoolValue.HasValue && game.Free == rule.BoolValue.Value;
                case QueuePriorityField.Mobile:
                    return rule.BoolValue.HasValue && game.Mobile == rule.BoolValue.Value;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Union of game ids that are pushed back by ANY enabled buffer. A game is in
        /// the push-back set if any of its Style/Series/Developer/Genre ids appears in
        /// the corresponding buffer's source set (Playing matches and/or last N
        /// completed). Playing games themselves are never pushed back.
        /// </summary>
        internal static HashSet<Guid> ComputePushBackSet(
            IList<Game> allGames,
            IList<Game> playingGames,
            QueueSettings settings)
        {
            var styleSet = BuildAttributeSet(allGames, playingGames, settings.StyleBuffer, g => g.CategoryIds);
            var seriesSet = BuildAttributeSet(allGames, playingGames, settings.SeriesBuffer, g => g.SeriesIds);
            var developerSet = BuildAttributeSet(allGames, playingGames, settings.DeveloperBuffer, g => g.DeveloperIds);
            var genreSet = BuildAttributeSet(allGames, playingGames, settings.GenreBuffer, g => g.GenreIds);

            var pushBack = new HashSet<Guid>();
            if (styleSet.Count == 0 && seriesSet.Count == 0 && developerSet.Count == 0 && genreSet.Count == 0)
            {
                return pushBack;
            }

            var playingIds = new HashSet<Guid>(playingGames.Select(g => g.Id));
            foreach (var game in allGames)
            {
                if (playingIds.Contains(game.Id))
                {
                    continue;
                }

                if (IntersectsAny(game.CategoryIds, styleSet)
                    || IntersectsAny(game.SeriesIds, seriesSet)
                    || IntersectsAny(game.DeveloperIds, developerSet)
                    || IntersectsAny(game.GenreIds, genreSet))
                {
                    pushBack.Add(game.Id);
                }
            }

            return pushBack;
        }

        private static HashSet<Guid> BuildAttributeSet(
            IList<Game> allGames,
            IList<Game> playingGames,
            QueueBufferSettings buffer,
            Func<Game, IList<Guid>> attributeSelector)
        {
            var set = new HashSet<Guid>();
            if (buffer == null)
            {
                return set;
            }

            if (buffer.PushIfPlayingMatches)
            {
                foreach (var g in playingGames)
                {
                    AddAll(set, attributeSelector(g));
                }
            }

            if (buffer.RecentCompletedCount > 0)
            {
                var recent = allGames
                    .Where(g => g.CompletedDate.HasValue)
                    .OrderByDescending(g => g.CompletedDate.Value)
                    .Take(buffer.RecentCompletedCount);

                foreach (var g in recent)
                {
                    AddAll(set, attributeSelector(g));
                }
            }

            return set;
        }

        private static void AddAll(HashSet<Guid> set, IList<Guid> ids)
        {
            if (ids == null)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                set.Add(ids[i]);
            }
        }

        private static bool IntersectsAny(IList<Guid> ids, HashSet<Guid> set)
        {
            if (ids == null || set.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (set.Contains(ids[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsId(IList<Guid> ids, Guid id)
        {
            if (ids == null)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Walks the ordered list top-to-bottom and applies grouping as a *replacement*:
        /// each kept slot's game is swapped for the earliest-by-release-date game from
        /// the same Developer (when <paramref name="byDeveloper"/>) or Series (when
        /// <paramref name="bySeries"/>) group. Subsequent games that share any of those
        /// attributes are then suppressed. A game with no developer/series is never
        /// suppressed or replaced by the corresponding rule.
        /// </summary>
        /// <remarks>
        /// "Replacement" (not pure dedupe) is the user's spec: enabling Group by
        /// Developer should surface the earliest game by that developer (e.g. Elder
        /// Scrolls Arena for Bethesda) regardless of which Bethesda game happened to
        /// land in the slot via the date sort. Replacement candidates are drawn from
        /// the eligible <paramref name="ordered"/> set so OnHold / hidden / terminal
        /// games never become the replacement.
        /// </remarks>
        internal static IList<Game> ApplyGrouping(IList<Game> ordered, bool byDeveloper, bool bySeries)
        {
            if (!byDeveloper && !bySeries)
            {
                return ordered;
            }

            var earliestByDev = byDeveloper
                ? BuildEarliestByAttribute(ordered, g => g.DeveloperIds)
                : null;
            var earliestBySeries = bySeries
                ? BuildEarliestByAttribute(ordered, g => g.SeriesIds)
                : null;

            var seenDevelopers = new HashSet<Guid>();
            var seenSeries = new HashSet<Guid>();
            var keptIds = new HashSet<Guid>();
            var kept = new List<Game>(ordered.Count);

            foreach (var game in ordered)
            {
                var suppressedByDev = byDeveloper && IntersectsAny(game.DeveloperIds, seenDevelopers);
                var suppressedBySeries = bySeries && IntersectsAny(game.SeriesIds, seenSeries);
                if (suppressedByDev || suppressedBySeries)
                {
                    continue;
                }

                // Pick the displayed game: across the slot game's developer and/or
                // series ids, take the candidate with the oldest release date (null
                // releases sort last, so a real release always wins). Falls back to
                // the slot game if no enabled rule matches.
                var displayed = game;
                var displayedKey = ReleaseDateKey(game);

                if (earliestByDev != null)
                {
                    PickEarlier(earliestByDev, game.DeveloperIds, ref displayed, ref displayedKey);
                }

                if (earliestBySeries != null)
                {
                    PickEarlier(earliestBySeries, game.SeriesIds, ref displayed, ref displayedKey);
                }

                // The replacement may have already been displayed at a previous slot
                // (e.g. when two distinct slot games share a developer whose earliest
                // release we already showed). Fall back to the original slot game so
                // the slot isn't filled with a duplicate tile.
                if (keptIds.Contains(displayed.Id))
                {
                    if (keptIds.Contains(game.Id))
                    {
                        continue;
                    }

                    displayed = game;
                }

                kept.Add(displayed);
                keptIds.Add(displayed.Id);

                // Suppress subsequent slot games sharing attributes with EITHER the
                // original slot game or the actually displayed replacement, so the
                // dedupe pass behaves consistently regardless of which one we ended
                // up showing.
                if (byDeveloper)
                {
                    AddAll(seenDevelopers, game.DeveloperIds);
                    AddAll(seenDevelopers, displayed.DeveloperIds);
                }
                if (bySeries)
                {
                    AddAll(seenSeries, game.SeriesIds);
                    AddAll(seenSeries, displayed.SeriesIds);
                }
            }

            return kept;
        }

        private static Dictionary<Guid, Game> BuildEarliestByAttribute(
            IList<Game> games,
            Func<Game, IList<Guid>> attributeSelector)
        {
            var map = new Dictionary<Guid, Game>();
            foreach (var game in games)
            {
                var ids = attributeSelector(game);
                if (ids == null || ids.Count == 0)
                {
                    continue;
                }

                var key = ReleaseDateKey(game);
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    if (!map.TryGetValue(id, out var current) || ReleaseDateKey(current) > key)
                    {
                        map[id] = game;
                    }
                }
            }

            return map;
        }

        private static void PickEarlier(
            Dictionary<Guid, Game> earliestMap,
            IList<Guid> ids,
            ref Game best,
            ref DateTime bestKey)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (earliestMap.TryGetValue(ids[i], out var candidate))
                {
                    var k = ReleaseDateKey(candidate);
                    if (k < bestKey)
                    {
                        best = candidate;
                        bestKey = k;
                    }
                }
            }
        }

        private static DateTime ReleaseDateKey(Game game)
        {
            var rd = game.ReleaseDate;
            if (rd.HasValue && rd.Value.Year != 0)
            {
                return rd.Value.Date;
            }

            return DateTime.MaxValue;
        }

        private class RankedGame
        {
            public RankedGame(Game game, int ruleRank, DateTime dateKey, DateTime secondaryDateKey, int originalIndex)
            {
                Game = game;
                RuleRank = ruleRank;
                DateKey = dateKey;
                SecondaryDateKey = secondaryDateKey;
                OriginalIndex = originalIndex;
            }

            public Game Game { get; }
            public int RuleRank { get; }
            public DateTime DateKey { get; }
            public DateTime SecondaryDateKey { get; }
            public int OriginalIndex { get; }
            public bool PushBack { get; set; }
        }
    }
}
