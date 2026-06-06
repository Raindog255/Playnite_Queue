using Playnite.Database;
using Playnite.LibrarySanitizer;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace Playnite.DesktopApp.ViewModels
{
    /// <summary>
    /// Owns the Library Sanitizer action list. Subscribes to the game/series/
    /// company collections and the LibrarySanitizerSettings, coalescing all
    /// change notifications into a single deferred rebuild that delegates to
    /// the pure <see cref="LibrarySanitizerEngine"/> for action generation.
    /// </summary>
    /// <remarks>
    /// Apply, Dismiss, Restore, EditGame, and Refresh commands all live here so
    /// the view template can stay declarative. The engine treats actions as
    /// throwaway and regenerates them on every rebuild.
    /// </remarks>
    public class LibrarySanitizerViewModel : ObservableObject, IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly DesktopAppViewModel mainModel;
        private readonly PlayniteSettings appSettings;
        private readonly IGameDatabaseMain database;
        private readonly Dispatcher dispatcher;
        private readonly object actionsLock = new object();

        private LibrarySanitizerSettings boundSettings;
        private ObservableCollection<string> boundDismissed;
        private ObservableCollection<string> boundEditionKeywords;
        private ObservableCollection<string> boundCollectionKeywords;
        private bool rebuildScheduled;
        private bool disposed;

        public ObservableCollection<SanitizerAction> Actions { get; } = new ObservableCollection<SanitizerAction>();

        public bool IsEmpty => Actions.Count == 0;

        public int ActionCount => Actions.Count;

        public RelayCommand<SanitizerAction> ApplyCommand { get; }
        public RelayCommand<SanitizerAction> DismissCommand { get; }
        public RelayCommand<SanitizerAction> EditGameCommand { get; }
        public RelayCommand RestoreDismissedCommand { get; }
        public RelayCommand RefreshCommand { get; }

        public LibrarySanitizerViewModel(DesktopAppViewModel mainModel)
        {
            if (mainModel == null) throw new ArgumentNullException(nameof(mainModel));
            this.mainModel = mainModel;
            appSettings = mainModel.AppSettings;
            database = mainModel.Database;
            dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            BindingOperations.EnableCollectionSynchronization(Actions, actionsLock);

            Actions.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ActionCount));
            };

            ApplyCommand = new RelayCommand<SanitizerAction>(ApplyAction, a => a != null);
            DismissCommand = new RelayCommand<SanitizerAction>(DismissAction, a => a != null);
            EditGameCommand = new RelayCommand<SanitizerAction>(EditGame, a => a?.GetTargetGame() != null);
            RestoreDismissedCommand = new RelayCommand(RestoreDismissed,
                () => boundSettings?.DismissedActionSignatures?.Count > 0);
            RefreshCommand = new RelayCommand(Refresh);

            SubscribeAppSettings();
            SubscribeDatabase();
            BindSettings(appSettings?.LibrarySanitizerSettings);
            Refresh();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UnsubscribeAppSettings();
            UnsubscribeDatabase();
            UnbindSettings();
        }

        // ---------------- Subscriptions ----------------

        private void SubscribeAppSettings()
        {
            if (appSettings != null)
            {
                appSettings.PropertyChanged += AppSettings_PropertyChanged;
            }
        }

        private void UnsubscribeAppSettings()
        {
            if (appSettings != null)
            {
                appSettings.PropertyChanged -= AppSettings_PropertyChanged;
            }
        }

        private void SubscribeDatabase()
        {
            if (database == null) return;
            database.Games.ItemUpdated += Db_GamesUpdated;
            database.Games.ItemCollectionChanged += Db_GamesCollectionChanged;
            database.Series.ItemUpdated += Db_GenericUpdated;
            database.Series.ItemCollectionChanged += Db_GenericCollectionChanged;
            database.Companies.ItemUpdated += Db_GenericUpdated;
            database.Companies.ItemCollectionChanged += Db_GenericCollectionChanged;
            database.Genres.ItemUpdated += Db_GenericUpdated;
            database.Genres.ItemCollectionChanged += Db_GenericCollectionChanged;
            database.Tags.ItemUpdated += Db_GenericUpdated;
            database.Tags.ItemCollectionChanged += Db_GenericCollectionChanged;
            database.CompletionStatuses.ItemUpdated += Db_GenericUpdated;
        }

        private void UnsubscribeDatabase()
        {
            if (database == null) return;
            database.Games.ItemUpdated -= Db_GamesUpdated;
            database.Games.ItemCollectionChanged -= Db_GamesCollectionChanged;
            database.Series.ItemUpdated -= Db_GenericUpdated;
            database.Series.ItemCollectionChanged -= Db_GenericCollectionChanged;
            database.Companies.ItemUpdated -= Db_GenericUpdated;
            database.Companies.ItemCollectionChanged -= Db_GenericCollectionChanged;
            database.Genres.ItemUpdated -= Db_GenericUpdated;
            database.Genres.ItemCollectionChanged -= Db_GenericCollectionChanged;
            database.Tags.ItemUpdated -= Db_GenericUpdated;
            database.Tags.ItemCollectionChanged -= Db_GenericCollectionChanged;
            database.CompletionStatuses.ItemUpdated -= Db_GenericUpdated;
        }

        private void AppSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayniteSettings.LibrarySanitizerSettings))
            {
                BindSettings(appSettings?.LibrarySanitizerSettings);
                ScheduleRebuild();
            }
        }

        private void BindSettings(LibrarySanitizerSettings settings)
        {
            UnbindSettings();
            boundSettings = settings;
            if (boundSettings == null) return;

            boundSettings.PropertyChanged += Settings_PropertyChanged;
            BindDismissed(boundSettings.DismissedActionSignatures);
            BindEditionKeywords(boundSettings.EditionKeywords);
            BindCollectionKeywords(boundSettings.CollectionKeywords);
        }

        private void UnbindSettings()
        {
            if (boundSettings != null)
            {
                boundSettings.PropertyChanged -= Settings_PropertyChanged;
                boundSettings = null;
            }
            UnbindDismissed();
            UnbindEditionKeywords();
            UnbindCollectionKeywords();
        }

        private void BindDismissed(ObservableCollection<string> coll)
        {
            UnbindDismissed();
            boundDismissed = coll;
            if (boundDismissed != null)
            {
                boundDismissed.CollectionChanged += Dismissed_CollectionChanged;
            }
        }

        private void UnbindDismissed()
        {
            if (boundDismissed != null)
            {
                boundDismissed.CollectionChanged -= Dismissed_CollectionChanged;
                boundDismissed = null;
            }
        }

        private void BindEditionKeywords(ObservableCollection<string> coll)
        {
            UnbindEditionKeywords();
            boundEditionKeywords = coll;
            if (boundEditionKeywords != null)
            {
                boundEditionKeywords.CollectionChanged += Keywords_CollectionChanged;
            }
        }

        private void UnbindEditionKeywords()
        {
            if (boundEditionKeywords != null)
            {
                boundEditionKeywords.CollectionChanged -= Keywords_CollectionChanged;
                boundEditionKeywords = null;
            }
        }

        private void BindCollectionKeywords(ObservableCollection<string> coll)
        {
            UnbindCollectionKeywords();
            boundCollectionKeywords = coll;
            if (boundCollectionKeywords != null)
            {
                boundCollectionKeywords.CollectionChanged += Keywords_CollectionChanged;
            }
        }

        private void UnbindCollectionKeywords()
        {
            if (boundCollectionKeywords != null)
            {
                boundCollectionKeywords.CollectionChanged -= Keywords_CollectionChanged;
                boundCollectionKeywords = null;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // The setters for these collection-typed properties replace the
            // backing collection wholesale; reattach to the new instance.
            switch (e.PropertyName)
            {
                case nameof(LibrarySanitizerSettings.DismissedActionSignatures):
                    BindDismissed(boundSettings?.DismissedActionSignatures);
                    break;
                case nameof(LibrarySanitizerSettings.EditionKeywords):
                    BindEditionKeywords(boundSettings?.EditionKeywords);
                    break;
                case nameof(LibrarySanitizerSettings.CollectionKeywords):
                    BindCollectionKeywords(boundSettings?.CollectionKeywords);
                    break;
            }
            ScheduleRebuild();
        }

        private void Dismissed_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) =>
            ScheduleRebuild();

        private void Keywords_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) =>
            ScheduleRebuild();

        private void Db_GamesUpdated(object sender, ItemUpdatedEventArgs<Game> e) => ScheduleRebuild();

        private void Db_GamesCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e) =>
            ScheduleRebuild();

        private void Db_GenericUpdated<T>(object sender, ItemUpdatedEventArgs<T> e) where T : DatabaseObject =>
            ScheduleRebuild();

        private void Db_GenericCollectionChanged<T>(object sender, ItemCollectionChangedEventArgs<T> e)
            where T : DatabaseObject =>
            ScheduleRebuild();

        // ---------------- Rebuild pipeline ----------------

        public void Refresh()
        {
            Rebuild();
        }

        private void ScheduleRebuild()
        {
            if (disposed) return;

            lock (actionsLock)
            {
                if (rebuildScheduled) return;
                rebuildScheduled = true;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                lock (actionsLock)
                {
                    rebuildScheduled = false;
                }
                Rebuild();
            }), DispatcherPriority.Background);
        }

        private void Rebuild()
        {
            if (disposed) return;

            try
            {
                if (database?.IsOpen != true || boundSettings == null)
                {
                    ApplyActions(new List<SanitizerAction>());
                    return;
                }

                var dismissed = new HashSet<string>(boundSettings.DismissedActionSignatures ?? new ObservableCollection<string>());
                var actions = LibrarySanitizerEngine.BuildActions(
                    database.Games,
                    database.Series,
                    database.Companies,
                    boundSettings,
                    dismissed,
                    database.Genres,
                    database.Tags,
                    database.CompletionStatuses);
                ApplyActions(actions);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to rebuild library sanitizer actions.");
            }
        }

        private void ApplyActions(IList<SanitizerAction> next)
        {
            lock (actionsLock)
            {
                Actions.Clear();
                foreach (var a in next)
                {
                    Actions.Add(a);
                }
            }
        }

        // ---------------- Commands ----------------

        private void ApplyAction(SanitizerAction action)
        {
            if (action == null || database == null) return;
            try
            {
                action.Apply(database);
                RefreshMetadataCaches(action);
                // The DB update event will eventually trigger a rebuild that
                // recomputes the action list. Remove the card immediately so
                // the user sees the apply land without waiting for the
                // dispatcher round-trip.
                lock (actionsLock)
                {
                    Actions.Remove(action);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to apply sanitizer action {action.Signature}.");
            }
        }

        private void RefreshMetadataCaches(SanitizerAction action)
        {
            if (!(action is RemoveUnusedMetadataAction removeUnused))
            {
                return;
            }

            var fields = removeUnused.GetAffectedCollections();
            if (fields == null || fields.Count == 0)
            {
                return;
            }

            mainModel?.DatabaseFilters?.RefreshCollections(fields.ToArray());
        }

        private void DismissAction(SanitizerAction action)
        {
            if (action == null || boundSettings == null) return;
            if (!boundSettings.DismissedActionSignatures.Contains(action.Signature))
            {
                boundSettings.DismissedActionSignatures.Add(action.Signature);
            }
            // Dismissed_CollectionChanged will trigger a rebuild and refresh
            // the can-execute on RestoreDismissedCommand. We still pop the
            // card immediately for snappier feedback.
            lock (actionsLock)
            {
                Actions.Remove(action);
            }
        }

        private void EditGame(SanitizerAction action)
        {
            if (action is MergeAction merge)
            {
                ((DesktopGamesEditor)mainModel.GamesEditor).EditMergeGroup(merge.Members);
                return;
            }

            var game = action?.GetTargetGame();
            if (game == null) return;
            mainModel.EditGame(game);
        }

        private void RestoreDismissed()
        {
            if (boundSettings?.DismissedActionSignatures == null) return;
            boundSettings.DismissedActionSignatures.Clear();
            // Cleared collection re-raises CollectionChanged which schedules
            // the rebuild and re-evaluates the restore button availability.
        }

    }
}
