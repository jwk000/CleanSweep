using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CleanSweep.Common;
using CleanSweep.Models;

namespace CleanSweep.Views
{
    public partial class PathsWindow : Window
    {
        public PathsWindow(FindingItem item)
        {
            InitializeComponent();
            DataContext = this;
            Paths = new ObservableCollection<string>(item.Paths ?? new System.Collections.Generic.List<string>());
            Header = item.Title;
            SubHeader = item.Basis;
            CountText = "共 " + Format.Count(Paths.Count, "个路径") + "；这些是审计与清理范围中的原始路径。";
        }

        public ObservableCollection<string> Paths { get; private set; }
        public string Header { get; private set; }
        public string SubHeader { get; private set; }
        public string CountText { get; private set; }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, Paths));
            }
            catch
            {
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
