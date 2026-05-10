using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls.Views
{
    /// <summary>
    /// Right-side settings panel for the Library Sanitizer view. Mirrors the
    /// Queue settings panel pattern: exposes the bound
    /// <see cref="LibrarySanitizerSettings"/> plus a small command surface
    /// for managing the editable string lists (edition keywords, collection
    /// keywords) and triggering a one-shot rebuild via
    /// <see cref="RefreshCommand"/>.
    /// </summary>
    public class LibrarySanitizerSettingsPanel : Control, INotifyPropertyChanged
    {
        private readonly DesktopAppViewModel mainModel;

        public LibrarySanitizerSettings Settings => mainModel?.AppSettings?.LibrarySanitizerSettings;

        private string newEditionKeyword;
        public string NewEditionKeyword
        {
            get => newEditionKeyword;
            set { newEditionKeyword = value; OnPropertyChanged(); }
        }

        private string newCollectionKeyword;
        public string NewCollectionKeyword
        {
            get => newCollectionKeyword;
            set { newCollectionKeyword = value; OnPropertyChanged(); }
        }

        public RelayCommand AddEditionKeywordCommand { get; }
        public RelayCommand<string> RemoveEditionKeywordCommand { get; }
        public RelayCommand AddCollectionKeywordCommand { get; }
        public RelayCommand<string> RemoveCollectionKeywordCommand { get; }
        public RelayCommand RestoreDismissedCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        static LibrarySanitizerSettingsPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LibrarySanitizerSettingsPanel),
                new FrameworkPropertyMetadata(typeof(LibrarySanitizerSettingsPanel)));
        }

        public LibrarySanitizerSettingsPanel() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public LibrarySanitizerSettingsPanel(DesktopAppViewModel mainModel)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.mainModel = DesignMainViewModel.DesignIntance;
            }
            else
            {
                this.mainModel = mainModel;
            }

            AddEditionKeywordCommand = new RelayCommand(AddEditionKeyword,
                () => !string.IsNullOrWhiteSpace(NewEditionKeyword));
            RemoveEditionKeywordCommand = new RelayCommand<string>(RemoveEditionKeyword);
            AddCollectionKeywordCommand = new RelayCommand(AddCollectionKeyword,
                () => !string.IsNullOrWhiteSpace(NewCollectionKeyword));
            RemoveCollectionKeywordCommand = new RelayCommand<string>(RemoveCollectionKeyword);
            RestoreDismissedCommand = new RelayCommand(RestoreDismissed,
                () => Settings?.DismissedActionSignatures?.Count > 0);
        }

        private void AddEditionKeyword()
        {
            var keyword = (NewEditionKeyword ?? string.Empty).Trim();
            if (keyword.Length == 0 || Settings?.EditionKeywords == null) return;
            // Case-insensitive dedupe so adding "GOTY" twice is a no-op.
            if (Settings.EditionKeywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase))) return;
            Settings.EditionKeywords.Add(keyword);
            NewEditionKeyword = string.Empty;
        }

        private void RemoveEditionKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword) || Settings?.EditionKeywords == null) return;
            Settings.EditionKeywords.Remove(keyword);
        }

        private void AddCollectionKeyword()
        {
            var keyword = (NewCollectionKeyword ?? string.Empty).Trim();
            if (keyword.Length == 0 || Settings?.CollectionKeywords == null) return;
            if (Settings.CollectionKeywords.Any(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase))) return;
            Settings.CollectionKeywords.Add(keyword);
            NewCollectionKeyword = string.Empty;
        }

        private void RemoveCollectionKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword) || Settings?.CollectionKeywords == null) return;
            Settings.CollectionKeywords.Remove(keyword);
        }

        private void RestoreDismissed()
        {
            Settings?.DismissedActionSignatures?.Clear();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
