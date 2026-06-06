using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// Applies Sync-bucket values and propagates sync edits across merge group members.
    /// </summary>
    public static class MergeGroupSync
    {
        public static void ApplyReducedValues(IList<Game> members, MergeGroupReducedValues values)
        {
            if (members == null || members.Count == 0 || values == null)
            {
                return;
            }

            foreach (var game in members)
            {
                ApplyReducedValuesToGame(game, values);
            }
        }

        public static void ApplyReducedValuesToGame(Game game, MergeGroupReducedValues values)
        {
            if (game == null || values == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(values.Name))
            {
                game.Name = values.Name;
            }

            game.SortingName = values.SortingName;
            game.Description = values.Description;
            game.ReleaseDate = values.ReleaseDate;
            game.CompletedDate = values.CompletedDate;
            if (values.CompletionStatusId != Guid.Empty)
            {
                game.CompletionStatusId = values.CompletionStatusId;
            }

            game.UserScore = values.UserScore;
            game.CriticScore = values.CriticScore;
            game.CommunityScore = values.CommunityScore;
            game.SeriesIds = CloneList(values.SeriesIds);
            game.GenreIds = CloneList(values.GenreIds);
            game.CategoryIds = CloneList(values.CategoryIds);
            game.DeveloperIds = CloneList(values.DeveloperIds);
            game.PublisherIds = CloneList(values.PublisherIds);
            game.OnHold = values.OnHold;
            game.Icon = values.Icon;
            game.CoverImage = values.CoverImage;
            game.BackgroundImage = values.BackgroundImage;
        }

        public static void PropagateSyncFieldsFromEditedGame(
            Game edited,
            Game beforeEdit,
            IList<Game> members,
            IGameDatabaseMain database)
        {
            if (edited == null || beforeEdit == null || members == null || members.Count <= 1)
            {
                return;
            }

            var others = members.Where(g => g.Id != edited.Id).ToList();
            if (others.Count == 0)
            {
                return;
            }

            foreach (var target in others)
            {
                CopyChangedSyncFields(beforeEdit, edited, target);
            }

            database?.Games.Update(others);
        }

        public static void ApplyTagSelectionToAllMembers(
            IList<Game> members,
            IList<Guid> selectedTagIds,
            IGameDatabaseMain database)
        {
            if (members == null || members.Count == 0)
            {
                return;
            }

            var desired = selectedTagIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
            foreach (var game in members)
            {
                game.TagIds = desired.Count == 0 ? null : desired.ToList();
            }

            database?.Games.Update(members);
        }

        internal static void CopyChangedSyncFields(Game before, Game after, Game target)
        {
            if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            {
                target.Name = after.Name;
            }

            if (!string.Equals(before.SortingName, after.SortingName, StringComparison.Ordinal))
            {
                target.SortingName = after.SortingName;
            }

            if (!string.Equals(before.Description, after.Description, StringComparison.Ordinal))
            {
                target.Description = after.Description;
            }

            if (before.ReleaseDate != after.ReleaseDate)
            {
                target.ReleaseDate = after.ReleaseDate;
            }

            if (before.CompletedDate != after.CompletedDate)
            {
                target.CompletedDate = after.CompletedDate;
            }

            if (before.CompletionStatusId != after.CompletionStatusId)
            {
                target.CompletionStatusId = after.CompletionStatusId;
            }

            if (before.UserScore != after.UserScore)
            {
                target.UserScore = after.UserScore;
            }

            if (before.CriticScore != after.CriticScore)
            {
                target.CriticScore = after.CriticScore;
            }

            if (before.CommunityScore != after.CommunityScore)
            {
                target.CommunityScore = after.CommunityScore;
            }

            if (!before.SeriesIds.IsListEqual(after.SeriesIds))
            {
                target.SeriesIds = CloneList(after.SeriesIds);
            }

            if (!before.GenreIds.IsListEqual(after.GenreIds))
            {
                target.GenreIds = CloneList(after.GenreIds);
            }

            if (!before.CategoryIds.IsListEqual(after.CategoryIds))
            {
                target.CategoryIds = CloneList(after.CategoryIds);
            }

            if (!before.DeveloperIds.IsListEqual(after.DeveloperIds))
            {
                target.DeveloperIds = CloneList(after.DeveloperIds);
            }

            if (!before.PublisherIds.IsListEqual(after.PublisherIds))
            {
                target.PublisherIds = CloneList(after.PublisherIds);
            }

            if (before.OnHold != after.OnHold)
            {
                target.OnHold = after.OnHold;
            }

            if (!string.Equals(before.Icon, after.Icon, StringComparison.Ordinal))
            {
                target.Icon = after.Icon;
            }

            if (!string.Equals(before.CoverImage, after.CoverImage, StringComparison.Ordinal))
            {
                target.CoverImage = after.CoverImage;
            }

            if (!string.Equals(before.BackgroundImage, after.BackgroundImage, StringComparison.Ordinal))
            {
                target.BackgroundImage = after.BackgroundImage;
            }
        }

        private static List<Guid> CloneList(List<Guid> source) =>
            source == null || source.Count == 0 ? null : source.ToList();
    }
}
