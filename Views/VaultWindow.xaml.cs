using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Effortless.Models;
using Effortless.Services;

namespace Effortless.Views;

public partial class VaultWindow : System.Windows.Window
{
    private readonly ScratchPadWindow _scratchPad;

    public VaultWindow(ScratchPadWindow scratchPad)
    {
        _scratchPad = scratchPad;
        InitializeComponent();
        VaultPathText.Text = StorageService.GetVaultFolder();
        RefreshThoughts();
    }

    private VaultThought? Selected => ThoughtList.SelectedItem as VaultThought;

    public void RefreshThoughts()
    {
        var previousId = Selected?.Id;
        var thoughts = StorageService.LoadVaultThoughts();

        ThoughtList.ItemsSource = thoughts;
        CountText.Text = thoughts.Count == 0
            ? string.Empty
            : $"· {thoughts.Count} thought{(thoughts.Count == 1 ? "" : "s")}";
        EmptyHint.Visibility = thoughts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Re-select the previously selected thought if it still exists.
        if (previousId is { } id)
        {
            var match = thoughts.FirstOrDefault(t => t.Id == id);
            if (match != null)
                ThoughtList.SelectedItem = match;
        }

        UpdateDetail();
    }

    private void ThoughtList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetail();
    }

    private void ThoughtList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected != null)
            LoadSelected();
    }

    // WPF's default ListBox wheel-scroll can stall when the ItemsPanel's
    // virtualizer thinks it's at the boundary even though the inner
    // ScrollViewer isn't. Walk the visual tree to the real ScrollViewer and
    // drive it directly, which scrolls reliably by pixels.
    private void ThoughtList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindScrollViewer(ThoughtList) is { } sv)
        {
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            var nested = FindScrollViewer(child);
            if (nested != null) return nested;
        }
        return null;
    }

    private void UpdateDetail()
    {
        var thought = Selected;
        var hasSelection = thought != null;

        LoadButton.IsEnabled = hasSelection;
        OpenMdButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;

        DetailTitle.Text = thought?.Title ?? string.Empty;
        DetailBody.Text = thought?.Content ?? string.Empty;

        if (thought == null)
        {
            DetailMeta.Text = string.Empty;
            return;
        }

        var meta = thought.DisplayDate;
        if (thought.Tags.Count > 0)
            meta += "   " + string.Join("  ", thought.Tags.Select(t => $"#{t}"));
        DetailMeta.Text = meta;
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (Selected is { } thought)
        {
            _scratchPad.LoadThoughtIntoPad(thought);
        }
    }

    private void OpenMdButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } thought)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = thought.FilePath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore - file may have been moved/deleted externally
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = StorageService.GetVaultFolder(),
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    private AskClaudeWindow? _askWindow;

    private void AskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_askWindow != null)
        {
            _askWindow.Close();
            _askWindow = null;
        }

        StorageService.EnsureVaultReadme();
        StorageService.RegenerateVaultIndex();

        _askWindow = new AskClaudeWindow("vault", StorageService.GetVaultFolder());
        _askWindow.Closed += (_, _) => _askWindow = null;
        _askWindow.Show();
        _askWindow.Activate();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } thought)
            return;

        var result = System.Windows.MessageBox.Show(
            $"Delete \"{thought.Title}\"?\n\nThis permanently removes the .md file from the vault.",
            "Delete thought",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.OK)
        {
            StorageService.DeleteThought(thought);
            RefreshThoughts();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // Hide instead of close so the app (OnExplicitShutdown) keeps running.
        e.Cancel = true;
        Hide();
    }
}
