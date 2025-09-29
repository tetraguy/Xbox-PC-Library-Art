// MainWindow.xaml.cs  — revised per your 4 requests
// 1) Read ALL *.png in the Steam folder (not only Steam-*.png)
// 2) No prompt to browse other folders if none found (silent, just shows count)
// 3) Never creates any extra folders (no Directory.CreateDirectory anywhere)
// 4) No image backups are created (removed WriteBackupOnce calls)

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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

        // %LOCALAPPDATA%\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries
        private readonly string _thirdPartyRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\ThirdPartyLibraries");

        private string SteamFolder => Path.Combine(_thirdPartyRoot, "steam");

        public MainWindow()
        {
            InitializeComponent();
            ImagesList.ItemsSource = _items;

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

                // If default folder is missing, allow choosing the Steam cache folder once.
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
                        return; // stop silently if nothing picked
                }

                ResetUi();

                // Load *all* PNGs. If none at top, search recursively. (No prompts.)
                var files = EnumeratePngs(path);

                foreach (var f in files)
                {
                    try { _items.Add(new GameImageItem(f, LibrarySource.Steam)); } catch { }
                }

                SelectedInfo.Text = files.Count > 0
                    ? $"Found {files.Count} image(s) in: {path}"
                    : $"No PNG images found in: {path}";
                GameTitle.Text = $"Steam Cache — {path}";

                // Resolve names via SteamGridDB only for items that expose a Steam AppID
                if (_sgdb != null && _items.Any(i => i.Source == LibrarySource.Steam))
                    await PopulateGameNamesAsync(_sgdb, _items.Where(i => i.Source == LibrarySource.Steam).ToList());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper: enumerate *.png (case-insensitive), top-level then recursive
        private static System.Collections.Generic.List<string> EnumeratePngs(string root)
        {
            var list = Directory.EnumerateFiles(root, "*.png", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();

            if (list.Count == 0)
            {
                list = Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories)
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToList();
            }
            return list;
        }

        // ====================== Scan Other Folders (GOG/Epic/Ubisoft) ======================
        // (This never creates folders; it only reads existing ones and loads their PNGs.)
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
                                GameName = FriendlyName(Path.GetFileNameWithoutExtension(f), lib)
                            };
                            _items.Add(item);
                        }
                        catch { /* ignore */ }
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

            // Enable SGDB only if it's a Steam item *and* we successfully parsed an AppID
            ReplaceButton.IsEnabled = (item != null &&
                                       item.Source == LibrarySource.Steam &&
                                       !string.IsNullOrWhiteSpace(item.GameId));

            // Add Your Image is always allowed
            AddYourImageButton.IsEnabled = (item != null);

            if (item != null)
            {
                GameTitle.Text = item.GameName ?? "";
                SelectedInfo.Text = (item.Source == LibrarySource.Steam && !string.IsNullOrWhiteSpace(item.GameId))
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

            if (string.IsNullOrWhiteSpace(item.GameId))
            {
                MessageBox.Show("Could not parse Steam AppID from the filename.",
                    "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            if (_sgdb is null)
            {
                MessageBox.Show("SteamGridDB API key missing.", "API", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(item.GameId))
            {
                MessageBox.Show("Could not parse Steam AppID from the filename.",
                    "Download Cover Art", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                ReplaceButton.IsEnabled = false;
                AddYourImageButton.IsEnabled = false;

                if (item.SgdbGameId is null)
                {
                    var game = await _sgdb.ResolveGameFromSteamAppIdAsync(item.GameId!);
                    if (game == null)
                    {
                        MessageBox.Show($"Could not resolve SteamGridDB game for Steam AppID {item.GameId}.",
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

                var picker = new IconPickerWindow(icons, item.GameName ?? $"Steam {item.GameId}") { Owner = this };
                if (picker.ShowDialog() == true && picker.SelectedUrl is string chosenUrl)
                {
                    var bytes = await _sgdb.DownloadBytesAsync(chosenUrl);
                    if (bytes is null || bytes.Length == 0)
                    {
                        MessageBox.Show($"Failed to download the selected icon.\n\nURL:\n{chosenUrl}",
                            "Download", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var pngBytes = ImageHelper.EnsurePng(bytes);
                    // No backup: directly overwrite the file as requested
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
                    // No backup: directly overwrite the file as requested
                    File.WriteAllBytes(item.FilePath, pngBytes);
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
                    catch { /* ignore */ }
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
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
