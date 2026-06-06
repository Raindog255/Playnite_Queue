using Playnite.Commands;
using Playnite.Database;
using Playnite.DesktopApp;
using Playnite.DesktopApp.Windows;
using Playnite.MergeGroups;
using Playnite.Plugins;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.Settings;
using Playnite.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Playnite.DesktopApp.ViewModels
{
    public class MergeGroupMemberItem : ObservableObject
    {
        public Game Game { get; }
        public string Label { get; }

        public MergeGroupMemberItem(Game game, string label)
        {
            Game = game;
            Label = label;
        }
    }

    public partial class GameEditViewModel
    {
        private readonly MultiEditGame originalMergeGroupObj;

        public bool IsMergeGroupEdit { get; }

        public IList<MergeGroupMemberItem> MergeGroupMemberItems { get; }

        public RelayCommand<MergeGroupMemberItem> EditMergeMemberCommand { get; }

        private GameEditViewModel(
            IList<Game> mergeMembers,
            GameDatabase database,
            IWindowFactory window,
            IDialogsFactory dialogs,
            IResourceProvider resources,
            ExtensionFactory extensions,
            PlayniteSettings appSettings,
            bool mergeGroupEdit)
        {
            if (!mergeGroupEdit)
            {
                throw new InvalidOperationException();
            }

            var members = mergeMembers?.Where(g => g != null).Select(g => g.GetClone()).ToList() ?? new List<Game>();
            if (members.Count < 2)
            {
                throw new ArgumentException("Merge group edit requires at least two games.", nameof(mergeMembers));
            }

            Games = members;
            Game = members[0].GetClone();
            IsSingleGameEdit = false;
            IsMultiGameEdit = false;
            IsMergeGroupEdit = true;
            EditingGame = new MultiEditGame();
            ShowCheckBoxes = true;
            ShowMetaDownload = false;
            Init(database, window, dialogs, resources, extensions, appSettings, null);
            MergeGroupEditSeeder.Seed(this, members, database.CompletionStatuses, appSettings.LibrarySanitizerSettings);
            originalMergeGroupObj = EditingGame.GetClone<Game, MultiEditGame>();

            MergeGroupMemberItems = members
                .Select(m => new MergeGroupMemberItem(
                    m,
                    FormatMergeMemberLabel(m, database)))
                .ToList();

            EditMergeMemberCommand = new RelayCommand<MergeGroupMemberItem>(EditMergeMember, m => m?.Game != null);
        }

        public static GameEditViewModel CreateMergeGroupEditViewModel(
            IList<Game> mergeMembers,
            GameDatabase database,
            IWindowFactory window,
            IDialogsFactory dialogs,
            IResourceProvider resources,
            ExtensionFactory extensions,
            PlayniteSettings appSettings)
        {
            return new GameEditViewModel(
                mergeMembers,
                database,
                window,
                dialogs,
                resources,
                extensions,
                appSettings,
                mergeGroupEdit: true);
        }

        internal void RefreshSelectableListsFromEditingGame()
        {
            Genres?.SetSelection(EditingGame.GenreIds);
            Developers?.SetSelection(EditingGame.DeveloperIds);
            Publishers?.SetSelection(EditingGame.PublisherIds);
            Categories?.SetSelection(EditingGame.CategoryIds);
            Tags?.SetSelection(EditingGame.TagIds);
            Features?.SetSelection(EditingGame.FeatureIds);
            Platforms?.SetSelection(EditingGame.PlatformIds);
            Series?.SetSelection(EditingGame.SeriesIds);
            AgeRatings?.SetSelection(EditingGame.AgeRatingIds);
            Regions?.SetSelection(EditingGame.RegionIds);
        }

        private static string FormatMergeMemberLabel(Game game, GameDatabase database)
        {
            var source = game.SourceId != Guid.Empty
                ? database?.Sources?.Get(game.SourceId)?.Name
                : null;
            var platform = game.PlatformIds?.FirstOrDefault() is Guid pid && pid != Guid.Empty
                ? database?.Platforms?.Get(pid)?.Name
                : null;
            if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(platform))
            {
                return source + " — " + platform;
            }

            return source ?? platform ?? game.Name;
        }

        private void EditMergeMember(MergeGroupMemberItem item)
        {
            if (item?.Game == null)
            {
                return;
            }

            var live = database?.Games?.Get(item.Game.Id) ?? item.Game;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                var editor = new GameEditViewModel(
                    live,
                    database,
                    new GameEditWindowFactory(),
                    dialogs,
                    resources,
                    extensions,
                    appSettings);
                editor.OpenView();
            }), DispatcherPriority.ApplicationIdle);

            ignoreClosingEvent = true;
            CloseView(false, false);
        }

        private void FinalizeMergeGroupSave()
        {
            if (!IsMergeGroupEdit || Games == null || !Games.Any())
            {
                return;
            }

            var groupId = Games.First().MergeGroupId ?? Guid.NewGuid();
            foreach (var member in Games)
            {
                member.MergeGroupId = groupId;
            }

            if (UseTagChanges)
            {
                MergeGroupSync.ApplyTagSelectionToAllMembers(Games.ToList(), Tags?.GetSelectedIds(), database);
            }
        }

        private void PropagateSingleMemberSyncIfNeeded(Game beforeSaveSnapshot)
        {
            if (!IsSingleGameEdit || Game?.MergeGroupId == null || beforeSaveSnapshot == null)
            {
                return;
            }

            var members = database.MergeGroups.GetMembersForGame(Game).OfType<Game>().ToList();
            if (members.Count <= 1)
            {
                return;
            }

            MergeGroupSync.PropagateSyncFieldsFromEditedGame(Game, beforeSaveSnapshot, members, database);
        }
    }
}
