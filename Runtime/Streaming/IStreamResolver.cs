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
        public string Error { get; set; }
        public bool Success => string.IsNullOrEmpty(Error) && !string.IsNullOrEmpty(DirectUrl);
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
            return quality switch
            {
                StreamQuality.Low => 360,
                StreamQuality.Medium => 480,
                StreamQuality.High => 720,
                StreamQuality.Full => 1080,
                StreamQuality.QHD => 1440,
                StreamQuality.UHD4K => 2160,
                StreamQuality.UHD8K => 4320,
                _ => 0 // Auto - let resolver decide
            };
        }
    }
}
