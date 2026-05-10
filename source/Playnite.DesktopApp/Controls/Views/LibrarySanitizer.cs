using Playnite.DesktopApp.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls.Views
{
    /// <summary>
    /// Library Sanitizer sidebar view. Hosts a <see cref="LibrarySanitizerViewModel"/>
    /// that watches the database + sanitizer settings and rebuilds the
    /// action card list. The XAML template binds an ItemsControl to
    /// <c>LibrarySanitizerViewModel.Actions</c> and routes per-card
    /// commands (Apply/Dismiss/ToggleEdit) back through the view model.
    /// </summary>
    public class LibrarySanitizer : Control
    {
        private readonly DesktopAppViewModel mainModel;
        private LibrarySanitizerViewModel viewModel;

        static LibrarySanitizer()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LibrarySanitizer),
                new FrameworkPropertyMetadata(typeof(LibrarySanitizer)));
        }

        public LibrarySanitizer() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public LibrarySanitizer(DesktopAppViewModel mainModel)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.mainModel = DesignMainViewModel.DesignIntance;
            }
            else if (mainModel != null)
            {
                this.mainModel = mainModel;
            }

            // Match the Queue view: defer building the view model until the
            // view is loaded (so the database has been opened) but reuse the
            // same instance across navigations to avoid re-subscribing on
            // every sidebar switch.
            Loaded += LibrarySanitizer_Loaded;
        }

        private void LibrarySanitizer_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this) || mainModel == null) return;

            if (viewModel == null)
            {
                viewModel = new LibrarySanitizerViewModel(mainModel);
                DataContext = viewModel;
            }
            else
            {
                viewModel.Refresh();
            }
        }
    }
}
