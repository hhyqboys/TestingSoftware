using System;
using System.Windows;
using System.Windows.Controls;

namespace TestingSoftware.View
{
    /// <summary>
    /// Interaction logic for PidView.xaml
    /// </summary>
    public partial class PidView : UserControl
    {
        public PidView()
        {
            InitializeComponent();
        }

        private void PidLoaded(object sender, RoutedEventArgs e)
        {
            /* Trigger binding update. */
            var dataContext = DataContext;
            DataContext = null;
            DataContext = dataContext;
        }
    }
}