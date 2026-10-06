using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RssReader.App.ViewModels;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.App;

public partial class App : System.Windows.Application
{
	private IHost? _host;
	private ProfileChooserWindow? _profileChooserWindow;
	private MainWindow? _readerWindow;
	private SplashWindow? _splashWindow;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		_splashWindow = new SplashWindow();
		MainWindow = _splashWindow;
		_splashWindow.Show();
		await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

		try
		{
			await UpdateStartupStatusAsync("Preparing application services...");
			var databasePath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"RssReader",
				"rssreader.db");
			_host = Host.CreateDefaultBuilder()
				.ConfigureServices(services =>
				{
					services.AddSingleton<IProfileStore>(_ => new SqliteProfileStore(databasePath));
					services.AddSingleton<ICatalogStore>(_ => new SqliteCatalogStore(databasePath));
					services.AddSingleton(_ => new SqliteReaderStore(databasePath));
					services.AddSingleton<IReaderStore>(provider => provider.GetRequiredService<SqliteReaderStore>());
					services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
					services.AddSingleton<IFeedDownloader, SyndicationFeedDownloader>();
					services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
					services.AddSingleton<ProfileService>();
					services.AddSingleton<CatalogService>();
					services.AddSingleton<CatalogFeedPreviewService>();
					services.AddSingleton<ReadingService>();
					services.AddSingleton<ProfileFeedService>();
					services.AddSingleton<FeedRefreshService>();
				})
				.Build();

			await UpdateStartupStatusAsync("Starting application services...");
			await _host.StartAsync();
			await UpdateStartupStatusAsync("Opening the local profile database...");
			await _host.Services.GetRequiredService<ProfileService>().InitializeAsync();
			await UpdateStartupStatusAsync("Preparing the shared feed catalog...");
			await _host.Services.GetRequiredService<CatalogService>().InitializeAsync();
			await UpdateStartupStatusAsync("Preparing your reading library...");
			await _host.Services.GetRequiredService<SqliteReaderStore>().InitializeAsync();
			await UpdateStartupStatusAsync("Opening the profile chooser...");
			await ShowProfileChooserAsync();
			_splashWindow.Close();
			_splashWindow = null;
			ShutdownMode = ShutdownMode.OnLastWindowClose;
		}
		catch (Exception)
		{
			await UpdateStartupStatusAsync("Startup failed. See the error message for details.");
			MessageDialogWindow.Show(
				_splashWindow,
				"RSS Reader could not start. Check local application data permissions and try again.",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
			_splashWindow?.Close();
			_splashWindow = null;
			Shutdown(1);
		}
	}

	private async Task UpdateStartupStatusAsync(string status)
	{
		_splashWindow?.SetStatus(status);
		await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host is not null)
		{
			await _host.StopAsync();
			_host.Dispose();
		}

		base.OnExit(e);
	}

	private async Task ShowProfileChooserAsync()
	{
		var service = _host!.Services.GetRequiredService<ProfileService>();
		var viewModel = new ProfileChooserViewModel(service);
		await viewModel.InitializeAsync();
		viewModel.ProfileOpened += OpenReader;

		var window = new ProfileChooserWindow(viewModel);
		window.Closed += (_, _) =>
		{
			if (ReferenceEquals(_profileChooserWindow, window))
			{
				_profileChooserWindow = null;
			}
		};

		_profileChooserWindow = window;
		MainWindow = window;
		window.Show();
	}

	private async void OpenReader(Profile profile)
	{
		RssReader.App.MainWindow? readerWindow = null;
		var chooser = _profileChooserWindow;
		try
		{
			var catalogService = _host!.Services.GetRequiredService<CatalogService>();
			var readingService = _host.Services.GetRequiredService<ReadingService>();
			var feedRefreshService = _host.Services.GetRequiredService<FeedRefreshService>();
			var catalogFeedPreviewService = _host.Services.GetRequiredService<CatalogFeedPreviewService>();
			var profileService = _host.Services.GetRequiredService<ProfileService>();
			var preferences = await profileService.GetPreferencesAsync(profile.Id);
			var viewModel = new MainWindowViewModel(
				profile,
				catalogService,
				readingService,
				feedRefreshService,
				preferences,
				catalogFeedPreviewService,
				_host.Services.GetRequiredService<ProfileFeedService>(),
				profileService);
			readerWindow = new MainWindow(viewModel);
			readerWindow.LogoutRequested += LogOut;
			readerWindow.PreferencesRequested += () => ShowPreferences(readerWindow, viewModel);

			_readerWindow = readerWindow;
			MainWindow = readerWindow;
			readerWindow.Show();
			await viewModel.InitializeAsync();
			if (!viewModel.IsCatalogMaster)
			{
				_ = viewModel.RefreshNowAsync();
			}

			chooser?.Close();
			_profileChooserWindow = null;
		}
		catch (Exception exception)
		{
			readerWindow?.Close();
			_readerWindow = null;
			if (chooser is not null)
			{
				MainWindow = chooser;
				chooser.Activate();
			}

			System.Diagnostics.Debug.WriteLine(exception.ToString());
			MessageDialogWindow.Show(
				chooser,
				$"The selected profile could not be opened.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
	}

	private async void ShowPreferences(MainWindow owner, MainWindowViewModel viewModel)
	{
		try
		{
			var profileService = _host!.Services.GetRequiredService<ProfileService>();
			var preferences = await profileService.GetPreferencesAsync(viewModel.ActiveProfile.Id);
			var dialog = new PreferencesWindow(preferences) { Owner = owner };
			if (dialog.ShowDialog() == true)
			{
				await profileService.SavePreferencesAsync(viewModel.ActiveProfile.Id, dialog.Preferences);
				viewModel.ApplyPreferences(dialog.Preferences);
			}
		}
		catch (Exception exception)
		{
			MessageDialogWindow.Show(
				owner,
				$"Preferences could not be saved.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
	}

	private async void LogOut()
	{
		var previousWindow = _readerWindow;
		try
		{
			await ShowProfileChooserAsync();
			_readerWindow = null;
			previousWindow?.Close();
		}
		catch (Exception)
		{
			MessageDialogWindow.Show(
				_readerWindow,
				"The profile chooser could not be opened.",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
	}
}
