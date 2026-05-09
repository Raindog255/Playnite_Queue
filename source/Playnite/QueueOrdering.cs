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
        /// Builds the ordered queue. The result is what the Queue view should show,
        /// already truncated to <see cref="QueueSettings.VisibleCount"/> with any
        /// always-visible Playing games prepended (Playing games are exempt from the
        /// visible-count cap and may exceed it).
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
            // We capture the original index so any later rebucketing can preserve
            // order among ties without re-running the date sort.
            var ordered = eligible
                .Select((g, i) => new RankedGame(g, RuleRank(g, rules), DateKey(g, settings.DateSource), i))
                .OrderBy(r => r.RuleRank)
                .ThenBy(r => r.DateKey)
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
                    .ThenBy(r => r.OriginalIndex)
                    .ToList();
            }

            var orderedGames = ordered.Select(r => r.Game).ToList();

            // 5. Grouping replacement (Playing games are exempt — they're separate).
            var grouped = ApplyGrouping(orderedGames, settings.GroupByDeveloper, settings.GroupBySeries);

            // 6. Take top N.
            var visibleCount = Math.Max(0, settings.VisibleCount);
            var takeN = grouped.Take(visibleCount).ToList();

            // 7. Prepend Playing games (sorted by the same priority + date pipeline,
            // but never grouped or pushed back; they always show).
            var playingOrdered = playingGames
                .Select((g, i) => new RankedGame(g, RuleRank(g, rules), DateKey(g, settings.DateSource), i))
                .OrderBy(r => r.RuleRank)
                .ThenBy(r => r.DateKey)
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
                    return game.AcquiredDate ?? DateTime.MaxValue;

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
        /// Walks the ordered list top-to-bottom. The first game with a given Developer
        /// (when <paramref name="byDeveloper"/>) or Series (when <paramref name="bySeries"/>)
        /// is kept; subsequent games sharing any of those attributes are dropped. A game
        /// with no developer/series is never suppressed by the corresponding rule.
        /// </summary>
        internal static IList<Game> ApplyGrouping(IList<Game> ordered, bool byDeveloper, bool bySeries)
        {
            if (!byDeveloper && !bySeries)
            {
                return ordered;
            }

            var seenDevelopers = new HashSet<Guid>();
            var seenSeries = new HashSet<Guid>();
            var kept = new List<Game>(ordered.Count);

            foreach (var game in ordered)
            {
                if (byDeveloper && IntersectsAny(game.DeveloperIds, seenDevelopers))
                {
                    continue;
                }

                if (bySeries && IntersectsAny(game.SeriesIds, seenSeries))
                {
                    continue;
                }

                kept.Add(game);
                if (byDeveloper)
                {
                    AddAll(seenDevelopers, game.DeveloperIds);
                }
                if (bySeries)
                {
                    AddAll(seenSeries, game.SeriesIds);
                }
            }

            return kept;
        }

        private class RankedGame
        {
            public RankedGame(Game game, int ruleRank, DateTime dateKey, int originalIndex)
            {
                Game = game;
                RuleRank = ruleRank;
                DateKey = dateKey;
                OriginalIndex = originalIndex;
            }

            public Game Game { get; }
            public int RuleRank { get; }
            public DateTime DateKey { get; }
            public int OriginalIndex { get; }
            public bool PushBack { get; set; }
        }
    }
}
