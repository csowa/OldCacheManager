using System;
using System.Threading;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CacheManagement
{
	/// <summary>
	/// Contains parameters for a <see cref="CacheRegister"/> registered cache item
	/// </summary>
	/// <remarks>
	/// <see cref="CacheRegister"/> (may) track (key, (this)) in a dictionary; 
	/// may not derive from this class.
	/// <seealso cref="CacheRegister"/>
	/// </remarks>
	sealed public class CacheRegistration
	{
		public ILogger Logger { get; set; }
		public IMemoryCache MemoryCache { get; set; }

		/// <summary>
		/// cache item loader delegate
		/// </summary>
		public Func<object> Loader { get; set; }

		/// <summary>
		/// (read only) absolute expiration timespan (relative to "now")
		/// </summary>
		public TimeSpan Absolute { get; }

		/// <summary>
		/// (read only) sliding expiration timespan
		/// </summary>
		public TimeSpan Sliding { get; }

		/// <summary>
		/// (read only) cache priority
		/// </summary>
		public CacheItemPriority Priority { get; }

		/// <summary>
		/// (read only) cache key name
		/// </summary>
		public string KeyName { get; }

		/// <summary>
		/// check if value is still in the cache
		/// </summary>
		internal bool IsValid => Cache.IsValid;

		/// <summary>
		/// (read only) cache value access
		/// </summary>
		private CacheValue Cache { get; }

		/// <summary>
		/// (read only) manage thread synchchronization
		/// </summary>
		private CacheLock Synch { get; }

		/// <summary>
		/// shortcut to get current threadid value as string
		/// </summary>
		private string ThreadId => Thread.CurrentThread.ManagedThreadId.ToString();

		/// <summary>
		/// create new item for cachemanager.
		/// <see cref="Microsoft.Extensions.Caching.Memory"/> for more info on some parameters.
		/// </summary>
		/// <param name="key"></param>
		/// <param name="loader"></param>
		/// <param name="absolute"></param>
		/// <param name="sliding"></param>
		/// <param name="priority"></param>
		public CacheRegistration(string key, Func<object> loader, TimeSpan absolute, TimeSpan sliding, CacheItemPriority priority)
		{
			KeyName = key;
			Loader = loader;
			Absolute = absolute;
			Sliding = sliding;
			Priority = priority;

			Cache = new CacheValue(this);
			Synch = new CacheLock(this);

			//Trace("Created");
		}

		/// <summary>
		/// get value from cache, using synchronized access
		/// </summary>
		/// <returns></returns>
		public object GetValue()
		{
			if (!IsValid)
			{
				if (CacheLock.UseLock)
				{
					// wait here for the requested cache item in case
					// something else is loading it
					if (Synch.Lock("load"))
					{
						try
						{
							if (!IsValid)
							{
								Cache.Load();
							}
						}
						finally
						{
							Synch.Release();
						}
					}
				}
				else
				{
					Cache.Load();
				}
			}
			return Cache.Value;
		}

		/// <summary>
		/// invalidate cache value
		/// </summary>
		/// <remarks>
		/// the cached item will be reloaded at next "get"
		/// </remarks>
		public void Invalidate() => Cache.Invalidate();

		/// <summary>
		/// helper to write cachemanager trace messages
		/// </summary>
		/// <param name="message"></param>
		private void Trace(string message) => Trace(message, null);

		/// <summary>
		/// overload can specify trace.warn
		/// </summary>
		/// <param name="message"></param>
		/// <param name="exc"></param>
		private void Trace(string message, Exception exc)
		{
			Logger.LogTrace(exc, $"CacheRegistration [{ThreadId}]-[{KeyName}] {message}");
		}

		/// <summary>
		/// base class for cacheregistration child classes; provides access to parent
		/// </summary>
		private abstract class CacheChild
		{
            /// <summary>
            /// (read only) reference to parent cacheregistration item
            /// </summary>
            protected CacheRegistration Parent { get; }

			/// <summary>
			/// create child class &amp; initialize parent reference
			/// </summary>
			/// <param name="crItem"></param>
			protected CacheChild(CacheRegistration cacheRegistration) => Parent = cacheRegistration;
		}

		/// <summary>
		/// implement cache value wrapper
		/// </summary>
		/// <remarks>
		/// includes loading, (in)validating, getting, ...
		/// </remarks>
		private class CacheValue : CacheChild
		{
			/// <summary>
			/// reference to application cache
			/// </summary>
			private IMemoryCache Cache => Parent.MemoryCache;

            /// <summary>
            /// prior value in cache
            /// </summary>
            private object OldValue { get; set; } = null;

			/// <summary>
			/// check if an item is still in the cache
			/// </summary>
			internal bool IsValid => Cache.TryGetValue(Parent.KeyName, out _);

			/// <summary>
			/// return cached item value
			/// </summary>
			internal object Value
			{
				get
				{
					if (IsValid)
						return Cache.Get(Parent.KeyName);
					else
					{
						Parent.Trace("Using old value");
						return OldValue;
					}
				}
			}

			/// <summary>
			/// check if there is any value at all for this item (cache or oldvalue)
			/// </summary>
			/// <remarks>
			/// should only return true at app atartup
			/// </remarks>
			internal bool IsEmpty => !IsValid && OldValue == null;

            /// <summary>
            /// is value being loaded ?
            /// </summary>
            internal bool IsLoading { get; private set; } = false;

			/// <summary>
			/// create cachevalue cache implementation wrapper
			/// </summary>
			/// <param name="cacheRegistration"></param>
			internal CacheValue(CacheRegistration cacheRegistration) : base(cacheRegistration) { }

			/// <summary>
			/// invalidate specified cache item
			/// </summary>
			/// <remarks>
			/// the cached item will be reloaded at next "get"
			/// </remarks>
			internal void Invalidate()
			{
				Cache.Remove(Parent.KeyName);
				Parent.Trace("Cache invalidated");
			}

			/// <summary>
			/// call cache item loader and insert results into cache
			/// </summary>
			internal void Load()
			{
				try
				{
					IsLoading = true;
					Parent.Trace("Load started");

					//if (!bAsync || OldValue == null)
					Insert(Parent.Loader());
				}
				catch (Exception e)
				{
					string msg = "Load failed";
					Parent.Trace(msg, e);
					throw new Exception(msg, e);
				}
				finally
				{
					IsLoading = false;
				}
			}

			/// <summary>
			/// insert value into cache
			/// </summary>
			/// <param name="value"></param>
			/// <exception cref="System.Exception">
			/// failure to insert cache value
			/// </exception>
			private void Insert(object value)
			{
				try
				{
					var options = new MemoryCacheEntryOptions
					{
						AbsoluteExpirationRelativeToNow = Parent.Absolute,
						SlidingExpiration = Parent.Sliding,
						Priority = Parent.Priority,
					};
					options.RegisterPostEvictionCallback(new PostEvictionDelegate(OnRemoved));
					Cache.Set(Parent.KeyName, value, options);
					Parent.Trace("Cache loaded");
				}
				catch (Exception exc)
				{
					string msg = "Insert failed";
					Parent.Trace(msg, exc);
					throw new Exception(msg, exc);
				}
			}

			/// <summary>
			/// process cache removal event; store old value for retrieval while loader is busy
			/// </summary>
			/// <param name="key"></param>
			/// <param name="oldValue"></param>
			/// <param name="reason"></param>
			private void OnRemoved(object key, object oldValue, EvictionReason reason, object state)
			{
				// without oldvalue, all load calls are synchronous
				OldValue = oldValue;
				Parent.Trace($"Cache item removed, reason: {reason}");
			}
		}

		/// <summary>
		/// implement locking semantics for cached items.
		/// </summary>
		/// <remarks>
		/// wraps mutex
		/// </remarks>
		private class CacheLock : CacheChild
		{
			/// <summary>
			/// (read only) reference to related readerwriterlock 
			/// to handle cache loading synchronization
			/// </summary>
			/// <remarks>
			/// Change to <see cref="SemaphoreSlim"/>, along with other adjustments, to use in an async/await context.
			/// https://stackoverflow.com/questions/19659387/readerwriterlockslim-and-async-await
			/// </remarks>
			private ReaderWriterLockSlim ItemRWL { get; }

			/// <summary>
			/// create cachelock for synchronization of cache item access
			/// </summary>
			/// <param name="cacheRegistration"></param>
			internal CacheLock(CacheRegistration cacheRegistration) : base(cacheRegistration)
			{
				ItemRWL = new ReaderWriterLockSlim();
			}

			/// <summary>
			/// get lock
			/// </summary>
			/// <param name="reason">just for output to trace</param>
			/// <returns>true if lock acquired</returns>
			internal bool Lock(string reason)
			{
				Parent.Trace("Attempting to lock");
				if (Parent.Cache.IsEmpty)
				{
					// if no value, wait until first loader is done
					// 2007.06.26, css: but no longer than WaitLockNumber waits !
					int i = 0;
					while (!GetLock(WaitLockMillisec) && i < WaitLockNumber)
					{
						Parent.Trace($"Waiting for {reason}");
						i++;
					}
					if (i == WaitLockNumber)
					{
						// this is really an exception case, but one that doesn't 
						// necessitate a stop condition or raising...
						// just record in the application event log
						string msg = $"Ignoring prior {reason} lock";
						Exception exc = new Exception(msg);
						Parent.Trace(msg, exc);
					}
					return true;
				}
				else if (Parent.Cache.IsLoading)
				{
					// else drop through and use oldvalue while loader runs
					return false;
				}
				else
				{
					// first one here acquires lock, all others use oldvalue
					return GetLock(1);
				}
			}

			/// <summary>
			/// release lock
			/// </summary>
			/// <exception cref="System.Exception">
			/// normally thrown if attempt to release lock not held; just logged here.
			/// </exception>
			internal void Release()
			{
				try
				{
					ItemRWL.ExitWriteLock();
					Parent.Trace("Lock released");
				}
				catch (Exception ex)
				{
					Parent.Trace("Lock release error", ex);
				}
			}

			/// <summary>
			/// get lock within intTimeout milliseconds, return success or failure
			/// </summary>
			/// <param name="timeoutmilliseconds"></param>
			/// <returns></returns>
			private bool GetLock(int timeoutmilliseconds)
			{
				bool bSuccess = false;
				try
				{
					bSuccess = ItemRWL.TryEnterWriteLock(timeoutmilliseconds);
				}
				catch (Exception ex)
				{
					Parent.Trace("Lock acquire error", ex);
				}
				return bSuccess;
			}

			/// <summary>
			/// accesses UseLock from CacheConfiguration section
			/// </summary>
			internal static bool UseLock = true;
			//{ get { return CacheConfiguration.Instance().CacheLock.UseLock; } }

			/// <summary>
			/// accesses WaitLockMillisec from CacheConfiguration section
			/// </summary>
			private static int WaitLockMillisec = 100;
			//{ get { return CacheConfiguration.Instance().CacheLock.WaitLockMillisec; } }

			/// <summary>
			/// accesses WaitLockNumber from CacheConfiguration section
			/// </summary>
			private static int WaitLockNumber = 5;
			//{ get { return CacheConfiguration.Instance().CacheLock.WaitLockNumber; } }

		}
	}
}
