using Playnite.Database;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
// ObservableObject lives in System.Collections.Generic per Playnite SDK convention.

namespace Playnite.LibrarySanitizer
{
    /// <summary>
    /// Discriminator used by the action card list to pick a DataTemplate. The
    /// XAML DataTemplateSelector keys off this enum.
    /// </summary>
    public enum SanitizerActionType
    {
        Sanitize,
        MissingProperty,
        SplitCollection,
    }

    /// <summary>
    /// Identifies which kind of database record an action targets.
    /// </summary>
    public enum SanitizerTargetKind
    {
        Game,
        Series,
        Company,
    }

    /// <summary>
    /// Base class for everything that can appear in the Library Sanitizer
    /// action list. Concrete subclasses describe the change being proposed and
    /// know how to commit it via <see cref="Apply"/>. The <see cref="Signature"/>
    /// is a stable string used to dedupe rebuilds and persist dismissals.
    /// </summary>
    public abstract class SanitizerAction : ObservableObject
    {
        public abstract SanitizerActionType Type { get; }

        /// <summary>
        /// Stable identifier composed of action kind + target id + change
        /// digest. Used by the engine to skip rebuilds the user previously
        /// dismissed and by the view as a binding key for cards.
        /// </summary>
        public abstract string Signature { get; }

        /// <summary>
        /// Headline shown in the card title bar -- usually the affected
        /// record's display name.
        /// </summary>
        public abstract string DisplayTitle { get; }

        /// <summary>
        /// Whether the user has expanded this card into edit mode. Cards
        /// use the flag to surface optional fields (e.g. SanitizeAction's
        /// sorting / version rows when the rule didn't propose anything for
        /// them) so the user can fill them in by hand.
        /// </summary>
        private bool isEditing;
        public bool IsEditing
        {
            get => isEditing;
            set
            {
                if (isEditing == value) return;
                isEditing = value;
                OnPropertyChanged();
                OnEditingChanged();
            }
        }

        /// <summary>
        /// Hook for subclasses that need to refresh dependent computed
        /// properties when <see cref="IsEditing"/> flips.
        /// </summary>
        protected virtual void OnEditingChanged() { }

        /// <summary>
        /// When this action targets a <see cref="Game"/>, returns that record
        /// so the sanitizer view can open the standard game edit window.
        /// </summary>
        public virtual Game GetTargetGame() => null;

        /// <summary>
        /// Commits the proposed change. Implementations are responsible for
        /// calling the appropriate IItemCollection.Update method so listeners
        /// (Library / Queue views) refresh.
        /// </summary>
        public abstract void Apply(IGameDatabaseMain database);
    }

    /// <summary>
    /// Proposes a name (and optional Version / SortingName) change for a
    /// single Game, Series, or Company record after running the sanitizer
    /// rule pipeline.
    /// </summary>
    public class SanitizeAction : SanitizerAction
    {
        public SanitizerTargetKind TargetKind { get; }
        public Guid TargetId { get; }
        public string OriginalName { get; }
        public string ProposedName { get; set; }
        public string ProposedSortingName { get; set; }
        public string ProposedVersion { get; set; }
        public IList<SanitizerRuleId> AppliedRules { get; }

        // Strong references to the record so Apply() can commit without a
        // second lookup. The engine is the only place these get constructed,
        // and the engine always pulls live records from the db.
        private readonly Game gameRef;
        private readonly Series seriesRef;
        private readonly Company companyRef;

        private SanitizeAction(
            SanitizerTargetKind kind,
            Guid id,
            string original,
            SanitizationResult result,
            Game gameRef,
            Series seriesRef,
            Company companyRef)
        {
            TargetKind = kind;
            TargetId = id;
            OriginalName = original ?? string.Empty;
            ProposedName = result.SanitizedName;
            ProposedSortingName = result.SortingNameOverride;
            ProposedVersion = result.SpilledVersion;
            AppliedRules = new List<SanitizerRuleId>(result.AppliedRules);
            this.gameRef = gameRef;
            this.seriesRef = seriesRef;
            this.companyRef = companyRef;
        }

        public static SanitizeAction ForGame(Game g, SanitizationResult r) =>
            new SanitizeAction(SanitizerTargetKind.Game, g.Id, g.Name, r, g, null, null);

        public static SanitizeAction ForSeries(Series s, SanitizationResult r) =>
            new SanitizeAction(SanitizerTargetKind.Series, s.Id, s.Name, r, null, s, null);

        public static SanitizeAction ForCompany(Company c, SanitizationResult r) =>
            new SanitizeAction(SanitizerTargetKind.Company, c.Id, c.Name, r, null, null, c);

        public override SanitizerActionType Type => SanitizerActionType.Sanitize;

        public override string DisplayTitle => OriginalName;

        public override Game GetTargetGame() =>
            TargetKind == SanitizerTargetKind.Game ? gameRef : null;

        /// <summary>
        /// Whether the SortingName row should be visible on the card.
        /// True if the rule already proposed one or the user has expanded
        /// the card to fill it in by hand.
        /// </summary>
        public bool ShowSortingName => IsEditing || !string.IsNullOrEmpty(ProposedSortingName);

        /// <summary>
        /// Whether the Version row should be visible on the card. Same
        /// rule as <see cref="ShowSortingName"/>.
        /// </summary>
        public bool ShowVersion => IsEditing || !string.IsNullOrEmpty(ProposedVersion);

        protected override void OnEditingChanged()
        {
            OnPropertyChanged(nameof(ShowSortingName));
            OnPropertyChanged(nameof(ShowVersion));
        }

        public override string Signature
        {
            get
            {
                var sb = new StringBuilder("Sanitize|");
                sb.Append(TargetKind).Append('|');
                sb.Append(TargetId).Append('|');
                sb.Append(ProposedName).Append('|');
                sb.Append(ProposedSortingName ?? string.Empty).Append('|');
                sb.Append(ProposedVersion ?? string.Empty);
                return sb.ToString();
            }
        }

        public override void Apply(IGameDatabaseMain database)
        {
            switch (TargetKind)
            {
                case SanitizerTargetKind.Game:
                    if (gameRef == null) return;
                    gameRef.Name = ProposedName;
                    if (!string.IsNullOrEmpty(ProposedSortingName))
                    {
                        gameRef.SortingName = ProposedSortingName;
                    }
                    if (!string.IsNullOrEmpty(ProposedVersion))
                    {
                        gameRef.Version = string.IsNullOrEmpty(gameRef.Version)
                            ? ProposedVersion
                            : gameRef.Version + " " + ProposedVersion;
                    }
                    database.Games.Update(gameRef);
                    break;
                case SanitizerTargetKind.Series:
                    if (seriesRef == null) return;
                    seriesRef.Name = ProposedName;
                    database.Series.Update(seriesRef);
                    break;
                case SanitizerTargetKind.Company:
                    if (companyRef == null) return;
                    companyRef.Name = ProposedName;
                    database.Companies.Update(companyRef);
                    break;
            }
        }
    }
}
