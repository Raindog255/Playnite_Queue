using NUnit.Framework;
using Playnite;
using Playnite.Common;
using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.Tests
{
    [TestFixture]
    public class QueueCompletedDateAutoFillerTests
    {
        private static QueueSettings SettingsWithTerminals(params Guid[] terminalIds)
        {
            var s = new QueueSettings();
            foreach (var id in terminalIds)
            {
                s.TerminalStatusIds.Add(id);
            }

            return s;
        }

        // Convenience: open a real GameDatabase, plant a Playing/Beaten/Completed
        // status, run the test body, dispose everything.
        private static void WithDb(Action<GameDatabase, CompletionStatus, CompletionStatus, CompletionStatus> body)
        {
            using (var temp = TempDirectory.Create())
            using (var db = new GameDatabase(temp.TempPath))
            {
                db.OpenDatabase();

                // Strip the auto-seeded statuses so we control the fixture.
                db.CompletionStatuses.Remove(db.CompletionStatuses.ToList());

                var playing = new CompletionStatus { Name = "Playing" };
                var beaten = new CompletionStatus { Name = "Beaten" };
                var completed = new CompletionStatus { Name = "Completed" };
                db.CompletionStatuses.Add(playing);
                db.CompletionStatuses.Add(beaten);
                db.CompletionStatuses.Add(completed);

                body(db, playing, beaten, completed);
            }
        }

        [Test]
        public void Transition_NonTerminalToTerminal_FillsBlankCompletedDate()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var game = new Game("Test") { CompletionStatusId = playing.Id };
                    db.Games.Add(game);

                    game.CompletionStatusId = beaten.Id;
                    db.Games.Update(game);

                    var stored = db.Games[game.Id];
                    Assert.IsTrue(stored.CompletedDate.HasValue, "Expected CompletedDate to be auto-filled.");
                    Assert.AreEqual(DateTime.Today, stored.CompletedDate.Value);
                }
            });
        }

        [Test]
        public void Transition_NonTerminalToTerminal_PreservesExistingCompletedDate()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                var existingDate = new DateTime(2024, 6, 15);
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var game = new Game("Test")
                    {
                        CompletionStatusId = playing.Id,
                        CompletedDate = existingDate
                    };
                    db.Games.Add(game);

                    game.CompletionStatusId = beaten.Id;
                    db.Games.Update(game);

                    var stored = db.Games[game.Id];
                    Assert.AreEqual(existingDate, stored.CompletedDate);
                }
            });
        }

        [Test]
        public void Transition_BetweenTwoTerminalStatuses_FillsBlankCompletedDate()
        {
            // User originally marked the game terminal before configuring TerminalStatusIds
            // (so CompletedDate stayed blank), then later switches between two terminals.
            // We want the date to land on the second transition.
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var game = new Game("Test") { CompletionStatusId = beaten.Id };
                    db.Games.Add(game);

                    Assert.IsFalse(db.Games[game.Id].CompletedDate.HasValue,
                        "Initial Add should not have triggered the filler (no transition).");

                    game.CompletionStatusId = completed.Id;
                    db.Games.Update(game);

                    var stored = db.Games[game.Id];
                    Assert.IsTrue(stored.CompletedDate.HasValue);
                    Assert.AreEqual(DateTime.Today, stored.CompletedDate.Value);
                }
            });
        }

        [Test]
        public void NoTransition_EditingOtherFields_DoesNotRefillCompletedDate()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var game = new Game("Test") { CompletionStatusId = beaten.Id };
                    db.Games.Add(game);

                    // CompletedDate is null and the game is in a terminal status, but
                    // there's no status transition — the user is just editing notes.
                    game.Notes = "tweak";
                    db.Games.Update(game);

                    Assert.IsFalse(db.Games[game.Id].CompletedDate.HasValue);
                }
            });
        }

        [Test]
        public void Transition_ToNonTerminal_DoesNotFillCompletedDate()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var game = new Game("Test") { CompletionStatusId = beaten.Id };
                    db.Games.Add(game);

                    game.CompletionStatusId = playing.Id;
                    db.Games.Update(game);

                    Assert.IsFalse(db.Games[game.Id].CompletedDate.HasValue);
                }
            });
        }

        [Test]
        public void NoTerminalsConfigured_NeverFills()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, new QueueSettings()))
                {
                    var game = new Game("Test") { CompletionStatusId = playing.Id };
                    db.Games.Add(game);

                    game.CompletionStatusId = beaten.Id;
                    db.Games.Update(game);

                    Assert.IsFalse(db.Games[game.Id].CompletedDate.HasValue);
                }
            });
        }

        [Test]
        public void BulkUpdate_FillsAllQualifyingGamesInOnePass()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                using (var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id)))
                {
                    var games = new List<Game>();
                    for (var i = 0; i < 5; i++)
                    {
                        var g = new Game("G" + i) { CompletionStatusId = playing.Id };
                        db.Games.Add(g);
                        games.Add(g);
                    }

                    using (db.BufferedUpdate())
                    {
                        foreach (var g in games)
                        {
                            g.CompletionStatusId = beaten.Id;
                            db.Games.Update(g);
                        }
                    }

                    foreach (var g in games)
                    {
                        var stored = db.Games[g.Id];
                        Assert.IsTrue(stored.CompletedDate.HasValue, $"{stored.Name} should be filled");
                        Assert.AreEqual(DateTime.Today, stored.CompletedDate.Value);
                    }
                }
            });
        }

        [Test]
        public void Disposed_StopsListening()
        {
            WithDb((db, playing, beaten, completed) =>
            {
                var filler = new QueueCompletedDateAutoFiller(db, SettingsWithTerminals(beaten.Id, completed.Id));
                filler.Dispose();

                var game = new Game("Test") { CompletionStatusId = playing.Id };
                db.Games.Add(game);

                game.CompletionStatusId = beaten.Id;
                db.Games.Update(game);

                Assert.IsFalse(db.Games[game.Id].CompletedDate.HasValue);
            });
        }
    }
}
