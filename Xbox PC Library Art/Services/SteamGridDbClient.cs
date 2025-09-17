using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace XboxSteamCoverArtFixer.Services
{
    public class SteamGridDbClient
    {
        private readonly HttpClient _api;
        private static readonly HttpClient _cdn = CreateCdnClient();

        public SteamGridDbClient(string apiKey)
        {
            _api = new HttpClient { BaseAddress = new Uri("https://www.steamgriddb.com/api/v2/") };
            _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _api.DefaultRequestHeaders.UserAgent.ParseAdd("XboxSteamCoverArtFixer/1.1");
            _api.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        private static HttpClient CreateCdnClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            var c = new HttpClient(handler);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("XboxSteamCoverArtFixer/1.1");
            return c;
        }

        // NEW: resolve BOTH SGDB id and name from Steam AppID (the number in Steam-<id>.png)
        public async Task<SgdbGame?> ResolveGameFromSteamAppIdAsync(string steamAppId)
        {
            var resp = await _api.GetAsync($"games/steam/{steamAppId}");
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await JsonSerializer.DeserializeAsync<SgdbGameResponse>(await resp.Content.ReadAsStreamAsync());
            return doc?.Data;
        }

        public async Task<List<SgdbIcon>> GetIconsForGameAsync(int sgdbGameId)
        {
            var resp = await _api.GetAsync($"icons/game/{sgdbGameId}?types=static");
            if (!resp.IsSuccessStatusCode) return new();
            var doc = await JsonSerializer.DeserializeAsync<SgdbIconResponse>(await resp.Content.ReadAsStreamAsync());
            return doc?.Data ?? new();
        }

        public async Task<byte[]?> DownloadBytesAsync(string url)
        {
            try
            {
                using var resp = await _cdn.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return null;
                return await resp.Content.ReadAsByteArrayAsync();
            }
            catch { return null; }
        }

        // DTOs
        public class SgdbGameResponse { [JsonPropertyName("data")] public SgdbGame? Data { get; set; } }
        public class SgdbGame
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
        }
        public class SgdbIconResponse { [JsonPropertyName("data")] public List<SgdbIcon> Data { get; set; } = new(); }
        public class SgdbIcon
        {
            [JsonPropertyName("id")] public int Id { get; set; }
            [JsonPropertyName("url")] public string Url { get; set; } = "";
            [JsonPropertyName("thumb")] public string? Thumb { get; set; }
            [JsonPropertyName("mime")] public string? Mime { get; set; }
        }
    }
}
