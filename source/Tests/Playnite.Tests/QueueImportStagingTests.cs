using NUnit.Framework;
using Playnite;
using Playnite.Common;
using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.IO;
using System.Linq;

namespace Playnite.Tests
{
    [TestFixture]
    public class QueueImportStagingTests
    {
        private static void WithDb(Action<GameDatabase, string> body)
        {
            using (var temp = TempDirectory.Create())
            using (var db = new GameDatabase(Path.Combine(temp.TempPath, "library")))
            {
                db.OpenDatabase();
                body(db, Path.Combine(temp.TempPath, QueueImportStaging.StagingFileName));
            }
        }

        private static QueueImportStagingResult Stage(GameDatabase db, string stagingPath, string csv)
        {
            using (var reader = new StringReader(csv))
            {
                return QueueImportStaging.StageFromReader(
                    reader,
                    "test.csv",
                    db,
                    new LibrarySanitizerSettings(),
                    stagingPath);
            }
        }

        [Test]
        public void Stage_DoesNotMutateGamesUntilCommit()
        {
            WithDb((db, path) =>
            {
                var game = new Game("Hades");
                db.Games.Add(game);

                var staged = Stage(db, path, "Name,Free\r\nHades,true\r\n");

                Assert.AreEqual(1, staged.RowsStaged);
                Assert.AreEqual(1, staged.CleanRows);
                Assert.IsFalse(db.Games[game.Id].Free, "Staging should not apply values to the live game.");

                var commit = QueueImportStaging.Commit(QueueImportStaging.Load(path), db, path);

                Assert.AreEqual(1, commit.Updated);
                Assert.IsTrue(db.Games[game.Id].Free);
                Assert.IsFalse(File.Exists(path), "Empty staging file should be deleted after commit.");
            });
        }

        [Test]
        public void Stage_BlocksWhenPendingRowsExist()
        {
            WithDb((db, path) =>
            {
                db.Games.Add(new Game("Hades"));
                Stage(db, path, "Name,Free\r\nHades,true\r\n");

                Assert.Throws<InvalidOperationException>(() =>
                    Stage(db, path, "Name,Free\r\nHades,false\r\n"));
            });
        }

        [Test]
        public void Stage_SanitizableNameNeedsReviewButCanBeSanitizedAndCommitted()
        {
            WithDb((db, path) =>
            {
                var game = new Game("Final Fantasy VII");
                db.Games.Add(game);

                var staged = Stage(db, path, "Name,Free\r\nFinal Fantasy 7,true\r\n");

                Assert.AreEqual(1, staged.NeedsReview);
                var file = QueueImportStaging.Load(path);
                var row = file.Rows.Single();
                Assert.AreEqual(0, row.MatchedGameIds.Count);

                row.Name = "Final Fantasy VII";
                row.Fields[file.HeaderMap["name"]] = row.Name;
                QueueImportStaging.RefreshMatches(row, db);
                QueueImportStaging.Save(file, path);

                var commit = QueueImportStaging.Commit(QueueImportStaging.Load(path), db, path);

                Assert.AreEqual(1, commit.Updated);
                Assert.IsTrue(db.Games[game.Id].Free);
            });
        }

        [Test]
        public void Commit_DeletedMatchedGameStaysStagedAsNoMatch()
        {
            WithDb((db, path) =>
            {
                var game = new Game("Hades");
                db.Games.Add(game);
                Stage(db, path, "Name,Free\r\nHades,true\r\n");
                db.Games.Remove(game);

                var commit = QueueImportStaging.Commit(QueueImportStaging.Load(path), db, path);

                Assert.AreEqual(1, commit.NotFound);
                var remaining = QueueImportStaging.Load(path).Rows.Single();
                Assert.AreEqual("Hades", remaining.Name);
                Assert.AreEqual(0, remaining.MatchedGameIds.Count);
            });
        }

        [Test]
        public void CreateGameForRow_UnmatchedRowCommitsToNewGame()
        {
            WithDb((db, path) =>
            {
                Stage(db, path, "Name,Free\r\nNew Title,true\r\n");
                var file = QueueImportStaging.Load(path);
                var row = file.Rows.Single();
                Assert.AreEqual(0, row.MatchedGameIds.Count);

                var created = QueueImportStaging.CreateGameForRow(row, db);
                QueueImportStaging.Save(file, path);

                Assert.AreEqual(1, row.MatchedGameIds.Count);
                Assert.AreEqual(created.Id, row.MatchedGameIds[0]);
                Assert.AreEqual("New Title", db.Games[created.Id].Name);

                var commit = QueueImportStaging.Commit(QueueImportStaging.Load(path), db, path);

                Assert.AreEqual(1, commit.Updated);
                Assert.IsTrue(db.Games[created.Id].Free);
            });
        }

        [Test]
        public void ManualMatch_SelectedDuplicateNameAppliesToAllMatches()
        {
            WithDb((db, path) =>
            {
                var a = new Game("Same Name");
                var b = new Game("Same Name");
                db.Games.Add(a);
                db.Games.Add(b);
                Stage(db, path, "Name,Free\r\nTypo Name,true\r\n");
                var file = QueueImportStaging.Load(path);
                var row = file.Rows.Single();

                QueueImportStaging.SetManualMatch(row, a, db);
                QueueImportStaging.Save(file, path);
                var commit = QueueImportStaging.Commit(QueueImportStaging.Load(path), db, path);

                Assert.AreEqual(2, commit.Matched);
                Assert.IsTrue(db.Games[a.Id].Free);
                Assert.IsTrue(db.Games[b.Id].Free);
            });
        }
    }
}
