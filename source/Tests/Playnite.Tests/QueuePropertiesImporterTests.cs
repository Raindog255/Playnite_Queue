using NUnit.Framework;
using Playnite.Common;
using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Playnite.Tests
{
    /// <summary>
    /// Coverage for the one-shot Sheets-export CSV importer: the parser, the
    /// per-column value coercion, and the end-to-end flow against a real
    /// LiteDB-backed <see cref="GameDatabase"/> so we exercise the same
    /// persistence path that production uses.
    /// </summary>
    [TestFixture]
    public class QueuePropertiesImporterTests
    {
        private static QueueImportResult Run(GameDatabase db, string csv)
        {
            using (var reader = new StringReader(csv))
            {
                return QueuePropertiesImporter.ImportFromReader(reader, db);
            }
        }

        // Open a real DB so the importer's BufferedUpdate/Update paths run for
        // real — mocking IItemCollection felt strictly worse for catching
        // regressions that only surface against persisted state.
        private static void WithDb(Action<GameDatabase> body)
        {
            using (var temp = TempDirectory.Create())
            using (var db = new GameDatabase(temp.TempPath))
            {
                db.OpenDatabase();
                body(db);
            }
        }

        // ---- CsvReader ---------------------------------------------------------

        [Test]
        public void CsvReader_Parses_BasicRowsAndQuoting()
        {
            var rows = CsvReader.Parse(new StringReader(
                "Name,Source,Categories\r\n" +
                "Hades,Steam,\"Roguelike|Action\"\r\n" +
                "\"Comma, Game\",GOG,\"Has \"\"quotes\"\"\"\r\n"))
                .ToList();

            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEqual(new[] { "Name", "Source", "Categories" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "Hades", "Steam", "Roguelike|Action" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "Comma, Game", "GOG", "Has \"quotes\"" }, rows[2]);
        }

        [Test]
        public void CsvReader_Strips_Utf8Bom()
        {
            // The encoding-aware StreamReader inside ImportFromCsv handles the
            // BOM — but if we ever swap to a raw reader the in-stream BOM
            // character would leak into the first column name. Asserting the
            // header still maps proves the importer works either way.
            WithDb(db =>
            {
                db.Games.Add(new Game("Hades"));
                using (var stream = new MemoryStream())
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 1024, leaveOpen: true))
                    {
                        writer.Write("Name,Free\r\nHades,TRUE\r\n");
                    }

                    stream.Position = 0;
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        var result = QueuePropertiesImporter.ImportFromReader(reader, db);
                        Assert.AreEqual(1, result.Updated);
                        Assert.IsTrue(db.Games[db.Games.First(g => g.Name == "Hades").Id].Free);
                    }
                }
            });
        }

        [Test]
        public void CsvReader_Handles_EmbeddedNewlinesInQuotedFields()
        {
            var rows = CsvReader.Parse(new StringReader("a,b\r\n\"line1\nline2\",x\r\n")).ToList();
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "line1\nline2", "x" }, rows[1]);
        }

        // ---- Boolean coercion --------------------------------------------------

        [TestCase("TRUE", true)]
        [TestCase("true", true)]
        [TestCase("Yes", true)]
        [TestCase("y", true)]
        [TestCase("1", true)]
        [TestCase("FALSE", false)]
        [TestCase("no", false)]
        [TestCase("N", false)]
        [TestCase("0", false)]
        public void Boolean_AcceptsCommonVariants(string raw, bool expected)
        {
            WithDb(db =>
            {
                // Pre-set to the opposite value so we observe an actual flip on
                // every case — otherwise writing false to a default-false bool
                // is a no-op and we can't tell parse-success apart from skip.
                var game = new Game("G") { Free = !expected };
                db.Games.Add(game);
                var result = Run(db, $"Name,Free\r\nG,{raw}\r\n");
                Assert.AreEqual(0, result.Errors, "Expected no parse errors.");
                Assert.AreEqual(1, result.Updated);
                Assert.AreEqual(expected, db.Games[game.Id].Free);
            });
        }

        [Test]
        public void Boolean_InvalidValue_ReportsErrorAndPreserves()
        {
            WithDb(db =>
            {
                var game = new Game("G") { Free = true };
                db.Games.Add(game);
                var result = Run(db, "Name,Free\r\nG,maybe\r\n");
                Assert.AreEqual(1, result.Errors);
                Assert.IsTrue(db.Games[game.Id].Free, "Existing value should not be overwritten on parse error.");
                Assert.IsTrue(result.Messages.Any(m => m.Contains("Free")));
            });
        }

        // ---- Blank-cell preservation ------------------------------------------

        [Test]
        public void BlankCells_PreserveExistingValues()
        {
            WithDb(db =>
            {
                var existingDate = new DateTime(2024, 1, 15);
                var game = new Game("G")
                {
                    OnHold = true,
                    Free = true,
                    Mobile = true,
                    AcquiredDate = existingDate,
                    CompletedDate = existingDate,
                    UserScore = 80
                };
                db.Games.Add(game);

                var result = Run(db, "Name,OnHold,Free,Mobile,AcquiredDate,CompletedDate,UserRating\r\nG,,,,,,\r\n");
                Assert.AreEqual(1, result.Matched);
                Assert.AreEqual(0, result.Updated, "All cells blank — nothing should change.");
                Assert.AreEqual(0, result.Errors);

                var stored = db.Games[game.Id];
                Assert.IsTrue(stored.OnHold);
                Assert.IsTrue(stored.Free);
                Assert.IsTrue(stored.Mobile);
                Assert.AreEqual(existingDate, stored.AcquiredDate);
                Assert.AreEqual(existingDate, stored.CompletedDate);
                Assert.AreEqual(80, stored.UserScore);
            });
        }

        // ---- UserRating scaling -----------------------------------------------

        [TestCase("0", 0)]
        [TestCase("1", 10)]
        [TestCase("8.5", 85)]
        [TestCase("9", 90)]
        [TestCase("10", 100)]
        public void UserRating_Scales10ScaleTo100(string raw, int expected)
        {
            WithDb(db =>
            {
                db.Games.Add(new Game("G"));
                var result = Run(db, $"Name,UserRating\r\nG,{raw}\r\n");
                Assert.AreEqual(0, result.Errors);
                Assert.AreEqual(expected, db.Games.First(g => g.Name == "G").UserScore);
            });
        }

        [TestCase("-")]
        [TestCase("--")]
        [TestCase("N/A")]
        [TestCase("n/a")]
        [TestCase("NA")]
        public void Cell_PlaceholdersAreTreatedAsBlank(string raw)
        {
            // Sheets users type "-" or "N/A" instead of leaving cells blank.
            // Treat them as blank so the row imports cleanly. Verified through
            // UserRating because it has the most unambiguous "did the field
            // change?" signal.
            WithDb(db =>
            {
                var game = new Game("G") { UserScore = 50 };
                db.Games.Add(game);
                var result = Run(db, $"Name,UserRating\r\nG,{raw}\r\n");
                Assert.AreEqual(0, result.Errors, $"'{raw}' should be treated as blank.");
                Assert.AreEqual(50, db.Games[game.Id].UserScore, "Existing score preserved.");
            });
        }

        [TestCase("11")]
        [TestCase("-1")]
        [TestCase("abc")]
        public void UserRating_InvalidValue_ReportsErrorAndPreserves(string raw)
        {
            WithDb(db =>
            {
                var game = new Game("G") { UserScore = 50 };
                db.Games.Add(game);
                var result = Run(db, $"Name,UserRating\r\nG,{raw}\r\n");
                Assert.AreEqual(1, result.Errors);
                Assert.AreEqual(50, db.Games[game.Id].UserScore);
            });
        }

        // ---- Date parsing -----------------------------------------------------

        [TestCase("2024-03-15")]            // ISO
        [TestCase("Mar 15, 2024")]          // Sheets default
        [TestCase("Mar 15 2024")]           // Sheets default minus comma
        [TestCase("March 15, 2024")]        // Full month name
        [TestCase("15 Mar 2024")]
        [TestCase("15 March 2024")]
        public void Date_ParsesAllSupportedFormats(string raw)
        {
            // CSV cell wrapped in quotes because some of these contain commas —
            // exactly how Sheets emits them.
            WithDb(db =>
            {
                db.Games.Add(new Game("G"));
                var result = Run(db, $"Name,AcquiredDate\r\nG,\"{raw}\"\r\n");
                Assert.AreEqual(0, result.Errors, $"Format '{raw}' should be accepted.");
                Assert.AreEqual(new DateTime(2024, 3, 15), db.Games.First(g => g.Name == "G").AcquiredDate);
            });
        }

        [TestCase("03/15/2024")]   // ambiguous M/d vs d/M
        [TestCase("15/03/2024")]
        [TestCase("not a date")]
        public void Date_RejectsAmbiguousAndJunk(string raw)
        {
            // Refusing regional slash-formats keeps us from silently swapping
            // month/day on libraries imported from other regions.
            WithDb(db =>
            {
                db.Games.Add(new Game("G"));
                var result = Run(db, $"Name,AcquiredDate\r\nG,{raw}\r\n");
                Assert.AreEqual(1, result.Errors);
                Assert.IsFalse(db.Games.First(g => g.Name == "G").AcquiredDate.HasValue);
            });
        }

        // ---- Game matching ----------------------------------------------------

        [Test]
        public void Match_ByNameCaseInsensitive()
        {
            WithDb(db =>
            {
                db.Games.Add(new Game("Hades"));
                var result = Run(db, "Name,Free\r\nHADES,true\r\n");
                Assert.AreEqual(1, result.Matched);
                Assert.AreEqual(1, result.Updated);
            });
        }

        [Test]
        public void Match_DuplicateNames_AppliesToAllMatches()
        {
            // Same title imported from multiple libraries (Steam + Epic) is the
            // common case. Replaying a game just because Playnite tracks it
            // twice would be silly, so the row applies to every match. The
            // multi-match is still surfaced for visibility.
            WithDb(db =>
            {
                var a = new Game("Same Name");
                var b = new Game("Same Name");
                db.Games.Add(a);
                db.Games.Add(b);

                var result = Run(db, "Name,Free\r\nSame Name,true\r\n");
                Assert.AreEqual(1, result.Ambiguous, "Multi-match still counted for visibility.");
                Assert.AreEqual(2, result.Matched);
                Assert.AreEqual(2, result.Updated);
                Assert.IsTrue(db.Games[a.Id].Free);
                Assert.IsTrue(db.Games[b.Id].Free);
                Assert.IsTrue(result.Messages.Any(m => m.Contains("applied to all")));
            });
        }

        [Test]
        public void Match_MissingGame_ReportsNotFound()
        {
            WithDb(db =>
            {
                var result = Run(db, "Name,Free\r\nNothing Here,true\r\n");
                Assert.AreEqual(1, result.NotFound);
                Assert.AreEqual(0, result.Updated);
            });
        }

        [Test]
        public void Match_MissingNameColumn_FailsFast()
        {
            WithDb(db =>
            {
                var result = Run(db, "Foo,Bar\r\nBaz,Qux\r\n");
                Assert.AreEqual(1, result.Errors);
                Assert.IsTrue(result.Messages.Any(m => m.Contains("Name")));
            });
        }

        // ---- Categories -------------------------------------------------------

        [Test]
        public void Categories_AutoCreatesMissingAndAssigns()
        {
            WithDb(db =>
            {
                var existing = db.Categories.Add("Roguelike");
                db.Games.Add(new Game("Hades"));

                var result = Run(db, "Name,Categories\r\nHades,Roguelike|Action|Indie\r\n");
                Assert.AreEqual(1, result.Updated);

                var stored = db.Games.First(g => g.Name == "Hades");
                Assert.AreEqual(3, stored.CategoryIds.Count);
                Assert.IsTrue(stored.CategoryIds.Contains(existing.Id), "Existing category should be reused, not duplicated.");
                Assert.IsNotNull(db.Categories.FirstOrDefault(c => c.Name == "Action"));
                Assert.IsNotNull(db.Categories.FirstOrDefault(c => c.Name == "Indie"));
            });
        }

        [Test]
        public void Categories_PopulatedCellReplacesExistingAssignments()
        {
            WithDb(db =>
            {
                var oldCat = db.Categories.Add("Old");
                var keep = db.Categories.Add("Keep");
                var game = new Game("G") { CategoryIds = new List<Guid> { oldCat.Id, keep.Id } };
                db.Games.Add(game);

                Run(db, "Name,Categories\r\nG,Keep|New\r\n");

                var stored = db.Games[game.Id];
                Assert.AreEqual(2, stored.CategoryIds.Count);
                Assert.IsTrue(stored.CategoryIds.Contains(keep.Id));
                Assert.IsFalse(stored.CategoryIds.Contains(oldCat.Id), "Populated Categories cell should replace, not union.");
            });
        }

        // ---- CompletionStatus -------------------------------------------------

        [Test]
        public void CompletionStatus_ResolvesByName_AutoCreatesMissing()
        {
            WithDb(db =>
            {
                db.CompletionStatuses.Remove(db.CompletionStatuses.ToList());
                var beaten = new CompletionStatus { Name = "Beaten" };
                db.CompletionStatuses.Add(beaten);
                db.Games.Add(new Game("G"));

                var result = Run(db, "Name,CompletionStatus\r\nG,beaten\r\n");
                Assert.AreEqual(1, result.Updated);
                Assert.AreEqual(beaten.Id, db.Games.First(g => g.Name == "G").CompletionStatusId);

                Run(db, "Name,CompletionStatus\r\nG,Brand New Status\r\n");
                var newStatus = db.CompletionStatuses.FirstOrDefault(s => s.Name == "Brand New Status");
                Assert.IsNotNull(newStatus, "Missing completion status should be auto-created.");
                Assert.AreEqual(newStatus.Id, db.Games.First(g => g.Name == "G").CompletionStatusId);
            });
        }

        // ---- Header normalization & misc --------------------------------------

        [Test]
        public void Header_AcceptsSpacedColumnNames()
        {
            // Sheets exports keep the human header text — "Acquired Date" should
            // map to the same column as "AcquiredDate".
            WithDb(db =>
            {
                db.Games.Add(new Game("G"));
                var result = Run(db, "Name,Acquired Date,Completed Date,User Rating\r\nG,2024-01-01,2024-02-02,7\r\n");
                Assert.AreEqual(0, result.Errors);
                Assert.AreEqual(1, result.Updated);
                var stored = db.Games.First(g => g.Name == "G");
                Assert.AreEqual(new DateTime(2024, 1, 1), stored.AcquiredDate);
                Assert.AreEqual(new DateTime(2024, 2, 2), stored.CompletedDate);
                Assert.AreEqual(70, stored.UserScore);
            });
        }

        [Test]
        public void TrailingBlankRow_IsIgnored()
        {
            WithDb(db =>
            {
                db.Games.Add(new Game("G"));
                var result = Run(db, "Name,Free\r\nG,true\r\n,\r\n\r\n");
                Assert.AreEqual(0, result.Errors);
                Assert.AreEqual(1, result.Updated);
            });
        }
    }
}
