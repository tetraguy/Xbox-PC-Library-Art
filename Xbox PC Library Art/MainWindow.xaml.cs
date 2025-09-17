using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using XboxSteamCoverArtFixer.Models;
using XboxSteamCoverArtFixer.Services;
using WinForms = System.Windows.Forms;

namespace XboxSteamCoverArtFixer
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GameImageItem> _items = new();
        private SteamGridDbClient? _sgdb;

        private readonly string _defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries\Steam");

        public MainWindow()
        {
            InitializeComponent();
            ImagesList.ItemsSource = _items;

            var apiKey = XboxSteamCoverArtFixer.Services.Config.SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("SteamGridDB API key is missing.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                _sgdb = new SteamGridDbClient(apiKey);
            }
        }

        // 1) Rescan always clears and reloads (images + game names)
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = _defaultPath;

                if (!Directory.Exists(path))
                {
                    MessageBox.Show(
                        $"Default path not found:\n{_defaultPath}\n\nPick the Xbox Steam cache folder.",
                        "Scan",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // pick folder when default path isn't found
                    var dlg = new WinForms.FolderBrowserDialog
                    {
                        Description = "Select the Xbox app's Steam cache folder",
                        // UseDescriptionForTitle is available on .NET 6+; if your IDE balks, remove it.
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false
                    };

                    var result = dlg.ShowDialog();  // System.Windows.Forms.DialogResult
                    if (result == WinForms.GetDialogResult() && Directory.Exists(dlg.SelectedPath))
                    {
                        path = dlg.SelectedPath;
                    }
                    else
                    {
                        return; // user cancelled
                    }

                }

                // Clear UI
                _items.Clear();
                PreviewImage.Source = null;
                GameTitle.Text = "";
                SelectedInfo.Text = "Select an item...";
                ReplaceButton.IsEnabled = false;

                // Load files
                var files = Directory.EnumerateFiles(path, "*.png", SearchOption.TopDirectoryOnly)
                                     .Where(f => Path.GetFileName(f).StartsWith("Steam-", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

                foreach (var f in files)
                {
                    try { _items.Add(new GameImageItem(f)); } catch { /* ignore bad */ }
                }

                // Resolve names in background
                if (_sgdb != null)
                {
                    await PopulateGameNamesAsync(_sgdb, _items);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Resolve SGDB id + game name for each item (throttled)
        private static async Task PopulateGameNamesAsync(SteamGridDbClient client, ObservableCollection<GameImageItem> items)
        {
            using var gate = new SemaphoreSlim(6);
            var tasks = items
                .Where(i => !string.IsNullOrWhiteSpace(i.GameId))
                .Select(async i =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        var game = await client.ResolveGameFromSteamAppIdAsync(i.GameId!);
                        // Notify UI via SetGameInfo (raises PropertyChanged on UI thread)
                        i.SetGameInfo(game?.Id, game?.Name);
                    }
                    catch
                    {
                        // leave as-is if lookup fails
                    }
                    finally { gate.Release(); }
                }).ToList();

            await Task.WhenAll(tasks);
        }


        private void ImagesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var item = ImagesList.SelectedItem as GameImageItem;
            ReplaceButton.IsEnabled = item != null;

            if (item != null)
            {
                GameTitle.Text = item.GameName ?? "";
                SelectedInfo.Text = $"File: {item.FileName}  •  GameId: {item.GameId ?? "N/A"}";
                PreviewImage.Source = LoadFull(item.FilePath);
            }
            else
            {
                GameTitle.Text = "";
                SelectedInfo.Text = "Select an item...";
                PreviewImage.Source = null;
            }
        }

        private static BitmapImage LoadFull(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // 2) Download Cover Art (was "Replace")
        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sgdb is null)
            {
                MessageBox.Show("SteamGridDB API key missing.", "API", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ImagesList.SelectedItem is not GameImageItem item || string.IsNullOrWhiteSpace(item.GameId))
            {
                MessageBox.Show("Select a valid 'Steam-<gameid>.png' item first.", "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ReplaceButton.IsEnabled = false;

                // Resolve game id/name if not yet set
                if (item.SgdbGameId is null)
                {
                    var game = await _sgdb.ResolveGameFromSteamAppIdAsync(item.GameId!);
                    if (game == null)
                    {
                        MessageBox.Show($"Could not resolve SteamGridDB game for Steam AppID {item.GameId}.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    item.SgdbGameId = game.Id;
                    item.GameName = game.Name;
                }

                var icons = await _sgdb.GetIconsForGameAsync(item.SgdbGameId.Value);
                if (icons.Count == 0)
                {
                    MessageBox.Show("No icons found for this game.", "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var picker = new IconPickerWindow(icons, item.GameName ?? $"Steam {item.GameId}") { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedUrl is string chosenUrl)
                {
                    var bytes = await _sgdb.DownloadBytesAsync(chosenUrl);
                    if (bytes is null || bytes.Length == 0)
                    {
                        MessageBox.Show($"Failed to download the selected icon.\n\nURL:\n{chosenUrl}", "Download", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var pngBytes = ImageHelper.EnsurePng(bytes);
                    ImageHelper.WriteBackupOnce(item.FilePath);
                    File.WriteAllBytes(item.FilePath, pngBytes);

                    PreviewImage.Source = LoadFull(item.FilePath);
                    MessageBox.Show("Icon replaced successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Download error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ReplaceButton.IsEnabled = ImagesList.SelectedItem != null;
            }
        }
     
        }
    }

