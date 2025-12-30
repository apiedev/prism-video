using System;
using System.Threading.Tasks;

namespace Prism.Streaming
{
    public class StreamInfo
    {
        public string DirectUrl { get; set; }
        public string Title { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
        public bool IsLiveStream { get; set; }
        public bool IsHls { get; set; }
        public string Error { get; set; }
        public string Warning { get; set; }
        public bool Success { get { return string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(DirectUrl); } }
    }

    public interface IStreamResolver
    {
        string Name { get; }
        bool CanResolve(string url);
        Task<StreamInfo> ResolveAsync(string url, StreamQuality quality = StreamQuality.Auto);
    }

    public enum StreamQuality
    {
        Auto,
        Low,      // 360p
        Medium,   // 480p
        High,     // 720p
        Full,     // 1080p
        QHD,      // 1440p
        UHD4K,    // 2160p
        UHD8K     // 4320p
    }

    public static class StreamQualityExtensions
    {
        public static int ToHeight(this StreamQuality quality)
        {
            switch (quality)
            {
                case StreamQuality.Low: return 360;
                case StreamQuality.Medium: return 480;
                case StreamQuality.High: return 720;
                case StreamQuality.Full: return 1080;
                case StreamQuality.QHD: return 1440;
                case StreamQuality.UHD4K: return 2160;
                case StreamQuality.UHD8K: return 4320;
                default: return 0; // Auto - let resolver decide
            }
        }
    }
}
