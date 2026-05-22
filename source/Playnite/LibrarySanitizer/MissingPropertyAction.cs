using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Identifies which queue-relevant property is missing on a game. Drives
    /// both the card's missing-fields summary and the inline editor's input
    /// surface (one input per flagged field).
    /// </summary>
    public enum MissingField
    {
        AcquiredDate,
        ReleaseDate,
        CompletionStatus,
        Series,
        Developers,
        Categories,
        Genres,
    }

    /// <summary>
    /// Surfaces a single Game record that's missing one or more user-required
    /// fields. The action holds proposed values populated by the inline
    /// editor; <see cref="Apply"/> writes back any non-null/non-empty value
    /// and leaves the others untouched. The signature is keyed off the game
    /// id and the set of missing fields so dismissals only suppress the
    /// current state -- new gaps re-surface even after dismissal.
    /// </summary>
    public class MissingPropertyAction : SanitizerAction
    {
        public Guid GameId { get; }
        public string GameName { get; }
        public IList<MissingField> MissingFields { get; }

        // Proposed values are populated by the inline editor before Apply.
        // Null / empty means "leave field as-is" so a partial fill is OK.
        public DateTime? ProposedAcquiredDate { get; set; }
        public ReleaseDate? ProposedReleaseDate { get; set; }
        public Guid? ProposedCompletionStatusId { get; set; }
        public IList<Guid> ProposedSeriesIds { get; set; }
        public IList<Guid> ProposedDeveloperIds { get; set; }
        public IList<Guid> ProposedCategoryIds { get; set; }
        public IList<Guid> ProposedGenreIds { get; set; }

        private readonly Game gameRef;

        public MissingPropertyAction(Game game, IList<MissingField> missing)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            gameRef = game;
            GameId = game.Id;
            GameName = game.Name;
            MissingFields = new List<MissingField>(missing ?? new List<MissingField>());
        }

        public override SanitizerActionType Type => SanitizerActionType.MissingProperty;

        public override string DisplayTitle => GameName;

        public override Game GetTargetGame() => gameRef;

        public override string Signature
        {
            get
            {
                var sb = new StringBuilder("Missing|");
                sb.Append(GameId).Append('|');
                // Sort the missing-field list so signature is stable
                // regardless of detection order.
                foreach (var f in MissingFields.OrderBy(f => f.ToString()))
                {
                    sb.Append(f).Append(',');
                }
                return sb.ToString();
            }
        }

        public override void Apply(IGameDatabaseMain database)
        {
            if (gameRef == null) return;
            var changed = false;

            if (MissingFields.Contains(MissingField.AcquiredDate) && ProposedAcquiredDate.HasValue)
            {
                gameRef.AcquiredDate = ProposedAcquiredDate;
                changed = true;
            }
            if (MissingFields.Contains(MissingField.ReleaseDate) && ProposedReleaseDate.HasValue)
            {
                gameRef.ReleaseDate = ProposedReleaseDate;
                changed = true;
            }
            if (MissingFields.Contains(MissingField.CompletionStatus) && ProposedCompletionStatusId.HasValue && ProposedCompletionStatusId.Value != Guid.Empty)
            {
                gameRef.CompletionStatusId = ProposedCompletionStatusId.Value;
                changed = true;
            }
            if (MissingFields.Contains(MissingField.Series) && ProposedSeriesIds != null && ProposedSeriesIds.Count > 0)
            {
                gameRef.SeriesIds = new List<Guid>(ProposedSeriesIds);
                changed = true;
            }
            if (MissingFields.Contains(MissingField.Developers) && ProposedDeveloperIds != null && ProposedDeveloperIds.Count > 0)
            {
                gameRef.DeveloperIds = new List<Guid>(ProposedDeveloperIds);
                changed = true;
            }
            if (MissingFields.Contains(MissingField.Categories) && ProposedCategoryIds != null && ProposedCategoryIds.Count > 0)
            {
                gameRef.CategoryIds = new List<Guid>(ProposedCategoryIds);
                changed = true;
            }
            if (MissingFields.Contains(MissingField.Genres) && ProposedGenreIds != null && ProposedGenreIds.Count > 0)
            {
                gameRef.GenreIds = new List<Guid>(ProposedGenreIds);
                changed = true;
            }

            if (changed)
            {
                database.Games.Update(gameRef);
            }
        }
    }
}
