using Playnite;
using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Playnite.MergeGroups
{
    /// <summary>
    /// Applies Sync-bucket values and propagates sync edits across merge group members.
    /// </summary>
    public static class MergeGroupSync
    {
        public static void ApplyReducedValues(IList<Game> members, MergeGroupReducedValues values, IGameDatabaseMain database)
        {
            if (members == null || members.Count == 0 || values == null)
            {
                return;
            }

            foreach (var game in members)
            {
                ApplyReducedValuesToGame(game, values, database);
            }
        }

        public static void ApplyReducedValuesToGame(Game game, MergeGroupReducedValues values, IGameDatabaseMain database)
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
            game.Icon = SyncIconToGame(game, values.Icon, database);
            game.CoverImage = SyncCoverToGame(game, values.CoverImage, database);
            game.BackgroundImage = SyncBackgroundToGame(game, values.BackgroundImage, database);
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
                CopyChangedSyncFields(beforeEdit, edited, target, database);
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

        internal static void CopyChangedSyncFields(Game before, Game after, Game target, IGameDatabaseMain database)
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
                target.Icon = SyncIconToGame(target, after.Icon, database);
            }

            if (!string.Equals(before.CoverImage, after.CoverImage, StringComparison.Ordinal))
            {
                target.CoverImage = SyncCoverToGame(target, after.CoverImage, database);
            }

            if (!string.Equals(before.BackgroundImage, after.BackgroundImage, StringComparison.Ordinal))
            {
                target.BackgroundImage = SyncBackgroundToGame(target, after.BackgroundImage, database);
            }
        }

        internal static string SyncIconToGame(Game target, string sourceDbPath, IGameDatabaseMain database) =>
            CopyImageToGame(target, sourceDbPath, database);

        internal static string SyncCoverToGame(Game target, string sourceDbPath, IGameDatabaseMain database) =>
            CopyImageToGame(target, sourceDbPath, database);

        internal static string SyncBackgroundToGame(Game target, string sourceDbPath, IGameDatabaseMain database)
        {
            if (string.IsNullOrEmpty(sourceDbPath))
            {
                return null;
            }

            if (sourceDbPath.IsHttpUrl())
            {
                return sourceDbPath;
            }

            return CopyImageToGame(target, sourceDbPath, database);
        }

        private static string CopyImageToGame(Game target, string sourceDbPath, IGameDatabaseMain database)
        {
            if (string.IsNullOrEmpty(sourceDbPath))
            {
                return null;
            }

            if (IsPathForGame(sourceDbPath, target.Id))
            {
                return sourceDbPath;
            }

            if (database == null)
            {
                return sourceDbPath;
            }

            var fullPath = ResolveFullFilePath(sourceDbPath, database);
            if (fullPath == null)
            {
                return sourceDbPath;
            }

            return database.AddFile(fullPath, target.Id, true, CancellationToken.None);
        }

        private static string ResolveFullFilePath(string sourceDbPath, IGameDatabaseMain database)
        {
            if (sourceDbPath.IsHttpUrl())
            {
                return sourceDbPath;
            }

            var fullPath = database.GetFullFilePath(sourceDbPath);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            if (File.Exists(sourceDbPath))
            {
                return sourceDbPath;
            }

            return null;
        }

        private static bool IsPathForGame(string dbPath, Guid gameId)
        {
            if (string.IsNullOrEmpty(dbPath))
            {
                return false;
            }

            var prefix = gameId.ToString();
            return dbPath.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || dbPath.StartsWith(prefix + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || dbPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static List<Guid> CloneList(List<Guid> source) =>
            source == null || source.Count == 0 ? null : source.ToList();
    }
}
