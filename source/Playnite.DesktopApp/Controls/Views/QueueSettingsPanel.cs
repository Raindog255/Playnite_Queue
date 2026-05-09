using Playnite.Common;
using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

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

    public class QueueTerminalStatusSelection : ObservableObject
    {
        private Guid statusId;
        public Guid StatusId
        {
            get => statusId;
            set
            {
                statusId = value;
                OnPropertyChanged();
            }
        }
    }

    public class QueueSettingsPanel : Control, INotifyPropertyChanged
    {
        private readonly DesktopAppViewModel mainModel;

        public QueueSettings Settings => mainModel?.AppSettings?.QueueSettings;

        public ObservableCollection<QueueDateSource> DateSources { get; } =
            new ObservableCollection<QueueDateSource>(Enum.GetValues(typeof(QueueDateSource)).Cast<QueueDateSource>());

        public ObservableCollection<QueuePriorityField> PriorityFields { get; } =
            new ObservableCollection<QueuePriorityField>(Enum.GetValues(typeof(QueuePriorityField)).Cast<QueuePriorityField>());

        public ObservableCollection<CompletionStatus> CompletionStatuses { get; } = new ObservableCollection<CompletionStatus>();

        public ObservableCollection<QueuePriorityValueOption> PriorityValueOptions { get; } =
            new ObservableCollection<QueuePriorityValueOption>();

        public ObservableCollection<QueueBoolValueOption> BoolValueOptions { get; } =
            new ObservableCollection<QueueBoolValueOption>
            {
                new QueueBoolValueOption(null, string.Empty),
                new QueueBoolValueOption(true, "True"),
                new QueueBoolValueOption(false, "False")
            };

        public ObservableCollection<QueueTerminalStatusSelection> TerminalStatusSelections { get; } =
            new ObservableCollection<QueueTerminalStatusSelection>();

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

        public RelayCommand AddPriorityRuleCommand { get; }
        public RelayCommand<QueuePriorityRule> RemovePriorityRuleCommand { get; }
        public RelayCommand<QueuePriorityRule> MovePriorityRuleUpCommand { get; }
        public RelayCommand<QueuePriorityRule> MovePriorityRuleDownCommand { get; }
        public RelayCommand AddTerminalStatusCommand { get; }
        public RelayCommand<QueueTerminalStatusSelection> RemoveTerminalStatusCommand { get; }

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

            AddPriorityRuleCommand = new RelayCommand(AddPriorityRule);
            RemovePriorityRuleCommand = new RelayCommand<QueuePriorityRule>(RemovePriorityRule);
            MovePriorityRuleUpCommand = new RelayCommand<QueuePriorityRule>(MovePriorityRuleUp);
            MovePriorityRuleDownCommand = new RelayCommand<QueuePriorityRule>(MovePriorityRuleDown);
            AddTerminalStatusCommand = new RelayCommand(AddTerminalStatus, () => NewTerminalStatusId.HasValue);
            RemoveTerminalStatusCommand = new RelayCommand<QueueTerminalStatusSelection>(RemoveTerminalStatus);

            LoadOptions();
            LoadTerminalStatusSelections();
        }

        private void LoadOptions()
        {
            CompletionStatuses.Clear();
            PriorityValueOptions.Clear();

            var database = mainModel?.Database;
            if (database?.IsOpen == true)
            {
                database.CompletionStatuses.OrderBy(a => a.Name).ForEach(CompletionStatuses.Add);
                AddPriorityValues(QueuePriorityField.Series, database.Series);
                AddPriorityValues(QueuePriorityField.Developer, database.Companies);
                AddLibraryPriorityValues(mainModel.Extensions?.LibraryPlugins);
                AddPriorityValues(QueuePriorityField.Style, database.Categories);
                AddPriorityValues(QueuePriorityField.Genre, database.Genres);
            }

            if (CompletionStatuses.Count > 0 && NewTerminalStatusId == null)
            {
                NewTerminalStatusId = CompletionStatuses.First().Id;
            }
        }

        private void AddPriorityValues<T>(QueuePriorityField field, IEnumerable<T> items)
            where T : DatabaseObject
        {
            if (items == null)
            {
                return;
            }

            foreach (var item in items.OrderBy(a => a.Name))
            {
                PriorityValueOptions.Add(new QueuePriorityValueOption(item.Id, $"{field}: {item.Name}"));
            }
        }

        private void AddLibraryPriorityValues(IEnumerable<LibraryPlugin> libraryPlugins)
        {
            var libs = new List<LibraryPlugin>();
            if (libraryPlugins != null)
            {
                libs.AddRange(libraryPlugins.OrderBy(a => a.Name));
            }

            libs.Add(new FakePlayniteLibraryPlugin());
            foreach (var plugin in libs.OrderBy(a => a.Name))
            {
                PriorityValueOptions.Add(new QueuePriorityValueOption(plugin.Id, $"{QueuePriorityField.Library}: {plugin.Name}"));
            }
        }

        private void LoadTerminalStatusSelections()
        {
            TerminalStatusSelections.Clear();
            foreach (var id in Settings?.TerminalStatusIds ?? new ObservableCollection<Guid>())
            {
                AddTerminalSelection(id);
            }
        }

        private void AddPriorityRule()
        {
            if (Settings == null)
            {
                return;
            }

            Settings.PriorityRules.Add(new QueuePriorityRule
            {
                Field = QueuePriorityField.Series,
                Order = Settings.PriorityRules.Count
            });
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

        private void MovePriorityRuleUp(QueuePriorityRule rule)
        {
            MovePriorityRule(rule, -1);
        }

        private void MovePriorityRuleDown(QueuePriorityRule rule)
        {
            MovePriorityRule(rule, 1);
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
            AddTerminalSelection(NewTerminalStatusId.Value);
        }

        private void AddTerminalSelection(Guid statusId)
        {
            var item = new QueueTerminalStatusSelection { StatusId = statusId };
            item.PropertyChanged += TerminalStatusSelection_PropertyChanged;
            TerminalStatusSelections.Add(item);
        }

        private void RemoveTerminalStatus(QueueTerminalStatusSelection item)
        {
            if (item == null || Settings == null)
            {
                return;
            }

            item.PropertyChanged -= TerminalStatusSelection_PropertyChanged;
            TerminalStatusSelections.Remove(item);
            Settings.TerminalStatusIds.Remove(item.StatusId);
        }

        private void TerminalStatusSelection_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(QueueTerminalStatusSelection.StatusId) || Settings == null)
            {
                return;
            }

            Settings.TerminalStatusIds.Clear();
            foreach (var item in TerminalStatusSelections.Select(a => a.StatusId).Distinct())
            {
                Settings.TerminalStatusIds.Add(item);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
