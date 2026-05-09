using Playnite.DesktopApp.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;

namespace Playnite.DesktopApp.Controls.Views
{
    /// <summary>
    /// Sidebar view stub for the Queue. Gets fleshed out in the queue_view_control
    /// feature with a QueueViewModel + GameItemTemplate-backed list. For now it
    /// exists so the sidebar entry can be wired up and exercised end-to-end.
    /// </summary>
    public class Queue : Control
    {
        private readonly DesktopAppViewModel mainModel;

        static Queue()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Queue), new System.Windows.FrameworkPropertyMetadata(typeof(Queue)));
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
        }
    }
}
