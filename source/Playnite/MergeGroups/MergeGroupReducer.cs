using Playnite.LibrarySanitizer;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// Computes Sync-bucket values for a merge group and exposes concat display helpers.
    /// </summary>
    public static class MergeGroupReducer
    {
        public static MergeGroupReducedValues Reduce(
            IList<Game> members,
            LibrarySanitizerSettings sanitizerSettings,
            IEnumerable<CompletionStatus> completionStatuses = null)
        {
            if (members == null || members.Count == 0)
            {
                return new MergeGroupReducedValues();
            }

            var ordered = OrderMembersStable(members);
            var canonicalName = ReduceCanonicalName(ordered, sanitizerSettings);
            return new MergeGroupReducedValues
            {
                Name = canonicalName.Name,
                SortingName = canonicalName.SortingName,
                Description = FirstNonEmpty(ordered, g => g.Description),
                ReleaseDate = MinReleaseDate(ordered),
                CompletedDate = MinNullable(ordered.Select(g => g.CompletedDate)),
                CompletionStatusId = HighestCompletionStatus(ordered, completionStatuses),
                UserScore = MaxNullableInt(ordered.Select(g => g.UserScore)),
                CriticScore = AverageNullableInt(ordered.Select(g => g.CriticScore)),
                CommunityScore = AverageNullableInt(ordered.Select(g => g.CommunityScore)),
                SeriesIds = UnionIdLists(ordered.Select(g => g.SeriesIds)),
                GenreIds = UnionIdLists(ordered.Select(g => g.GenreIds)),
                CategoryIds = UnionIdLists(ordered.Select(g => g.CategoryIds)),
                DeveloperIds = DevelopersFromEarliestRelease(ordered),
                PublisherIds = PublishersFromEarliestRelease(ordered),
                OnHold = ordered.Any(g => g.OnHold),
                Icon = FirstNonEmpty(ordered, g => g.Icon),
                CoverImage = FirstNonEmpty(ordered, g => g.CoverImage),
                BackgroundImage = FirstNonEmpty(ordered, g => g.BackgroundImage),
            };
        }

        public static string GetMergeMatchKey(Game game, LibrarySanitizerSettings settings)
        {
            if (game == null || string.IsNullOrEmpty(game.Name))
            {
                return null;
            }

            var result = SanitizerRules.Apply(game.Name, SanitizerScope.GameName, settings);
            var name = result.SanitizedName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            var sorting = !string.IsNullOrEmpty(result.SortingNameOverride)
                ? result.SortingNameOverride
                : game.SortingName;
            return name + "\u001f" + (sorting ?? string.Empty);
        }

        internal static IList<Game> OrderMembersStable(IEnumerable<Game> members) =>
            members?.Where(g => g != null).OrderBy(g => g.Added).ThenBy(g => g.Id).ToList()
            ?? new List<Game>();

        internal static (string Name, string SortingName) ReduceCanonicalName(
            IList<Game> ordered,
            LibrarySanitizerSettings settings)
        {
            if (ordered.Count == 0)
            {
                return (string.Empty, null);
            }

            var best = ordered
                .Select(g =>
                {
                    var r = SanitizerRules.Apply(g.Name, SanitizerScope.GameName, settings);
                    return new
                    {
                        Name = r.SanitizedName,
                        Sorting = !string.IsNullOrEmpty(r.SortingNameOverride) ? r.SortingNameOverride : g.SortingName
                    };
                })
                .FirstOrDefault(x => !string.IsNullOrEmpty(x.Name));

            if (best == null)
            {
                return (ordered[0].Name, ordered[0].SortingName);
            }

            return (best.Name, best.Sorting);
        }

        internal static string FirstNonEmpty(IList<Game> ordered, Func<Game, string> selector)
        {
            foreach (var game in ordered)
            {
                var value = selector(game);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        internal static ReleaseDate? MinReleaseDate(IList<Game> ordered)
        {
            ReleaseDate? min = null;
            foreach (var game in ordered)
            {
                var rd = game.ReleaseDate;
                if (rd == null || rd.Value.Year == 0)
                {
                    continue;
                }

                if (min == null || rd.Value.Date < min.Value.Date)
                {
                    min = rd;
                }
            }

            return min;
        }

        internal static DateTime? MinNullable(IEnumerable<DateTime?> values)
        {
            DateTime? min = null;
            foreach (var value in values)
            {
                if (!value.HasValue)
                {
                    continue;
                }

                if (!min.HasValue || value.Value < min.Value)
                {
                    min = value;
                }
            }

            return min;
        }

        internal static int? MaxNullableInt(IEnumerable<int?> values)
        {
            int? max = null;
            foreach (var value in values)
            {
                if (!value.HasValue)
                {
                    continue;
                }

                if (!max.HasValue || value.Value > max.Value)
                {
                    max = value;
                }
            }

            return max;
        }

        internal static int? AverageNullableInt(IEnumerable<int?> values)
        {
            var nums = values.Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (nums.Count == 0)
            {
                return null;
            }

            return (int)Math.Round(nums.Average());
        }

        internal static List<Guid> UnionIdLists(IEnumerable<List<Guid>> lists)
        {
            var set = new HashSet<Guid>();
            foreach (var list in lists)
            {
                if (list == null)
                {
                    continue;
                }

                foreach (var id in list)
                {
                    if (id != Guid.Empty)
                    {
                        set.Add(id);
                    }
                }
            }

            return set.Count == 0 ? null : set.ToList();
        }

        internal static List<Guid> DevelopersFromEarliestRelease(IList<Game> ordered)
        {
            var anchor = ordered
                .Where(g => g.ReleaseDate != null && g.ReleaseDate.Value.Year != 0)
                .OrderBy(g => g.ReleaseDate.Value.Date)
                .ThenBy(g => g.Added)
                .FirstOrDefault();
            if (anchor?.DeveloperIds?.Count > 0)
            {
                return anchor.DeveloperIds.ToList();
            }

            return UnionIdLists(ordered.Select(g => g.DeveloperIds));
        }

        internal static List<Guid> PublishersFromEarliestRelease(IList<Game> ordered)
        {
            var anchor = ordered
                .Where(g => g.ReleaseDate != null && g.ReleaseDate.Value.Year != 0)
                .OrderBy(g => g.ReleaseDate.Value.Date)
                .ThenBy(g => g.Added)
                .FirstOrDefault();
            if (anchor?.PublisherIds?.Count > 0)
            {
                return anchor.PublisherIds.ToList();
            }

            return UnionIdLists(ordered.Select(g => g.PublisherIds));
        }

        internal static Guid HighestCompletionStatus(IList<Game> ordered, IEnumerable<CompletionStatus> completionStatuses)
        {
            var statusById = completionStatuses?.ToDictionary(s => s.Id) ?? new Dictionary<Guid, CompletionStatus>();
            var best = ordered.FirstOrDefault()?.CompletionStatusId ?? Guid.Empty;
            var bestRank = int.MinValue;
            foreach (var game in ordered)
            {
                statusById.TryGetValue(game.CompletionStatusId, out var status);
                var rank = CompletionStatusRank(status?.Name);
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = game.CompletionStatusId;
                }
            }

            return best;
        }

        internal static int CompletionStatusRank(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return 0;
            }

            var n = name.Trim();
            if (n.Equals("Playing", StringComparison.OrdinalIgnoreCase))
            {
                return 100;
            }

            if (n.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Beaten", StringComparison.OrdinalIgnoreCase)
                || n.Equals("Played", StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }

            if (n.Equals("Abandoned", StringComparison.OrdinalIgnoreCase))
            {
                return 40;
            }

            if (n.Equals("Not Played", StringComparison.OrdinalIgnoreCase)
                || n.Equals("NotPlayed", StringComparison.OrdinalIgnoreCase))
            {
                return 10;
            }

            return 20;
        }
    }
}
