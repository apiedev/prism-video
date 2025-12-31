using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace Prism.Streaming
{
    public static class YtdlpDownloader
    {
        private const string GITHUB_RELEASES_BASE = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

        public static string GetPlatformFilename()
        {
            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return "yt-dlp.exe";
            #elif UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                return "yt-dlp_macos";
            #else
                return "yt-dlp";
            #endif
        }

        public static string GetDownloadUrl()
        {
            return GITHUB_RELEASES_BASE + GetPlatformFilename();
        }

        public static string GetInstallPath()
        {
            string streamingAssets = Application.streamingAssetsPath;

            // Ensure directory exists
            if (!Directory.Exists(streamingAssets))
            {
                Directory.CreateDirectory(streamingAssets);
            }

            #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return Path.Combine(streamingAssets, "yt-dlp.exe");
            #else
                return Path.Combine(streamingAssets, "yt-dlp");
            #endif
        }

        public static bool IsInstalled()
        {
            string path = GetInstallPath();
            return File.Exists(path);
        }

        public static async Task<DownloadResult> DownloadAsync(Action<float> onProgress = null)
        {
            DownloadResult result = new DownloadResult();
            string url = GetDownloadUrl();
            string installPath = GetInstallPath();

            Debug.Log("[Prism] Downloading yt-dlp from: " + url);

            try
            {
                // Ensure StreamingAssets directory exists
                string directory = Path.GetDirectoryName(installPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Download using WebClient for progress support
                using (WebClient client = new WebClient())
                {
                    // Track progress
                    bool downloadComplete = false;
                    Exception downloadError = null;

                    client.DownloadProgressChanged += (sender, e) =>
                    {
                        float progress = (float)e.ProgressPercentage / 100f;
                        if (onProgress != null)
                        {
                            onProgress(progress);
                        }
                    };

                    client.DownloadFileCompleted += (sender, e) =>
                    {
                        if (e.Error != null)
                        {
                            downloadError = e.Error;
                        }
                        downloadComplete = true;
                    };

                    // Start async download
                    client.DownloadFileAsync(new Uri(url), installPath);

                    // Wait for completion
                    while (!downloadComplete)
                    {
                        await Task.Delay(100);
                    }

                    if (downloadError != null)
                    {
                        result.Success = false;
                        result.Error = downloadError.Message;
                        return result;
                    }
                }

                // Make executable on Unix platforms
                #if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    MakeExecutable(installPath);
                #endif

                result.Success = true;
                result.InstallPath = installPath;
                Debug.Log("[Prism] yt-dlp downloaded successfully to: " + installPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                Debug.LogError("[Prism] Failed to download yt-dlp: " + ex.Message);
            }

            return result;
        }

        public static DownloadResult DownloadSync(Action<float> onProgress = null)
        {
            DownloadResult result = new DownloadResult();
            string url = GetDownloadUrl();
            string installPath = GetInstallPath();

            Debug.Log("[Prism] Downloading yt-dlp from: " + url);

            try
            {
                // Ensure StreamingAssets directory exists
                string directory = Path.GetDirectoryName(installPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (WebClient client = new WebClient())
                {
                    if (onProgress != null)
                    {
                        client.DownloadProgressChanged += (sender, e) =>
                        {
                            float progress = (float)e.ProgressPercentage / 100f;
                            onProgress(progress);
                        };
                    }

                    client.DownloadFile(new Uri(url), installPath);
                }

                // Make executable on Unix platforms
                #if UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    MakeExecutable(installPath);
                #endif

                result.Success = true;
                result.InstallPath = installPath;
                Debug.Log("[Prism] yt-dlp downloaded successfully to: " + installPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                Debug.LogError("[Prism] Failed to download yt-dlp: " + ex.Message);
            }

            return result;
        }

        private static void MakeExecutable(string path)
        {
            try
            {
                // Use chmod via Process on Unix systems
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "+x \"" + path + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Prism] Could not set executable permission: " + ex.Message);
            }
        }

        public static void Delete()
        {
            string path = GetInstallPath();
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    Debug.Log("[Prism] yt-dlp deleted from: " + path);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Prism] Failed to delete yt-dlp: " + ex.Message);
                }
            }
        }
    }

    public class DownloadResult
    {
        public bool Success;
        public string InstallPath;
        public string Error;
    }
}
