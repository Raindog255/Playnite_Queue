using Playnite.Database;
using Playnite.MergeGroups;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Proposes merging two or more games with matching sanitized names into a
    /// soft-merge group linked by <see cref="Game.MergeGroupId"/>.
    /// </summary>
    public class MergeAction : SanitizerAction
    {
        public IList<Game> Members { get; }
        public string ProposedName { get; }
        public string ProposedSortingName { get; }
        private readonly LibrarySanitizerSettings sanitizerSettings;

        public override SanitizerActionType Type => SanitizerActionType.Merge;

        public override string DisplayTitle => ProposedName;

        public override Game GetTargetGame() => Members?.FirstOrDefault();

        public override string Signature
        {
            get
            {
                var sb = new StringBuilder("Merge|");
                foreach (var id in Members.Select(m => m.Id).OrderBy(i => i))
                {
                    sb.Append(id).Append(',');
                }
                return sb.ToString();
            }
        }

        public MergeAction(IList<Game> members, string proposedName, string proposedSortingName, LibrarySanitizerSettings sanitizerSettings)
        {
            Members = members?.ToList() ?? new List<Game>();
            ProposedName = proposedName ?? string.Empty;
            ProposedSortingName = proposedSortingName;
            this.sanitizerSettings = sanitizerSettings;
        }

        public override void Apply(IGameDatabaseMain database)
        {
            if (database == null || Members.Count < 2)
            {
                return;
            }

            var reduced = MergeGroupReducer.Reduce(Members, sanitizerSettings, database?.CompletionStatuses);
            if (!string.IsNullOrEmpty(ProposedName))
            {
                reduced.Name = ProposedName;
            }

            if (!string.IsNullOrEmpty(ProposedSortingName))
            {
                reduced.SortingName = ProposedSortingName;
            }

            var groupId = Guid.NewGuid();
            using (database.BufferedUpdate())
            {
                foreach (var member in Members)
                {
                    member.MergeGroupId = groupId;
                    MergeGroupSync.ApplyReducedValuesToGame(member, reduced);
                }

                database.Games.Update(Members);
            }
        }
    }
}
