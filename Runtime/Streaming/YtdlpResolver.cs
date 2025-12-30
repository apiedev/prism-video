using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Prism.Streaming
{
    public class YtdlpResolver : IStreamResolver
    {
        private static readonly string[] KnownHosts = {
            "youtube.com", "youtu.be", "www.youtube.com",
            "twitch.tv", "www.twitch.tv",
            "vimeo.com", "www.vimeo.com",
            "dailymotion.com", "www.dailymotion.com",
            "facebook.com", "www.facebook.com", "fb.watch",
            "twitter.com", "x.com",
            "instagram.com", "www.instagram.com",
            "tiktok.com", "www.tiktok.com"
        };

        private string _ytdlpPath;

        public string Name => "yt-dlp";
        public string YtdlpPath => _ytdlpPath;
        public bool IsAvailable => !string.IsNullOrEmpty(_ytdlpPath);

        public YtdlpResolver()
        {
            _ytdlpPath = FindYtdlp();
            if (string.IsNullOrEmpty(_ytdlpPath))
            {
                Debug.LogWarning("[Prism] yt-dlp not found. YouTube/Twitch streaming will not work. " +
                    "Install from: https://github.com/yt-dlp/yt-dlp");
            }
            else
            {
                Debug.Log($"[Prism] Found yt-dlp at: {_ytdlpPath}");
            }
        }

        public YtdlpResolver(string customPath)
        {
            _ytdlpPath = customPath;
        }

        public bool CanResolve(string url)
        {
            if (string.IsNullOrEmpty(url) || !IsAvailable)
                return false;

            try
            {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();

                foreach (var knownHost in KnownHosts)
                {
                    if (host.Contains(knownHost) || knownHost.Contains(host))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public async Task<StreamInfo> ResolveAsync(string url, StreamQuality quality = StreamQuality.Auto)
        {
            if (!IsAvailable)
            {
                return new StreamInfo { Error = "yt-dlp not found" };
            }

            try
            {
                var height = quality.ToHeight();
                var formatArg = height > 0
                    ? $"bestvideo[height<={height}][ext=mp4]+bestaudio[ext=m4a]/best[height<={height}][ext=mp4]/best"
                    : "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";

                // For live streams, we need different handling
                var isLive = await CheckIfLiveAsync(url);
                if (isLive)
                {
                    formatArg = height > 0
                        ? $"best[height<={height}]/best"
                        : "best";
                }

                var args = $"--no-warnings --no-check-certificate -f \"{formatArg}\" --get-url \"{url}\"";

                var result = await RunYtdlpAsync(args);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    return new StreamInfo { Error = result.Error };
                }

                var directUrl = result.Output?.Trim();
                if (string.IsNullOrEmpty(directUrl))
                {
                    return new StreamInfo { Error = "No URL returned from yt-dlp" };
                }

                // Get additional info
                var info = await GetStreamInfoAsync(url);

                return new StreamInfo
                {
                    DirectUrl = directUrl,
                    Title = info.Title,
                    Width = info.Width,
                    Height = info.Height,
                    IsLiveStream = isLive,
                    Format = isLive ? "HLS" : "MP4"
                };
            }
            catch (Exception ex)
            {
                return new StreamInfo { Error = $"Resolution failed: {ex.Message}" };
            }
        }

        private async Task<bool> CheckIfLiveAsync(string url)
        {
            var args = $"--no-warnings --no-check-certificate --print is_live \"{url}\"";
            var result = await RunYtdlpAsync(args);
            return result.Output?.Trim().ToLowerInvariant() == "true";
        }

        private async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            var info = new StreamInfo();

            try
            {
                var args = $"--no-warnings --no-check-certificate --print title --print width --print height \"{url}\"";
                var result = await RunYtdlpAsync(args);

                if (!string.IsNullOrEmpty(result.Output))
                {
                    var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 1)
                    {
                        info.Title = lines[0];
                    }
                    if (lines.Length >= 2)
                    {
                        int width;
                        if (int.TryParse(lines[1], out width))
                            info.Width = width;
                    }
                    if (lines.Length >= 3)
                    {
                        int height;
                        if (int.TryParse(lines[2], out height))
                            info.Height = height;
                    }
                }
            }
            catch
            {
                // Info gathering is optional, don't fail
            }

            return info;
        }

        private async Task<(string Output, string Error)> RunYtdlpAsync(string args)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = _ytdlpPath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            return (null, "Failed to start yt-dlp process");
                        }

                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();

                        process.WaitForExit(30000); // 30 second timeout

                        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                        {
                            return (null, error);
                        }

                        return (output, null);
                    }
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }
            });
        }

        private static string FindYtdlp()
        {
            // Check common locations
            var candidates = new[]
            {
                // Windows
                Path.Combine(Application.dataPath, "StreamingAssets", "yt-dlp.exe"),
                Path.Combine(Application.dataPath, "Plugins", "yt-dlp.exe"),
                @"C:\Program Files\yt-dlp\yt-dlp.exe",
                @"C:\yt-dlp\yt-dlp.exe",

                // Linux/Mac
                "/usr/local/bin/yt-dlp",
                "/usr/bin/yt-dlp",
                Path.Combine(Application.dataPath, "StreamingAssets", "yt-dlp"),
                Path.Combine(Application.dataPath, "Plugins", "yt-dlp"),

                // Home directory
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "yt-dlp"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            // Try PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var separator = Application.platform == RuntimePlatform.WindowsPlayer ||
                               Application.platform == RuntimePlatform.WindowsEditor
                    ? ';' : ':';

                var paths = pathEnv.Split(separator);
                var exeNames = new[] { "yt-dlp", "yt-dlp.exe" };

                foreach (var dir in paths)
                {
                    foreach (var exe in exeNames)
                    {
                        var fullPath = Path.Combine(dir, exe);
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }
            }

            return null;
        }
    }
}
