using System.Windows;
using System.Windows.Input;
using RssReader.App.ViewModels;

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

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && !_shiftRevealActive)
        {
            _shiftRevealActive = true;
            await UpdateCatalogMasterVisibilityAsync(true);
        }
    }

    private async void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift &&
            !Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
        {
            _shiftRevealActive = false;
            await UpdateCatalogMasterVisibilityAsync(false);
        }
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_shiftRevealActive)
        {
            _shiftRevealActive = false;
            await UpdateCatalogMasterVisibilityAsync(false);
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
            System.Windows.MessageBox.Show(
                "Profiles could not be loaded.",
                "RSS Reader",
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