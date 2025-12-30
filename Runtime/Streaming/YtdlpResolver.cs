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
        private class ProcessResult
        {
            public string Output;
            public string Error;
        }

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
        private bool _downloadAttempted;

        public string Name { get { return "yt-dlp"; } }
        public string YtdlpPath { get { return _ytdlpPath; } }
        public bool IsAvailable { get { return !string.IsNullOrEmpty(_ytdlpPath); } }

        public YtdlpResolver()
        {
            _ytdlpPath = FindYtdlp();
            _downloadAttempted = false;

            if (string.IsNullOrEmpty(_ytdlpPath))
            {
                Debug.Log("[Prism] yt-dlp not found locally. Will attempt to download on first use.");
            }
            else
            {
                Debug.Log("[Prism] Found yt-dlp at: " + _ytdlpPath);
            }
        }

        public YtdlpResolver(string customPath)
        {
            _ytdlpPath = customPath;
            _downloadAttempted = false;
        }

        public async Task<bool> EnsureAvailableAsync(Action<float> onProgress = null)
        {
            if (IsAvailable)
            {
                return true;
            }

            if (_downloadAttempted)
            {
                return false;
            }

            _downloadAttempted = true;
            Debug.Log("[Prism] Downloading yt-dlp...");

            DownloadResult result = await YtdlpDownloader.DownloadAsync(onProgress);

            if (result.Success)
            {
                _ytdlpPath = result.InstallPath;
                return true;
            }

            Debug.LogError("[Prism] Failed to download yt-dlp: " + result.Error);
            return false;
        }

        public void RefreshPath()
        {
            _ytdlpPath = FindYtdlp();
            _downloadAttempted = false;
        }

        public bool CanResolve(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // We can resolve known hosts even if yt-dlp isn't installed yet
            // (we'll download it on demand)
            try
            {
                Uri uri = new Uri(url);
                string host = uri.Host.ToLowerInvariant();

                foreach (string knownHost in KnownHosts)
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
            // Try to download yt-dlp if not available
            if (!IsAvailable)
            {
                bool downloaded = await EnsureAvailableAsync();
                if (!downloaded)
                {
                    return new StreamInfo { Error = "yt-dlp not found and download failed. Check your internet connection." };
                }
            }

            try
            {
                int height = quality.ToHeight();
                string formatArg;

                // Check if this is a live stream first
                bool isLive = await CheckIfLiveAsync(url);

                if (isLive)
                {
                    // Live streams - try to get a direct URL, avoid HLS if possible
                    // Note: Most live streams only offer HLS which Unity can't play on Windows
                    if (height > 0)
                    {
                        formatArg = "best[height<=" + height + "][protocol!=m3u8]/best[height<=" + height + "][protocol!=m3u8_native]/best[height<=" + height + "]";
                    }
                    else
                    {
                        formatArg = "best[protocol!=m3u8]/best[protocol!=m3u8_native]/best";
                    }
                }
                else
                {
                    // VODs - strongly prefer MP4, avoid HLS/DASH
                    if (height > 0)
                    {
                        formatArg = "bestvideo[height<=" + height + "][ext=mp4][protocol!=m3u8]+bestaudio[ext=m4a]/best[height<=" + height + "][ext=mp4][protocol!=m3u8]/best[height<=" + height + "][ext=mp4]/best[ext=mp4]/best";
                    }
                    else
                    {
                        formatArg = "bestvideo[ext=mp4][protocol!=m3u8]+bestaudio[ext=m4a]/best[ext=mp4][protocol!=m3u8]/best[ext=mp4]/best";
                    }
                }

                string args = "--no-warnings --no-check-certificate -f \"" + formatArg + "\" --get-url \"" + url + "\"";

                ProcessResult result = await RunYtdlpAsync(args);

                if (!string.IsNullOrEmpty(result.Error))
                {
                    return new StreamInfo { Error = result.Error };
                }

                string directUrl = result.Output != null ? result.Output.Trim() : null;
                if (string.IsNullOrEmpty(directUrl))
                {
                    return new StreamInfo { Error = "No URL returned from yt-dlp" };
                }

                // Check if we got an HLS stream (Unity can't play these on Windows)
                bool isHls = directUrl.Contains(".m3u8") || directUrl.Contains("m3u8");
                string format = isHls ? "HLS" : "MP4";
                string warning = null;

                if (isHls)
                {
                    #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    warning = "HLS streams are not supported on Windows. Try a VOD or different source.";
                    Debug.LogWarning("[Prism] " + warning);
                    #endif
                }

                // Get additional info
                StreamInfo info = await GetStreamInfoAsync(url);

                return new StreamInfo
                {
                    DirectUrl = directUrl,
                    Title = info.Title,
                    Width = info.Width,
                    Height = info.Height,
                    IsLiveStream = isLive,
                    IsHls = isHls,
                    Format = format,
                    Warning = warning
                };
            }
            catch (Exception ex)
            {
                return new StreamInfo { Error = "Resolution failed: " + ex.Message };
            }
        }

        private async Task<bool> CheckIfLiveAsync(string url)
        {
            string args = "--no-warnings --no-check-certificate --print is_live \"" + url + "\"";
            ProcessResult result = await RunYtdlpAsync(args);
            string output = result.Output != null ? result.Output.Trim().ToLowerInvariant() : "";
            return output == "true";
        }

        private async Task<StreamInfo> GetStreamInfoAsync(string url)
        {
            StreamInfo info = new StreamInfo();

            try
            {
                string args = "--no-warnings --no-check-certificate --print title --print width --print height \"" + url + "\"";
                ProcessResult result = await RunYtdlpAsync(args);

                if (!string.IsNullOrEmpty(result.Output))
                {
                    string[] lines = result.Output.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
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

        private Task<ProcessResult> RunYtdlpAsync(string args)
        {
            return Task.Run(() =>
            {
                ProcessResult result = new ProcessResult();

                try
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = _ytdlpPath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(startInfo))
                    {
                        if (process == null)
                        {
                            result.Error = "Failed to start yt-dlp process";
                            return result;
                        }

                        result.Output = process.StandardOutput.ReadToEnd();
                        string errorOutput = process.StandardError.ReadToEnd();

                        process.WaitForExit(30000); // 30 second timeout

                        if (process.ExitCode != 0 && !string.IsNullOrEmpty(errorOutput))
                        {
                            result.Error = errorOutput;
                            return result;
                        }

                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    return result;
                }
            });
        }

        private static string FindYtdlp()
        {
            // Check common locations
            string[] candidates = new string[]
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

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            // Try PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char separator;
                if (Application.platform == RuntimePlatform.WindowsPlayer ||
                    Application.platform == RuntimePlatform.WindowsEditor)
                {
                    separator = ';';
                }
                else
                {
                    separator = ':';
                }

                string[] paths = pathEnv.Split(separator);
                string[] exeNames = new string[] { "yt-dlp", "yt-dlp.exe" };

                foreach (string dir in paths)
                {
                    foreach (string exe in exeNames)
                    {
                        string fullPath = Path.Combine(dir, exe);
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                }
            }

            return null;
        }
    }
}
