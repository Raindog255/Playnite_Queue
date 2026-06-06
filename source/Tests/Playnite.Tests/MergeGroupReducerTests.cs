using NUnit.Framework;
using Playnite.LibrarySanitizer;
using Playnite.MergeGroups;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace Playnite.Tests
{
    [TestFixture]
    public class MergeGroupReducerTests
    {
        private static LibrarySanitizerSettings DefaultSettings() => new LibrarySanitizerSettings();

        [Test]
        public void Reduce_UnionsGenresAndTagsDisplay()
        {
            var g1 = new Game { Name = "Hades", GenreIds = new List<Guid> { Guid.NewGuid() } };
            var g2 = new Game { Name = "HADES", GenreIds = new List<Guid> { Guid.NewGuid() } };
            var reduced = MergeGroupReducer.Reduce(new[] { g1, g2 }, DefaultSettings());
            Assert.AreEqual(2, reduced.GenreIds.Count);
        }

        [Test]
        public void Reduce_SyncsArtworkFromFirstNonEmptyMember()
        {
            var coverId = "cover-a";
            var g1 = new Game { Name = "Game", CoverImage = null };
            var g2 = new Game { Name = "Game", CoverImage = coverId };
            var reduced = MergeGroupReducer.Reduce(new[] { g1, g2 }, DefaultSettings());
            Assert.AreEqual(coverId, reduced.CoverImage);
        }

        [Test]
        public void Display_FreeAllRequiresEveryMemberFree()
        {
            var members = new List<Game>
            {
                new Game { Free = true },
                new Game { Free = false }
            };
            Assert.IsFalse(MergeGroupDisplay.FreeAll(members));
        }

        [Test]
        public void GetMergeMatchKey_GroupsSanitizedEquivalents()
        {
            var settings = DefaultSettings();
            var a = MergeGroupReducer.GetMergeMatchKey(new Game { Name = "Final Fantasy 7" }, settings);
            var b = MergeGroupReducer.GetMergeMatchKey(new Game { Name = "Final Fantasy VII" }, settings);
            Assert.AreEqual(a, b);
        }
    }
}
