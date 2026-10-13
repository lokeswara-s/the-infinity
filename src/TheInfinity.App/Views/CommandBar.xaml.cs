using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheInfinity.App.ViewModels;

namespace TheInfinity.App.Views;

public partial class CommandBar : UserControl
{
    public CommandBar()
    {
        InitializeComponent();
    }

    public void FocusSearch() => SearchBox.Focus();

    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel vm)
            return;
        if (e.Key == Key.Enter && vm.SearchResults.Count > 0)
        {
            vm.Activate(vm.SearchResults[0].Id);
            vm.SearchText = "";
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.SearchText = "";
            e.Handled = true;
        }
    }

    private void ResultActivated(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShellViewModel vm && sender is ListBox { SelectedItem: WindowItemViewModel item })
        {
            vm.Activate(item.Id);
            vm.SearchText = "";
        }
    }

    private void ResultKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ShellViewModel vm && sender is ListBox { SelectedItem: WindowItemViewModel item })
        {
            vm.Activate(item.Id);
            vm.SearchText = "";
        }
    }

    private DateTime _layoutsClosedAt;

    private async void LayoutsClick(object sender, RoutedEventArgs e)
    {
        // Clicking the button while the popup is open first closes it (click outside); don't reopen it straight away.
        if (DateTime.UtcNow - _layoutsClosedAt < TimeSpan.FromMilliseconds(250) || DataContext is not ShellViewModel vm)
            return;
        await vm.ReloadLayoutsAsync();
        LayoutsPopup.IsOpen = true;
    }

    private void LayoutsClosed(object? sender, EventArgs e) => _layoutsClosedAt = DateTime.UtcNow;

    private void LayoutNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ShellViewModel vm)
        {
            vm.SaveLayoutCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void LayoutRowClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ShellViewModel vm && sender is ListBoxItem { DataContext: LayoutItemViewModel layout })
        {
            LayoutsPopup.IsOpen = false;
            vm.ApplyLayoutCommand.Execute(layout.Id);
        }
    }

    private void LayoutDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel vm && sender is Button { DataContext: LayoutItemViewModel layout })
            vm.DeleteLayoutCommand.Execute(layout.Id);
    }
}