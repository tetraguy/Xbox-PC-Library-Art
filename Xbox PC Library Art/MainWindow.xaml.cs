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
using Microsoft.Win32; // for WPF OpenFileDialog (used in AddYourImage)

namespace XboxSteamCoverArtFixer
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<GameImageItem> _items = new();
        private SteamGridDbClient? _sgdb;

        private readonly string _thirdPartyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");

        private string SteamFolder => Path.Combine(_thirdPartyRoot, "Steam");

        public MainWindow()
        {
            InitializeComponent();
            ImagesList.ItemsSource = _items;

            var apiKey = XboxSteamCoverArtFixer.Services.Config.SteamGridDbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
                _sgdb = new SteamGridDbClient(apiKey);
        }

        // ---------- existing Steam scan ----------
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SteamFolder;

                if (!Directory.Exists(path))
                {
                    MessageBox.Show(
                        $"Default path not found:\n{SteamFolder}\n\nPick the Xbox Steam cache folder.",
                        "Scan",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);


                }

                ResetUi();

                var files = Directory.EnumerateFiles(path, "*.png", SearchOption.TopDirectoryOnly)
                                     .Where(f => Path.GetFileName(f).StartsWith("Steam-", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

                foreach (var f in files)
                {
                    try { _items.Add(new GameImageItem(f, LibrarySource.Steam)); } catch { }
                }

                if (_sgdb != null)
                    await PopulateGameNamesAsync(_sgdb, _items.Where(i => i.Source == LibrarySource.Steam).ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- NEW: Scan Other Folders (GOG/Epic/Ubisoft) ----------
        private void ScanOtherButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ResetUi();

                var libs = new[] { "GOG", "Epic", "Ubisoft" };
                foreach (var lib in libs)
                {
                    var p = Path.Combine(_thirdPartyRoot, lib);
                    if (!Directory.Exists(p)) continue;

                    var files = Directory.EnumerateFiles(p, "*.png", SearchOption.TopDirectoryOnly)
                                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (var f in files)
                    {
                        try
                        {
                            var item = new GameImageItem(f, LibrarySource.Other)
                            {
                                // Nice display name from filename (no API)
                                GameName = FriendlyName(Path.GetFileNameWithoutExtension(f))
                            };
                            _items.Add(item);
                        }
                        catch { /* ignore bad file */ }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan Other Folders", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FriendlyName(string stem)
        {
            // Strip common prefixes and prettify
            string s = stem;
            foreach (var pref in new[] { "GOG-", "Epic-", "Ubisoft-" })
                if (s.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                    s = s[pref.Length..];

            s = s.Replace('_', ' ').Replace('-', ' ').Trim();
            return s;
        }

        // ---------- shared helpers ----------
        private static async Task PopulateGameNamesAsync(SteamGridDbClient client, System.Collections.Generic.IEnumerable<GameImageItem> items)
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
                        i.SetGameInfo(game?.Id, game?.Name);
                    }
                    catch { }
                    finally { gate.Release(); }
                }).ToList();

            await Task.WhenAll(tasks);
        }

        private void ResetUi()
        {
            _items.Clear();
            PreviewImage.Source = null;
            GameTitle.Text = "";
            SelectedInfo.Text = "Select an item...";
            ReplaceButton.IsEnabled = false;
            AddYourImageButton.IsEnabled = false;
        }

        private void ImagesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var item = ImagesList.SelectedItem as GameImageItem;

            // Only Steam items can use SteamGridDB button
            ReplaceButton.IsEnabled = (item != null && item.Source == LibrarySource.Steam);
            // Any item can use Add Your Image
            AddYourImageButton.IsEnabled = (item != null);

            if (item != null)
            {
                GameTitle.Text = item.GameName ?? "";
                SelectedInfo.Text = $"File: {item.FileName}" + (item.Source == LibrarySource.Steam && item.GameId != null ? $"  •  GameId: {item.GameId}" : "");
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

            if (ImagesList.SelectedItem is GameImageItem selected && selected.Source != LibrarySource.Steam)
            {
                MessageBox.Show("This entry is not from Steam. Use 'Add Your Image' to replace its artwork.", "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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


        private void AddYourImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (ImagesList.SelectedItem is not GameImageItem item) return;

            var ofd = new OpenFileDialog
            {
                Title = "Choose an image file",
                Filter = "Image Files|*.png;*.jpg;*.jpeg|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg",
                CheckFileExists = true,
                Multiselect = false
            };

            // WPF dialog returns bool?, no enum needed
            var ok = ofd.ShowDialog(this) == true;
            if (!ok) return;

            try
            {
                var bytes = File.ReadAllBytes(ofd.FileName);
                var cropper = new CropWindow(bytes, System.IO.Path.GetFileName(ofd.FileName)) { Owner = this };

                if (cropper.ShowDialog() == true && cropper.CroppedPng is { Length: > 0 } pngBytes)
                {
                    ImageHelper.WriteBackupOnce(item.FilePath);
                    File.WriteAllBytes(item.FilePath, pngBytes);
                    PreviewImage.Source = LoadFull(item.FilePath);
                    MessageBox.Show("Image replaced successfully!", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Add Your Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }

}

