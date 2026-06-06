using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// Concat-bucket display aggregators for merged tiles.
    /// </summary>
    public static class MergeGroupDisplay
    {
        public static DateTime? AcquiredDate(IList<Game> members) =>
            MergeGroupReducer.MinNullable(members?.Select(g => g.AcquiredDate));

        public static bool MobileAny(IList<Game> members) =>
            members?.Any(g => g != null && g.Mobile) == true;

        public static bool FavoriteAny(IList<Game> members) =>
            members?.Any(g => g != null && g.Favorite) == true;

        /// <summary>
        /// Free only when every member is free.
        /// </summary>
        public static bool FreeAll(IList<Game> members) =>
            members?.Count > 0 && members.All(g => g != null && g.Free);

        public static ulong PlaytimeSum(IList<Game> members)
        {
            ulong sum = 0;
            if (members == null)
            {
                return sum;
            }

            foreach (var game in members)
            {
                if (game != null)
                {
                    sum += game.Playtime;
                }
            }

            return sum;
        }

        public static ulong PlayCountSum(IList<Game> members)
        {
            ulong sum = 0;
            if (members == null)
            {
                return sum;
            }

            foreach (var game in members)
            {
                if (game != null)
                {
                    sum += game.PlayCount;
                }
            }

            return sum;
        }

        public static DateTime? LastActivityMax(IList<Game> members)
        {
            DateTime? max = null;
            foreach (var game in members ?? Enumerable.Empty<Game>())
            {
                if (game?.LastActivity == null)
                {
                    continue;
                }

                if (!max.HasValue || game.LastActivity.Value > max.Value)
                {
                    max = game.LastActivity;
                }
            }

            return max;
        }

        public static DateTime? RecentActivityMax(IList<Game> members)
        {
            DateTime? max = null;
            foreach (var game in members ?? Enumerable.Empty<Game>())
            {
                if (game?.RecentActivity == null)
                {
                    continue;
                }

                if (!max.HasValue || game.RecentActivity.Value > max.Value)
                {
                    max = game.RecentActivity;
                }
            }

            return max;
        }

        public static List<Guid> TagIdsUnion(IList<Game> members) =>
            MergeGroupReducer.UnionIdLists(members?.Select(g => g?.TagIds));

        public static List<Guid> PlatformIdsUnion(IList<Game> members) =>
            MergeGroupReducer.UnionIdLists(members?.Select(g => g?.PlatformIds));

        public static List<Guid> AgeRatingIdsConcat(IList<Game> members) =>
            MergeGroupReducer.UnionIdLists(members?.Select(g => g?.AgeRatingIds));

        public static List<Guid> RegionIdsConcat(IList<Game> members) =>
            MergeGroupReducer.UnionIdLists(members?.Select(g => g?.RegionIds));

        public static HashSet<Guid> SourceIds(IList<Game> members)
        {
            var set = new HashSet<Guid>();
            foreach (var game in members ?? Enumerable.Empty<Game>())
            {
                if (game != null && game.SourceId != Guid.Empty)
                {
                    set.Add(game.SourceId);
                }
            }

            return set;
        }
    }
}
