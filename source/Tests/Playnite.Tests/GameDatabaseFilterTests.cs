using NUnit.Framework;
using Playnite;
using Playnite.Common;
using Playnite.Database;
using Playnite.SDK.Models;
using System.Linq;

namespace Playnite.Tests
{
    [TestFixture]
    public class GameDatabaseFilterTests
    {
        [Test]
        public void MissingCoverImageFilter_MatchesOnlyGamesWithoutCover()
        {
            using (var temp = TempDirectory.Create())
            using (var db = new GameDatabase(temp.TempPath))
            {
                db.OpenDatabase();
                var withCover = new Game("With cover") { CoverImage = "cover.png" };
                var withoutCover = new Game("Without cover");
                db.Games.Add(withCover);
                db.Games.Add(withoutCover);

                var filter = new FilterSettings { MissingCoverImage = true };
                var matches = db.GetFilteredGames(filter, false).ToList();

                Assert.AreEqual(1, matches.Count);
                Assert.AreEqual(withoutCover.Id, matches[0].Id);
            }
        }
    }
}
