using System.Collections.ObjectModel;
using RssReader.App.Commands;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.App.ViewModels;

public sealed class ProfileChooserViewModel : ObservableObject
{
    private readonly ProfileService _profileService;
    private bool _isCreatingProfile;
    private bool _isUnlockingProfile;
    private bool _isCatalogMasterRevealed;
    private Profile? _profileToUnlock;
    private string _profileName = string.Empty;
    private string _createPassword = string.Empty;
    private string _recoveryEmail = string.Empty;
    private string _unlockPassword = string.Empty;
    private string _errorMessage = string.Empty;

    public ProfileChooserViewModel(ProfileService profileService)
    {
        _profileService = profileService;
        OpenProfileCommand = new AsyncCommand<Profile>(OpenProfileAsync);
        CreateProfileCommand = new RelayCommand(BeginCreateProfile);
        CancelCreateCommand = new RelayCommand(CancelCreateProfile);
        SubmitCreateProfileCommand = new AsyncCommand(SubmitCreateProfileAsync);
        ConfirmUnlockCommand = new AsyncCommand(ConfirmUnlockAsync);
        CancelUnlockCommand = new RelayCommand(CancelUnlock);
    }

    public ObservableCollection<Profile> Profiles { get; } = [];

    public AsyncCommand<Profile> OpenProfileCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand CancelCreateCommand { get; }
    public AsyncCommand SubmitCreateProfileCommand { get; }
    public AsyncCommand ConfirmUnlockCommand { get; }
    public RelayCommand CancelUnlockCommand { get; }

    public event Action<Profile>? ProfileOpened;

    public bool HasProfiles => Profiles.Count > 0;
    public bool IsEmptyStateVisible => !HasProfiles && !IsCreatingProfile && !IsUnlockingProfile;
    public bool IsProfileListVisible => HasProfiles && !IsCreatingProfile && !IsUnlockingProfile;

    public bool IsCreatingProfile
    {
        get => _isCreatingProfile;
        private set
        {
            if (SetProperty(ref _isCreatingProfile, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsProfileListVisible));
            }
        }
    }

    public bool IsUnlockingProfile
    {
        get => _isUnlockingProfile;
        private set
        {
            if (SetProperty(ref _isUnlockingProfile, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsProfileListVisible));
            }
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    public string CreatePassword
    {
        get => _createPassword;
        set => SetProperty(ref _createPassword, value);
    }

    public string RecoveryEmail
    {
        get => _recoveryEmail;
        set => SetProperty(ref _recoveryEmail, value);
    }

    public string UnlockPassword
    {
        get => _unlockPassword;
        set => SetProperty(ref _unlockPassword, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default) =>
        await RefreshProfilesAsync(cancellationToken);

    public async Task SetCatalogMasterRevealedAsync(
        bool revealed,
        CancellationToken cancellationToken = default)
    {
        if (_isCatalogMasterRevealed == revealed)
        {
            return;
        }

        _isCatalogMasterRevealed = revealed;
        await RefreshProfilesAsync(cancellationToken);
    }

    private async Task RefreshProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _profileService.GetAvailableProfilesAsync(
            _isCatalogMasterRevealed,
            cancellationToken);
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
        }

        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsProfileListVisible));
    }

    private async Task OpenProfileAsync(Profile profile)
    {
        ErrorMessage = string.Empty;
        if (profile.PasswordHash is not null)
        {
            _profileToUnlock = profile;
            UnlockPassword = string.Empty;
            IsUnlockingProfile = true;
            return;
        }

        var opened = await _profileService.OpenProfileAsync(profile.Id, null);
        if (opened is not null)
        {
            ProfileOpened?.Invoke(opened);
        }
    }

    private async Task SubmitCreateProfileAsync()
    {
        ErrorMessage = string.Empty;
        try
        {
            var profile = await _profileService.CreateProfileAsync(ProfileName, CreatePassword, RecoveryEmail);
            await RefreshProfilesAsync();
            ProfileName = string.Empty;
            CreatePassword = string.Empty;
            RecoveryEmail = string.Empty;
            IsCreatingProfile = false;
            ProfileOpened?.Invoke(profile);
        }
        catch (ProfileNameValidationException exception)
        {
            ErrorMessage = exception.Error switch
            {
                ProfileNameError.Required => "Enter a profile name.",
                ProfileNameError.Reserved => "That name is reserved.",
                _ => "Use letters, numbers, and spaces only."
            };
        }
        catch (DuplicateProfileNameException)
        {
            ErrorMessage = "A profile with that name already exists.";
        }
        catch (Exception)
        {
            ErrorMessage = "The profile could not be created.";
        }
    }

    private async Task ConfirmUnlockAsync()
    {
        if (_profileToUnlock is null)
        {
            return;
        }

        try
        {
            var opened = await _profileService.OpenProfileAsync(_profileToUnlock.Id, UnlockPassword);
            if (opened is null)
            {
                ErrorMessage = "Incorrect password.";
                UnlockPassword = string.Empty;
                return;
            }

            ProfileOpened?.Invoke(opened);
        }
        catch (Exception)
        {
            ErrorMessage = "The profile could not be opened.";
        }
    }

    private void BeginCreateProfile()
    {
        ErrorMessage = string.Empty;
        IsCreatingProfile = true;
    }

    private void CancelCreateProfile()
    {
        ErrorMessage = string.Empty;
        IsCreatingProfile = false;
    }

    private void CancelUnlock()
    {
        ErrorMessage = string.Empty;
        UnlockPassword = string.Empty;
        _profileToUnlock = null;
        IsUnlockingProfile = false;
    }
}