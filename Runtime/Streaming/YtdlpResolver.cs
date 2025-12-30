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

        public string Name { get { return "yt-dlp"; } }
        public string YtdlpPath { get { return _ytdlpPath; } }
        public bool IsAvailable { get { return !string.IsNullOrEmpty(_ytdlpPath); } }

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
                Debug.Log("[Prism] Found yt-dlp at: " + _ytdlpPath);
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
            if (!IsAvailable)
            {
                return new StreamInfo { Error = "yt-dlp not found" };
            }

            try
            {
                int height = quality.ToHeight();
                string formatArg;
                if (height > 0)
                {
                    formatArg = "bestvideo[height<=" + height + "][ext=mp4]+bestaudio[ext=m4a]/best[height<=" + height + "][ext=mp4]/best";
                }
                else
                {
                    formatArg = "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best";
                }

                // For live streams, we need different handling
                bool isLive = await CheckIfLiveAsync(url);
                if (isLive)
                {
                    if (height > 0)
                    {
                        formatArg = "best[height<=" + height + "]/best";
                    }
                    else
                    {
                        formatArg = "best";
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

                // Get additional info
                StreamInfo info = await GetStreamInfoAsync(url);

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
