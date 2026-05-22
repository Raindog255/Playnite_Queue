using Playnite.Common;
using Playnite.Database;
using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Playnite.DesktopApp.Controls.Views
{
    public class QueuePriorityValueOption
    {
        public QueuePriorityValueOption(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public Guid Id { get; }
        public string Name { get; }
    }

    public class QueueBoolValueOption
    {
        public QueueBoolValueOption(bool? value, string name)
        {
            Value = value;
            Name = name;
        }

        public bool? Value { get; }
        public string Name { get; }
    }

    /// <summary>
    /// One terminal completion status row in the queue settings panel (display-only).
    /// </summary>
    public sealed class QueueTerminalStatusRow
    {
        public QueueTerminalStatusRow(Guid statusId, string displayName, ICommand removeCommand)
        {
            StatusId = statusId;
            DisplayName = displayName ?? string.Empty;
            RemoveCommand = removeCommand;
        }

        public Guid StatusId { get; }
        public string DisplayName { get; }
        public ICommand RemoveCommand { get; }
    }

    /// <summary>
    /// Priority rule list row: commands are per-row so DataTemplates avoid FindAncestor bindings.
    /// </summary>
    public sealed class QueuePriorityRuleRow
    {
        public QueuePriorityRuleRow(
            QueuePriorityRule rule,
            RelayCommand moveUp,
            RelayCommand moveDown,
            RelayCommand remove)
        {
            Rule = rule;
            MoveUpCommand = moveUp;
            MoveDownCommand = moveDown;
            RemoveCommand = remove;
        }

        public QueuePriorityRule Rule { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand RemoveCommand { get; }
    }

    [TemplatePart(Name = "PART_NewPriorityValueCombo", Type = typeof(ComboBox))]
    public class QueueSettingsPanel : Control, INotifyPropertyChanged
    {
        private const int MaxPriorityValuePickerResults = 200;

        private readonly DesktopAppViewModel mainModel;
        private ComboBox newPriorityValueCombo;
        private bool syncingPriorityValueCombo;

        private QueueSettings attachedQueueSettings;
        private ObservableCollection<Guid> subscribedTerminalIds;
        private ObservableCollection<QueuePriorityRule> subscribedPriorityRules;

        /// <summary>
        /// Pre-sorted priority value options per field (built once, filtered into the ComboBox).
        /// </summary>
        private readonly Dictionary<QueuePriorityField, IReadOnlyList<QueuePriorityValueOption>> priorityOptionsCache =
            new Dictionary<QueuePriorityField, IReadOnlyList<QueuePriorityValueOption>>();

        private bool priorityOptionsCacheValid;
        private GameDatabase subscribedPriorityCacheDatabase;
        private DispatcherTimer priorityFilterDebounceTimer;

        /// <summary>
        /// Guard against re-entrant refresh: clearing picker ItemsSource forces the playing-status
        /// ComboBox to write null back via TwoWay binding, which would otherwise recurse infinitely.
        /// </summary>
        private int pickerRefreshDepth;

        public QueueSettings Settings => mainModel?.AppSettings?.QueueSettings;

        public ObservableCollection<QueueDateSource> DateSources { get; } =
            new ObservableCollection<QueueDateSource>(Enum.GetValues(typeof(QueueDateSource)).Cast<QueueDateSource>());

        public ObservableCollection<QueuePriorityField> PriorityFields { get; } =
            new ObservableCollection<QueuePriorityField>(Enum.GetValues(typeof(QueuePriorityField)).Cast<QueuePriorityField>());

        private readonly ObservableCollection<CompletionStatus> masterCompletionStatuses =
            new ObservableCollection<CompletionStatus>();

        public ObservableCollection<CompletionStatus> PlayingStatusPickerItems { get; } =
            new ObservableCollection<CompletionStatus>();

        public ObservableCollection<CompletionStatus> TerminalAddPickerItems { get; } =
            new ObservableCollection<CompletionStatus>();

        public ObservableCollection<QueuePriorityValueOption> NewPriorityFilteredValueOptions { get; } =
            new ObservableCollection<QueuePriorityValueOption>();

        public ObservableCollection<QueueBoolValueOption> PriorityBoolPickOptions { get; } =
            new ObservableCollection<QueueBoolValueOption>
            {
                new QueueBoolValueOption(true, "Yes"),
                new QueueBoolValueOption(false, "No")
            };

        public ObservableCollection<QueueBoolValueOption> BoolValueOptions { get; } =
            new ObservableCollection<QueueBoolValueOption>
            {
                new QueueBoolValueOption(null, string.Empty),
                new QueueBoolValueOption(true, "True"),
                new QueueBoolValueOption(false, "False")
            };

        public ObservableCollection<QueueTerminalStatusRow> TerminalStatusRows { get; } =
            new ObservableCollection<QueueTerminalStatusRow>();

        public ObservableCollection<QueuePriorityRuleRow> PriorityRuleRows { get; } =
            new ObservableCollection<QueuePriorityRuleRow>();

        private Guid? newTerminalStatusId;
        public Guid? NewTerminalStatusId
        {
            get => newTerminalStatusId;
            set
            {
                newTerminalStatusId = value;
                OnPropertyChanged();
            }
        }

        private QueuePriorityField? newPriorityField;
        public QueuePriorityField? NewPriorityField
        {
            get => newPriorityField;
            set
            {
                if (newPriorityField == value)
                {
                    return;
                }

                newPriorityField = value;
                OnPropertyChanged();
                NewPrioritySelectedOption = null;
                NewPriorityValueId = null;
                NewPriorityBoolValue = null;
                SetNewPriorityValueSearchText(string.Empty, refreshFilter: true);
                OnPropertyChanged(nameof(ShowNewPriorityIdValueEditor));
                OnPropertyChanged(nameof(ShowNewPriorityBoolEditor));
                InvalidatePriorityAddCommand();
            }
        }

        private Guid? newPriorityValueId;
        public Guid? NewPriorityValueId
        {
            get => newPriorityValueId;
            set
            {
                newPriorityValueId = value;
                OnPropertyChanged();
                InvalidatePriorityAddCommand();
            }
        }

        private QueuePriorityValueOption newPrioritySelectedOption;
        public QueuePriorityValueOption NewPrioritySelectedOption
        {
            get => newPrioritySelectedOption;
            set
            {
                if (newPrioritySelectedOption == value)
                {
                    return;
                }

                newPrioritySelectedOption = value;
                NewPriorityValueId = value?.Id;
                OnPropertyChanged();
                InvalidatePriorityAddCommand();
            }
        }

        private bool? newPriorityBoolValue;
        public bool? NewPriorityBoolValue
        {
            get => newPriorityBoolValue;
            set
            {
                newPriorityBoolValue = value;
                OnPropertyChanged();
                InvalidatePriorityAddCommand();
            }
        }

        private string newPriorityValueSearchText;
        public string NewPriorityValueSearchText
        {
            get => newPriorityValueSearchText;
            set => SetNewPriorityValueSearchText(value, refreshFilter: true);
        }

        private void SetNewPriorityValueSearchText(string value, bool refreshFilter)
        {
            value = value ?? string.Empty;
            if (newPriorityValueSearchText == value)
            {
                return;
            }

            newPriorityValueSearchText = value;
            if (!syncingPriorityValueCombo &&
                newPrioritySelectedOption != null &&
                !string.Equals(newPrioritySelectedOption.Name, value, StringComparison.Ordinal))
            {
                NewPrioritySelectedOption = null;
            }

            OnPropertyChanged(nameof(NewPriorityValueSearchText));
            if (refreshFilter)
            {
                ScheduleApplyNewPriorityValueFilter();
            }
        }

        public bool ShowNewPriorityIdValueEditor =>
            NewPriorityField.HasValue && IsIdBasedPriorityField(NewPriorityField.Value);

        public bool ShowNewPriorityBoolEditor =>
            NewPriorityField.HasValue && !IsIdBasedPriorityField(NewPriorityField.Value);

        public RelayCommand AddPriorityRuleCommand { get; }
        public RelayCommand AddTerminalStatusCommand { get; }
        public RelayCommand<QueueTerminalStatusRow> RemoveTerminalStatusCommand { get; }

        public event PropertyChangedEventHandler PropertyChanged;

        static QueueSettingsPanel()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(QueueSettingsPanel),
                new FrameworkPropertyMetadata(typeof(QueueSettingsPanel)));
        }

        public QueueSettingsPanel() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public QueueSettingsPanel(DesktopAppViewModel mainModel)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.mainModel = DesignMainViewModel.DesignIntance;
            }
            else
            {
                this.mainModel = mainModel;
            }

            AddPriorityRuleCommand = new RelayCommand(AddPriorityRuleFromEditor, CanAddPriorityRuleFromEditor);
            AddTerminalStatusCommand = new RelayCommand(AddTerminalStatus, () => NewTerminalStatusId.HasValue);
            RemoveTerminalStatusCommand = new RelayCommand<QueueTerminalStatusRow>(RemoveTerminalStatus);

            Loaded += QueueSettingsPanel_Loaded;
            Unloaded += QueueSettingsPanel_Unloaded;

            LoadOptions();
            SyncTerminalRowsFromSettings();
        }

        private void QueueSettingsPanel_Loaded(object sender, RoutedEventArgs e)
        {
            AttachQueueSettingsHandlers();
            SubscribePriorityOptionsCacheInvalidation();
            if (mainModel?.Database?.IsOpen == true)
            {
                EnsurePriorityOptionsCache();
                RefreshStatusPickerLists();
            }
        }

        private void QueueSettingsPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            DetachQueueSettingsHandlers();
            UnsubscribePriorityOptionsCacheInvalidation();
            DetachNewPriorityValueComboHandlers();
            priorityFilterDebounceTimer?.Stop();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            DetachNewPriorityValueComboHandlers();
            newPriorityValueCombo = Template.FindName("PART_NewPriorityValueCombo", this) as ComboBox;
            if (newPriorityValueCombo != null && !DesignerProperties.GetIsInDesignMode(this))
            {
                newPriorityValueCombo.DropDownOpened += NewPriorityValueCombo_DropDownOpened;
                newPriorityValueCombo.SelectionChanged += NewPriorityValueCombo_SelectionChanged;
            }
        }

        private void DetachNewPriorityValueComboHandlers()
        {
            if (newPriorityValueCombo == null)
            {
                return;
            }

            newPriorityValueCombo.DropDownOpened -= NewPriorityValueCombo_DropDownOpened;
            newPriorityValueCombo.SelectionChanged -= NewPriorityValueCombo_SelectionChanged;
            newPriorityValueCombo = null;
        }

        private void NewPriorityValueCombo_DropDownOpened(object sender, EventArgs e)
        {
            ApplyNewPriorityValueFilter(listAllWhenSearchEmpty: true);
        }

        private void NewPriorityValueCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingPriorityValueCombo || newPriorityValueCombo == null)
            {
                return;
            }

            if (newPriorityValueCombo.SelectedItem is QueuePriorityValueOption opt)
            {
                syncingPriorityValueCombo = true;
                try
                {
                    NewPrioritySelectedOption = opt;
                    SetNewPriorityValueSearchText(opt.Name, refreshFilter: false);
                    newPriorityValueCombo.IsDropDownOpen = false;
                }
                finally
                {
                    syncingPriorityValueCombo = false;
                }
            }
        }

        private void AttachQueueSettingsHandlers()
        {
            DetachQueueSettingsHandlers();
            var s = Settings;
            if (s == null)
            {
                return;
            }

            attachedQueueSettings = s;
            s.PropertyChanged += QueueSettings_PropertyChanged;
            SubscribeTerminalIdsCollection(s.TerminalStatusIds);
            SubscribePriorityRulesCollection(s.PriorityRules);
        }

        private void DetachQueueSettingsHandlers()
        {
            UnsubscribeTerminalIdsCollection();
            UnsubscribePriorityRulesCollection();

            if (attachedQueueSettings != null)
            {
                attachedQueueSettings.PropertyChanged -= QueueSettings_PropertyChanged;
                attachedQueueSettings = null;
            }
        }

        private void SubscribeTerminalIdsCollection(ObservableCollection<Guid> coll)
        {
            UnsubscribeTerminalIdsCollection();
            if (coll == null)
            {
                return;
            }

            coll.CollectionChanged += TerminalStatusIds_CollectionChanged;
            subscribedTerminalIds = coll;
        }

        private void UnsubscribeTerminalIdsCollection()
        {
            if (subscribedTerminalIds != null)
            {
                subscribedTerminalIds.CollectionChanged -= TerminalStatusIds_CollectionChanged;
                subscribedTerminalIds = null;
            }
        }

        private void SubscribePriorityRulesCollection(ObservableCollection<QueuePriorityRule> coll)
        {
            UnsubscribePriorityRulesCollection();
            if (coll == null)
            {
                return;
            }

            coll.CollectionChanged += PriorityRules_CollectionChanged_SyncRows;
            subscribedPriorityRules = coll;
            RebuildPriorityRuleRows();
        }

        private void UnsubscribePriorityRulesCollection()
        {
            if (subscribedPriorityRules != null)
            {
                subscribedPriorityRules.CollectionChanged -= PriorityRules_CollectionChanged_SyncRows;
                subscribedPriorityRules = null;
            }

            PriorityRuleRows.Clear();
        }

        private void PriorityRules_CollectionChanged_SyncRows(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildPriorityRuleRows();
        }

        private void RebuildPriorityRuleRows()
        {
            PriorityRuleRows.Clear();
            if (subscribedPriorityRules == null)
            {
                return;
            }

            foreach (var rule in subscribedPriorityRules)
            {
                var r = rule;
                PriorityRuleRows.Add(new QueuePriorityRuleRow(
                    r,
                    new RelayCommand(() => MovePriorityRule(r, -1), () => CanMovePriorityRule(r, -1)),
                    new RelayCommand(() => MovePriorityRule(r, 1), () => CanMovePriorityRule(r, 1)),
                    new RelayCommand(() => RemovePriorityRule(r))));
            }
        }

        private bool CanMovePriorityRule(QueuePriorityRule rule, int offset)
        {
            if (rule == null || Settings?.PriorityRules == null)
            {
                return false;
            }

            var oldIndex = Settings.PriorityRules.IndexOf(rule);
            var newIndex = oldIndex + offset;
            return oldIndex >= 0 && newIndex >= 0 && newIndex < Settings.PriorityRules.Count;
        }

        private void QueueSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (pickerRefreshDepth > 0)
            {
                return;
            }

            if (e.PropertyName == nameof(QueueSettings.PlayingStatusId))
            {
                RefreshStatusPickerLists();
            }
            else if (e.PropertyName == nameof(QueueSettings.TerminalStatusIds))
            {
                var s = sender as QueueSettings;
                SubscribeTerminalIdsCollection(s?.TerminalStatusIds);
                SyncTerminalRowsFromSettings();
                ScheduleRefreshStatusPickerLists();
            }
            else if (e.PropertyName == nameof(QueueSettings.PriorityRules))
            {
                var s = sender as QueueSettings;
                SubscribePriorityRulesCollection(s?.PriorityRules);
            }
        }

        private void TerminalStatusIds_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncTerminalRowsFromSettings();
            ScheduleRefreshStatusPickerLists();
        }

        private void LoadOptions()
        {
            masterCompletionStatuses.Clear();
            InvalidatePriorityOptionsCache();

            var database = mainModel?.Database;
            if (database?.IsOpen == true)
            {
                database.CompletionStatuses.OrderBy(a => a.Name).ForEach(masterCompletionStatuses.Add);
                EnsurePriorityOptionsCache();
            }

            ApplyNewPriorityValueFilter();

            if (mainModel?.Database?.IsOpen == true)
            {
                RefreshStatusPickerLists();
            }
        }

        private static bool IsIdBasedPriorityField(QueuePriorityField field) =>
            field != QueuePriorityField.Free && field != QueuePriorityField.Mobile;

        private void InvalidatePriorityOptionsCache()
        {
            priorityOptionsCacheValid = false;
        }

        private void EnsurePriorityOptionsCache()
        {
            if (priorityOptionsCacheValid)
            {
                return;
            }

            var db = mainModel?.Database as GameDatabase;
            if (db?.IsOpen != true)
            {
                return;
            }

            RebuildPriorityOptionsCache(db);
            priorityOptionsCacheValid = true;
        }

        private void RebuildPriorityOptionsCache(GameDatabase db)
        {
            priorityOptionsCache.Clear();
            foreach (QueuePriorityField field in Enum.GetValues(typeof(QueuePriorityField)))
            {
                priorityOptionsCache[field] = Array.Empty<QueuePriorityValueOption>();
            }

            priorityOptionsCache[QueuePriorityField.Series] = BuildSortedPriorityOptions(
                db.UsedSeries.Select(id => db.Series.Get(id)));
            priorityOptionsCache[QueuePriorityField.Developer] = BuildSortedPriorityOptions(
                db.UsedDevelopers.Select(id => db.Companies.Get(id)));
            priorityOptionsCache[QueuePriorityField.Style] = BuildSortedPriorityOptions(
                db.UsedCategories.Select(id => db.Categories.Get(id)));
            priorityOptionsCache[QueuePriorityField.Genre] = BuildSortedPriorityOptions(
                db.UsedGenres.Select(id => db.Genres.Get(id)));
            priorityOptionsCache[QueuePriorityField.Library] = BuildLibraryPriorityOptions(
                mainModel.Extensions?.LibraryPlugins);
        }

        private static IReadOnlyList<QueuePriorityValueOption> BuildSortedPriorityOptions(
            IEnumerable<DatabaseObject> items)
        {
            if (items == null)
            {
                return Array.Empty<QueuePriorityValueOption>();
            }

            return items
                .Where(item => item != null)
                .OrderBy(item => item.Name)
                .Select(item => new QueuePriorityValueOption(item.Id, item.Name))
                .ToList();
        }

        private static IReadOnlyList<QueuePriorityValueOption> BuildLibraryPriorityOptions(
            IEnumerable<LibraryPlugin> libraryPlugins)
        {
            var libs = new List<LibraryPlugin>();
            if (libraryPlugins != null)
            {
                libs.AddRange(libraryPlugins);
            }

            libs.Add(new FakePlayniteLibraryPlugin());
            return libs
                .OrderBy(a => a.Name)
                .Select(plugin => new QueuePriorityValueOption(plugin.Id, plugin.Name))
                .ToList();
        }

        private void SubscribePriorityOptionsCacheInvalidation()
        {
            UnsubscribePriorityOptionsCacheInvalidation();
            var db = mainModel?.Database as GameDatabase;
            if (db == null)
            {
                return;
            }

            subscribedPriorityCacheDatabase = db;
            db.SeriesInUseUpdated += PriorityOptionsCache_InUseChanged;
            db.DevelopersInUseUpdated += PriorityOptionsCache_InUseChanged;
            db.CategoriesInUseUpdated += PriorityOptionsCache_InUseChanged;
            db.GenresInUseUpdated += PriorityOptionsCache_InUseChanged;
        }

        private void UnsubscribePriorityOptionsCacheInvalidation()
        {
            if (subscribedPriorityCacheDatabase == null)
            {
                return;
            }

            var db = subscribedPriorityCacheDatabase;
            db.SeriesInUseUpdated -= PriorityOptionsCache_InUseChanged;
            db.DevelopersInUseUpdated -= PriorityOptionsCache_InUseChanged;
            db.CategoriesInUseUpdated -= PriorityOptionsCache_InUseChanged;
            db.GenresInUseUpdated -= PriorityOptionsCache_InUseChanged;
            subscribedPriorityCacheDatabase = null;
        }

        private void PriorityOptionsCache_InUseChanged(object sender, EventArgs e) =>
            RefreshPriorityOptionsCache();

        private void RefreshPriorityOptionsCache()
        {
            InvalidatePriorityOptionsCache();
            if (mainModel?.Database?.IsOpen == true)
            {
                EnsurePriorityOptionsCache();
                ApplyNewPriorityValueFilter();
            }
        }

        private void ScheduleApplyNewPriorityValueFilter()
        {
            if (Dispatcher.HasShutdownStarted)
            {
                return;
            }

            if (priorityFilterDebounceTimer == null)
            {
                priorityFilterDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                priorityFilterDebounceTimer.Tick += (_, __) =>
                {
                    priorityFilterDebounceTimer.Stop();
                    ApplyNewPriorityValueFilter();
                    OpenPriorityValueDropDownWhenFiltering();
                };
            }

            priorityFilterDebounceTimer.Stop();
            priorityFilterDebounceTimer.Start();
        }

        /// <summary>
        /// Copies a capped slice from <see cref="priorityOptionsCache"/> into the bound ComboBox.
        /// When <paramref name="listAllWhenSearchEmpty"/> is true (dropdown opened), an empty query lists the first
        /// <see cref="MaxPriorityValuePickerResults"/> entries; otherwise an empty query yields no items until the user types.
        /// </summary>
        private void ApplyNewPriorityValueFilter(bool listAllWhenSearchEmpty = false)
        {
            NewPriorityFilteredValueOptions.Clear();
            if (!NewPriorityField.HasValue || !IsIdBasedPriorityField(NewPriorityField.Value))
            {
                return;
            }

            EnsurePriorityOptionsCache();
            if (!priorityOptionsCache.TryGetValue(NewPriorityField.Value, out var full) || full.Count == 0)
            {
                return;
            }

            var q = (NewPriorityValueSearchText ?? string.Empty).Trim();
            IEnumerable<QueuePriorityValueOption> matches;
            if (q.Length == 0)
            {
                matches = listAllWhenSearchEmpty ? full : Enumerable.Empty<QueuePriorityValueOption>();
            }
            else
            {
                matches = full.Where(opt => opt.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var results = matches.Take(MaxPriorityValuePickerResults).ToList();
            if (NewPrioritySelectedOption != null && results.All(opt => opt.Id != NewPrioritySelectedOption.Id))
            {
                results.Insert(0, NewPrioritySelectedOption);
            }

            foreach (var opt in results)
            {
                NewPriorityFilteredValueOptions.Add(opt);
            }
        }

        private void OpenPriorityValueDropDownWhenFiltering()
        {
            if (newPriorityValueCombo == null)
            {
                return;
            }

            var q = (NewPriorityValueSearchText ?? string.Empty).Trim();
            if (q.Length > 0 && NewPriorityFilteredValueOptions.Count > 0)
            {
                newPriorityValueCombo.IsDropDownOpen = true;
            }
        }

        private void InvalidatePriorityAddCommand() =>
            CommandManager.InvalidateRequerySuggested();

        private bool CanAddPriorityRuleFromEditor()
        {
            if (Settings == null || !NewPriorityField.HasValue)
            {
                return false;
            }

            var field = NewPriorityField.Value;
            if (IsIdBasedPriorityField(field))
            {
                return NewPrioritySelectedOption != null;
            }

            return NewPriorityBoolValue.HasValue;
        }

        private void AddPriorityRuleFromEditor()
        {
            if (!CanAddPriorityRuleFromEditor())
            {
                return;
            }

            var field = NewPriorityField.Value;
            var rule = new QueuePriorityRule
            {
                Field = field,
                Order = Settings.PriorityRules.Count
            };

            if (IsIdBasedPriorityField(field))
            {
                rule.ValueId = NewPrioritySelectedOption.Id;
                rule.BoolValue = null;
            }
            else
            {
                rule.BoolValue = NewPriorityBoolValue.Value;
                rule.ValueId = null;
            }

            Settings.PriorityRules.Add(rule);
            UpdateRuleOrder();
            ResetNewPriorityEditor();
        }

        private void ResetNewPriorityEditor()
        {
            NewPriorityField = null;
            NewPrioritySelectedOption = null;
            NewPriorityValueId = null;
            NewPriorityBoolValue = null;
            SetNewPriorityValueSearchText(string.Empty, refreshFilter: false);
            NewPriorityFilteredValueOptions.Clear();
            if (newPriorityValueCombo != null)
            {
                newPriorityValueCombo.IsDropDownOpen = false;
            }
            OnPropertyChanged(nameof(ShowNewPriorityIdValueEditor));
            OnPropertyChanged(nameof(ShowNewPriorityBoolEditor));
            InvalidatePriorityAddCommand();
        }

        private void ScheduleRefreshStatusPickerLists()
        {
            if (Dispatcher.HasShutdownStarted || mainModel?.Database?.IsOpen != true)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (mainModel?.Database?.IsOpen == true)
                {
                    RefreshStatusPickerLists();
                }
            }), DispatcherPriority.DataBind);
        }

        /// <summary>
        /// Playing picker: all statuses except those used as terminal (always includes current playing pick so the selection stays visible).
        /// Terminal add picker: statuses not used as playing and not already terminal.
        /// </summary>
        private void RefreshStatusPickerLists()
        {
            if (Settings == null || mainModel?.Database?.IsOpen != true)
            {
                return;
            }

            pickerRefreshDepth++;
            try
            {
                var savedPlaying = Settings.PlayingStatusId;

                PlayingStatusPickerItems.Clear();
                TerminalAddPickerItems.Clear();

                var terminalSet = new HashSet<Guid>(Settings.TerminalStatusIds ?? Enumerable.Empty<Guid>());

                foreach (var s in masterCompletionStatuses)
                {
                    if (!terminalSet.Contains(s.Id) || (savedPlaying.HasValue && s.Id == savedPlaying.Value))
                    {
                        PlayingStatusPickerItems.Add(s);
                    }
                }

                EnsurePlayingStatusInPicker(savedPlaying);

                foreach (var s in masterCompletionStatuses)
                {
                    var excludedByPlaying = savedPlaying.HasValue && s.Id == savedPlaying.Value;
                    var excludedByTerminal = terminalSet.Contains(s.Id);
                    if (!excludedByPlaying && !excludedByTerminal)
                    {
                        TerminalAddPickerItems.Add(s);
                    }
                }

                if (!Nullable.Equals(Settings.PlayingStatusId, savedPlaying))
                {
                    Settings.PlayingStatusId = savedPlaying;
                }

                if (!NewTerminalStatusId.HasValue ||
                    !TerminalAddPickerItems.Any(x => x.Id == NewTerminalStatusId.Value))
                {
                    NewTerminalStatusId = TerminalAddPickerItems.FirstOrDefault()?.Id;
                }

                OnPropertyChanged(nameof(PlayingStatusPickerItems));
                OnPropertyChanged(nameof(TerminalAddPickerItems));
            }
            finally
            {
                pickerRefreshDepth--;
            }
        }

        /// <summary>
        /// Keeps the playing ComboBox bindable when the status exists in the DB but was filtered out of the main loop.
        /// </summary>
        private void EnsurePlayingStatusInPicker(Guid? playingId)
        {
            if (!playingId.HasValue || playingId.Value == Guid.Empty)
            {
                return;
            }

            if (PlayingStatusPickerItems.Any(x => x.Id == playingId.Value))
            {
                return;
            }

            var db = mainModel?.Database?.CompletionStatuses;
            var status = db?.Get(playingId.Value);
            if (status != null)
            {
                PlayingStatusPickerItems.Insert(0, status);
            }
        }

        private void SyncTerminalRowsFromSettings()
        {
            TerminalStatusRows.Clear();
            foreach (var id in Settings?.TerminalStatusIds ?? Enumerable.Empty<Guid>())
            {
                TerminalStatusRows.Add(CreateTerminalRow(id));
            }
        }

        private QueueTerminalStatusRow CreateTerminalRow(Guid statusId)
        {
            var cs = mainModel?.Database?.CompletionStatuses?.Get(statusId);
            return new QueueTerminalStatusRow(statusId, cs?.Name ?? statusId.ToString(), RemoveTerminalStatusCommand);
        }

        private void RemovePriorityRule(QueuePriorityRule rule)
        {
            if (rule == null || Settings == null)
            {
                return;
            }

            Settings.PriorityRules.Remove(rule);
            UpdateRuleOrder();
        }

        private void MovePriorityRule(QueuePriorityRule rule, int offset)
        {
            if (rule == null || Settings == null)
            {
                return;
            }

            var oldIndex = Settings.PriorityRules.IndexOf(rule);
            var newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= Settings.PriorityRules.Count)
            {
                return;
            }

            Settings.PriorityRules.Move(oldIndex, newIndex);
            UpdateRuleOrder();
        }

        private void UpdateRuleOrder()
        {
            if (Settings == null)
            {
                return;
            }

            for (var i = 0; i < Settings.PriorityRules.Count; i++)
            {
                Settings.PriorityRules[i].Order = i;
            }
        }

        private void AddTerminalStatus()
        {
            if (!NewTerminalStatusId.HasValue || Settings == null)
            {
                return;
            }

            if (Settings.TerminalStatusIds.Contains(NewTerminalStatusId.Value))
            {
                return;
            }

            Settings.TerminalStatusIds.Add(NewTerminalStatusId.Value);
        }

        private void RemoveTerminalStatus(QueueTerminalStatusRow row)
        {
            if (row == null || Settings == null)
            {
                return;
            }

            Settings.TerminalStatusIds.Remove(row.StatusId);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
