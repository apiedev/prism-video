using System;
using System.Threading.Tasks;

namespace Prism.Streaming
{
    public class DirectUrlResolver : IStreamResolver
    {
        private static readonly string[] SupportedExtensions = {
            ".mp4", ".webm", ".ogv", ".ogg", ".mov", ".avi", ".wmv",
            ".m3u8", ".m3u", ".mpd" // HLS and DASH
        };

        public string Name => "Direct URL";

        public bool CanResolve(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            // Check if it's a file:// URL
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return true;

            // Check for supported extensions
            var lowerUrl = url.ToLowerInvariant();
            foreach (var ext in SupportedExtensions)
            {
                if (lowerUrl.Contains(ext))
                    return true;
            }

            return false;
        }

        public Task<StreamInfo> ResolveAsync(string url, StreamQuality quality = StreamQuality.Auto)
        {
            var info = new StreamInfo
            {
                DirectUrl = url,
                Format = DetectFormat(url),
                IsLiveStream = IsLikelyLiveStream(url)
            };

            return Task.FromResult(info);
        }

        private string DetectFormat(string url)
        {
            var lowerUrl = url.ToLowerInvariant();

            if (lowerUrl.Contains(".m3u8") || lowerUrl.Contains(".m3u"))
                return "HLS";
            if (lowerUrl.Contains(".mpd"))
                return "DASH";
            if (lowerUrl.Contains(".mp4"))
                return "MP4";
            if (lowerUrl.Contains(".webm"))
                return "WebM";
            if (lowerUrl.Contains(".ogv") || lowerUrl.Contains(".ogg"))
                return "Ogg";

            return "Unknown";
        }

        private bool IsLikelyLiveStream(string url)
        {
            var lowerUrl = url.ToLowerInvariant();
            return lowerUrl.Contains(".m3u8") ||
                   lowerUrl.Contains("live") ||
                   lowerUrl.Contains("stream");
        }
    }
}
