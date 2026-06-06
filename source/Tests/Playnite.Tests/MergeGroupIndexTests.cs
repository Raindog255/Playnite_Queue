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
    public class MergeGroupIndexTests
    {
        private static Guid PlayingId = Guid.NewGuid();
        private static Guid CompletedId = Guid.NewGuid();

        private static QueueSettings DefaultSettings()
        {
            var settings = new QueueSettings
            {
                PlayingStatusId = PlayingId
            };
            settings.TerminalStatusIds.Add(CompletedId);
            return settings;
        }

        private static Game Member(Guid groupId, string name, DateTime added, Guid statusId = default)
        {
            return new Game
            {
                Id = Guid.NewGuid(),
                Name = name,
                MergeGroupId = groupId,
                Added = added,
                CompletionStatusId = statusId
            };
        }

        [Test]
        public void PrepareForQueue_ProjectsPlayingStatusFromAnyMember()
        {
            var groupId = Guid.NewGuid();
            var rep = Member(groupId, "Rep", new DateTime(2020, 1, 1));
            var playingMember = Member(groupId, "Copy", new DateTime(2020, 1, 2), PlayingId);
            var games = new List<Game> { rep, playingMember };

            var index = new MergeGroupIndex();
            index.Rebuild(games);

            var prepared = index.PrepareForQueue(games, DefaultSettings());
            var projected = prepared.Single();

            Assert.AreEqual(rep.Id, projected.Id);
            Assert.AreEqual(PlayingId, projected.CompletionStatusId);
        }

        [Test]
        public void PrepareForQueue_CollapsesToRepresentativesOnly()
        {
            var groupId = Guid.NewGuid();
            var rep = Member(groupId, "Rep", new DateTime(2020, 1, 1));
            var member = Member(groupId, "Copy", new DateTime(2020, 1, 2));
            var other = new Game { Id = Guid.NewGuid(), Name = "Other" };
            var games = new List<Game> { rep, member, other };

            var index = new MergeGroupIndex();
            index.Rebuild(games);

            var prepared = index.PrepareForQueue(games, DefaultSettings());

            Assert.AreEqual(2, prepared.Count);
            Assert.AreEqual(1, prepared.Count(g => g.MergeGroupId == groupId));
            Assert.IsTrue(prepared.Any(g => g.Id == other.Id));
        }
    }
}
