using System.Windows;
using System.Windows.Input;
using RssReader.App.ViewModels;
using RssReader.Domain;

namespace RssReader.App;

public partial class ProfileChooserWindow : Window
{
    private readonly ProfileChooserViewModel _viewModel;
    private bool _shiftRevealActive;

    public ProfileChooserWindow(ProfileChooserViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && !_shiftRevealActive)
        {
            _shiftRevealActive = true;
            _viewModel.IsDeleteModeActive = true;
            await UpdateCatalogMasterVisibilityAsync(true);
        }
    }

    private async void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift &&
            !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            _shiftRevealActive = false;
            _viewModel.IsDeleteModeActive = false;
            await UpdateCatalogMasterVisibilityAsync(false);
        }
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_shiftRevealActive)
        {
            _shiftRevealActive = false;
            _viewModel.IsDeleteModeActive = false;
            await UpdateCatalogMasterVisibilityAsync(false);
        }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Profile profile } || profile.IsCatalogMaster)
        {
            return;
        }

        var result = MessageDialogWindow.Show(
            this,
            $"Delete the profile \"{profile.Name}\"? Its subscriptions, folders, and reading state will be removed from this device. This action cannot be undone.",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _viewModel.DeleteProfileAsync(profile);
        }
        catch (Exception)
        {
            MessageDialogWindow.Show(
                this,
                "The profile could not be deleted.",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task UpdateCatalogMasterVisibilityAsync(bool revealed)
    {
        try
        {
            await _viewModel.SetCatalogMasterRevealedAsync(revealed);
        }
        catch (Exception)
        {
            MessageDialogWindow.Show(
                this,
                "Profiles could not be loaded.",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CreatePasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.CreatePassword = CreatePasswordBox.Password;

    private void UnlockPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.UnlockPassword = UnlockPasswordBox.Password;

    private void CancelCreate_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelCreateCommand.Execute(null);
        CreatePasswordBox.Clear();
    }

    private void CancelUnlock_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CancelUnlockCommand.Execute(null);
        UnlockPasswordBox.Clear();
    }
}