using System.Collections.Generic;
using UnityEngine;

namespace Prism.Streaming
{
    public static class StreamResolverFactory
    {
        private static List<IStreamResolver> _resolvers;
        private static YtdlpResolver _ytdlpResolver;
        private static DirectUrlResolver _directResolver;

        public static IReadOnlyList<IStreamResolver> Resolvers
        {
            get
            {
                EnsureInitialized();
                return _resolvers;
            }
        }

        public static YtdlpResolver YtdlpResolver
        {
            get
            {
                EnsureInitialized();
                return _ytdlpResolver;
            }
        }

        public static bool IsYtdlpAvailable => YtdlpResolver?.IsAvailable ?? false;

        private static void EnsureInitialized()
        {
            if (_resolvers != null)
                return;

            _directResolver = new DirectUrlResolver();
            _ytdlpResolver = new YtdlpResolver();

            _resolvers = new List<IStreamResolver>
            {
                _ytdlpResolver,  // Check yt-dlp first for known platforms
                _directResolver  // Fall back to direct URL
            };
        }

        public static IStreamResolver GetResolver(string url)
        {
            EnsureInitialized();

            foreach (var resolver in _resolvers)
            {
                if (resolver.CanResolve(url))
                {
                    return resolver;
                }
            }

            // Default to direct resolver (will attempt to play URL as-is)
            return _directResolver;
        }

        public static void RegisterResolver(IStreamResolver resolver, int priority = -1)
        {
            EnsureInitialized();

            if (priority < 0 || priority >= _resolvers.Count)
            {
                _resolvers.Insert(0, resolver); // Add at beginning (highest priority)
            }
            else
            {
                _resolvers.Insert(priority, resolver);
            }

            Debug.Log($"[Prism] Registered stream resolver: {resolver.Name}");
        }

        public static void SetYtdlpPath(string path)
        {
            _ytdlpResolver = new YtdlpResolver(path);

            // Update in resolvers list
            for (int i = 0; i < _resolvers.Count; i++)
            {
                if (_resolvers[i] is YtdlpResolver)
                {
                    _resolvers[i] = _ytdlpResolver;
                    break;
                }
            }
        }
    }
}
