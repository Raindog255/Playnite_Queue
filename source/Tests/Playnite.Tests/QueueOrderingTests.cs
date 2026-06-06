using NUnit.Framework;
using Playnite;
using Playnite.MergeGroups;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.Tests
{
    [TestFixture]
    public class QueueOrderingTests
    {
        private static Guid PlayingId = Guid.NewGuid();
        private static Guid CompletedId = Guid.NewGuid();
        private static Guid AbandonedId = Guid.NewGuid();

        // ---- Fixture helpers ------------------------------------------------

        private static Game NewGame(
            string name,
            DateTime? acquired = null,
            DateTime? released = null,
            DateTime? completed = null,
            Guid? statusId = null,
            bool onHold = false,
            bool hidden = false,
            bool free = false,
            bool mobile = false,
            Guid pluginId = default(Guid),
            IEnumerable<Guid> developers = null,
            IEnumerable<Guid> publishers = null,
            IEnumerable<Guid> series = null,
            IEnumerable<Guid> genres = null,
            IEnumerable<Guid> categories = null,
            IEnumerable<Guid> tags = null)
        {
            var g = new Game
            {
                Name = name,
                Id = Guid.NewGuid(),
                AcquiredDate = acquired,
                CompletedDate = completed,
                CompletionStatusId = statusId ?? Guid.Empty,
                OnHold = onHold,
                Hidden = hidden,
                Free = free,
                Mobile = mobile,
                PluginId = pluginId,
                DeveloperIds = developers?.ToList() ?? new List<Guid>(),
                PublisherIds = publishers?.ToList() ?? new List<Guid>(),
                SeriesIds = series?.ToList() ?? new List<Guid>(),
                GenreIds = genres?.ToList() ?? new List<Guid>(),
                CategoryIds = categories?.ToList() ?? new List<Guid>(),
                TagIds = tags?.ToList() ?? new List<Guid>()
            };

            if (released.HasValue)
            {
                g.ReleaseDate = new ReleaseDate(released.Value);
            }

            return g;
        }

        private static QueueSettings DefaultSettings(int n = 10)
        {
            var s = new QueueSettings
            {
                VisibleCount = n,
                DateSource = QueueDateSource.AcquiredDate,
                PlayingStatusId = PlayingId
            };
            s.TerminalStatusIds.Add(CompletedId);
            s.TerminalStatusIds.Add(AbandonedId);
            return s;
        }

        // ---- 1. Eligibility filter -----------------------------------------

        [Test]
        public void Eligibility_ExcludesHiddenAndOnHoldAndTerminalStatuses()
        {
            var visible = NewGame("Visible", acquired: new DateTime(2024, 1, 1));
            var hidden = NewGame("Hidden", acquired: new DateTime(2024, 1, 2), hidden: true);
            var onHold = NewGame("OnHold", acquired: new DateTime(2024, 1, 3), onHold: true);
            var beaten = NewGame("Beaten", acquired: new DateTime(2024, 1, 4), statusId: CompletedId);

            var queue = QueueOrdering.BuildQueue(new[] { visible, hidden, onHold, beaten }, DefaultSettings());

            CollectionAssert.AreEqual(new[] { "Visible" }, queue.Select(q => q.Name).ToArray());
        }

        // ---- 2. Date sort ---------------------------------------------------

        [Test]
        public void Sort_AscendingByAcquiredDate_NullsLast()
        {
            var noDate = NewGame("NoDate");
            var newer = NewGame("Newer", acquired: new DateTime(2025, 6, 1));
            var older = NewGame("Older", acquired: new DateTime(2024, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { noDate, newer, older }, DefaultSettings());

            CollectionAssert.AreEqual(new[] { "Older", "Newer", "NoDate" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Sort_AcquiredDateIgnoresTimeComponent()
        {
            // Two games "acquired the same day" should not be reshuffled by import-time
            // hh:mm:ss noise from whatever library plugin happened to import first.
            var sameDayLate = NewGame("Late", acquired: new DateTime(2024, 1, 15, 23, 59, 59));
            var sameDayEarly = NewGame("Early", acquired: new DateTime(2024, 1, 15, 0, 0, 1));
            var nextDay = NewGame("NextDay", acquired: new DateTime(2024, 1, 16, 8, 0, 0));

            // Tiebreaker is older release date, then enumeration order. Give Early
            // an older release so we can assert the primary tie is broken predictably.
            sameDayLate.ReleaseDate = new ReleaseDate(new DateTime(2020, 1, 1));
            sameDayEarly.ReleaseDate = new ReleaseDate(new DateTime(2010, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { sameDayLate, sameDayEarly, nextDay }, DefaultSettings());

            CollectionAssert.AreEqual(new[] { "Early", "Late", "NextDay" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Sort_SecondaryDateBreaksTiesWithinSamePrimaryDate()
        {
            // Both acquired the same day; older release should bubble up.
            var newerRelease = NewGame(
                "NewerRelease",
                acquired: new DateTime(2024, 1, 15),
                released: new DateTime(2022, 6, 1));
            var olderRelease = NewGame(
                "OlderRelease",
                acquired: new DateTime(2024, 1, 15),
                released: new DateTime(2008, 3, 10));
            var noRelease = NewGame("NoRelease", acquired: new DateTime(2024, 1, 15));

            var queue = QueueOrdering.BuildQueue(new[] { newerRelease, noRelease, olderRelease }, DefaultSettings());

            // OlderRelease wins the tiebreaker, NewerRelease next, NoRelease last
            // (null secondary key sorts last).
            CollectionAssert.AreEqual(
                new[] { "OlderRelease", "NewerRelease", "NoRelease" },
                queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Sort_SecondaryDateKey_UsesAcquiredDateWhenPrimaryIsRelease()
        {
            // Mirror of the above test with the primary/secondary swapped: when
            // sorting primarily by ReleaseDate, two games released the same year
            // break the tie by older AcquiredDate (the older purchase clears first).
            var settings = DefaultSettings();
            settings.DateSource = QueueDateSource.ReleaseDate;

            var newerAcq = NewGame(
                "NewerAcquired",
                acquired: new DateTime(2024, 1, 15),
                released: new DateTime(2010, 5, 1));
            var olderAcq = NewGame(
                "OlderAcquired",
                acquired: new DateTime(2018, 6, 1),
                released: new DateTime(2010, 5, 1));

            var queue = QueueOrdering.BuildQueue(new[] { newerAcq, olderAcq }, settings);

            CollectionAssert.AreEqual(
                new[] { "OlderAcquired", "NewerAcquired" },
                queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Sort_AscendingByReleaseDate_EmptyReleaseDateSortsLast()
        {
            var settings = DefaultSettings();
            settings.DateSource = QueueDateSource.ReleaseDate;

            var noRelease = NewGame("NoRelease", acquired: new DateTime(2020, 1, 1));
            var newRelease = NewGame("New", acquired: new DateTime(2020, 1, 1), released: new DateTime(2024, 1, 1));
            var oldRelease = NewGame("Old", acquired: new DateTime(2020, 1, 1), released: new DateTime(2010, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { noRelease, newRelease, oldRelease }, settings);

            CollectionAssert.AreEqual(new[] { "Old", "New", "NoRelease" }, queue.Select(q => q.Name).ToArray());
        }

        // ---- 3. Priority rules ----------------------------------------------

        [Test]
        public void PriorityRule_PromotesMatchingGames_AboveDateSort()
        {
            var devA = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.PriorityRules.Add(new QueuePriorityRule
            {
                Field = QueuePriorityField.Developer,
                ValueId = devA
            });

            var oldUnmatched = NewGame("OldOther", acquired: new DateTime(2020, 1, 1));
            var newerMatched = NewGame("NewerByDevA", acquired: new DateTime(2024, 1, 1), developers: new[] { devA });
            var olderMatched = NewGame("OlderByDevA", acquired: new DateTime(2023, 1, 1), developers: new[] { devA });

            var queue = QueueOrdering.BuildQueue(new[] { oldUnmatched, newerMatched, olderMatched }, settings);

            // Matched games come first (within the matched tier they preserve date sort),
            // unmatched falls below.
            CollectionAssert.AreEqual(new[] { "OlderByDevA", "NewerByDevA", "OldOther" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void PriorityRules_HigherListedRule_OutranksLower()
        {
            var devTop = Guid.NewGuid();
            var devLow = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.PriorityRules.Add(new QueuePriorityRule { Field = QueuePriorityField.Developer, ValueId = devTop });
            settings.PriorityRules.Add(new QueuePriorityRule { Field = QueuePriorityField.Developer, ValueId = devLow });

            var none = NewGame("None", acquired: new DateTime(2020, 1, 1));
            var low = NewGame("Low", acquired: new DateTime(2019, 1, 1), developers: new[] { devLow });
            var top = NewGame("Top", acquired: new DateTime(2024, 1, 1), developers: new[] { devTop });

            var queue = QueueOrdering.BuildQueue(new[] { none, low, top }, settings);

            CollectionAssert.AreEqual(new[] { "Top", "Low", "None" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void PriorityRule_Tag_PromotesMatchingGames()
        {
            var tagId = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.PriorityRules.Add(new QueuePriorityRule
            {
                Field = QueuePriorityField.Tag,
                ValueId = tagId
            });

            var unmatched = NewGame("Other", acquired: new DateTime(2020, 1, 1));
            var tagged = NewGame("Tagged", acquired: new DateTime(2024, 1, 1), tags: new[] { tagId });

            var queue = QueueOrdering.BuildQueue(new[] { unmatched, tagged }, settings);

            CollectionAssert.AreEqual(new[] { "Tagged", "Other" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void PriorityRule_BoolFieldFree_MatchesByValue()
        {
            var settings = DefaultSettings();
            settings.PriorityRules.Add(new QueuePriorityRule { Field = QueuePriorityField.Free, BoolValue = true });

            var paid = NewGame("Paid", acquired: new DateTime(2020, 1, 1));
            var freeNew = NewGame("FreeNew", acquired: new DateTime(2024, 1, 1), free: true);

            var queue = QueueOrdering.BuildQueue(new[] { paid, freeNew }, settings);

            CollectionAssert.AreEqual(new[] { "FreeNew", "Paid" }, queue.Select(q => q.Name).ToArray());
        }

        // ---- 4. Buffers -----------------------------------------------------

        [Test]
        public void Buffer_PushIfPlayingMatches_PushesGamesSharingDeveloperWithPlaying()
        {
            var sharedDev = Guid.NewGuid();
            var otherDev = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.DeveloperBuffer.PushIfPlayingMatches = true;

            var playing = NewGame("Playing", acquired: new DateTime(2020, 1, 1), statusId: PlayingId, developers: new[] { sharedDev });
            var oldPushed = NewGame("OldPushed", acquired: new DateTime(2018, 1, 1), developers: new[] { sharedDev });
            var newKept = NewGame("NewKept", acquired: new DateTime(2024, 1, 1), developers: new[] { otherDev });

            var queue = QueueOrdering.BuildQueue(new[] { playing, oldPushed, newKept }, settings);

            // Playing is prepended; among non-playing, the pushed-back game lands last
            // even though its date is older.
            CollectionAssert.AreEqual(new[] { "Playing", "NewKept", "OldPushed" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Buffer_RecentCompletedCount_PushesByLastNCompletedGenres()
        {
            var hotGenre = Guid.NewGuid();
            var coolGenre = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.GenreBuffer.RecentCompletedCount = 1;

            var recentlyCompleted = NewGame(
                "RecentDone",
                acquired: new DateTime(2010, 1, 1),
                completed: new DateTime(2025, 5, 1),
                statusId: CompletedId,
                genres: new[] { hotGenre });

            var staleCompleted = NewGame(
                "StaleDone",
                acquired: new DateTime(2010, 1, 1),
                completed: new DateTime(2020, 1, 1),
                statusId: CompletedId,
                genres: new[] { coolGenre });

            var oldHot = NewGame("OldHot", acquired: new DateTime(2018, 1, 1), genres: new[] { hotGenre });
            var newCool = NewGame("NewCool", acquired: new DateTime(2024, 1, 1), genres: new[] { coolGenre });

            var queue = QueueOrdering.BuildQueue(new[] { recentlyCompleted, staleCompleted, oldHot, newCool }, settings);

            // OldHot shares the hotGenre with the most recent completed game and gets
            // pushed back. NewCool is unaffected because we only look at the last 1
            // completed game.
            CollectionAssert.AreEqual(new[] { "NewCool", "OldHot" }, queue.Select(q => q.Name).ToArray());
        }

        // ---- 5. Grouping replacement ---------------------------------------

        [Test]
        public void Grouping_ByDeveloper_SuppressesSubsequentSameDeveloperSlots()
        {
            var devA = Guid.NewGuid();
            var devB = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.GroupByDeveloper = true;

            var a1 = NewGame("A1", acquired: new DateTime(2020, 1, 1), developers: new[] { devA });
            var a2 = NewGame("A2", acquired: new DateTime(2021, 1, 1), developers: new[] { devA });
            var b1 = NewGame("B1", acquired: new DateTime(2022, 1, 1), developers: new[] { devB });

            var queue = QueueOrdering.BuildQueue(new[] { a1, a2, b1 }, settings);

            // Both A games have null release dates so the replacement is whichever is
            // first; A2 is suppressed by the Developer A "seen" rule. B1 is unaffected.
            CollectionAssert.AreEqual(new[] { "A1", "B1" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Grouping_BySeries_DoesNotSuppressGamesWithoutSeries()
        {
            var seriesX = Guid.NewGuid();
            var settings = DefaultSettings();
            settings.GroupBySeries = true;

            var inSeries1 = NewGame("InSeries1", acquired: new DateTime(2020, 1, 1), series: new[] { seriesX });
            var inSeries2 = NewGame("InSeries2", acquired: new DateTime(2021, 1, 1), series: new[] { seriesX });
            var standalone = NewGame("Standalone", acquired: new DateTime(2022, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { inSeries1, inSeries2, standalone }, settings);

            CollectionAssert.AreEqual(new[] { "InSeries1", "Standalone" }, queue.Select(q => q.Name).ToArray());
        }

        [Test]
        public void Grouping_ByDeveloper_ReplacesSlotWithEarliestReleaseInGroup()
        {
            // Spec: enabling Group by Developer should surface the earliest-by-release
            // game from each developer group, even when the slot's date-sorted game is
            // a later release. Mirrors the user's Bethesda/Arena test scenario:
            // three Bethesda games tied at the oldest acquired date with later release
            // years, plus Arena (the earliest Bethesda release at 1994) acquired a few
            // days later. With GroupByDeveloper on, slot 0 should display Arena.
            var bethesda = Guid.NewGuid();
            var settings = DefaultSettings(n: 1);
            settings.GroupByDeveloper = true;

            var morrowind = NewGame("Morrowind",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2002, 5, 1),
                developers: new[] { bethesda });
            var oblivion = NewGame("Oblivion",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2006, 3, 1),
                developers: new[] { bethesda });
            var skyrim = NewGame("Skyrim",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2011, 11, 11),
                developers: new[] { bethesda });
            var arena = NewGame("Arena",
                acquired: new DateTime(2020, 1, 5),
                released: new DateTime(1994, 3, 1),
                developers: new[] { bethesda });

            var queue = QueueOrdering.BuildQueue(new[] { morrowind, oblivion, skyrim, arena }, settings);

            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual("Arena", queue[0].Name);
        }

        [Test]
        public void Grouping_Off_ShowsSlotGameNotReplacement()
        {
            // Same Bethesda/Arena scenario but with grouping off — slot 0 should be
            // the date-sort winner (the earliest-release Bethesda among the games tied
            // at the oldest acquired date), NOT Arena (which has a later acquired date).
            var bethesda = Guid.NewGuid();
            var settings = DefaultSettings(n: 1);

            var morrowind = NewGame("Morrowind",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2002, 5, 1),
                developers: new[] { bethesda });
            var oblivion = NewGame("Oblivion",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2006, 3, 1),
                developers: new[] { bethesda });
            var skyrim = NewGame("Skyrim",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2011, 11, 11),
                developers: new[] { bethesda });
            var arena = NewGame("Arena",
                acquired: new DateTime(2020, 1, 5),
                released: new DateTime(1994, 3, 1),
                developers: new[] { bethesda });

            var queue = QueueOrdering.BuildQueue(new[] { morrowind, oblivion, skyrim, arena }, settings);

            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual("Morrowind", queue[0].Name);
        }

        [Test]
        public void Grouping_BySeries_ReplacesSlotWithEarliestReleaseInSeries()
        {
            var series = Guid.NewGuid();
            var settings = DefaultSettings(n: 1);
            settings.GroupBySeries = true;

            var sequel = NewGame("Sequel",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2010, 1, 1),
                series: new[] { series });
            var original = NewGame("Original",
                acquired: new DateTime(2020, 6, 1),
                released: new DateTime(1995, 1, 1),
                series: new[] { series });

            var queue = QueueOrdering.BuildQueue(new[] { sequel, original }, settings);

            Assert.AreEqual(1, queue.Count);
            Assert.AreEqual("Original", queue[0].Name);
        }

        [Test]
        public void Grouping_ReplacementNeverDuplicatedAcrossSlots()
        {
            // Two slot games share a developer whose earliest release we already
            // showed. The second slot should fall back to the original slot game so
            // we don't render the same tile twice.
            var devShared = Guid.NewGuid();
            var devOther = Guid.NewGuid();
            var settings = DefaultSettings(n: 3);
            settings.GroupByDeveloper = true;

            // Earliest by devShared (1990) — gets pulled up to slot 0 as the replacement.
            var earlySharedDev = NewGame("EarlySharedDev",
                acquired: new DateTime(2020, 1, 5),
                released: new DateTime(1990, 1, 1),
                developers: new[] { devShared });

            // Slot 0 by acquired-date sort: a game co-credited to devShared and devOther.
            // GroupByDeveloper should replace it with EarlySharedDev (earliest devShared
            // release). devOther is then marked seen via the original slot game.
            var slot0 = NewGame("Slot0",
                acquired: new DateTime(2020, 1, 1),
                released: new DateTime(2015, 1, 1),
                developers: new[] { devShared, devOther });

            // Another devOther game later in the queue. Suppressed because devOther was
            // seen via slot0 above.
            var slot1OtherDev = NewGame("Slot1OtherDev",
                acquired: new DateTime(2020, 2, 1),
                released: new DateTime(2018, 1, 1),
                developers: new[] { devOther });

            // An unrelated developer's game.
            var unrelated = NewGame("Unrelated",
                acquired: new DateTime(2020, 3, 1),
                released: new DateTime(2019, 1, 1),
                developers: new[] { Guid.NewGuid() });

            var queue = QueueOrdering.BuildQueue(
                new[] { slot0, slot1OtherDev, earlySharedDev, unrelated }, settings);

            // Slot 0 = EarlySharedDev (replacement). Slot1OtherDev suppressed because
            // devOther was seen via slot0's secondary developer. Unrelated fills slot 1.
            CollectionAssert.AreEqual(
                new[] { "EarlySharedDev", "Unrelated" },
                queue.Select(q => q.Name).ToArray());
        }

        // ---- 6. Take top N --------------------------------------------------

        [Test]
        public void TakeN_LimitsResultsToVisibleCount()
        {
            var settings = DefaultSettings(n: 2);

            var games = Enumerable.Range(0, 5)
                .Select(i => NewGame("G" + i, acquired: new DateTime(2020 + i, 1, 1)))
                .ToArray();

            var queue = QueueOrdering.BuildQueue(games, settings);

            CollectionAssert.AreEqual(new[] { "G0", "G1" }, queue.Select(q => q.Name).ToArray());
        }

        // ---- 7. Playing merge ----------------------------------------------

        [Test]
        public void PlayingMerge_PlayingGamesConsumeVisibleSlots()
        {
            // VisibleCount=2 with 1 Playing game: 1 Playing + 1 queued = 2 total.
            // The cap is the *total* number of concurrent slots, not just the
            // queue portion.
            var settings = DefaultSettings(n: 2);
            var playing = NewGame("P1", acquired: new DateTime(2018, 1, 1), statusId: PlayingId);
            var q1 = NewGame("Q1", acquired: new DateTime(2020, 1, 1));
            var q2 = NewGame("Q2", acquired: new DateTime(2021, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { playing, q1, q2 }, settings);

            CollectionAssert.AreEqual(new[] { "P1", "Q1" }, queue.Select(g => g.Name).ToArray());
        }

        [Test]
        public void PlayingMerge_PlayingFillsAllSlots_NoQueueItemsShown()
        {
            // The user-reported bug: VisibleCount=1 with 2 Playing games.
            // Before fix the queue would show P1+P2+Q1 (3 tiles). After fix
            // the queue is just the Playing games — no upcoming-queue overflow.
            var settings = DefaultSettings(n: 1);
            var p1 = NewGame("P1", acquired: new DateTime(2018, 1, 1), statusId: PlayingId);
            var p2 = NewGame("P2", acquired: new DateTime(2019, 1, 1), statusId: PlayingId);
            var q1 = NewGame("Q1", acquired: new DateTime(2020, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { p1, p2, q1 }, settings);

            CollectionAssert.AreEqual(new[] { "P1", "P2" }, queue.Select(g => g.Name).ToArray());
        }

        [Test]
        public void PlayingMerge_MoreThanVisibleCountPlaying_AllPlayingStillShown()
        {
            // Playing tiles are never hidden — they only ever overflow the cap,
            // never get truncated. With n=1 and 3 Playing games, all 3 show
            // (and zero queue items).
            var settings = DefaultSettings(n: 1);
            var p1 = NewGame("P1", acquired: new DateTime(2018, 1, 1), statusId: PlayingId);
            var p2 = NewGame("P2", acquired: new DateTime(2017, 1, 1), statusId: PlayingId);
            var p3 = NewGame("P3", acquired: new DateTime(2019, 1, 1), statusId: PlayingId);
            var q1 = NewGame("Q1", acquired: new DateTime(2020, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { q1, p3, p1, p2 }, settings);

            CollectionAssert.AreEqual(new[] { "P2", "P1", "P3" }, queue.Select(g => g.Name).ToArray());
        }

        [Test]
        public void BuildQueue_PreparedMergeGroupUsesSingleVisibleSlot()
        {
            var groupId = Guid.NewGuid();
            var mergeIndex = new MergeGroupIndex();
            var m1 = NewGame("Merge 1", acquired: new DateTime(2020, 1, 1));
            m1.MergeGroupId = groupId;
            m1.Added = new DateTime(2020, 1, 1);
            var m2 = NewGame("Merge 2", acquired: new DateTime(2020, 1, 2));
            m2.MergeGroupId = groupId;
            m2.Added = new DateTime(2020, 1, 2);
            var m3 = NewGame("Merge 3", acquired: new DateTime(2020, 1, 3));
            m3.MergeGroupId = groupId;
            m3.Added = new DateTime(2020, 1, 3);
            var g4 = NewGame("Game 4", acquired: new DateTime(2020, 2, 1));
            var g5 = NewGame("Game 5", acquired: new DateTime(2020, 2, 2));
            var g6 = NewGame("Game 6", acquired: new DateTime(2020, 2, 3));
            var g7 = NewGame("Game 7", acquired: new DateTime(2020, 2, 4));
            var games = new List<Game> { m1, m2, m3, g4, g5, g6, g7 };
            mergeIndex.Rebuild(games);

            var unprepared = QueueOrdering.BuildQueue(games, DefaultSettings(n: 4));
            Assert.AreEqual(4, unprepared.Count);
            Assert.AreEqual(3, unprepared.Count(g => g.MergeGroupId == groupId));

            var prepared = mergeIndex.PrepareForQueue(games, DefaultSettings(n: 4));
            var queue = QueueOrdering.BuildQueue(prepared, DefaultSettings(n: 4));

            Assert.AreEqual(4, queue.Count);
            Assert.AreEqual(1, queue.Count(g => g.MergeGroupId == groupId));
        }

        [Test]
        public void PlayingMerge_PlayingGameNeverDuplicatedIntoMainQueue()
        {
            var settings = DefaultSettings(n: 5);
            var playing = NewGame("Playing", acquired: new DateTime(2020, 1, 1), statusId: PlayingId);
            var other = NewGame("Other", acquired: new DateTime(2021, 1, 1));

            var queue = QueueOrdering.BuildQueue(new[] { playing, other }, settings);

            Assert.AreEqual(2, queue.Count);
            Assert.AreEqual("Playing", queue[0].Name);
            Assert.AreEqual("Other", queue[1].Name);
        }
    }
}
