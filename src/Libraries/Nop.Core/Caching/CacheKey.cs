namespace Nop.Core.Caching
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public partial class CacheKey
    {
        protected string _keyFormat = string.Empty;

        public CacheKey(CacheKey cacheKey, Func<object, object> createCacheKeyParameters, params object[] keyObjects)
        {
            Key = cacheKey.Key;

            _keyFormat = cacheKey.Key;

            CacheTime = cacheKey.CacheTime;

            Prefixes.AddRange(cacheKey.Prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));

            if (!keyObjects.Any())
                return;

            Key = string.Format(_keyFormat, keyObjects.Select(createCacheKeyParameters).ToArray());

            for (var i = 0; i < Prefixes.Count; i++)
                Prefixes[i] = string.Format(Prefixes[i], keyObjects.Select(createCacheKeyParameters).ToArray());
        }

        public CacheKey(string cacheKey, int? cacheTime = null, params string[] prefixes)
        {
            Key = cacheKey;

            _keyFormat = cacheKey;

            if (cacheTime.HasValue)
                CacheTime = cacheTime.Value;

            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        public CacheKey(string cacheKey, params string[] prefixes)
        {
            Key = cacheKey;

            _keyFormat = cacheKey;

            Prefixes.AddRange(prefixes.Where(prefix => !string.IsNullOrEmpty(prefix)));
        }

        /// <summary>
        /// Cache time in minutes
        /// </summary>
        public int CacheTime { get; set; } = NopCachingDefaults.CacheTime;

        /// <summary>
        /// Cache key
        /// </summary>
        public string Key { get; protected set; }

        /// <summary>
        /// Prefixes to remove by prefix functionality
        /// </summary>
        public List<string> Prefixes { get; protected set; } = new List<string>();
    }
}
