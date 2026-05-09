using Playnite.DesktopApp.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls.Views
{
    /// <summary>
    /// Queue sidebar view. Hosts a <see cref="QueueViewModel"/> that recomputes the
    /// visible queue collection whenever the database or queue settings change. The
    /// XAML template binds an ItemsControl to <c>QueueViewModel.QueueGames</c> and
    /// renders each entry as a <c>GameListItem</c> (same styling as the library grid).
    /// </summary>
    public class Queue : Control
    {
        private readonly DesktopAppViewModel mainModel;
        private QueueViewModel viewModel;

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

        private void Queue_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this) || mainModel == null)
            {
                return;
            }

            if (viewModel == null)
            {
                viewModel = new QueueViewModel(mainModel);
                DataContext = viewModel;
            }
        }

        private void Queue_Unloaded(object sender, RoutedEventArgs e)
        {
            // The control is reused as the user switches sidebar views; we keep the
            // view model alive across navigations so subscriptions don't churn.
        }
    }
}
