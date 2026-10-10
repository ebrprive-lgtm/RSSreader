using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class SidebarView : UserControl
{
    private bool _isPeekOpen;

    public SidebarView()
    {
        InitializeComponent();
    }

    public event Action? LogoutRequested;

    public event Action? PreferencesRequested;

    public void UpdatePresentation()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var sidebarActionName = viewModel.IsSidebarPinned ? "Hide sidebar" : "Pin sidebar";
        SidebarPinButton.ToolTip = sidebarActionName;
        AutomationProperties.SetName(SidebarPinButton, sidebarActionName);
        SidebarPeekButton.ToolTip = sidebarActionName;
        AutomationProperties.SetName(SidebarPeekButton, sidebarActionName);
        if (viewModel.IsSidebarPinned)
        {
            _isPeekOpen = false;
            SidebarPeekButton.Visibility = Visibility.Collapsed;
            SidebarPanel.Visibility = Visibility.Visible;
            SetSidebarOffset(0, animate: false);
        }
        else
        {
            SidebarPeekButton.Visibility = Visibility.Visible;
            if (!_isPeekOpen)
            {
                SidebarPanel.Visibility = Visibility.Collapsed;
                SetSidebarOffset(-viewModel.SidebarPanelWidth, animate: false);
            }
        }
    }

    private void SidebarPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ShowSidebarPeek();

    private void SidebarPeekButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => ShowSidebarPeek();

    private void SidebarLinkRoot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Grid { DataContext: SidebarLink link } row &&
            (link.IsFolder || link.IsFeedEntry || !string.IsNullOrWhiteSpace(link.Count)))
        {
            row.Tag = true;
        }
    }

    private void SidebarLinkRoot_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Grid { DataContext: SidebarLink link } row &&
            (link.IsFolder || link.IsFeedEntry || !string.IsNullOrWhiteSpace(link.Count)))
        {
            row.Tag = false;
        }
    }

    private void SidebarPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleSidebarPeekClose();

    private void SidebarPeekButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ScheduleSidebarPeekClose();

    private void ShowSidebarPeek()
    {
        if (DataContext is not MainWindowViewModel { IsSidebarPinned: false } viewModel)
        {
            return;
        }

        var wasCollapsed = SidebarPanel.Visibility == Visibility.Collapsed;
        _isPeekOpen = true;
        if (wasCollapsed)
        {
            SetSidebarOffset(-viewModel.SidebarPanelWidth, animate: false);
        }

        SidebarPanel.Visibility = Visibility.Visible;
        SetSidebarOffset(0, animate: true);
    }

    private void ScheduleSidebarPeekClose()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (DataContext is not MainWindowViewModel { IsSidebarPinned: false } viewModel ||
                SidebarPanel.IsMouseOver ||
                SidebarPeekButton.IsMouseOver)
            {
                return;
            }

            _isPeekOpen = false;
            SidebarPeekButton.ToolTip = "Show sidebar";
            var animation = CreateSidebarAnimation(-viewModel.SidebarPanelWidth);
            animation.Completed += (_, _) =>
            {
                if (!_isPeekOpen && !viewModel.IsSidebarPinned)
                {
                    SidebarPanel.Visibility = Visibility.Collapsed;
                }
            };
            ((TranslateTransform)SidebarPanel.RenderTransform).BeginAnimation(TranslateTransform.XProperty, animation);
        }));
    }

    private void SetSidebarOffset(double offset, bool animate)
    {
        var transform = (TranslateTransform)SidebarPanel.RenderTransform;
        if (animate)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, CreateSidebarAnimation(offset));
            return;
        }

        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = offset;
    }

    private static DoubleAnimation CreateSidebarAnimation(double offset) => new(offset, TimeSpan.FromMilliseconds(180))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private async void ManageFeedTags_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not FrameworkElement { DataContext: SidebarLink { IsFeedEntry: true } feedLink })
        {
            return;
        }

        var feedId = feedLink.Route["feed:".Length..];
        try
        {
            var tagData = await viewModel.GetFeedTagEditorDataAsync(feedId);
            var dialog = new FeedTagEditorWindow(feedLink.Label, tagData.AvailableTags, tagData.AssignedTags)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() == true)
            {
                await viewModel.UpdateFeedTagsAsync(feedId, dialog.SelectedTagNames);
            }
        }
        catch (Exception exception)
        {
            MessageDialogWindow.Show(
                Window.GetWindow(this),
                exception.Message,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void PreferencesMenuItem_Click(object sender, RoutedEventArgs e) => PreferencesRequested?.Invoke();

    private void LogoutMenuItem_Click(object sender, RoutedEventArgs e) => LogoutRequested?.Invoke();
}
