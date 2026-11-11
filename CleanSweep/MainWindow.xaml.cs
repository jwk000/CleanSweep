using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CleanSweep.ViewModels;

namespace CleanSweep
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.InitializeAsync();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                MaxHeight = SystemParameters.WorkArea.Height;
                MaxWidth = SystemParameters.WorkArea.Width;
                if (MaximizeGlyph != null)
                {
                    MaximizeGlyph.Text = "\uE923";
                    MaximizeButton.ToolTip = "还原";
                }
            }
            else
            {
                MaxHeight = double.PositiveInfinity;
                MaxWidth = double.PositiveInfinity;
                if (MaximizeGlyph != null)
                {
                    MaximizeGlyph.Text = "\uE922";
                    MaximizeButton.ToolTip = "最大化";
                }
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_viewModel.IsBusy)
            {
                MessageBox.Show("任务正在进行中。请等待完成，或先点击“取消”。", "净界 CleanSweep",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                e.Cancel = true;
                return;
            }

            _viewModel.Dispose();
        }
    }
}
