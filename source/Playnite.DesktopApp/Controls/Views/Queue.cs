using Playnite.Common;
using Playnite.Controls;
using Playnite.DesktopApp.ViewModels;
using Playnite.SDK;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Playnite.DesktopApp.Controls.Views
{
    /// <summary>
    /// Queue sidebar view. Hosts a <see cref="QueueViewModel"/> that recomputes the
    /// visible queue collection whenever the database or queue settings change. The
    /// XAML template binds an ExtendedListBox to <c>QueueViewModel.QueueGames</c> and
    /// renders each entry as a <c>GameListItem</c> (same styling as the library grid).
    /// </summary>
    [TemplatePart(Name = "PART_ListGames", Type = typeof(ExtendedListBox))]
    public class Queue : Control
    {
        private readonly DesktopAppViewModel mainModel;
        private QueueViewModel viewModel;
        private ExtendedListBox listGames;

        /// <summary>
        /// Binds the queue list to <see cref="QueueViewModel"/> before <see cref="FrameworkElement.DataContext"/>
        /// is assigned on <see cref="Queue_Loaded"/>, so list/empty state never reads the wrong data context
        /// (inherited <see cref="DesktopAppViewModel"/>) during initial measure.
        /// </summary>
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel),
            typeof(QueueViewModel),
            typeof(Queue),
            new PropertyMetadata(null));

        public QueueViewModel ViewModel => (QueueViewModel)GetValue(ViewModelProperty);

        static Queue()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Queue), new FrameworkPropertyMetadata(typeof(Queue)));
        }

        public Queue() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public Queue(DesktopAppViewModel mainModel)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.mainModel = DesignMainViewModel.DesignIntance;
            }
            else if (mainModel != null)
            {
                this.mainModel = mainModel;
            }

            // Defer building the QueueViewModel until the database is open. The view
            // is constructed in LoadSideBarItems before the database opens, so binding
            // immediately would skip change events fired during the initial load.
            Loaded += Queue_Loaded;
            Unloaded += Queue_Unloaded;
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            listGames = Template.FindName("PART_ListGames", this) as ExtendedListBox;
            if (listGames != null && !DesignerProperties.GetIsInDesignMode(this) && mainModel != null)
            {
                // Same selection plumbing the Library uses: a left-click in the queue
                // selects the tile, which sets mainModel.SelectedGames so the
                // GameListItem context menu (bound to SelectedGames) edits the right game.
                BindingTools.SetBinding(listGames,
                    ExtendedListBox.SelectedItemsListProperty,
                    mainModel,
                    nameof(DesktopAppViewModel.SelectedGamesBinder),
                    BindingMode.TwoWay);

                // Standard ListBox does not select on right-click, so without this the
                // context menu would still operate on whatever was last selected
                // elsewhere (typically the Library's selection) instead of the tile
                // under the cursor.
                listGames.PreviewMouseRightButtonDown += ListGames_PreviewMouseRightButtonDown;
            }
        }

        private void ListGames_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item == null || item.IsSelected)
            {
                return;
            }

            // Only steal selection when the click is on a tile that isn't already
            // part of the current selection; otherwise multi-select right-clicks
            // would collapse to a single item.
            listGames.SelectedItems.Clear();
            item.IsSelected = true;
        }

        private static T FindAncestor<T>(DependencyObject from) where T : DependencyObject
        {
            while (from != null)
            {
                if (from is T match)
                {
                    return match;
                }

                from = VisualTreeHelper.GetParent(from);
            }

            return null;
        }

        private void Queue_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this) || mainModel == null)
            {
                return;
            }

            if (viewModel == null)
            {
                viewModel = new QueueViewModel(mainModel);
            }
            else
            {
                viewModel.Refresh();
            }

            SetCurrentValue(ViewModelProperty, viewModel);
            DataContext = viewModel;
        }

        private void Queue_Unloaded(object sender, RoutedEventArgs e)
        {
            // The control is reused as the user switches sidebar views; we keep the
            // view model alive across navigations so subscriptions don't churn.
        }
    }
}
