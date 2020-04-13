using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CacheManagement
{
	/// <summary>
	/// Common application-wide functionality for cached item management.
	/// </summary>
	public class CacheRegister
	{
		public static CacheRegister Current { get; private set; }

		/// <summary>
		/// all registered cache items to manage (key, CacheRegistration)
		/// </summary>
		public ConcurrentDictionary<string, CacheRegistration> CacheRegistry { get; } = new ConcurrentDictionary<string, CacheRegistration>();

		private ILogger<CacheRegister> Logger { get; set; }
		private IMemoryCache MemoryCache { get; set; }

		public CacheRegister(ILogger<CacheRegister> logger, IMemoryCache memoryCache)
		{
			Logger = logger;
			MemoryCache = memoryCache;
			Current = this;
		}

		/// <summary>
		/// register a (new) item to be managed
		/// </summary>
		/// <remarks>
		/// 2007.08.23, css: 
		/// old intent: overwrites any existing item with the same key.
		/// this was never done properly in the first place. 
		/// in any case, it seems best to use semantics where a cache 
		/// registration is never overwritten, instead forcing an
		/// explicit "unregister" first.
		/// </remarks>
		/// <param name="cacheRegistration"></param>
		public void RegisterItem(CacheRegistration cacheRegistration)
		{
			cacheRegistration.Logger = Logger;
			cacheRegistration.MemoryCache = MemoryCache;

			CacheRegistry.TryAdd(cacheRegistration.KeyName, cacheRegistration);
			Logger.LogTrace($"CacheManager: Cache item registered: {cacheRegistration.KeyName}");
		}

		/// <summary>
		/// retrieve a managed cache registration
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public CacheRegistration GetRegistrationItem(string key) => CacheRegistry[key];

		/// <summary>
		/// unregister an existing managed item
		/// </summary>
		/// <param name="key"></param>
		public void UnregisterItem(string key)
		{
			CacheRegistry.TryRemove(key, out _);
		}

		/// <summary>
		/// check if a cache registration exists for a key
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public bool IsRegistered(string key)
		{
			return CacheRegistry.ContainsKey(key);
		}

		/// <summary>
		/// check if an item is still in the cache
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public bool IsValid(string key) => GetRegistrationItem(key).IsValid;

		/// <summary>
		/// get value of previously registered item, re-loading if necessary
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public object GetItem(string key) => GetItem(GetRegistrationItem(key));

		/// <summary>
		/// get value of item described by a CacheRegistration object
		/// </summary>
		/// <remarks>
		/// useful for handling (unregistered) instance loaders (as opposed to static ones)
		/// </remarks>
		/// <param name="cacheRegistration"></param>
		/// <returns></returns>
		public object GetItem(CacheRegistration cacheRegistration) => cacheRegistration.GetValue();

		/// <summary>
		/// invalidate specified cache item; no arg = invalidate all
		/// </summary>
		/// <remarks>
		/// the cached item will be reloaded at next "get"
		/// </remarks>
		/// <param name="key"></param>
		public void Invalidate(string key) => GetRegistrationItem(key).Invalidate();

		/// <summary>
		/// invalidate all registered cached items
		/// </summary>
		public void Invalidate()
		{
			foreach (var key in CacheRegistry.Keys)
			{
				Invalidate(key);
			}
		}

	}
}
