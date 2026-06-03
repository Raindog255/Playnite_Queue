using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Playnite
{
    /// <summary>
    /// Persistent configuration for the Library Sanitizer view. Drives which
    /// action sources run, which sanitization rules are enabled, which fields
    /// are considered "required" for queue readiness (Add Missing Properties),
    /// and which keywords trigger Split Collection detection. Dismissed action
    /// signatures are stored here so the user can clear them in one place.
    /// </summary>
    public class LibrarySanitizerSettings : ObservableObject
    {
        // ---------------- Action sources ----------------

        private bool sanitizeEnabled = true;
        public bool SanitizeEnabled
        {
            get => sanitizeEnabled;
            set { sanitizeEnabled = value; OnPropertyChanged(); }
        }

        private bool missingPropertiesEnabled = true;
        public bool MissingPropertiesEnabled
        {
            get => missingPropertiesEnabled;
            set { missingPropertiesEnabled = value; OnPropertyChanged(); }
        }

        private bool splitCollectionEnabled = true;
        public bool SplitCollectionEnabled
        {
            get => splitCollectionEnabled;
            set { splitCollectionEnabled = value; OnPropertyChanged(); }
        }

        private bool removeUnusedEnabled = true;
        public bool RemoveUnusedEnabled
        {
            get => removeUnusedEnabled;
            set { removeUnusedEnabled = value; OnPropertyChanged(); }
        }

        // ---------------- Remove unused scope ----------------

        private bool removeUnusedSeries = true;
        public bool RemoveUnusedSeries
        {
            get => removeUnusedSeries;
            set { removeUnusedSeries = value; OnPropertyChanged(); }
        }

        private bool removeUnusedGenres = true;
        public bool RemoveUnusedGenres
        {
            get => removeUnusedGenres;
            set { removeUnusedGenres = value; OnPropertyChanged(); }
        }

        private bool removeUnusedTags = true;
        public bool RemoveUnusedTags
        {
            get => removeUnusedTags;
            set { removeUnusedTags = value; OnPropertyChanged(); }
        }

        private bool removeUnusedDevelopers = true;
        public bool RemoveUnusedDevelopers
        {
            get => removeUnusedDevelopers;
            set { removeUnusedDevelopers = value; OnPropertyChanged(); }
        }

        private bool removeUnusedPublishers = true;
        public bool RemoveUnusedPublishers
        {
            get => removeUnusedPublishers;
            set { removeUnusedPublishers = value; OnPropertyChanged(); }
        }

        // ---------------- Sanitize scope ----------------

        private bool sanitizeGames = true;
        public bool SanitizeGames
        {
            get => sanitizeGames;
            set { sanitizeGames = value; OnPropertyChanged(); }
        }

        private bool sanitizeSeries = true;
        public bool SanitizeSeries
        {
            get => sanitizeSeries;
            set { sanitizeSeries = value; OnPropertyChanged(); }
        }

        private bool sanitizeDevelopers = true;
        public bool SanitizeDevelopers
        {
            get => sanitizeDevelopers;
            set { sanitizeDevelopers = value; OnPropertyChanged(); }
        }

        private bool sanitizePublishers = true;
        public bool SanitizePublishers
        {
            get => sanitizePublishers;
            set { sanitizePublishers = value; OnPropertyChanged(); }
        }

        // ---------------- Sanitize rule toggles ----------------

        private bool whitespaceRule = true;
        public bool WhitespaceRule
        {
            get => whitespaceRule;
            set { whitespaceRule = value; OnPropertyChanged(); }
        }

        private bool smartPunctuationRule = true;
        public bool SmartPunctuationRule
        {
            get => smartPunctuationRule;
            set { smartPunctuationRule = value; OnPropertyChanged(); }
        }

        private bool trademarkSymbolsRule = true;
        public bool TrademarkSymbolsRule
        {
            get => trademarkSymbolsRule;
            set { trademarkSymbolsRule = value; OnPropertyChanged(); }
        }

        private bool separatorRule = true;
        public bool SeparatorRule
        {
            get => separatorRule;
            set { separatorRule = value; OnPropertyChanged(); }
        }

        private bool numeralsRule = true;
        public bool NumeralsRule
        {
            get => numeralsRule;
            set { numeralsRule = value; OnPropertyChanged(); }
        }

        private bool editionSpilloverRule = true;
        public bool EditionSpilloverRule
        {
            get => editionSpilloverRule;
            set { editionSpilloverRule = value; OnPropertyChanged(); }
        }

        // Off by default. Users who track editions tagged in the title (e.g.
        // "Football Manager 2024") would lose useful information.
        private bool yearSuffixRule;
        public bool YearSuffixRule
        {
            get => yearSuffixRule;
            set { yearSuffixRule = value; OnPropertyChanged(); }
        }

        private bool htmlEntitiesRule = true;
        public bool HtmlEntitiesRule
        {
            get => htmlEntitiesRule;
            set { htmlEntitiesRule = value; OnPropertyChanged(); }
        }

        // Off by default; flipping ampersand-to-and is a stylistic call.
        private bool ampersandToAndRule;
        public bool AmpersandToAndRule
        {
            get => ampersandToAndRule;
            set { ampersandToAndRule = value; OnPropertyChanged(); }
        }

        private bool titleCaseRule = true;
        public bool TitleCaseRule
        {
            get => titleCaseRule;
            set { titleCaseRule = value; OnPropertyChanged(); }
        }

        private ObservableCollection<string> editionKeywords = new ObservableCollection<string>(DefaultEditionKeywords);
        public ObservableCollection<string> EditionKeywords
        {
            get => editionKeywords;
            set { editionKeywords = value ?? new ObservableCollection<string>(); OnPropertyChanged(); }
        }

        // ---------------- Missing properties: required-field checklist ----------------

        private bool requireAcquiredDate = true;
        public bool RequireAcquiredDate
        {
            get => requireAcquiredDate;
            set { requireAcquiredDate = value; OnPropertyChanged(); }
        }

        private bool requireReleaseDate = true;
        public bool RequireReleaseDate
        {
            get => requireReleaseDate;
            set { requireReleaseDate = value; OnPropertyChanged(); }
        }

        private bool requireCompletionStatus = true;
        public bool RequireCompletionStatus
        {
            get => requireCompletionStatus;
            set { requireCompletionStatus = value; OnPropertyChanged(); }
        }

        // Off by default -- not all games belong to a series.
        private bool requireSeries;
        public bool RequireSeries
        {
            get => requireSeries;
            set { requireSeries = value; OnPropertyChanged(); }
        }

        private bool requireDevelopers = true;
        public bool RequireDevelopers
        {
            get => requireDevelopers;
            set { requireDevelopers = value; OnPropertyChanged(); }
        }

        // Off by default -- categories ("Style") are user-curated and many users
        // don't classify every game.
        private bool requireCategories;
        public bool RequireCategories
        {
            get => requireCategories;
            set { requireCategories = value; OnPropertyChanged(); }
        }

        private bool requireGenres = true;
        public bool RequireGenres
        {
            get => requireGenres;
            set { requireGenres = value; OnPropertyChanged(); }
        }

        // ---------------- Split Collection ----------------

        private ObservableCollection<string> collectionKeywords = new ObservableCollection<string>(DefaultCollectionKeywords);
        public ObservableCollection<string> CollectionKeywords
        {
            get => collectionKeywords;
            set { collectionKeywords = value ?? new ObservableCollection<string>(); OnPropertyChanged(); }
        }

        // When on, also flag titles like "X / Y", "X + Y", "X & Y", "X 1 & 2", "X I-II".
        private bool detectMultiTitlePatterns = true;
        public bool DetectMultiTitlePatterns
        {
            get => detectMultiTitlePatterns;
            set { detectMultiTitlePatterns = value; OnPropertyChanged(); }
        }

        // ---------------- Dismissals ----------------

        // Persisted as a flat list of action signatures. The engine consults
        // this set when materializing actions and skips any that match.
        private ObservableCollection<string> dismissedActionSignatures = new ObservableCollection<string>();
        public ObservableCollection<string> DismissedActionSignatures
        {
            get => dismissedActionSignatures;
            set { dismissedActionSignatures = value ?? new ObservableCollection<string>(); OnPropertyChanged(); }
        }

        // ---------------- Defaults ----------------

        private static readonly string[] DefaultEditionKeywords =
        {
            "Game of the Year Edition",
            "Game of the Year",
            "GOTY Edition",
            "GOTY",
            "Definitive Edition",
            "Ultimate Edition",
            "Deluxe Edition",
            "Complete Edition",
            "Enhanced Edition",
            "Special Edition",
            "Collector's Edition",
            "Anniversary Edition",
            "Director's Cut",
            "Remastered Edition",
            "Remastered",
            "Anthology",
        };

        private static readonly string[] DefaultCollectionKeywords =
        {
            "Collection",
            "Trilogy",
            "Anthology",
            "Bundle",
            "Pack",
            "Saga",
            "Compilation",
        };
    }
}
