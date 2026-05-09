using Playnite.Database;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace Playnite
{
    /// <summary>
    /// When a game's CompletionStatus transitions to one of the user's configured
    /// terminal statuses (e.g. Beaten, Completed, Played, Abandoned) and its
    /// <see cref="Game.CompletedDate"/> is still blank, automatically stamp it with
    /// the current date. Idempotent — never overwrites an existing CompletedDate
    /// and only acts on actual status transitions, so editing other fields on a
    /// game that's already terminal does not refill the date.
    /// </summary>
    /// <remarks>
    /// Hooks <see cref="IGameDatabaseMain.Games"/>.ItemUpdated, which fires for
    /// every code path that mutates a game (edit window, right-click status menu,
    /// bulk operations, plugin/script API, the future CSV importer). Bulk
    /// operations wrapped in BufferedUpdate produce a single batched event, which
    /// this handler processes in one pass and re-emits as a single batched update.
    /// </remarks>
    public class QueueCompletedDateAutoFiller : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IGameDatabaseMain database;
        private readonly QueueSettings settings;
        private bool disposed;

        public QueueCompletedDateAutoFiller(IGameDatabaseMain database, QueueSettings settings)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (database.IsOpen)
            {
                database.Games.ItemUpdated += OnGamesUpdated;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (database?.Games != null)
            {
                database.Games.ItemUpdated -= OnGamesUpdated;
            }
        }

        private void OnGamesUpdated(object sender, ItemUpdatedEventArgs<Game> e)
        {
            if (disposed || e?.UpdatedItems == null)
            {
                return;
            }

            var terminalIds = settings?.TerminalStatusIds;
            if (terminalIds == null || terminalIds.Count == 0)
            {
                return;
            }

            List<Game> toUpdate = null;
            try
            {
                foreach (var update in e.UpdatedItems)
                {
                    var oldData = update.OldData;
                    var newData = update.NewData;
                    if (oldData == null || newData == null)
                    {
                        continue;
                    }

                    // Only act on actual transitions. Editing unrelated fields on a
                    // game that already has a terminal status should not retroactively
                    // stamp a CompletedDate.
                    if (oldData.CompletionStatusId == newData.CompletionStatusId)
                    {
                        continue;
                    }

                    if (!terminalIds.Contains(newData.CompletionStatusId))
                    {
                        continue;
                    }

                    // OldData/NewData are snapshots; mutate the live DB instance.
                    var live = database.Games.Get(newData.Id);
                    if (live == null || live.CompletedDate.HasValue)
                    {
                        continue;
                    }

                    live.CompletedDate = DateTime.Today;
                    if (toUpdate == null)
                    {
                        toUpdate = new List<Game>();
                    }

                    toUpdate.Add(live);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to scan games for queue auto-completed-date fill.");
                return;
            }

            if (toUpdate == null)
            {
                return;
            }

            try
            {
                using (database.BufferedUpdate())
                {
                    foreach (var game in toUpdate)
                    {
                        database.Games.Update(game);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to persist auto-filled CompletedDate for queue.");
            }
        }
    }
}
