using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace XboxSteamCoverArtFixer.Models
{
    public enum LibrarySource { Steam, Other }

    public class GameImageItem : INotifyPropertyChanged
    {
        public string FilePath { get; }
        public string FileName => Path.GetFileName(FilePath);
        public LibrarySource Source { get; }               // NEW

        public string? GameId { get; }                     // Steam AppID if Steam
        private int? _sgdbGameId;
        public int? SgdbGameId
        {
            get => _sgdbGameId;
            set { if (_sgdbGameId != value) { _sgdbGameId = value; OnPropertyChanged(); } }
        }

        private string? _gameName;
        public string? GameName
        {
            get => _gameName;
            set { if (_gameName != value) { _gameName = value; OnPropertyChanged(); } }
        }

        public BitmapImage Thumbnail { get; }

        private static readonly Regex IdRegex = new(@"Steam-(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public GameImageItem(string path, LibrarySource source = LibrarySource.Steam)
        {
            FilePath = path;
            Source = source;
            GameId = (source == LibrarySource.Steam) ? TryExtractSteamId(path) : null;
            Thumbnail = LoadThumb(path);
        }

        public void SetGameInfo(int? sgdbId, string? name)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
            {
                disp.Invoke(() => { SgdbGameId = sgdbId; GameName = name; });
            }
            else
            {
                SgdbGameId = sgdbId;
                GameName = name;
            }
        }

        private static string? TryExtractSteamId(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var m = IdRegex.Match(name);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static BitmapImage LoadThumb(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 96;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
