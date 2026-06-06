using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// In-memory index of merge groups for O(1) member lookup and tile collapse.
    /// </summary>
    public class MergeGroupIndex
    {
        private readonly Dictionary<Guid, List<Game>> groupsById = new Dictionary<Guid, List<Game>>();
        private readonly Dictionary<Guid, Guid> groupIdByGameId = new Dictionary<Guid, Guid>();

        public void Rebuild(IEnumerable<Game> games)
        {
            groupsById.Clear();
            groupIdByGameId.Clear();
            if (games == null)
            {
                return;
            }

            foreach (var game in games)
            {
                if (game?.MergeGroupId == null)
                {
                    continue;
                }

                var groupId = game.MergeGroupId.Value;
                if (!groupsById.TryGetValue(groupId, out var list))
                {
                    list = new List<Game>();
                    groupsById[groupId] = list;
                }

                list.Add(game);
                groupIdByGameId[game.Id] = groupId;
            }

            foreach (var list in groupsById.Values)
            {
                list.Sort((a, b) =>
                {
                    var cmp = Nullable.Compare(a.Added, b.Added);
                    if (cmp != 0)
                    {
                        return cmp;
                    }

                    return a.Id.CompareTo(b.Id);
                });
            }
        }

        public IList<Game> GetMembers(Guid mergeGroupId)
        {
            if (groupsById.TryGetValue(mergeGroupId, out var list))
            {
                return list;
            }

            return Array.Empty<Game>();
        }

        public IList<Game> GetMembersForGame(Game game)
        {
            if (game?.MergeGroupId == null)
            {
                return game == null ? Array.Empty<Game>() : new[] { game };
            }

            return GetMembers(game.MergeGroupId.Value);
        }

        public Game GetRepresentative(Guid mergeGroupId)
        {
            var members = GetMembers(mergeGroupId);
            return members.FirstOrDefault();
        }

        public bool IsRepresentative(Game game)
        {
            if (game == null)
            {
                return false;
            }

            if (game.MergeGroupId == null)
            {
                return true;
            }

            var rep = GetRepresentative(game.MergeGroupId.Value);
            return rep != null && rep.Id == game.Id;
        }

        public bool ShouldShowAsLibraryTile(Game game) => IsRepresentative(game);

        public IEnumerable<Game> CollapseForDisplay(IEnumerable<Game> games)
        {
            if (games == null)
            {
                yield break;
            }

            var seenGroups = new HashSet<Guid>();
            foreach (var game in games)
            {
                if (game == null)
                {
                    continue;
                }

                if (game.MergeGroupId == null)
                {
                    yield return game;
                    continue;
                }

                var groupId = game.MergeGroupId.Value;
                if (seenGroups.Contains(groupId))
                {
                    continue;
                }

                seenGroups.Add(groupId);
                var rep = GetRepresentative(groupId) ?? game;
                yield return rep;
            }
        }

        public int GetMemberCount(Game game)
        {
            if (game?.MergeGroupId == null)
            {
                return 1;
            }

            return GetMembers(game.MergeGroupId.Value).Count;
        }

        /// <summary>
        /// Collapses merge groups to representatives and projects group-wide queue
        /// state so <see cref="QueueOrdering.BuildQueue"/> treats each merged title
        /// as one slot (including playing / terminal / hold / hidden checks).
        /// </summary>
        public IList<Game> PrepareForQueue(IEnumerable<Game> allGames, QueueSettings settings)
        {
            var result = new List<Game>();
            if (allGames == null)
            {
                return result;
            }

            foreach (var rep in CollapseForDisplay(allGames))
            {
                if (rep == null)
                {
                    continue;
                }

                if (rep.MergeGroupId == null)
                {
                    result.Add(rep);
                    continue;
                }

                var members = GetMembers(rep.MergeGroupId.Value);
                if (members.Count <= 1)
                {
                    result.Add(rep);
                    continue;
                }

                var projected = rep.GetCopy();
                projected.Hidden = members.All(m => m.Hidden);
                projected.OnHold = members.All(m => m.OnHold);

                if (settings?.PlayingStatusId is Guid playingId
                    && playingId != Guid.Empty
                    && members.Any(m => m.CompletionStatusId == playingId))
                {
                    projected.CompletionStatusId = playingId;
                }
                else if (settings?.TerminalStatusIds?.Count > 0
                    && members.All(m => settings.TerminalStatusIds.Contains(m.CompletionStatusId)))
                {
                    projected.CompletionStatusId = members[0].CompletionStatusId;
                }

                result.Add(projected);
            }

            return result;
        }
    }
}
