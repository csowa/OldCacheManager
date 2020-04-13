using System;
using Microsoft.Extensions.Caching.Memory;

namespace CacheManagement
{
    /// <summary>
    /// Handle concurrent cache access and value initialization.
    /// </summary>
    /// <remarks>
    /// This is just rudimentary handling of concurrent cache loading semantics.  
    /// For actual cache *management*, need to also deal with the full lifecycle of cached items,
    /// including cache eviction and invalidation, and maybe also centrally storing
    /// information about what's in the cache.  Will leave this aside for now; implement when needed.
    /// <para/>Better is to start doing something with distributed cache systems (redis, memcache, etc.)
    /// </remarks>
    public class CacheManager
    {
        public static CacheManager Current { get; private set; }

        /// <summary>Access to injected instance of <see cref="MemoryCache"/> used by <see cref="CacheManager"/></summary>
        public IMemoryCache Cache { get; }

        public CacheManager(IMemoryCache memoryCache)
        {
            Cache = memoryCache;
            Current = this;
        }

        /// <summary>
        /// Get or add the cache entry identified by <paramref name="key"/>, 
        /// initializing it with <paramref name="valueFactory"/> if needed,
        /// and using <paramref name="cachePolicy"/>.
        /// </summary>
        /// <typeparam name="T">Stored value type</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="valueFactory">Value initializer method</param>
        /// <param name="cachePolicy">Cache policy</param>
        /// <returns>Value that was cached (newly created if needed)</returns>
        /// <remarks>
        /// Modern approach.
        /// <para/>
        /// Informed by https://medium.com/falafel-software/working-with-system-runtime-caching-memorycache-9f8548172ccd
        /// and https://github.com/alastairtree/LazyCache .
        /// </remarks>
        public T AddOrGetExisting<T>(string key, Func<T> valueFactory, MemoryCacheEntryOptions cacheOptions)
        {
            if (cacheOptions is null)
            {
                throw new ArgumentNullException(nameof(cacheOptions));
            }

            var lazyCacheEntry = Cache.GetOrCreate(key, cacheEntry =>
            {
                cacheEntry.SetOptions(cacheOptions);
                return new Lazy<T>(valueFactory);
            });

            try
            {
                return lazyCacheEntry.Value;
            }
            catch
            {
                // Handle cached lazy exception by evicting from cache.
                Cache.Remove(key);
                throw;
            }
        }

        /// <summary>
        /// Remove a cached item from the cache
        /// </summary>
        /// <param name="key">The key for the item in the cache (make it specific)</param>
        public void RemoveCacheEntry(string key)
        {
            Cache.Remove(key);
        }
    }
}