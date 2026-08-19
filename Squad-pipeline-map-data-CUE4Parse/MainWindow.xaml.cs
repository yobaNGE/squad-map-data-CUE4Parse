using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Squad_pipeline_map_data_CUE4Parse.Presentation;

namespace Squad_pipeline_map_data_CUE4Parse;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        _viewModel.ErrorOccurred += OnErrorOccurred;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();

    private async void BrowseSquad_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Squad installation directory",
            InitialDirectory = Directory.Exists(_viewModel.SquadPath) ? _viewModel.SquadPath : null
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.SetSquadPathAsync(dialog.FolderName);
    }

    private void BrowseMappings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select mappings for the installed Squad version",
            Filter = "Unreal mappings (*.usmap)|*.usmap|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) _viewModel.MappingsPath = dialog.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the JSON export directory",
            InitialDirectory = Directory.Exists(_viewModel.OutputDirectory) ? _viewModel.OutputDirectory : null
        };
        if (dialog.ShowDialog(this) == true) _viewModel.OutputDirectory = dialog.FolderName;
    }

    private void SaveSelection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save layer selection",
            Filter = "Layer selection (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = "squad-layer-selection.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) _viewModel.SaveSelection(dialog.FileName);
    }

    private void LoadSelection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load layer selection",
            Filter = "Layer selection (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) _viewModel.LoadSelection(dialog.FileName);
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_viewModel.OutputDirectory))
        {
            MessageBox.Show(this, "The output directory does not exist yet.", "Squad Pipeline",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(_viewModel.OutputDirectory) { UseShellExecute = true });
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ContentSourceSettingsViewModel source) return;
        var confirmation = MessageBox.Show(this,
            $"Delete the cached catalog and exported metadata for {source.Name}? You can rebuild it later.",
            "Clear cache", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation == MessageBoxResult.Yes) _viewModel.ClearCache(source);
    }

    private void OnErrorOccurred(object? sender, string message) =>
        MessageBox.Show(this, message, "Squad Pipeline", MessageBoxButton.OK, MessageBoxImage.Error);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _viewModel.ErrorOccurred -= OnErrorOccurred;
        _viewModel.Dispose();
    }
}
