﻿using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Navigation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MahApps.Metro.Controls.Dialogs;
using Dogstar.Resources;
using Dogstar.Properties;
using MahApps.Metro.Controls;

using static MahApps.Metro.ThemeManager;
using static Dogstar.Helper;
using static Dogstar.External;
using static System.Convert;

namespace Dogstar
{
	// TODO: Maybe save management_beta.txt to documents\sega\phantasystaronline2 because vanilla launcher does it
	// TODO: figure out documents\sega\pso2\download patch lists
	// TODO: Implement the functionality behind the toggles on the enhancements menu
	// TODO: When configuring PSO2 Proxy and plugin not installed, install plugin on success
	// TODO: Figure out why vanilla launcher keeps deleting version.ver
	// TODO: General download tab needs pause/cancel. Oohhhh boy.
	// TODO: Switch to general download tab when restoring backups.
	// TODO: Static strings for all patch names (e.g EnglishPatch, JPEnemies)
	// TODO: Update detection for enemies and e-codes
	// TODO: After ^, when an English Patch update is detected, "uninstall" JP patches since they overlap.
	// TODO: Fix all them WIN7 UIs

	public partial class MainWindow : IDisposable
	{
		private readonly DownloadManager _generalDownloadManager = new DownloadManager();
		private readonly TabController _gameTabController;

		private CancellationTokenSource _checkCancelSource = new CancellationTokenSource();
		private bool _isPrecedeDownloading;
		private bool _isCheckPaused;
		private double _lastTop;
		private double _lastLeft;

		public string WindowTittle => $"{ApplicationInfo.Name} {ApplicationInfo.Version}";

		public MainWindow()
		{
			ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
			InitializeComponent();

			_generalDownloadManager.DownloadStarted += DownloadStarted;
			_generalDownloadManager.DownloadProgressChanged += DownloadProgressChanged;

			Topmost = Settings.Default.AlwaysOnTop;
			Colors.SelectedIndex = Array.IndexOf(UiResources.GetColor().Values.ToArray(), Settings.Default.AccentColor);
			Themes.SelectedIndex = Array.IndexOf(UiResources.GetTheme().Values.ToArray(), Settings.Default.Theme);

			_gameTabController = new TabController(GameTabControl);
		}

		#region Events


		private void Debug_MouseDown(object sender, MouseButtonEventArgs e) => DebugFlyout.IsOpen = !DebugFlyout.IsOpen;

		private void Donate_MouseDown(object sender, MouseButtonEventArgs e) => DonationFlyout.IsOpen = !DebugFlyout.IsOpen;

		private void Twitter_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarTwitter);

