using Playnite.DesktopApp.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls.Views
{
    public class QueueImportReview : Control
    {
        private readonly DesktopAppViewModel mainModel;
        private QueueImportReviewViewModel viewModel;

        static QueueImportReview()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(QueueImportReview),
                new FrameworkPropertyMetadata(typeof(QueueImportReview)));
        }

        public QueueImportReview() : this(DesktopApplication.Current?.MainModel)
        {
        }

        public QueueImportReview(DesktopAppViewModel mainModel)
        {
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                this.mainModel = DesignMainViewModel.DesignIntance;
            }
            else
            {
                this.mainModel = mainModel;
            }

            Loaded += QueueImportReview_Loaded;
        }

        private void QueueImportReview_Loaded(object sender, RoutedEventArgs e)
        {
            if (DesignerProperties.GetIsInDesignMode(this) || mainModel == null) return;

            if (viewModel == null)
            {
                viewModel = new QueueImportReviewViewModel(mainModel);
                DataContext = viewModel;
            }
            else
            {
                viewModel.Refresh();
            }
        }
    }
}
