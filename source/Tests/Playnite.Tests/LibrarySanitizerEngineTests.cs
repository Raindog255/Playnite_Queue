using NUnit.Framework;
using Playnite;
using Playnite.Database;
using Playnite.LibrarySanitizer;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Playnite.Tests
{
    [TestFixture]
    public class LibrarySanitizerEngineTests
    {
        private static LibrarySanitizerSettings DefaultSettings()
        {
            return new LibrarySanitizerSettings();
        }

        // Sanitize-only preset: turns off the missing-properties and
        // split-collection detectors so a test that's only interested in
        // SanitizeAction emissions doesn't have to filter the cross-traffic.
        private static LibrarySanitizerSettings SanitizeOnlySettings()
        {
            var s = new LibrarySanitizerSettings();
            s.MissingPropertiesEnabled = false;
            s.SplitCollectionEnabled = false;
            return s;
        }

        private static Game Game(string name)
        {
            return new Game { Id = Guid.NewGuid(), Name = name };
        }

        private static Series Series(string name)
        {
            return new Series { Id = Guid.NewGuid(), Name = name };
        }

        private static Company Company(string name)
        {
            return new Company { Id = Guid.NewGuid(), Name = name };
        }

        // ---- Generation ---------------------------------------------------

        [Test]
        public void Build_EmitsActionForChangedGameOnly()
        {
            var clean = Game("Disco Elysium");
            var dirty = Game("Final Fantasy 7");

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { clean, dirty },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            var sanitize = actions.OfType<SanitizeAction>().ToList();
            Assert.AreEqual(1, sanitize.Count);
            var sa = sanitize[0];
            Assert.AreEqual(SanitizerTargetKind.Game, sa.TargetKind);
            Assert.AreEqual(dirty.Id, sa.TargetId);
            Assert.AreEqual("Final Fantasy VII", sa.ProposedName);
            Assert.AreEqual("Final Fantasy 7", sa.ProposedSortingName);
        }

        [Test]
        public void Build_EmitsActionForSeriesAndCompany()
        {
            var s = Series("  Halo  ");
            var c = Company("Bungie\u2122");

            var actions = LibrarySanitizerEngine.BuildActions(
                Enumerable.Empty<Game>(),
                new[] { s },
                new[] { c },
                SanitizeOnlySettings());

            var sanitize = actions.OfType<SanitizeAction>().ToList();
            Assert.AreEqual(2, sanitize.Count);
            Assert.IsTrue(sanitize.Any(a =>
                a.TargetKind == SanitizerTargetKind.Series && a.ProposedName == "Halo"));
            Assert.IsTrue(sanitize.Any(a =>
                a.TargetKind == SanitizerTargetKind.Company && a.ProposedName == "Bungie"));
        }

        [Test]
        public void Build_RespectsScopeToggles()
        {
            var s = SanitizeOnlySettings();
            s.SanitizeSeries = false;
            s.SanitizeDevelopers = false;
            s.SanitizePublishers = false;

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { Game("Final Fantasy 7") },
                new[] { Series("  Halo  ") },
                new[] { Company("Bungie\u2122") },
                s);

            var sanitize = actions.OfType<SanitizeAction>().ToList();
            Assert.AreEqual(1, sanitize.Count);
            Assert.AreEqual(SanitizerTargetKind.Game, sanitize[0].TargetKind);
        }

        [Test]
        public void SanitizeAction_EditModeRevealsOptionalRows()
        {
            // A trademark-stripping action carries no SortingName / Version
            // proposal, so by default both rows stay hidden. Flipping
            // IsEditing surfaces them so the user can fill them by hand.
            var g = Game("Bungie\u2122");
            var action = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), SanitizeOnlySettings())
                .OfType<SanitizeAction>().Single();

            Assert.IsFalse(action.ShowSortingName);
            Assert.IsFalse(action.ShowVersion);

            action.IsEditing = true;
            Assert.IsTrue(action.ShowSortingName);
            Assert.IsTrue(action.ShowVersion);

            action.IsEditing = false;
            Assert.IsFalse(action.ShowSortingName);
            Assert.IsFalse(action.ShowVersion);
        }

        [Test]
        public void SanitizeAction_EditModeRaisesPropertyChangedOnComputedRows()
        {
            // The XAML binds row visibility to ShowSortingName / ShowVersion,
            // so OnEditingChanged must raise PropertyChanged for both
            // computed flags or the rows won't refresh in the live view.
            var g = Game("Bungie\u2122");
            var action = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), SanitizeOnlySettings())
                .OfType<SanitizeAction>().Single();

            var raised = new HashSet<string>();
            action.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            action.IsEditing = true;

            Assert.Contains(nameof(SanitizeAction.IsEditing), raised.ToList());
            Assert.Contains(nameof(SanitizeAction.ShowSortingName), raised.ToList());
            Assert.Contains(nameof(SanitizeAction.ShowVersion), raised.ToList());
        }

        [Test]
        public void Build_DoesNotReEmitForGameAlreadyMatchingProposal()
        {
            // Mirror of the bug the user hit: the numerals "trailing Roman"
            // pass always proposes an Arabic SortingName, so without the
            // engine-side delta check the action card pops back the moment
            // the dispatcher rebuilds after Apply.
            var g = Game("Final Fantasy VII");
            g.SortingName = "Final Fantasy 7";

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            Assert.IsFalse(actions.OfType<SanitizeAction>().Any(),
                "Game with matching SortingName should not produce a redundant SanitizeAction.");
        }

        [Test]
        public void Build_StillEmitsWhenSortingNameOverrideDiffersFromCurrent()
        {
            // Same game, but the user has a stale SortingName -- the engine
            // should still surface the proposal so they can re-sync.
            var g = Game("Final Fantasy VII");
            g.SortingName = "Final Fantasy stale";

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            var sa = actions.OfType<SanitizeAction>().Single();
            Assert.AreEqual("Final Fantasy 7", sa.ProposedSortingName);
        }

        [Test]
        public void Build_DoesNotReEmitSpilledVersionAlreadyApplied()
        {
            // After the user applies a "GOTY Edition" spillover, the next
            // rebuild sees Name="Skyrim", Version="GOTY Edition". Nothing
            // left to spill -- and even if SpilledVersion happened to be
            // re-proposed with the same string, the engine should detect
            // it's already at the tail of Version and stay quiet.
            var g = Game("Skyrim");
            g.Version = "GOTY Edition";

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            Assert.IsFalse(actions.OfType<SanitizeAction>().Any());
        }

        [Test]
        public void Build_SkipsHiddenSourceAfterSplit()
        {
            // Splits hide their source bundle; the detector must not
            // re-detect that hidden record on the next rebuild.
            var g = Game("Mass Effect Trilogy");
            g.Hidden = true;

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                DefaultSettings());

            Assert.IsFalse(actions.OfType<SplitCollectionAction>().Any());
            Assert.IsFalse(actions.OfType<MissingPropertyAction>().Any());
        }

        [Test]
        public void Build_RespectsSanitizeMasterSwitch()
        {
            var s = SanitizeOnlySettings();
            s.SanitizeEnabled = false;

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { Game("Final Fantasy 7") },
                new[] { Series("  Halo  ") },
                new[] { Company("Bungie\u2122") },
                s);

            Assert.IsFalse(actions.OfType<SanitizeAction>().Any());
        }

        [Test]
        public void Build_FiltersDismissedSignatures()
        {
            var dirty = Game("Final Fantasy 7");
            var settings = SanitizeOnlySettings();

            // First pass to capture the signature this game produces.
            var first = LibrarySanitizerEngine.BuildActions(
                new[] { dirty },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                settings);
            Assert.AreEqual(1, first.Count);
            var dismissed = new HashSet<string> { first[0].Signature };

            var second = LibrarySanitizerEngine.BuildActions(
                new[] { dirty },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                settings,
                dismissed);

            Assert.AreEqual(0, second.Count, "Dismissed action should be suppressed.");
        }

        [Test]
        public void Build_NullSettings_ReturnsEmpty()
        {
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { Game("Anything") },
                null, null, null);
            Assert.AreEqual(0, actions.Count);
        }

        [Test]
        public void Build_HandlesNullCollectionsGracefully()
        {
            var actions = LibrarySanitizerEngine.BuildActions(null, null, null, DefaultSettings());
            Assert.AreEqual(0, actions.Count);
        }

        // ---- Apply --------------------------------------------------------

        [Test]
        public void SanitizeAction_Apply_UpdatesGameNameSortingNameAndVersion()
        {
            var db = new InMemoryGameDatabase();
            var g = Game("Fallout 4: Game of the Year Edition");
            db.Games.Add(g);

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            var sa = actions.OfType<SanitizeAction>().Single();
            sa.Apply(db);

            var stored = db.Games.Get(g.Id);
            Assert.AreEqual("Fallout IV", stored.Name);
            Assert.AreEqual("Fallout 4", stored.SortingName);
            Assert.AreEqual("Game of the Year Edition", stored.Version);
        }

        [Test]
        public void SanitizeAction_Apply_AppendsToExistingVersion()
        {
            var db = new InMemoryGameDatabase();
            var g = Game("Skyrim Special Edition");
            g.Version = "1.5.97";
            db.Games.Add(g);

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                SanitizeOnlySettings());

            var sa = actions.OfType<SanitizeAction>().Single();
            sa.Apply(db);

            var stored = db.Games.Get(g.Id);
            Assert.AreEqual("Skyrim", stored.Name);
            Assert.AreEqual("1.5.97 Special Edition", stored.Version);
        }

        [Test]
        public void SanitizeAction_Apply_UpdatesSeriesAndCompanyNames()
        {
            var db = new InMemoryGameDatabase();
            var s = Series("  Halo  ");
            var c = Company("Bungie\u2122");
            db.Series.Add(s);
            db.Companies.Add(c);

            var actions = LibrarySanitizerEngine.BuildActions(
                Enumerable.Empty<Game>(),
                new[] { s },
                new[] { c },
                SanitizeOnlySettings());

            foreach (var a in actions.OfType<SanitizeAction>()) a.Apply(db);

            Assert.AreEqual("Halo", db.Series.Get(s.Id).Name);
            Assert.AreEqual("Bungie", db.Companies.Get(c.Id).Name);
        }

        // ---- Signature stability ------------------------------------------

        [Test]
        public void Signature_StableAcrossRebuilds()
        {
            var settings = SanitizeOnlySettings();
            var g = Game("Final Fantasy 7");
            var first = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), settings);
            var second = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), settings);

            Assert.AreEqual(first[0].Signature, second[0].Signature);
        }

        [Test]
        public void Signature_DiffersWhenProposedNameDiffers()
        {
            var g = Game("Final Fantasy 7");
            var settings = SanitizeOnlySettings();
            var first = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), settings)
                .OfType<SanitizeAction>().Single();

            var settings2 = SanitizeOnlySettings();
            settings2.NumeralsRule = false;
            var second = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), settings2);

            // No numerals change and the title is otherwise clean -> no sanitize action.
            Assert.IsFalse(second.OfType<SanitizeAction>().Any());
            Assert.IsTrue(first.Signature.Contains("VII"));
        }

        // ---- Missing properties ------------------------------------------

        [Test]
        public void Build_MissingProperties_DetectsRequiredGaps()
        {
            var g = Game("Disco Elysium");
            // Default required fields: Acquired, Release, CompletionStatus,
            // Developers, Genres. None are set on this fresh record.
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g },
                Enumerable.Empty<Series>(),
                Enumerable.Empty<Company>(),
                DefaultSettings());

            var mp = actions.OfType<MissingPropertyAction>().Single();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    MissingField.AcquiredDate,
                    MissingField.ReleaseDate,
                    MissingField.CompletionStatus,
                    MissingField.Developers,
                    MissingField.Genres,
                },
                mp.MissingFields);
        }

        [Test]
        public void Build_MissingProperties_RespectsRequiredToggles()
        {
            var settings = DefaultSettings();
            settings.RequireAcquiredDate = false;
            settings.RequireReleaseDate = false;
            settings.RequireCompletionStatus = false;
            settings.RequireDevelopers = false;
            settings.RequireGenres = false;

            var g = Game("Disco Elysium");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), settings);

            Assert.IsFalse(actions.OfType<MissingPropertyAction>().Any(),
                "All required-field toggles off -> no missing-property action.");
        }

        [Test]
        public void Build_MissingProperties_SuppressedWhenAllFieldsPresent()
        {
            var g = Game("Disco Elysium");
            g.AcquiredDate = new DateTime(2020, 1, 1);
            g.ReleaseDate = new ReleaseDate(2019, 10, 15);
            g.CompletionStatusId = Guid.NewGuid();
            g.DeveloperIds = new List<Guid> { Guid.NewGuid() };
            g.GenreIds = new List<Guid> { Guid.NewGuid() };

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());

            Assert.IsFalse(actions.OfType<MissingPropertyAction>().Any());
        }

        [Test]
        public void MissingPropertyAction_Apply_OnlyWritesProvidedFields()
        {
            var db = new InMemoryGameDatabase();
            var g = Game("Disco Elysium");
            db.Games.Add(g);

            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var mp = actions.OfType<MissingPropertyAction>().Single();

            // Provide only the AcquiredDate. Apply should leave the other
            // missing fields untouched (the user can come back later).
            mp.ProposedAcquiredDate = new DateTime(2021, 6, 1);
            mp.Apply(db);

            var stored = db.Games.Get(g.Id);
            Assert.AreEqual(new DateTime(2021, 6, 1), stored.AcquiredDate);
            Assert.IsNull(stored.ReleaseDate);
            Assert.AreEqual(Guid.Empty, stored.CompletionStatusId);
        }

        [Test]
        public void MissingPropertyAction_Signature_IsStableForSameFieldSet()
        {
            var g = Game("Disco Elysium");
            var a = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<MissingPropertyAction>().Single();
            var b = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<MissingPropertyAction>().Single();
            Assert.AreEqual(a.Signature, b.Signature);
        }

        // ---- Split collection --------------------------------------------

        // Strips the auto-grown trailing blank so detector assertions can
        // focus on what was actually proposed.
        private static string[] ChildNames(SplitCollectionAction split) =>
            split.ProposedChildNames
                .Select(c => c.Name ?? string.Empty)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();

        [Test]
        public void Build_Split_KeywordTrilogyProposesThreeChildren()
        {
            var g = Game("Mass Effect Trilogy");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.Keyword, split.Reason);
            CollectionAssert.AreEqual(
                new[] { "Mass Effect 1", "Mass Effect 2", "Mass Effect 3" },
                ChildNames(split));
        }

        [Test]
        public void Build_Split_KeywordCollectionProposesTwoPlaceholders()
        {
            var g = Game("Halo: The Master Chief Collection");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.Keyword, split.Reason);
            // "Halo: The Master Chief Collection" -> head "Halo: The Master Chief".
            var names = ChildNames(split);
            Assert.AreEqual(2, names.Length);
            Assert.IsTrue(names[0].StartsWith("Halo: The Master Chief"));
        }

        [Test]
        public void Build_Split_NumericSequence()
        {
            var g = Game("BioShock 1+2+3");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.NumericSequence, split.Reason);
            CollectionAssert.AreEqual(
                new[] { "BioShock", "BioShock 2", "BioShock 3" },
                ChildNames(split));
        }

        [Test]
        public void Build_Split_RomanRange()
        {
            var g = Game("Final Fantasy I & II");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.RomanRange, split.Reason);
            CollectionAssert.AreEqual(
                new[] { "Final Fantasy I", "Final Fantasy II" },
                ChildNames(split));
        }

        [Test]
        public void Build_Split_AmpersandPair()
        {
            var g = Game("Portal & Portal 2");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.AmpersandPair, split.Reason);
            CollectionAssert.AreEqual(new[] { "Portal", "Portal 2" }, ChildNames(split));
        }

        [Test]
        public void Build_Split_SlashPair()
        {
            var g = Game("Pokemon Red / Blue");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            var split = actions.OfType<SplitCollectionAction>().Single();
            Assert.AreEqual(SplitCollectionReason.SlashPair, split.Reason);
            CollectionAssert.AreEqual(new[] { "Pokemon Red", "Blue" }, ChildNames(split));
        }

        [Test]
        public void Build_Split_IgnoresShortAmpersandPair()
        {
            var g = Game("Tom & Jerry");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings());
            Assert.IsFalse(actions.OfType<SplitCollectionAction>().Any(),
                "Two-short-words ampersand pair should not trip the splitter.");
        }

        [Test]
        public void Build_Split_DisabledByMasterToggle()
        {
            var s = DefaultSettings();
            s.SplitCollectionEnabled = false;

            var g = Game("Mass Effect Trilogy");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), s);
            Assert.IsFalse(actions.OfType<SplitCollectionAction>().Any());
        }

        [Test]
        public void Build_Split_MultiTitleToggleOff_StillRunsKeyword()
        {
            var s = DefaultSettings();
            s.DetectMultiTitlePatterns = false;

            var keywordHit = Game("Mass Effect Trilogy");
            var multi = Game("BioShock 1+2");
            var actions = LibrarySanitizerEngine.BuildActions(
                new[] { keywordHit, multi },
                Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), s);
            var splits = actions.OfType<SplitCollectionAction>().ToList();
            Assert.AreEqual(1, splits.Count);
            Assert.AreEqual(SplitCollectionReason.Keyword, splits[0].Reason);
        }

        [Test]
        public void SplitCollectionAction_AlwaysCarriesTrailingBlank()
        {
            var g = Game("Mass Effect Trilogy");
            var split = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<SplitCollectionAction>().Single();

            Assert.IsTrue(string.IsNullOrEmpty(split.ProposedChildNames.Last().Name),
                "Detector output should be padded with a trailing blank entry for inline editing.");
        }

        [Test]
        public void SplitCollectionAction_AppendsBlankWhenUserTypesIntoTrailingField()
        {
            var g = Game("Mass Effect Trilogy");
            var split = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<SplitCollectionAction>().Single();
            var beforeCount = split.ProposedChildNames.Count;

            // Simulate the user typing into the trailing blank.
            split.ProposedChildNames.Last().Name = "Mass Effect: Andromeda";

            Assert.AreEqual(beforeCount + 1, split.ProposedChildNames.Count,
                "Typing into the trailing blank should auto-append a fresh blank entry.");
            Assert.IsTrue(string.IsNullOrEmpty(split.ProposedChildNames.Last().Name));
        }

        [Test]
        public void SplitCollectionAction_DoesNotKeepGrowingAcrossEdits()
        {
            // Each edit to an interior entry should NOT spawn extra blanks --
            // the auto-grow only fires when the user has filled the
            // currently-trailing blank.
            var g = Game("Mass Effect Trilogy");
            var split = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<SplitCollectionAction>().Single();
            var blanksAfterDetector = split.ProposedChildNames.Count(c => string.IsNullOrEmpty(c.Name));

            split.ProposedChildNames[0].Name = "Mass Effect Edited";
            split.ProposedChildNames[1].Name = "Mass Effect 2 Edited";

            Assert.AreEqual(blanksAfterDetector,
                split.ProposedChildNames.Count(c => string.IsNullOrEmpty(c.Name)),
                "Editing existing non-blank entries shouldn't add new blanks.");
        }

        [Test]
        public void SplitCollectionAction_Apply_CreatesChildrenAndHidesSource()
        {
            var db = new InMemoryGameDatabase();
            var g = Game("Mass Effect Trilogy");
            g.GenreIds = new List<Guid> { Guid.NewGuid() };
            db.Games.Add(g);

            var split = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<SplitCollectionAction>().Single();
            split.SetProposedChildren(new[]
            {
                new EditableChildName("Mass Effect"),
                new EditableChildName("Mass Effect 2"),
                new EditableChildName("Mass Effect 3"),
            });
            split.Apply(db);

            Assert.IsTrue(db.Games.Get(g.Id).Hidden, "Source bundle should be hidden after split.");
            var children = db.Games.Where(x => x.Id != g.Id && !x.Hidden).ToList();
            Assert.AreEqual(3, children.Count);
            CollectionAssert.AreEquivalent(
                new[] { "Mass Effect", "Mass Effect 2", "Mass Effect 3" },
                children.Select(c => c.Name).ToArray());
            // Each child should inherit the genre list (deep-copied so a
            // later edit on one child doesn't ripple).
            Assert.IsTrue(children.All(c => c.GenreIds != null && c.GenreIds.Count == 1));
            Assert.AreNotSame(g.GenreIds, children[0].GenreIds);
        }

        [Test]
        public void SplitCollectionAction_Apply_NoChildrenIsNoop()
        {
            var db = new InMemoryGameDatabase();
            var g = Game("Mass Effect Trilogy");
            db.Games.Add(g);

            var split = LibrarySanitizerEngine.BuildActions(
                new[] { g }, Enumerable.Empty<Series>(), Enumerable.Empty<Company>(), DefaultSettings())
                .OfType<SplitCollectionAction>().Single();
            split.SetProposedChildren(new[]
            {
                new EditableChildName(""),
                new EditableChildName("  "),
                new EditableChildName(null),
            });
            split.Apply(db);

            Assert.IsFalse(db.Games.Get(g.Id).Hidden, "Empty proposal should not mutate state.");
            Assert.AreEqual(1, db.Games.Count);
        }
    }
}