		private void Github_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarGithub);

		private void Information_MouseDown(object sender, MouseButtonEventArgs e) => Process.Start(Properties.Resources.DogstarSupport);

		private void CancelCheckButton_Click(object sender, RoutedEventArgs e) => _checkCancelSource.Cancel();

		private void AlwaysOnTop_Changed(object sender, RoutedEventArgs e) => Topmost = Settings.Default.AlwaysOnTop = AlwaysOnTop.IsChecked.GetValueOrDefault();

		private void Launch_Changed(object sender, RoutedEventArgs e) => Settings.Default.CloseOnLaunch = Launch.IsChecked.GetValueOrDefault();

		private void DonateToDogstar_Click(object sender, RoutedEventArgs e) => Process.Start(Properties.Resources.DogstarDonation);

		private void DonateToPolaris_Click(object sender, RoutedEventArgs e) => Process.Start(Properties.Resources.PolarisDonation);

		private void EnhancementsTile_Click(object sender, RoutedEventArgs e) => _gameTabController.ChangeTab(EnhancementsTabItem);

		private void BackButton_Click(object sender, RoutedEventArgs e) => _gameTabController.PreviousTab();

		private void OtherTile_Click(object sender, RoutedEventArgs e) => _gameTabController.ChangeTab(OtherTabItem);

		private void GameSettingsTile_Click(object sender, RoutedEventArgs e) => _gameTabController.ChangeTab(GameSettingsTabItem);

		private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckGameFiles(UpdateMethod.FileCheck);

		private async void OtherProxyConfig_Click(object sender, RoutedEventArgs e) => await ConfigProxy();

		private async void OtherChangeGameDir_Click(object sender, RoutedEventArgs e) => await SelectGameFolder();

		private void OtherOpenGameDir_Click(object sender, RoutedEventArgs e) => Process.Start($"file://{Settings.Default.GameFolder}");

		private async void OtherInstallGame_Click(object sender, RoutedEventArgs e) => await SetupGameInfo();

		private async void metroWindow_Loaded(object sender, RoutedEventArgs e)
		{
			_lastTop = Top;
			_lastLeft = Left;

			if (!Settings.Default.IsGameInstalled)
			{
				var gamefolder = GetTweakerGameFolder();

				if (string.IsNullOrWhiteSpace(gamefolder))
				{
					await SetupGameInfo();
				}
				else
				{
					var result = await this.ShowMessageAsync(Text.GameDetected, $"\"{gamefolder}\"", AffirmNeg, YesNo);

					if (result == MessageDialogResult.Affirmative)
					{
						Settings.Default.GameFolder = gamefolder;
						Settings.Default.IsGameInstalled = true;
					}
					else
					{
						await SetupGameInfo();
					}
				}
			}

			if (Settings.Default.IsGameInstalled)
			{
				if (!Directory.Exists(Settings.Default.GameFolder))
				{
					var result = await this.ShowMessageAsync(Text.MissingFolder, Text.MovedOrDeleted, AffirmNeg, MovedDeleted);

					if (result == MessageDialogResult.Affirmative)
					{
						await SelectGameFolder();
					}
					else
					{
						Settings.Default.IsGameInstalled = false;
						Settings.Default.GameFolder = string.Empty;
						await SetupGameInfo();
					}
				}
			}

			if (Settings.Default.IsGameInstalled)
			{
				await Task.Run(() => CreateDirectoryIfNoneExists(GameConfigFolder));

				var editionPath = Path.Combine(Settings.Default.GameFolder, "edition.txt");

				var plugins = JsonConvert.DeserializeObject<PluginInfo[]>(Settings.Default.PluginSettings);
				var pluginInfoPullArray = plugins.Select(x =>
				{
					using (var client = AquaClient)
					{
						return client.DownloadStringTaskAsync(x.Url);
					}
				}).ToArray();

				if (File.Exists(editionPath))
				{
					var edition = await Task.Run(() => File.ReadAllText(editionPath));

					if (edition != "jp")
					{
						await this.ShowMessageAsync(Text.Warning, Text.NonJPPSO2);
					}
				}

				if (!await IsGameUpToDate())
				{
					var result = await this.ShowMessageAsync(Text.GameUpdate, Text.GameUpdateAvailable, AffirmNeg, YesNo);

					if (result == MessageDialogResult.Affirmative)
					{
						await CheckGameFiles(UpdateMethod.Update);
					}
				}
				else
				{
					SetPatchToggleSwitches();

					if (EnglishPatchToggle.IsChecked == true || LargeFilesToggle.IsChecked == true)
					{
						var files = (JArray)(await GetArghlexJson()).files;
						dynamic entryEn = files.Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).StartsWith("patch_"));
						dynamic entryLarge = files.Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).EndsWith("_largefiles.rar"));

						if (entryEn != null && await CheckEnglishPatchVersion((long)entryEn.modtime))
						{
							_gameTabController.ChangeTab(GeneralDownloadTab);
							await DownloadEnglishPatch((string)entryEn.name, (int)entryEn.size, (long)entryEn.modtime);
							_gameTabController.PreviousTab();
						}
						if (entryLarge != null && await CheckLargeFilesVersion((long)entryLarge.modtime))
						{
							_gameTabController.ChangeTab(GeneralDownloadTab);
							await DownloadLargeFiles((string)entryLarge.name, (int)entryLarge.size, (long)entryLarge.modtime);
							_gameTabController.PreviousTab();
						}
					}
				}

				var updatedPluginInfos = (await Task.WhenAll(pluginInfoPullArray)).Select(JsonConvert.DeserializeObject<PluginInfo>).ToArray();
				var pluginsUpdateingTasks = new List<Task>();
				PluginManager.DownloadManager = _generalDownloadManager;

				for (int index = 0; index < updatedPluginInfos.Length; index++)
				{
					if (updatedPluginInfos[index].CurrentVersion > plugins[index].CurrentVersion)
					{
						if (await this.ShowMessageAsync(Text.UpdateAvailable, string.Format(Text.DownloadLatest, plugins[index].Name), AffirmNeg, YesNo) == MessageDialogResult.Affirmative)
						{
							updatedPluginInfos[index].Url = plugins[index].Url;
							var plugin = plugins[index] = updatedPluginInfos[index];

							pluginsUpdateingTasks.Add(plugin.IsEnabled
								? PluginManager.Install(plugin)
								: PluginManager.Install(plugin).ContinueWith(x => PluginManager.Disable(plugin)));
						}
					}
				}

				if (pluginsUpdateingTasks.Count > 0)
				{
					_gameTabController.ChangeTab(GeneralDownloadTab);
					await Task.WhenAll(pluginsUpdateingTasks);
					_gameTabController.PreviousTab();
				}

				PluginManager.PluginSettings.AddRange(plugins);

				if (await IsNewPrecedeAvailable() && await this.ShowMessageAsync(Text.PrecedeAvailable, Text.DownloadLatestPreced, AffirmNeg, YesNo) == MessageDialogResult.Affirmative)
				{
					var precedeWindow = new PrecedeWindow { Owner = this, Top = Top + Height, Left = Left };
					_isPrecedeDownloading = true;
					precedeWindow.Show();
					precedeWindow.Closed += delegate { _isPrecedeDownloading = false; };
				}
			}

			EnableGameButtions(Settings.Default.IsGameInstalled);
			Settings.Default.Save();
		}

		private void MetroWindow_LocationChanged(object sender, EventArgs e)
		{
			foreach (Window window in OwnedWindows)
			{
				window.Top += Top - _lastTop;
				window.Left += Left - _lastLeft;
			}

			_lastTop = Top;
			_lastLeft = Left;
		}

		private void PauseCheckButton_Click(object sender, RoutedEventArgs e)
		{
			PauseCheckButton.Content = Text.Pausing;
			_isCheckPaused = !_isCheckPaused;
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			Process.Start(e.Uri.ToString());
			e.Handled = true;
		}

		private void DownloadStarted(object sender, string e)
		{
			CurrentGeneralDownloadActionLabel.Dispatcher.InvokeAsync(() =>
			{
				CurrentGeneralDownloadActionLabel.Content = Path.GetFileNameWithoutExtension(e);
			});
		}

		private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
		{
			CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
			{
				GeneralDownloadProgressbar.Value = e.ProgressPercentage;
				CurrentGeneralDownloadSizeActionLabel.Content = $"{SizeSuffix(e.BytesReceived)}/{SizeSuffix(e.TotalBytesToReceive)}";
			});
		}

		private async void LaunchButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (_isPrecedeDownloading && Settings.Default.CloseOnLaunch)
				{
					var result = await this.ShowMessageAsync(Text.DownloadInProgress, Text.LaunchDownloadInProgress, AffirmNeg, YesNo);
					if (result != MessageDialogResult.Affirmative)
					{
						return;
					}
				}

				await PullManagementData();
				if (ManagementData.ContainsKey("ManagementData") && ManagementData["IsInMaintenance"] == "1")
				{
					var result = await this.ShowMessageAsync(Text.ServerMaintenance, Text.GameIsDown, AffirmNeg, YesNo);
					if (result != MessageDialogResult.Affirmative)
					{
						return;
					}
				}

				if (await Task.Run((Func<bool>)LaunchGame) && Settings.Default.CloseOnLaunch && !_isPrecedeDownloading)
				{
					Close();
				}
			}
			catch (Exception ex)
			{
				await this.ShowMessageAsync(string.Empty, ex.Message);
			}
		}

		private void Color_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				Settings.Default.AccentColor = Colors.SelectedValue.ToString();
				ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
				Settings.Default.Save();
			}
		}

		private void Theme_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				Settings.Default.Theme = Themes.SelectedValue.ToString();
				ChangeAppStyle(Application.Current, GetAccent(Settings.Default.AccentColor), GetAppTheme(Settings.Default.Theme));
				Settings.Default.Save();
			}
		}

		private async void EnglishPatchToggle_Checked(object sender, RoutedEventArgs e)
		{
			ResetGeneralDownloadTab();
			CompletedGeneralDownloadActionLabel.Content = Text.DownloadingEngPatch;
			_gameTabController.ChangeTab(GeneralDownloadTab);

			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.files).Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).StartsWith("patch_"));
			if (entry != null)
			{
				await DownloadEnglishPatch((string)entry.name, (int)entry.size, (int)entry.modtime);
			}
			_gameTabController.PreviousTab();
			SetPatchToggleSwitches();
		}

		private async void LargeFilesToggle_Checked(object sender, RoutedEventArgs e)
		{
			ResetGeneralDownloadTab();
			CompletedGeneralDownloadActionLabel.Content = Text.DownloadingLargeFiles;
			_gameTabController.ChangeTab(GeneralDownloadTab);

			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.files).Select(x => (dynamic)x).FirstOrDefault(x => ((string)x.name).EndsWith("_largefiles.rar"));
			if (entry != null)
			{
				await DownloadLargeFiles((string)entry.name, (int)entry.size, (int)entry.modtime);
			}
			_gameTabController.PreviousTab();
		}

		private async void JpeCodesToggle_Checked(object sender, RoutedEventArgs e)
		{
			ResetGeneralDownloadTab();
			CompletedGeneralDownloadActionLabel.Content = Text.DownloadingJpECodes;
			GeneralDownloadIsIndeterminate(true);

			_gameTabController.ChangeTab(GeneralDownloadTab);

			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.folders).Select(x => (dynamic)x).FirstOrDefault(x => x.name != "docs" && x.name != "psu" && x.name != "temp");

			GeneralDownloadIsIndeterminate(false);

			if (entry != null)
			{
				var modtime = await DownloadJpFile((string)entry.name, JpeCodesFile, "JPECodes");

				if (modtime == 0)
				{
					JpeCodesToggle.IsChecked = false;
					await this.ShowMessageAsync(Text.Failed, string.Format(Text.GenericPatchFailed, "JP E-Codes"));
				}
				else
				{
					Settings.Default.InstalledJPECodes = modtime;
					Settings.Default.Save();
					await this.ShowMessageAsync(Text.Complete, string.Format(Text.GenericPatchSuccess, "JP E-Codes"));
				}
			}

			_gameTabController.PreviousTab();
		}

		private async void JpeCodesToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() => RestorePatchBackup("JPEcodes"));
			Settings.Default.InstalledJPECodes = 0;
			Settings.Default.Save();
		}

		private async void JpEnemiesToggle_Checked(object sender, RoutedEventArgs e)
		{
			ResetGeneralDownloadTab();
			CompletedGeneralDownloadActionLabel.Content = Text.DownloadingJpEnemyNames;
			GeneralDownloadIsIndeterminate(true);

			_gameTabController.ChangeTab(GeneralDownloadTab);

			dynamic jsonData = await GetArghlexJson();
			dynamic entry = ((JArray)jsonData.folders).Select(x => (dynamic)x).FirstOrDefault(x => x.name != "docs" && x.name != "psu" && x.name != "temp");

			GeneralDownloadIsIndeterminate(false);

			if (entry != null)
			{
				var modtime = await DownloadJpFile((string)entry.name, JpEnemiesFile, "JPEnemies");

				if (modtime == 0)
				{
					JpEnemiesToggle.IsChecked = false;
					await this.ShowMessageAsync(Text.Failed, string.Format(Text.GenericPatchFailed, "JP Enemies"));
				}
				else
				{
					Settings.Default.InstalledJPEnemies = modtime;
					Settings.Default.Save();
					await this.ShowMessageAsync(Text.Complete, string.Format(Text.GenericPatchSuccess, "JP Enemies"));
				}
			}

			_gameTabController.PreviousTab();
		}

		private async void JpEnemiesToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() => RestorePatchBackup("JPEnemies"));
			Settings.Default.InstalledJPEnemies = 0;
			Settings.Default.Save();
		}

		private async void Pso2ProxyToggle_Checked(object sender, RoutedEventArgs e)
		{
			if (await Task.Run((Func<bool>)ProxyCheck))
			{
				// TODO: Put dll in plugins
			}
			else
			{
				//Pso2ProxyToggle.IsEnabled = await ConfigProxy();
			}
		}

		private async void Pso2ProxyToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			// TODO: Remove DLL
			await Task.Run(() => File.WriteAllLines(HostsPath, StripProxyEntries(File.ReadAllLines(HostsPath))));
		}

		private async void EnglishPatchToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() =>
			{
				RestorePatchBackup("JPECodes");
				RestorePatchBackup("JPEnemies");
				RestorePatchBackup("EnglishPatch");
			});

			Settings.Default.InstalledEnglishPatch = 0;
			SetPatchToggleSwitches();
			Settings.Default.Save();
		}

		private async void LargeFilesToggle_Unchecked(object sender, RoutedEventArgs e)
		{
			await Task.Run(() => RestorePatchBackup("LargeFiles"));
			Settings.Default.InstalledLargeFiles = 0;
			Settings.Default.Save();
		}

		private void EnhancementsTabItem_OnSelected(object sender, RoutedEventArgs e)
		{
			// TODO: Do this better
			EnhancementsItemGrid.Children.RemoveRange(10, EnhancementsItemGrid.Children.Count - 10);

			foreach (var plugin in PluginManager.PluginSettings)
			{
				EnhancementsTabAddSubItem(plugin);
			}
		}

		private void EnhancementsTabItem_OnUnSelected(object sender, RoutedEventArgs e) => SavePluginSettings();

		private async void AddPluginButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var jsonUrl = await this.ShowInputAsync(Text.AddPlugin, string.Empty);
				_gameTabController.ChangeTab(GeneralDownloadTab);

				if (jsonUrl != null)
				{
					PluginManager.DownloadManager = _generalDownloadManager;
					var info = await PluginManager.InfoFromUrl(new Uri(jsonUrl));
					await PluginManager.Install(info);
					EnhancementsTabAddSubItem(info);
					PluginManager.PluginSettings.Add(info);
				}
			}
			catch (Exception ex)
			{
				await this.ShowMessageAsync(Text.Error, ex.Message);
			}

			_gameTabController.PreviousTab();
		}

		private void GameSettingsTabItem_OnSelected(object sender, RoutedEventArgs e)
		{
			PsoSettings.Reload();

			// Math is used to map the Vsync values to indexes to remove the need for a Switch or an Array
			VsyncComboBox.SelectedIndex = (int)(PsoSettings.Vsync / 140f * 5f);
			WindowModeComboBox.SelectedIndex = PsoSettings.VirtualFullScreen ? 2 : ToInt32(PsoSettings.FullScreen);
			MonitorPlaybackCheckBox.IsChecked = PsoSettings.MoviePlay;
			TextureComboBox.SelectedIndex = PsoSettings.TextureResolution;
			ShaderQualityCombobox.SelectedIndex = PsoSettings.ShaderQuality;
			InterfaceSizeComboBox.SelectedIndex = PsoSettings.InterfaceSize;
			MusicSlider.Value = PsoSettings.Music;
			SoundSlider.Value = PsoSettings.Sound;
			VoiceSlider.Value = PsoSettings.Voice;
			VideoSlider.Value = PsoSettings.Video;
			SurroundToggle.IsChecked = PsoSettings.Surround;
			GlobalFocusToggle.IsChecked = PsoSettings.GlobalFocus;

			var resolution = $"{PsoSettings.WindowWidth}x{PsoSettings.WindowHight}";
			UiResources.GetResolutions().Add(resolution);
			ResolutionsCombobox.SelectedItem = resolution;
		}

		private void GameSettingsTabItem_OnUnSelected(object sender, RoutedEventArgs e) => PsoSettings.Save();

		private void TextureComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				PsoSettings.TextureResolution = TextureComboBox.SelectedIndex;
			}
		}

		private void ShaderQualityCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				PsoSettings.ShaderQuality = ShaderQualityCombobox.SelectedIndex;
			}
		}

		private void VsyncComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				PsoSettings.Vsync = ToInt32(((dynamic)VsyncComboBox.SelectedValue).Content.Replace("Off", "0"));
			}
		}

		private void WindowModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				if (WindowModeComboBox.SelectedIndex == 2)
				{
					PsoSettings.FullScreen = false;
					PsoSettings.VirtualFullScreen = true;
				}
				else
				{
					PsoSettings.FullScreen = ToBoolean(WindowModeComboBox.SelectedIndex);
					PsoSettings.VirtualFullScreen = false;
				}
			}
		}

		private void InterfaceSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				PsoSettings.InterfaceSize = InterfaceSizeComboBox.SelectedIndex;
			}
		}

		private void MonitorPlaybackCheckBox_Checked(object sender, RoutedEventArgs e) => PsoSettings.MoviePlay = true;

		private void MonitorPlaybackCheckBox_Unchecked(object sender, RoutedEventArgs e) => PsoSettings.MoviePlay = false;

		private void ResolutionsCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (IsLoaded)
			{
				var resolution = ResolutionsCombobox.SelectedItem.ToString().Split('x');

				PsoSettings.WindowWidth = ToInt32(resolution[0]);
				PsoSettings.WindowHight = ToInt32(resolution[1]);
			}
		}

		private void MusicSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PsoSettings.Music = (int)MusicSlider.Value;

		private void SoundSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PsoSettings.Sound = (int)SoundSlider.Value;

		private void VoiceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PsoSettings.Voice = (int)VoiceSlider.Value;

		private void VideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => PsoSettings.Video = (int)VideoSlider.Value;

		private void SurroundToggle_Checked(object sender, RoutedEventArgs e) => PsoSettings.Surround = true;

		private void SurroundToggle_Unchecked(object sender, RoutedEventArgs e) => PsoSettings.Surround = false;

		private void GlobalFocusToggle_Checked(object sender, RoutedEventArgs e) => PsoSettings.GlobalFocus = true;

		private void GlobalFocusToggle_Unchecked(object sender, RoutedEventArgs e) => PsoSettings.GlobalFocus = false;

		#endregion

		#region Functions

		private void EnableGameButtions(bool isEnabled)
		{
			CheckButton.IsEnabled = isEnabled;
			LaunchButton.IsEnabled = isEnabled;
			GameSettingsTile.IsEnabled = isEnabled;
			EnhancementsTile.IsEnabled = isEnabled;
			EnhancementsTile.IsEnabled = isEnabled;
			OtherProxyConfig.IsEnabled = isEnabled;
		}

		private void SetPatchToggleSwitches()
		{
			EnglishPatchToggle.IsChecked = Settings.Default.InstalledEnglishPatch != 0;
			LargeFilesToggle.IsChecked = Settings.Default.InstalledLargeFiles != 0;

			JpeCodesToggle.IsEnabled = EnglishPatchToggle.IsChecked == true;
			JpEnemiesToggle.IsEnabled = EnglishPatchToggle.IsChecked == true;

			if (EnglishPatchToggle.IsChecked == false)
			{
				Settings.Default.InstalledJPECodes = 0;
				Settings.Default.InstalledJPEnemies = 0;
			}

			JpeCodesToggle.IsChecked = JpeCodesToggle.IsEnabled && Settings.Default.InstalledJPECodes != 0;
			JpEnemiesToggle.IsChecked = JpEnemiesToggle.IsEnabled && Settings.Default.InstalledJPEnemies != 0;
		}

		private void ResetGeneralDownloadTab()
		{
			CompletedGeneralDownloadActionLabel.Content = string.Empty;
			CurrentGeneralDownloadActionLabel.Content = string.Empty;
			CurrentGeneralDownloadSizeActionLabel.Content = string.Empty;
			GeneralDownloadProgressbar.Maximum = 100;
			GeneralDownloadProgressbar.Value = 0;
			GeneralDownloadIsIndeterminate(false);
		}

		private void GeneralDownloadIsIndeterminate(bool value)
		{
			CurrentGeneralDownloadSizeActionLabel.Visibility = value ? Visibility.Hidden : Visibility.Visible;
			GeneralDownloadProgressbar.IsIndeterminate = value;
		}

		private void EnhancementsTabAddSubItem(PluginInfo settings)
		{
			EnhancementsItemGrid.RowDefinitions.Add(new RowDefinition());

			var lable = new Label
			{
				FontSize = 14,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 5, 0),
				Content = $"{settings.Name}:",
				ToolTip = settings.Description
			};

			lable.SetValue(Grid.ColumnProperty, 0);
			lable.SetValue(Grid.RowProperty, EnhancementsItemGrid.RowDefinitions.Count - 1);

			var toggleSwitch = new ToggleSwitch
			{
				Name = settings.Name + "Toggle",
				VerticalAlignment = VerticalAlignment.Center,
				IsChecked = settings.IsEnabled,
			};

			toggleSwitch.Checked += delegate { PluginManager.Enable(settings); settings.IsEnabled = true; };
			toggleSwitch.Unchecked += delegate { PluginManager.Disable(settings); settings.IsEnabled = false; };
			toggleSwitch.SetValue(Grid.ColumnProperty, 1);
			toggleSwitch.SetValue(Grid.RowProperty, EnhancementsItemGrid.RowDefinitions.Count - 1);

			EnhancementsItemGrid.Children.Add(lable);
			EnhancementsItemGrid.Children.Add(toggleSwitch);
		}

		private async Task<bool> CheckGameFiles(UpdateMethod method)
		{
			_gameTabController.ChangeTab(FileCheckTabItem);

			_checkCancelSource = new CancellationTokenSource();
			_isCheckPaused = false;
			CompletedCheckDownloadActionsLabel.Content = string.Empty;
			CompletedCheckActionsLabel.Content = string.Empty;
			CurrentCheckActionLabel.Content = string.Empty;
			CurrentCheckDownloadActionLabel.Content = string.Empty;
			CurrentCheckSizeActionLabel.Content = string.Empty;
			PauseCheckButton.Content = Text.Pause;
			CheckDownloadProgressbar.Value = 0;
			CheckProgressbar.Value = 0;
			var numberDownloaded = 0;
			var numberToDownload = 3;
			var fileOperations = new List<Task>();

			using (var manager = new DownloadManager())
			{
				manager.DownloadStarted += (s, e) =>
				{
					CurrentCheckDownloadActionLabel.Dispatcher.InvokeAsync(() =>
					{
						CheckDownloadProgressbar.Maximum = 100;
						CurrentCheckDownloadActionLabel.Content = Path.GetFileNameWithoutExtension(e);
					});
				};

				manager.DownloadProgressChanged += (s, e) =>
				{
					CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
					{
						CheckDownloadProgressbar.Value = e.ProgressPercentage;
						CurrentCheckSizeActionLabel.Content = $"{SizeSuffix(e.BytesReceived)}/{SizeSuffix(e.TotalBytesToReceive)}";
					});
				};

				manager.DownloadCompleted += (s, e) =>
				{
					CheckDownloadProgressbar.Dispatcher.InvokeAsync(() =>
					{
						numberDownloaded++;
						CompletedCheckDownloadActionsLabel.Content = Text.DownloadedOf.Format(numberDownloaded, numberToDownload);
					});
				};

				Func<Task> pause = async () =>
				{
					var taskSource = new TaskCompletionSource<object>();
					RoutedEventHandler unpause = (s, e) => taskSource.TrySetResult(null);
					RoutedEventHandler cancel = (s, e) => taskSource.TrySetCanceled();

					try
					{
						PauseCheckButton.Click += unpause;
						CancelCheckButton.Click += cancel;
						manager.PauseDownloads(taskSource.Task);
						await taskSource.Task;
					}
					finally
					{
						PauseCheckButton.Click -= unpause;
						CancelCheckButton.Click -= cancel;
					}
				};

				try
				{
					await Task.Run(() =>
					{
						CreateDirectoryIfNoneExists(GameConfigFolder);
						CreateDirectoryIfNoneExists(DataFolder);
					});

					var precedePath = Path.Combine(PrecedeFolder, "data", "win32");

					if (File.Exists(PrecedeTxtPath) && Directory.Exists(precedePath))
					{
						await PullManagementData();
						if (!ManagementData.ContainsKey("PrecedeVersion") || !ManagementData.ContainsKey("PrecedeCurrent"))
						{
							var result = await this.ShowMessageAsync(Text.ApplyPrecede, Text.ApplyPrecedeNow, AffirmNeg, YesNo);

							if (result == MessageDialogResult.Affirmative)
							{
								// TODO: not this
								CancelCheckButton.IsEnabled = false;
								PauseCheckButton.IsEnabled = false;

								var files = await Task.Run(() => Directory.GetFiles(precedePath));
								CheckProgressbar.Maximum = files.Length;

								foreach (var file in files)
								{
									CurrentCheckActionLabel.Content = Path.GetFileName(file);

									try
									{
										await Task.Run(() => MoveAndOverwriteFile(file, Path.Combine(DataFolder, Path.GetFileName(file ?? string.Empty))));
									}
									catch (Exception)
									{
										// ignored
									}

									CompletedCheckActionsLabel.Content = Text.ApplyingPrecede.Format(++CheckProgressbar.Value, CheckProgressbar.Maximum);
								}

								try
								{
									await Task.Run(() => Directory.Delete(PrecedeFolder, true));
								}
								catch (Exception)
								{
									await this.ShowMessageAsync(Text.Error, Text.PrecedeDeleteFailed);
								}

								// TODO: not this
								CancelCheckButton.IsEnabled = true;
								PauseCheckButton.IsEnabled = true;

								method = UpdateMethod.FileCheck;
							}
						}
					}

					var launcherlist = await manager.DownloadStringTaskAsync(LauncherListUrl);
					var newlist = await manager.DownloadStringTaskAsync(PatchListUrl);
					var oldlist = await manager.DownloadStringTaskAsync(PatchListOldUrl);

					var launcherlistdata = ParsePatchList(launcherlist).ToArray();
					var newlistdata = ParsePatchList(newlist).ToArray();
					var oldlistdata = ParsePatchList(oldlist).ToArray();

					await RestoreAllPatchBackups();
					SetPatchToggleSwitches();

					if (method == UpdateMethod.Update && Directory.Exists(GameConfigFolder))
					{
						var entryComparer = new PatchListEntryComparer();

						if (File.Exists(LauncherListPath))
						{
							var storedLauncherlist = await Task.Run(() => ParsePatchList(File.ReadAllText(LauncherListPath)));
							launcherlistdata = launcherlistdata.Except(storedLauncherlist, entryComparer).ToArray();
						}

						if (File.Exists(PatchListPath))
						{
							var storedNewlist = await Task.Run(() => ParsePatchList(File.ReadAllText(PatchListPath)));
							newlistdata = newlistdata.Except(storedNewlist, entryComparer).ToArray();
						}

						if (File.Exists(PatchListOldPath))
						{
							var storedOldlist = await Task.Run(() => ParsePatchList(File.ReadAllText(PatchListOldPath)));
							oldlistdata = oldlistdata.Except(storedOldlist, entryComparer).ToArray();
						}
					}

					var lists = launcherlistdata.Concat(newlistdata.Concat(oldlistdata)).ToArray();
					var groups = (from v in lists group v by v.Name into d select d.First()).ToArray();

					CheckProgressbar.Maximum = groups.Length;

					for (var index = 0; index < groups.Length;)
					{
						_checkCancelSource.Token.ThrowIfCancellationRequested();

						if (_isCheckPaused)
						{
							PauseCheckButton.Content = Text.Resume;
							await pause();
							PauseCheckButton.Content = Text.Pause;
						}
						else
						{
							var data = groups[index];
							CurrentCheckActionLabel.Content = Path.GetFileNameWithoutExtension(data.Name);
							var filePath = MakeLocalToGame(Path.ChangeExtension(data.Name, null));

							var upToDate = await Task.Run(() => IsFileUpToDate(filePath, data.Size, data.Hash));
							CheckProgressbar.Value = ++index;
							CompletedCheckActionsLabel.Content = Text.CheckedOf.Format(index, groups.Length);

							if (!upToDate)
							{
								var patPath = MakeLocalToGame(data.Name);

								fileOperations.Add(manager.DownloadFileTaskAsync(newlistdata.Contains(data) || launcherlistdata.Contains(data) ? new Uri(BasePatch, data.Name) : new Uri(BasePatchOld, data.Name), patPath).ContinueWith(x => MoveAndOverwriteFile(patPath, filePath)));

								numberToDownload++;
								CompletedCheckDownloadActionsLabel.Content = Text.DownloadedOf.Format(numberDownloaded, numberToDownload);
							}
						}
					}

					numberToDownload++;
					fileOperations.Add(manager.DownloadFileTaskAsync(VersionUrl, VersionPath));

					var downloads = Task.WhenAll(fileOperations);

					while (!downloads.IsCompleted && !downloads.IsCanceled && !downloads.IsFaulted)
					{
						_checkCancelSource.Token.ThrowIfCancellationRequested();

						if (_isCheckPaused)
						{
							await pause();
						}
						else
						{
							await Task.Delay(16);
						}
					}

					await downloads;

					await Task.Run(() =>
					{
						File.WriteAllText(LauncherListPath, launcherlist);
						File.WriteAllText(PatchListPath, newlist);
						File.WriteAllText(PatchListOldPath, oldlist);
						if (File.Exists(VersionPath))
							SetTweakerRemoteVersion(File.ReadAllText(VersionPath));
					});
				}
				catch when (_checkCancelSource.IsCancellationRequested)
				{
					manager.CancelDownloads();
					_gameTabController.PreviousTab();
					return false;
				}

			}

			if (GameTabItem.IsSelected)
			{
				FlashWindow(this, true);
				if (numberDownloaded > 4)
				{
					await this.ShowMessageAsync(Text.Updated, Text.FilesDownloaded.Format(numberDownloaded - 4));
				}
				else
				{
					await this.ShowMessageAsync(Text.Complete, Text.AllFilesValid);
				}
			}

			_gameTabController.PreviousTab();

			return true;
		}

		public async Task<bool> ConfigProxy()
		{
			try
			{
				var url = await this.ShowInputAsync(Text.EnterProxy, string.Empty);

				if (url != null)
				{
					return await SetupProxy(url);
				}
			}
			catch (Exception ex)
			{
				await this.ShowMessageAsync(Text.Error, ex.Message);
			}

			return false;
		}

		public async Task<bool> SetupProxy(string url)
		{
			using (var client = AquaClient)
			{
				var json = await client.DownloadStringTaskAsync(new Uri(url));
				dynamic jsonData = JsonConvert.DeserializeObject(json);

				if (jsonData.version != 1)
				{
					await this.ShowMessageAsync(string.Empty, Text.BadProxyVersion);
					return false;
				}

				var hostAddress = (await Dns.GetHostAddressesAsync((string)jsonData.host))?.FirstOrDefault()?.ToString();
				var file = StripProxyEntries(File.ReadAllLines(HostsPath)).ToArray();
				var lines = file.Concat(new[]
				{
					"# Dogstar Proxy Start", $"{hostAddress} gs001.pso2gs.net # {jsonData.name} Ship 01", $"{hostAddress} gs016.pso2gs.net # {jsonData.name} Ship 02", $"{hostAddress} gs031.pso2gs.net # {jsonData.name} Ship 03", $"{hostAddress} gs046.pso2gs.net # {jsonData.name} Ship 04", $"{hostAddress} gs061.pso2gs.net # {jsonData.name} Ship 05", $"{hostAddress} gs076.pso2gs.net # {jsonData.name} Ship 06", $"{hostAddress} gs091.pso2gs.net # {jsonData.name} Ship 07", $"{hostAddress} gs106.pso2gs.net # {jsonData.name} Ship 08", $"{hostAddress} gs121.pso2gs.net # {jsonData.name} Ship 09", $"{hostAddress} gs136.pso2gs.net # {jsonData.name} Ship 10", "# Dogstar Proxy End"
				});

				File.WriteAllLines(HostsPath, lines);

				client.DownloadFileAsync(new Uri((string)jsonData.publickeyurl), Path.Combine(Settings.Default.GameFolder, "publickey.blob"));

				var gameHost = (await Dns.GetHostAddressesAsync("gs001.pso2gs.net"))?.FirstOrDefault()?.ToString();

				if (gameHost != hostAddress)
				{
					await this.ShowMessageAsync(Text.Failed, Text.ProxyRevert);
					File.WriteAllLines(HostsPath, file);
					return false;
				}

				return true;
			}
		}

		private async Task<bool> InstallGame(string path)
		{
			try
			{
				CreateDirectoryIfNoneExists(path);
				Settings.Default.GameFolder = path;

				if (await CheckGameFiles(UpdateMethod.FileCheck))
				{
					Settings.Default.IsGameInstalled = true;
					Settings.Default.Save();
				}

				return true;
			}
			catch (Exception)
			{
				Settings.Default.GameFolder = string.Empty;
				Settings.Default.IsGameInstalled = false;
				Settings.Default.Save();
			}

			return false;
		}

		private async Task SetupGameInfo()
		{
			var result = await this.ShowMessageAsync(Text.HaveInstalled, string.Empty, AffirmNeg, YesNo);

			if (result != MessageDialogResult.Affirmative)
			{
				result = await this.ShowMessageAsync(Text.WouldYoulikeToInstall, string.Empty, AffirmNeg, YesNo);

				if (result == MessageDialogResult.Affirmative)
				{
					var path = await this.ShowFolderSelectAsync(string.Empty, Text.SelectInstallLocation, Properties.Resources.DefaultInstallDir);

					while (!string.IsNullOrWhiteSpace(path) && !await InstallGame(Path.Combine(path, "PHANTASYSTARONLINE2", "pso2_bin")))
					{
						if (await this.ShowMessageAsync(string.Empty, Uri.IsWellFormedUriString(path, UriKind.Absolute) ? Text.InvalidPathTryAgain : Text.GoneWrong, AffirmNeg, YesNo) == MessageDialogResult.Negative)
						{
							break;
						}

						path = await this.ShowFolderSelectAsync(string.Empty, Text.SelectInstallLocation, Properties.Resources.DefaultInstallDir);
					}
				}
			}
			else
			{
				await SelectGameFolder();
			}

			EnableGameButtions(Settings.Default.IsGameInstalled);
		}

		private async Task SelectGameFolder()
		{
			var result = MessageDialogResult.Affirmative;

			while (result == MessageDialogResult.Affirmative)
			{
				var gamePath = await this.ShowFileSelectAsync(string.Empty, Text.SelectExe, Properties.Resources.GameFilter);

				if (await Task.Run(() => !string.IsNullOrWhiteSpace(gamePath) && File.Exists(gamePath)))
				{
					Settings.Default.GameFolder = Path.GetDirectoryName(gamePath);
					Settings.Default.IsGameInstalled = true;
					Settings.Default.Save();
					EnableGameButtions(Settings.Default.IsGameInstalled);
					result = MessageDialogResult.Negative;
				}
				else if (gamePath == null)
				{
					result = MessageDialogResult.Negative;
				}
				else
				{
					result = await this.ShowMessageAsync(string.Empty, Text.InvalidPathTryAgain, AffirmNeg, YesNo);
				}
			}
		}

		private async Task<bool> DownloadLanguagePatch(Uri baseUri, string name, int size, string patchname)
		{
			var url = new Uri(baseUri, name);
			var filepath = Path.Combine(Path.GetTempPath(), name);
			var info = new FileInfo(filepath);

			if (!info.Exists || info.Length != size)
			{
				await _generalDownloadManager.DownloadFileTaskAsync(url, filepath);
			}

			GeneralDownloadIsIndeterminate(true);
			var succeeded = await Task.Run(() => InstallPatchArchive(filepath, patchname));
			GeneralDownloadIsIndeterminate(false);

			if (succeeded)
			{
				await this.ShowMessageAsync(Text.Complete, string.Format(Text.GenericPatchSuccess, patchname));
			}
			else
			{
				await this.ShowMessageAsync(Text.Failed, string.Format(Text.GenericPatchFailed, patchname));
			}

			info.Refresh();
			if (info.Exists)
			{
				File.Delete(filepath);
			}

			return succeeded;
		}

		private async Task<long> DownloadJpFile(string dir, string filename, string patchname)
		{
			var dataFolder = DataFolder;
			var backupFolder = Path.Combine(dataFolder, "backup");
			var dataFilename = Path.Combine(dataFolder, filename);
			var backupFilename = Path.Combine(backupFolder, patchname, filename);
			var downloadFilename = Path.Combine(Path.GetTempPath(), filename);

			dynamic jsonData = await GetArghlexJson(dir);
			dynamic entry = ((JArray)jsonData.files).Select(x => (dynamic)x).FirstOrDefault(x => x.name == filename);

			if (entry == null)
				return 0;

			var modtime = (long)entry.modtime;

			try
			{
				await _generalDownloadManager.DownloadFileTaskAsync(new Uri(new Uri(Arghlex, dir + "/"), filename), downloadFilename);
			}
			catch (Exception)
			{
				return 0;
			}

			if (!File.Exists(backupFilename))
			{
				await Task.Run(() =>
				{
					CreateDirectoryIfNoneExists(Path.Combine(backupFolder, patchname));
					File.Move(dataFilename, backupFilename);
					File.Move(downloadFilename, dataFilename);
				});
			}

			return modtime;
		}

		private async Task<bool> CheckEnglishPatchVersion(long modtime)
		{
			if (Settings.Default.InstalledEnglishPatch != 0 && Settings.Default.InstalledEnglishPatch < modtime)
			{
				var result = await this.ShowMessageAsync(Text.NewLangPatch, string.Format(Text.NewGenericPatch, "English Patch"), AffirmNeg, YesNo);
				return result == MessageDialogResult.Affirmative;
			}

			return false;
		}

		private async Task<bool> CheckLargeFilesVersion(long modtime)
		{
			if (Settings.Default.InstalledLargeFiles != 0 && Settings.Default.InstalledLargeFiles < modtime)
			{
				var result = await this.ShowMessageAsync(Text.NewLangPatch, string.Format(Text.NewGenericPatch, "Large Files"), AffirmNeg, YesNo);
				return result == MessageDialogResult.Affirmative;
			}

			return false;
		}

		private async Task DownloadEnglishPatch(string name, int size, long modtime)
		{
			if (await DownloadLanguagePatch(Arghlex, name, size, "EnglishPatch"))
			{
				Settings.Default.InstalledEnglishPatch = modtime;
			}
			else
			{
				EnglishPatchToggle.IsChecked = false;
				Settings.Default.InstalledEnglishPatch = 0;
			}

			Settings.Default.Save();
		}

		private async Task DownloadLargeFiles(string name, int size, long modtime)
		{
			if (await DownloadLanguagePatch(Arghlex, name, size, "LargeFiles"))
			{
				Settings.Default.InstalledLargeFiles = modtime;
			}
			else
			{
				LargeFilesToggle.IsChecked = false;
				Settings.Default.InstalledLargeFiles = 0;
			}

			Settings.Default.Save();
		}

		public void Dispose()
		{
			_generalDownloadManager.Dispose();
		}
		#endregion Functions


	}
}
