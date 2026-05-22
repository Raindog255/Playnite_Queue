using Playnite.Database;
using Playnite.LibrarySanitizer;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Playnite.DesktopApp.ViewModels
{
    public class QueueImportGameMatch
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Library { get; set; }
        public Game Game { get; set; }
        public string DisplayName => string.IsNullOrEmpty(Library) ? Name : $"{Name} ({Library})";
    }

    public class QueueImportReviewRowViewModel : ObservableObject
    {
        private readonly IGameDatabaseMain database;
        private readonly LibrarySanitizerSettings sanitizerSettings;
        private readonly Action save;

        public QueueImportStagedRow Row { get; }
        public RelayCommand UseMatchCommand { get; }
        public RelayCommand CreateNewGameCommand { get; }
        public RelayCommand DismissCommand { get; }

        public ObservableCollection<QueueImportGameMatch> SearchResults { get; } =
            new ObservableCollection<QueueImportGameMatch>();

        private string searchText;
        public string SearchText
        {
            get => searchText;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(searchText, next, StringComparison.Ordinal))
                {
                    return;
                }

                searchText = next;
                if (SelectedMatch != null &&
                    !string.Equals(SelectedMatch.DisplayName, searchText, StringComparison.Ordinal))
                {
                    selectedMatch = null;
                    OnPropertyChanged(nameof(SelectedMatch));
                    OnPropertyChanged(nameof(HasSelectedMatch));
                }

                OnPropertyChanged();
                RefreshSearchResults();
            }
        }

        private QueueImportGameMatch selectedMatch;
        public QueueImportGameMatch SelectedMatch
        {
            get => selectedMatch;
            set
            {
                if (selectedMatch == value)
                {
                    return;
                }

                selectedMatch = value;
                if (selectedMatch != null &&
                    !string.Equals(searchText, selectedMatch.DisplayName, StringComparison.Ordinal))
                {
                    searchText = selectedMatch.DisplayName;
                    OnPropertyChanged(nameof(SearchText));
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedMatch));
            }
        }

        public string Name => Row.Name;
        public string OriginalName => Row.OriginalName;
        public int RowNumber => Row.RowNumber;
        public bool HasMatch => Row.MatchedGameIds?.Count > 0;
        public string MatchSummary => HasMatch
            ? $"{Row.MatchedGameIds.Count} matched game(s)"
            : "No matching game";

        public string SanitizedName
        {
            get
            {
                var result = SanitizerRules.Apply(Name, SanitizerScope.GameName, sanitizerSettings);
                return result.SanitizedName;
            }
        }

        public bool HasSanitizeProposal =>
            !string.Equals(Name, SanitizedName, StringComparison.Ordinal);
        public bool HasSelectedMatch => SelectedMatch != null;

        public QueueImportReviewRowViewModel(
            QueueImportStagedRow row,
            IGameDatabaseMain database,
            LibrarySanitizerSettings sanitizerSettings,
            Action save,
            Action<QueueImportReviewRowViewModel> dismiss)
        {
            Row = row;
            this.database = database;
            this.sanitizerSettings = sanitizerSettings;
            this.save = save;
            UseMatchCommand = new RelayCommand(UseMatch, () => HasSelectedMatch);
            CreateNewGameCommand = new RelayCommand(CreateNewGame, () => !HasMatch);
            DismissCommand = new RelayCommand(() => dismiss?.Invoke(this));

            // When the inbound name has no library match but the sanitized form
            // does, prefill the manual-match search so the candidate list is
            // already populated when the user opens the card. Saves a typing
            // step in the common "import row needs a sanitization-aware match"
            // case while leaving the inbound CSV value untouched.
            TryPrefillSearchFromSanitized();
        }

        private void UseMatch()
        {
            if (SelectedMatch?.Game == null) return;
            QueueImportStaging.SetManualMatch(Row, SelectedMatch.Game, database);
            RefreshComputed();
            save?.Invoke();
        }

        private void CreateNewGame()
        {
            if (HasMatch) return;
            QueueImportStaging.CreateGameForRow(Row, database);
            RefreshComputed();
            save?.Invoke();
        }

        private void TryPrefillSearchFromSanitized()
        {
            if (HasMatch) return;
            if (!HasSanitizeProposal) return;

            var sanitized = SanitizedName;
            if (string.IsNullOrWhiteSpace(sanitized)) return;

            var matches = QueueImportStaging.FindMatches(sanitized, database);
            if (matches.Count == 0) return;

            SearchText = sanitized;
            SelectedMatch = SearchResults.FirstOrDefault(m =>
                string.Equals(m.Name, sanitized, StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshSearchResults()
        {
            SearchResults.Clear();
            if (database == null || string.IsNullOrWhiteSpace(SearchText))
            {
                return;
            }

            var text = SearchText.Trim();
            foreach (var game in database.Games
                .Where(g => g?.Name?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(g => g.Name)
                .Take(12))
            {
                SearchResults.Add(new QueueImportGameMatch
                {
                    Id = game.Id,
                    Name = game.Name,
                    Library = game.Source?.Name,
                    Game = game
                });
            }
        }

        public void RefreshComputed()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(HasMatch));
            OnPropertyChanged(nameof(MatchSummary));
            OnPropertyChanged(nameof(SanitizedName));
            OnPropertyChanged(nameof(HasSanitizeProposal));
            OnPropertyChanged(nameof(HasSelectedMatch));
        }
    }

    public class QueueImportReviewViewModel : ObservableObject
    {
        private readonly DesktopAppViewModel mainModel;
        private readonly IGameDatabaseMain database;
        private QueueImportStagingFile staging;

        public ObservableCollection<QueueImportReviewRowViewModel> Rows { get; } =
            new ObservableCollection<QueueImportReviewRowViewModel>();

        public RelayCommand RefreshCommand { get; }
        public RelayCommand CommitCommand { get; }
        public RelayCommand DiscardCommand { get; }

        public QueueImportReviewViewModel(DesktopAppViewModel mainModel)
        {
            this.mainModel = mainModel ?? throw new ArgumentNullException(nameof(mainModel));
            database = mainModel.Database;
            RefreshCommand = new RelayCommand(Refresh);
            CommitCommand = new RelayCommand(Commit, () => StagedCount > 0);
            DiscardCommand = new RelayCommand(Discard, () => StagedCount > 0);
            Refresh();
        }

        public int ReviewCount => Rows.Count;
        public int StagedCount => staging?.Rows?.Count(r => !r.Dismissed) ?? 0;
        public int CleanCount => Math.Max(0, StagedCount - ReviewCount);
        public bool IsEmpty => StagedCount == 0;
        public string Summary => $"{CleanCount} clean row(s), {ReviewCount} needing review";

        public void Refresh()
        {
            staging = QueueImportStaging.Load();
            Rows.Clear();

            if (staging?.Rows != null)
            {
                foreach (var row in staging.Rows.Where(r => !r.Dismissed))
                {
                    QueueImportStaging.RefreshMatches(row, database);
                    // A row only needs review if matching couldn't pin it to a
                    // library game. Sanitization differences are surfaced as
                    // read-only context inside those review cards (and as a
                    // search prefill), but never push an otherwise-clean match
                    // into the queue.
                    if (row.MatchedGameIds.Count == 0)
                    {
                        Rows.Add(new QueueImportReviewRowViewModel(
                            row,
                            database,
                            mainModel.AppSettings.LibrarySanitizerSettings,
                            SaveAndRefresh,
                            Dismiss));
                    }
                }
                Save();
            }

            RaiseCounts();
        }

        private void Save()
        {
            QueueImportStaging.Save(staging);
        }

        private void SaveAndRefresh()
        {
            Save();
            Refresh();
        }

        private void Dismiss(QueueImportReviewRowViewModel row)
        {
            if (row == null) return;
            row.Row.Dismissed = true;
            Rows.Remove(row);
            Save();
            RaiseCounts();
        }

        private void Commit()
        {
            var result = QueueImportStaging.Commit(staging, database);
            foreach (var msg in result.Messages)
            {
                MainViewModelBase.Logger.Info($"[QueueImportStaging] {msg}");
            }

            var summary = $"Updated {result.Updated} game(s). Matched {result.Matched}, not found {result.NotFound}, errors {result.Errors}.";
            mainModel.Dialogs.ShowMessage(summary, "Queue Import Review", MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }

        private void Discard()
        {
            if (mainModel.Dialogs.ShowMessage(
                    "Discard the staged queue import?",
                    "Queue Import Review",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            QueueImportStaging.Delete();
            Refresh();
        }

        private void RaiseCounts()
        {
            OnPropertyChanged(nameof(ReviewCount));
            OnPropertyChanged(nameof(StagedCount));
            OnPropertyChanged(nameof(CleanCount));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(Summary));
        }
    }
}
