using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32; // WPF OpenFileDialog
using XboxSteamCoverArtFixer.Models;
using XboxSteamCoverArtFixer.Services;

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

            // Sharper preview rendering (blurriness fix)
            RenderOptions.SetBitmapScalingMode(PreviewImage, BitmapScalingMode.HighQuality);

            var apiKey = Config.SteamGridDbApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                apiKey = Environment.GetEnvironmentVariable("STEAMGRIDDB_API_KEY");
            if (!string.IsNullOrWhiteSpace(apiKey))
                _sgdb = new SteamGridDbClient(apiKey);
        }

        // ====================== Scan (Steam) ======================
        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SteamFolder;

                if (!Directory.Exists(path))
                {
                    MessageBox.Show(
                        $"Default path not found:\n{SteamFolder}\n\nPick the Xbox Steam cache folder.",
                        "Scan", MessageBoxButton.OK, MessageBoxImage.Information);

                    var dlg = new System.Windows.Forms.FolderBrowserDialog
                    {
                        Description = "Select the Xbox app's Steam cache folder",
#if NET6_0_OR_GREATER
                        UseDescriptionForTitle = true,
#endif
                        ShowNewFolderButton = false
                    };
                    dlg.ShowDialog();
                    if (!string.IsNullOrWhiteSpace(dlg.SelectedPath) && Directory.Exists(dlg.SelectedPath))
                        path = dlg.SelectedPath;
                    else
                        return;
                }

                ResetUi();

                // Read ALL pngs from the Steam folder (top-level first, then recursive)
                var files = Directory.EnumerateFiles(path, "*.png", SearchOption.TopDirectoryOnly)
                                     .Concat(Directory.EnumerateFiles(path, "*.png", SearchOption.AllDirectories))
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

                foreach (var f in files)
                {
                    try { _items.Add(new GameImageItem(f, LibrarySource.Steam)); } catch { }
                }

                SelectedInfo.Text = files.Count > 0
                    ? $"Found {files.Count} image(s) in: {path}"
                    : $"No PNG images found in: {path}";
                GameTitle.Text = $"Steam Cache — {path}";

                // Resolve names via SteamGridDB only for items with AppIDs
                if (_sgdb != null && _items.Any(i => i.Source == LibrarySource.Steam && !string.IsNullOrWhiteSpace(i.GameId)))
                    await PopulateGameNamesAsync(_sgdb, _items.Where(i => i.Source == LibrarySource.Steam).ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================== Scan Other Folders (GOG/Epic/Ubisoft) ======================
        private void ScanOtherButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ResetUi();

                var libs = new[] { "GOG", "Epic", "Ubisoft" };
                foreach (var lib in libs)
                {
                    var p = Path.Combine(_thirdPartyRoot, lib);
                    if (!Directory.Exists(p)) continue; // never create anything

                    var files = Directory.EnumerateFiles(p, "*.png", SearchOption.TopDirectoryOnly)
                                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

                    foreach (var f in files)
                    {
                        try
                        {
                            var item = new GameImageItem(f, LibrarySource.Other)
                            {
                                GameName = FriendlyName(Path.GetFileNameWithoutExtension(f), lib)
                            };
                            _items.Add(item);
                        }
                        catch { }
                    }
                }

                SelectedInfo.Text = _items.Count > 0
                    ? $"Loaded {_items.Count} image(s) from GOG/Epic/Ubisoft."
                    : "No PNG files found in GOG/Epic/Ubisoft folders.";
                GameTitle.Text = "Other Libraries (GOG / Epic / Ubisoft)";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan Other Folders", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string FriendlyName(string stem, string lib)
        {
            string s = stem;
            foreach (var pref in new[] { "GOG-", "Epic-", "Ubisoft-", "Steam-" })
                if (s.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
                    s = s[pref.Length..];

            s = s.Replace('_', ' ').Replace('-', ' ').Trim();
            return string.IsNullOrWhiteSpace(s) ? lib : $"{s} ({lib})";
        }

        // ====================== Selection ======================
        private void ImagesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var item = ImagesList.SelectedItem as GameImageItem;

            ReplaceButton.IsEnabled = (item != null && item.Source == LibrarySource.Steam);
            AddYourImageButton.IsEnabled = (item != null);

            if (item != null)
            {
                GameTitle.Text = item.GameName ?? "";
                SelectedInfo.Text = item.Source == LibrarySource.Steam && !string.IsNullOrWhiteSpace(item.GameId)
                    ? $"File: {item.FileName}  •  GameId: {item.GameId}"
                    : $"File: {item.FileName}";
                PreviewImage.Source = LoadFull(item.FilePath);
            }
            else
            {
                GameTitle.Text = "";
                SelectedInfo.Text = "Select an item...";
                PreviewImage.Source = null;
            }
        }

        // ====================== Download Cover Art (SteamGridDB) ======================
        private async void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            var item = ImagesList.SelectedItem as GameImageItem;
            if (item == null) return;

            if (item.Source != LibrarySource.Steam)
            {
                MessageBox.Show("This entry is not from Steam. Use 'Add Your Image' instead.",
                    "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_sgdb is null)
            {
                MessageBox.Show("SteamGridDB API key missing.", "API", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Robust AppID extraction (fallback to re-extract from filename)
            var appId = item.GameId ?? ExtractSteamAppIdFromFileName(item.FilePath);
            if (string.IsNullOrWhiteSpace(appId))
            {
                MessageBox.Show("Could not parse Steam AppID from the filename.",
                    "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ReplaceButton.IsEnabled = false;
                AddYourImageButton.IsEnabled = false;

                // Ensure SGDB id/name present
                if (item.SgdbGameId is null)
                {
                    var game = await _sgdb.ResolveGameFromSteamAppIdAsync(appId);
                    if (game == null)
                    {
                        MessageBox.Show($"SteamGridDB has no match for Steam AppID {appId}.",
                            "SteamGridDB", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    item.SetGameInfo(game.Id, game.Name);
                }

                var icons = await _sgdb.GetIconsForGameAsync(item.SgdbGameId!.Value);
                if (icons.Count == 0)
                {
                    MessageBox.Show("No icons found for this game.", "SteamGridDB",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // (Quality) Prefer larger icons first if API provides dimensions
                icons = icons
                    .OrderByDescending(i => i.Width)   // Width/Height supported by SGDB
                    .ThenByDescending(i => i.Height)
                    .ToList();

                var picker = new IconPickerWindow(icons, item.GameName ?? $"Steam {appId}") { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedUrl is string chosenUrl)
                {
                    var bytes = await _sgdb.DownloadBytesAsync(chosenUrl);
                    if (bytes is null || bytes.Length == 0)
                    {
                        MessageBox.Show($"Failed to download the selected icon.\n\nURL:\n{chosenUrl}",
                            "Download", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Keep bytes as-is (no re-encode), only convert if not a PNG
                    var pngBytes = ImageHelper.EnsurePng(bytes);

                    // No backup: overwrite directly
                    File.WriteAllBytes(item.FilePath, pngBytes);

                    PreviewImage.Source = LoadFull(item.FilePath);
                    MessageBox.Show("Icon replaced successfully!", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Download error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ReplaceButton.IsEnabled = ImagesList.SelectedItem is GameImageItem g && g.Source == LibrarySource.Steam;
                AddYourImageButton.IsEnabled = ImagesList.SelectedItem != null;
            }
        }

        // Extracts a Steam AppID from any filename form:
        // "Steam-1150690.png", "steam_1150690", "steam1150690", etc.
        private static string? ExtractSteamAppIdFromFileName(string path)
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            // prefer a "steam" prefix if present
            var m = Regex.Match(stem, @"(?i)steam[-_ ]?(\d{3,9})");
            if (m.Success) return m.Groups[1].Value;

            // otherwise, any 3–9 digit run in the name
            m = Regex.Match(stem, @"(?<!\d)(\d{3,9})(?!\d)");
            return m.Success ? m.Groups[1].Value : null;
        }

        // ====================== Add Your Image (Crop to 1:1) ======================
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

            if (ofd.ShowDialog(this) != true) return;

            try
            {
                var bytes = File.ReadAllBytes(ofd.FileName);
                var cropper = new CropWindow(bytes, System.IO.Path.GetFileName(ofd.FileName)) { Owner = this };

                if (cropper.ShowDialog() == true && cropper.CroppedPng is { Length: > 0 } pngBytes)
                {
                    File.WriteAllBytes(item.FilePath, pngBytes); // direct overwrite
                    PreviewImage.Source = LoadFull(item.FilePath);
                    MessageBox.Show("Image replaced successfully!", "Done",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Add Your Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ====================== Helpers ======================
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

        private static BitmapImage LoadFull(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            // Keep full fidelity in preview, render high quality via BitmapScalingMode
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
