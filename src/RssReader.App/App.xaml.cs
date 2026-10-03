using System.IO;
using System.Net.Http;
using System.Windows;
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

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		try
		{
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
					services.AddSingleton<ReadingService>();
					services.AddSingleton<FeedRefreshService>();
				})
				.Build();

			await _host.StartAsync();
			await _host.Services.GetRequiredService<ProfileService>().InitializeAsync();
			await _host.Services.GetRequiredService<CatalogService>().InitializeAsync();
			await _host.Services.GetRequiredService<SqliteReaderStore>().InitializeAsync();
			await ShowProfileChooserAsync();
		}
		catch (Exception)
		{
			System.Windows.MessageBox.Show(
				"RSS Reader could not start. Check local application data permissions and try again.",
				"RSS Reader",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
			Shutdown(1);
		}
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
		try
		{
			var catalogService = _host!.Services.GetRequiredService<CatalogService>();
			var readingService = _host.Services.GetRequiredService<ReadingService>();
			var feedRefreshService = _host.Services.GetRequiredService<FeedRefreshService>();
			var viewModel = new MainWindowViewModel(profile, catalogService, readingService, feedRefreshService);
			await viewModel.InitializeAsync();
			var window = new MainWindow(viewModel);
			window.ProfileSwitchRequested += SwitchProfile;

			var chooser = _profileChooserWindow;
			_readerWindow = window;
			MainWindow = window;
			window.Show();
			chooser?.Close();
			_profileChooserWindow = null;
		}
		catch (Exception)
		{
			System.Windows.MessageBox.Show(
				"The selected profile could not be opened.",
				"RSS Reader",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
	}

	private async void SwitchProfile()
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
			System.Windows.MessageBox.Show(
				"The profile chooser could not be opened.",
				"RSS Reader",
				System.Windows.MessageBoxButton.OK,
				System.Windows.MessageBoxImage.Error);
		}
	}
}

