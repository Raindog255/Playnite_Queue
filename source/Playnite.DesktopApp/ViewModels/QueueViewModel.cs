using Playnite.Database;
using Playnite.Plugins;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
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
    /// Owns the visible Queue collection. Listens to game database, completion-status,
    /// series/category/genre/developer renames, and any change to <see cref="QueueSettings"/>
    /// (including nested rule/buffer mutations) and rebuilds <see cref="QueueGames"/>
    /// via the pure <see cref="QueueOrdering.BuildQueue"/> pipeline.
    /// </summary>
    /// <remarks>
    /// Multiple events from the database can fire in rapid succession (e.g. a metadata
    /// download touching dozens of games). To avoid rebuilding the queue once per event
    /// we coalesce all change notifications into a single deferred dispatcher rebuild.
    /// </remarks>
    public class QueueViewModel : ObservableObject, IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly DesktopAppViewModel mainModel;
        private readonly PlayniteSettings appSettings;
        private readonly IGameDatabaseMain database;
        private readonly ExtensionFactory extensions;
        private readonly Dispatcher dispatcher;
        private readonly object queueLock = new object();
        private readonly Dictionary<Guid, GamesCollectionViewEntry> entryCache = new Dictionary<Guid, GamesCollectionViewEntry>();

        private QueueSettings boundSettings;
        private ObservableCollection<QueuePriorityRule> boundPriorityRules;
        private ObservableCollection<Guid> boundTerminalStatuses;
        private QueueBufferSettings boundStyleBuffer;
        private QueueBufferSettings boundSeriesBuffer;
        private QueueBufferSettings boundDeveloperBuffer;
        private QueueBufferSettings boundGenreBuffer;

        private bool rebuildScheduled;
        private bool disposed;

        public ObservableCollection<GamesCollectionViewEntry> QueueGames { get; } =
            new ObservableCollection<GamesCollectionViewEntry>();

        public bool IsEmpty => QueueGames.Count == 0;

        public QueueViewModel(DesktopAppViewModel mainModel)
        {
            this.mainModel = mainModel ?? throw new ArgumentNullException(nameof(mainModel));
            appSettings = mainModel.AppSettings;
            database = mainModel.Database;
            extensions = mainModel.Extensions;
            dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            BindingOperations.EnableCollectionSynchronization(QueueGames, queueLock);

            QueueGames.CollectionChanged += (_, __) => OnPropertyChanged(nameof(IsEmpty));

            SubscribeAppSettings();
            SubscribeDatabase();
            BindSettings(appSettings?.QueueSettings);
            Refresh();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            UnsubscribeAppSettings();
            UnsubscribeDatabase();
            UnbindSettings();
            DisposeCachedEntries(entryCache.Values);
            entryCache.Clear();
        }

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
            if (database == null)
            {
                return;
            }

            database.Games.ItemUpdated += Db_GamesUpdated;
            database.Games.ItemCollectionChanged += Db_GamesCollectionChanged;
            database.CompletionStatuses.ItemUpdated += Db_GenericUpdated;
            database.Series.ItemUpdated += Db_GenericUpdated;
            database.Companies.ItemUpdated += Db_GenericUpdated;
            database.Categories.ItemUpdated += Db_GenericUpdated;
            database.Genres.ItemUpdated += Db_GenericUpdated;
        }

        private void UnsubscribeDatabase()
        {
            if (database == null)
            {
                return;
            }

            database.Games.ItemUpdated -= Db_GamesUpdated;
            database.Games.ItemCollectionChanged -= Db_GamesCollectionChanged;
            database.CompletionStatuses.ItemUpdated -= Db_GenericUpdated;
            database.Series.ItemUpdated -= Db_GenericUpdated;
            database.Companies.ItemUpdated -= Db_GenericUpdated;
            database.Categories.ItemUpdated -= Db_GenericUpdated;
            database.Genres.ItemUpdated -= Db_GenericUpdated;
        }

        private void AppSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayniteSettings.QueueSettings))
            {
                BindSettings(appSettings?.QueueSettings);
                ScheduleRebuild();
            }
        }

        private void BindSettings(QueueSettings settings)
        {
            UnbindSettings();
            boundSettings = settings;
            if (boundSettings == null)
            {
                return;
            }

            boundSettings.PropertyChanged += Settings_PropertyChanged;
            BindPriorityRules(boundSettings.PriorityRules);
            BindTerminalStatuses(boundSettings.TerminalStatusIds);
            boundStyleBuffer = HookBuffer(boundSettings.StyleBuffer);
            boundSeriesBuffer = HookBuffer(boundSettings.SeriesBuffer);
            boundDeveloperBuffer = HookBuffer(boundSettings.DeveloperBuffer);
            boundGenreBuffer = HookBuffer(boundSettings.GenreBuffer);
        }

        private void UnbindSettings()
        {
            if (boundSettings != null)
            {
                boundSettings.PropertyChanged -= Settings_PropertyChanged;
                boundSettings = null;
            }

            UnbindPriorityRules();
            UnbindTerminalStatuses();
            UnhookBuffer(ref boundStyleBuffer);
            UnhookBuffer(ref boundSeriesBuffer);
            UnhookBuffer(ref boundDeveloperBuffer);
            UnhookBuffer(ref boundGenreBuffer);
        }

        private QueueBufferSettings HookBuffer(QueueBufferSettings buffer)
        {
            if (buffer != null)
            {
                buffer.PropertyChanged += Buffer_PropertyChanged;
            }

            return buffer;
        }

        private void UnhookBuffer(ref QueueBufferSettings buffer)
        {
            if (buffer != null)
            {
                buffer.PropertyChanged -= Buffer_PropertyChanged;
                buffer = null;
            }
        }

        private void BindPriorityRules(ObservableCollection<QueuePriorityRule> rules)
        {
            UnbindPriorityRules();
            boundPriorityRules = rules;
            if (boundPriorityRules == null)
            {
                return;
            }

            boundPriorityRules.CollectionChanged += PriorityRules_CollectionChanged;
            foreach (var rule in boundPriorityRules)
            {
                rule.PropertyChanged += Rule_PropertyChanged;
            }
        }

        private void UnbindPriorityRules()
        {
            if (boundPriorityRules == null)
            {
                return;
            }

            boundPriorityRules.CollectionChanged -= PriorityRules_CollectionChanged;
            foreach (var rule in boundPriorityRules)
            {
                rule.PropertyChanged -= Rule_PropertyChanged;
            }

            boundPriorityRules = null;
        }

        private void BindTerminalStatuses(ObservableCollection<Guid> statuses)
        {
            UnbindTerminalStatuses();
            boundTerminalStatuses = statuses;
            if (boundTerminalStatuses != null)
            {
                boundTerminalStatuses.CollectionChanged += TerminalStatuses_CollectionChanged;
            }
        }

        private void UnbindTerminalStatuses()
        {
            if (boundTerminalStatuses != null)
            {
                boundTerminalStatuses.CollectionChanged -= TerminalStatuses_CollectionChanged;
                boundTerminalStatuses = null;
            }
        }

        private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Property reference may have been swapped (QueueSettings setters all
            // assign new collections). Re-attach handlers to the current targets.
            switch (e.PropertyName)
            {
                case nameof(QueueSettings.PriorityRules):
                    BindPriorityRules(boundSettings?.PriorityRules);
                    break;
                case nameof(QueueSettings.TerminalStatusIds):
                    BindTerminalStatuses(boundSettings?.TerminalStatusIds);
                    break;
                case nameof(QueueSettings.StyleBuffer):
                    UnhookBuffer(ref boundStyleBuffer);
                    boundStyleBuffer = HookBuffer(boundSettings?.StyleBuffer);
                    break;
                case nameof(QueueSettings.SeriesBuffer):
                    UnhookBuffer(ref boundSeriesBuffer);
                    boundSeriesBuffer = HookBuffer(boundSettings?.SeriesBuffer);
                    break;
                case nameof(QueueSettings.DeveloperBuffer):
                    UnhookBuffer(ref boundDeveloperBuffer);
                    boundDeveloperBuffer = HookBuffer(boundSettings?.DeveloperBuffer);
                    break;
                case nameof(QueueSettings.GenreBuffer):
                    UnhookBuffer(ref boundGenreBuffer);
                    boundGenreBuffer = HookBuffer(boundSettings?.GenreBuffer);
                    break;
            }

            ScheduleRebuild();
        }

        private void PriorityRules_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (QueuePriorityRule rule in e.OldItems)
                {
                    rule.PropertyChanged -= Rule_PropertyChanged;
                }
            }

            if (e.NewItems != null)
            {
                foreach (QueuePriorityRule rule in e.NewItems)
                {
                    rule.PropertyChanged += Rule_PropertyChanged;
                }
            }

            ScheduleRebuild();
        }

        private void TerminalStatuses_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) =>
            ScheduleRebuild();

        private void Rule_PropertyChanged(object sender, PropertyChangedEventArgs e) => ScheduleRebuild();

        private void Buffer_PropertyChanged(object sender, PropertyChangedEventArgs e) => ScheduleRebuild();

        private void Db_GamesUpdated(object sender, ItemUpdatedEventArgs<Game> e) => ScheduleRebuild();

        private void Db_GamesCollectionChanged(object sender, ItemCollectionChangedEventArgs<Game> e) => ScheduleRebuild();

        private void Db_GenericUpdated<T>(object sender, ItemUpdatedEventArgs<T> e) where T : DatabaseObject =>
            ScheduleRebuild();

        public void Refresh()
        {
            Rebuild();
        }

        private void ScheduleRebuild()
        {
            if (disposed)
            {
                return;
            }

            lock (queueLock)
            {
                if (rebuildScheduled)
                {
                    return;
                }

                rebuildScheduled = true;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                lock (queueLock)
                {
                    rebuildScheduled = false;
                }

                Rebuild();
            }), DispatcherPriority.Background);
        }

        private void Rebuild()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                if (database?.IsOpen != true || boundSettings == null)
                {
                    ApplyOrdered(new List<Game>());
                    return;
                }

                var sourceGames = database is GameDatabase gameDatabase
                    ? gameDatabase.MergeGroups.PrepareForQueue(database.Games, boundSettings)
                    : database.Games.ToList();
                var ordered = QueueOrdering.BuildQueue(sourceGames, boundSettings);

                ApplyOrdered(ordered);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to rebuild queue.");
            }
        }

        private void ApplyOrdered(IList<Game> ordered)
        {
            // Build the new entry list, reusing cached entries for stable identity
            // (avoids tearing down image bindings every rebuild).
            var newEntries = new List<GamesCollectionViewEntry>(ordered.Count);
            var keepIds = new HashSet<Guid>();
            foreach (var game in ordered)
            {
                if (!entryCache.TryGetValue(game.Id, out var entry))
                {
                    IList<Game> members = null;
                    if (database is GameDatabase db && game.MergeGroupId != null)
                    {
                        members = db.MergeGroups.GetMembers(game.MergeGroupId.Value);
                    }

                    entry = new GamesCollectionViewEntry(game, GetLibraryPlugin(game), appSettings, members);
                    entryCache[game.Id] = entry;
                }

                newEntries.Add(entry);
                keepIds.Add(game.Id);
            }

            // Drop cached entries that fell off the queue so their Game.PropertyChanged
            // hooks are released; otherwise the cache grows unbounded over the session.
            var toEvict = entryCache.Where(kv => !keepIds.Contains(kv.Key)).ToList();
            foreach (var kv in toEvict)
            {
                entryCache.Remove(kv.Key);
            }

            DisposeCachedEntries(toEvict.Select(kv => kv.Value));

            lock (queueLock)
            {
                QueueGames.Clear();
                foreach (var entry in newEntries)
                {
                    QueueGames.Add(entry);
                }
            }
        }

        private LibraryPlugin GetLibraryPlugin(Game game)
        {
            if (game?.PluginId != null && game.PluginId != Guid.Empty
                && extensions?.Plugins != null
                && extensions.Plugins.TryGetValue(game.PluginId, out var plugin)
                && plugin.Plugin is LibraryPlugin libPlugin)
            {
                return libPlugin;
            }

            return null;
        }

        private static void DisposeCachedEntries(IEnumerable<GamesCollectionViewEntry> entries)
        {
            foreach (var entry in entries)
            {
                try
                {
                    entry?.Dispose();
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, "Failed to dispose queue entry.");
                }
            }
        }
    }
}
