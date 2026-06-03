using Playnite.Database;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Identifies which metadata collection an unused-removal action targets.
    /// </summary>
    public enum UnusedMetadataKind
    {
        Series,
        Genre,
        Tag,
        /// <summary>
        /// A company record not referenced as developer or publisher on any game.
        /// </summary>
        Company,
    }

    /// <summary>
    /// Proposes bulk removal of metadata records not referenced by any game.
    /// One card is emitted per kind (series, genre, tag, company).
    /// </summary>
    public class RemoveUnusedMetadataAction : SanitizerAction
    {
        public UnusedMetadataKind Kind { get; }
        public IList<UnusedMetadataEntry> Items { get; }

        public int ItemCount => Items?.Count ?? 0;

        public IEnumerable<string> ItemNames =>
            Items?.Select(i => i.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            ?? Enumerable.Empty<string>();

        public RemoveUnusedMetadataAction(UnusedMetadataKind kind, IList<UnusedMetadataEntry> items)
        {
            Kind = kind;
            Items = new List<UnusedMetadataEntry>(items ?? new List<UnusedMetadataEntry>());
        }

        public override SanitizerActionType Type => SanitizerActionType.RemoveUnused;

        public override string DisplayTitle => FormatDisplayTitle(Kind, ItemCount);

        public override string Signature
        {
            get
            {
                var sb = new StringBuilder("RemoveUnused|");
                sb.Append(Kind).Append('|');
                foreach (var id in Items.Select(i => i.Id).OrderBy(i => i))
                {
                    sb.Append(id).Append(',');
                }
                return sb.ToString();
            }
        }

        public IReadOnlyList<GameDatabaseCollection> GetAffectedCollections()
        {
            switch (Kind)
            {
                case UnusedMetadataKind.Series:
                    return new[] { GameDatabaseCollection.Series };
                case UnusedMetadataKind.Genre:
                    return new[] { GameDatabaseCollection.Genres };
                case UnusedMetadataKind.Tag:
                    return new[] { GameDatabaseCollection.Tags };
                case UnusedMetadataKind.Company:
                    return new[] { GameDatabaseCollection.Companies };
                default:
                    return Array.Empty<GameDatabaseCollection>();
            }
        }

        public override void Apply(IGameDatabaseMain database)
        {
            if (database == null || Items == null || Items.Count == 0)
            {
                return;
            }

            IDisposable buffer = null;
            try
            {
                buffer = database.BufferedUpdate();
            }
            catch (NotImplementedException)
            {
                // In-memory test databases do not support buffered updates.
            }

            try
            {
                switch (Kind)
                {
                    case UnusedMetadataKind.Series:
                        RemoveFromCollection(database.Series);
                        break;
                    case UnusedMetadataKind.Genre:
                        RemoveFromCollection(database.Genres);
                        break;
                    case UnusedMetadataKind.Tag:
                        RemoveFromCollection(database.Tags);
                        break;
                    case UnusedMetadataKind.Company:
                        RemoveFromCollection(database.Companies);
                        break;
                }
            }
            finally
            {
                buffer?.Dispose();
            }
        }

        private void RemoveFromCollection<TItem>(IItemCollection<TItem> collection) where TItem : DatabaseObject
        {
            foreach (var entry in Items)
            {
                if (entry.Id == Guid.Empty)
                {
                    continue;
                }

                if (collection.Get(entry.Id) != null)
                {
                    collection.Remove(entry.Id);
                }
            }
        }

        internal static string FormatDisplayTitle(UnusedMetadataKind kind, int count)
        {
            string noun;
            switch (kind)
            {
                case UnusedMetadataKind.Series:
                    noun = "series";
                    break;
                case UnusedMetadataKind.Genre:
                    noun = "genres";
                    break;
                case UnusedMetadataKind.Tag:
                    noun = "tags";
                    break;
                case UnusedMetadataKind.Company:
                    noun = "companies";
                    break;
                default:
                    noun = "items";
                    break;
            }

            return $"{count} unused {noun}";
        }

        internal static IList<UnusedMetadataEntry> FromSeries(IEnumerable<Series> items) =>
            ToEntries(items?.Select(s => (Id: s.Id, Name: s.Name)));

        internal static IList<UnusedMetadataEntry> FromGenres(IEnumerable<Genre> items) =>
            ToEntries(items?.Select(g => (Id: g.Id, Name: g.Name)));

        internal static IList<UnusedMetadataEntry> FromTags(IEnumerable<Tag> items) =>
            ToEntries(items?.Select(t => (Id: t.Id, Name: t.Name)));

        internal static IList<UnusedMetadataEntry> FromCompanies(IEnumerable<Company> items) =>
            ToEntries(items?.Select(c => (Id: c.Id, Name: c.Name)));

        private static IList<UnusedMetadataEntry> ToEntries(IEnumerable<(Guid Id, string Name)> items)
        {
            return items?
                .Where(i => i.Id != Guid.Empty)
                .Select(i => new UnusedMetadataEntry(i.Id, i.Name ?? string.Empty))
                .ToList()
                ?? new List<UnusedMetadataEntry>();
        }
    }

    public class UnusedMetadataEntry
    {
        public Guid Id { get; }
        public string Name { get; }

        public UnusedMetadataEntry(Guid id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }
    }
}
