using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using XboxSteamCoverArtFixer.Services;

namespace XboxSteamCoverArtFixer
{
    public partial class IconPickerWindow : Window
    {
        private readonly List<SteamGridDbClient.SgdbIcon> _icons;
        private readonly HttpClient _http = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        });
        private readonly CancellationTokenSource _cts = new();

        private string? _selectedUrl;
        public string? SelectedUrl => _selectedUrl;

        public IconPickerWindow(List<SteamGridDbClient.SgdbIcon> icons, string gameName)
        {
            InitializeComponent();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("XboxSteamCoverArtFixer/1.1");

            _icons = icons;
            GameTitle.Text = gameName;

            LoadIcons();
            _ = LoadPreviewsAsync(_cts.Token);
        }

        private void LoadIcons()
        {
            IconsWrap.Items.Clear();
            foreach (var i in _icons)
            {
                var urlForPreview = string.IsNullOrWhiteSpace(i.Thumb) ? i.Url : i.Thumb;

                var border = new Border
                {
                    Width = 140,
                    Height = 140,
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6),
                    Margin = new Thickness(6),
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Tag = i.Url // store full url for final download
                };

                var grid = new Grid();
                var bg = new Border { Background = new SolidColorBrush(Color.FromRgb(242, 244, 248)), CornerRadius = new CornerRadius(8) };
                var img = new Image { Stretch = Stretch.Uniform };
                var loading = new TextBlock { Text = "Loading...", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

                grid.Children.Add(bg);
                grid.Children.Add(img);
                grid.Children.Add(loading);

                border.Child = grid;

                border.MouseLeftButtonUp += (_, __) => Select(border);

                // stash the preview url on the Image.Tag so we can load later
                img.Tag = urlForPreview;

                IconsWrap.Items.Add(border);
            }
        }

        private async Task LoadPreviewsAsync(CancellationToken ct)
        {
            var gates = new SemaphoreSlim(8);
            var borders = IconsWrap.Items.OfType<Border>().ToList();

            var tasks = borders.Select(async border =>
            {
                await gates.WaitAsync(ct);
                try
                {
                    var grid = (Grid)border.Child!;
                    var img = (Image)grid.Children[1];
                    var loading = (TextBlock)grid.Children[2];
                    var url = img.Tag as string;

                    var bmp = await TryLoadBitmapAsync(url!, ct)
                              ?? BytesToBitmap(EnsurePngForPreview(await SafeGetBytesAsync(url!, ct)));

                    if (bmp != null)
                    {
                        img.Source = bmp;
                        loading.Visibility = Visibility.Collapsed;
                    }
                }
                catch { /* ignore */ }
                finally { gates.Release(); }
            }).ToList();

            await Task.WhenAll(tasks);
        }

        private async Task<BitmapImage?> TryLoadBitmapAsync(string url, CancellationToken ct)
        {
            if (url.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(url, UriKind.Absolute);
                    bmp.DecodePixelWidth = 256;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { }
            }
            return null;
        }

        private async Task<byte[]?> SafeGetBytesAsync(string url, CancellationToken ct)
        {
            try
            {
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch { return null; }
        }

        private static byte[] EnsurePngForPreview(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return Array.Empty<byte>();
            if (bytes.Length > 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
                return bytes;

            try
            {
                using var ms = new MemoryStream(bytes);
                var dec = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = dec.Frames[0];
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(frame));
                using var outMs = new MemoryStream();
                enc.Save(outMs);
                return outMs.ToArray();
            }
            catch { return bytes; }
        }

        private static BitmapImage? BytesToBitmap(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.DecodePixelWidth = 256;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private void Select(Border chosen)
        {
            foreach (var child in IconsWrap.Items.OfType<Border>())
            {
                child.BorderBrush = Brushes.LightGray;
                child.BorderThickness = new Thickness(1);
            }
            chosen.BorderBrush = Brushes.DodgerBlue;
            chosen.BorderThickness = new Thickness(3);
            _selectedUrl = chosen.Tag as string;
            ConfirmButton.IsEnabled = _selectedUrl != null;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            DialogResult = false;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedUrl != null)
            {
                DialogResult = true;
            }
        }
    }
}
