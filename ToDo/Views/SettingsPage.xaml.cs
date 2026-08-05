using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Views;

public partial class SettingsPage : UserControl
{
    /// <summary>Nav section -> its realized container, used to map scroll positions.</summary>
    private readonly List<(SettingsSection Section, FrameworkElement Element)> _sectionElements = new();

    /// <summary>
    /// Guards against feedback loops between nav-click scrolling and scroll-driven
    /// selection updates: any selection/scroll change we make under our own control
    /// must be wrapped with this flag set, so the reciprocal handler ignores it.
    /// </summary>
    private bool _suppressScrollSync;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        var owner = Application.Current.MainWindow;
        var dialog = new Dialogs.TagManageDialog { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>About 区主页链接：用系统默认浏览器打开。</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("about", $"failed to open homepage: {ex.Message}");
        }
        e.Handled = true;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureSectionElements();
    }

    /// <summary>
    /// Snapshots the section -> container mapping. The ItemsControl isn't virtualized,
    /// so every container exists once the page is laid out; rebuild only when the
    /// cache no longer matches the VM's sections.
    /// </summary>
    private void EnsureSectionElements()
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (_sectionElements.Count == vm.Sections.Count &&
            _sectionElements.All(x => vm.Sections.Contains(x.Section)))
        {
            return;
        }

        _sectionElements.Clear();
        foreach (var section in vm.Sections)
        {
            if (SectionsPanel.ItemContainerGenerator.ContainerFromItem(section) is FrameworkElement container)
            {
                _sectionElements.Add((section, container));
            }
        }
    }

    /// <summary>Nav selection changed (click or keyboard): scroll the right pane to it.</summary>
    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScrollSync) return;
        if (ViewModel?.SelectedSection is { } section)
        {
            ScrollToSection(section);
        }
    }

    /// <summary>
    /// Also scroll when the already-selected nav item is clicked again — selection
    /// doesn't change in that case, so SelectionChanged won't fire.
    /// </summary>
    private void Nav_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressScrollSync) return;
        if (e.OriginalSource is DependencyObject source &&
            SettingsNav.ContainerFromElement(source) is ListBoxItem item &&
            item.DataContext is SettingsSection section)
        {
            ScrollToSection(section);
        }
    }

    /// <summary>Scroll-spy: keep the nav highlight in sync with the section on screen.</summary>
    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollSync || !IsLoaded) return;
        EnsureSectionElements();

        var viewportTop = SettingsScrollViewer.VerticalOffset;
        SettingsSection? active = null;
        foreach (var (section, element) in _sectionElements)
        {
            // Sections are laid out top-down, so the last one whose top edge is at
            // or above the viewport top is the one currently anchoring the screen.
            if (GetContentTop(element) <= viewportTop + 1)
            {
                active = section;
            }
            else
            {
                break;
            }
        }

        var vm = ViewModel;
        if (active != null && vm != null && !ReferenceEquals(active, vm.SelectedSection))
        {
            _suppressScrollSync = true;
            try
            {
                vm.SelectedSection = active;
            }
            finally
            {
                _suppressScrollSync = false;
            }
        }
    }

    private void ScrollToSection(SettingsSection section)
    {
        if (!IsLoaded) return;
        EnsureSectionElements();

        var element = _sectionElements.FirstOrDefault(x => ReferenceEquals(x.Section, section)).Element;
        if (element == null) return;

        _suppressScrollSync = true;
        try
        {
            SettingsScrollViewer.ScrollToVerticalOffset(GetContentTop(element));
        }
        finally
        {
            _suppressScrollSync = false;
        }
    }

    /// <summary>Y of the element's top edge in the scrollable content's coordinate space.</summary>
    private double GetContentTop(FrameworkElement element)
    {
        var inViewport = element.TransformToAncestor(SettingsScrollViewer).Transform(new Point(0, 0)).Y;
        return inViewport + SettingsScrollViewer.VerticalOffset;
    }
}
