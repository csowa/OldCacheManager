using System;
using System.Diagnostics;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Web;
using System.Web.Caching;

namespace TopLevelNamespace.Utility.CacheManager
{
	/// <summary>
	/// Contains parameters for a cachemanager registered cache item
	/// </summary>
	/// <remarks>
	/// cachemanager (may) track (key, (this)) in a private hashtable field; 
	/// may not derive from this class.
	/// <seealso cref="CacheManager"/>
	/// </remarks>
	sealed public class CacheRegistration 
	{
		/// <summary>
		/// cache item loader delegate
		/// </summary>
		public CacheManager.CacheItemLoader Loader
		{ 
			get { return _loader; } 
			set { _loader = value; }
		}
		private CacheManager.CacheItemLoader _loader;

		/// <summary>
		/// (read only) absolute expiration timespan (add to "now")
		/// </summary>
		public TimeSpan Absolute
		{ get { return _tsAbsolute; } }
		private TimeSpan _tsAbsolute;

		/// <summary>
		/// (read only) sliding expiration timespan
		/// </summary>
		public TimeSpan Sliding
		{ get{ return _tsSliding; } }
		private TimeSpan _tsSliding;

		/// <summary>
		/// (read only) cache priority
		/// </summary>
		public CacheItemPriority Priority
		{ get{ return _priority; } }
		private CacheItemPriority _priority;

		/// <summary>
		/// (read only) cache key name
		/// </summary>
		public string KeyName
		{ get{ return _KeyName; } }
		private string _KeyName;
		
		/// <summary>
		/// check if value is still in the cache
		/// </summary>
		internal bool IsValid
		{ get { return this.Cache.IsValid; } }

		/// <summary>
		/// (read only) cache value access
		/// </summary>
		private CacheValue Cache
		{ get{ return _cvCache; } }
		private CacheValue _cvCache;

		/// <summary>
		/// (read only) manage thread synchchronization
		/// </summary>
		private CacheLock Synch
		{ get{ return _clSynch; } }
		private CacheLock _clSynch;

		/// <summary>
		/// shortcut to get current threadid value as string
		/// </summary>
		private string ThreadId
		{ get { return AppDomain.GetCurrentThreadId().ToString(); } }

		/// <summary>
		/// reference to current httpcontext
		/// </summary>
		private HttpContext Context
		{ get { return HttpContext.Current; } }

		/// <summary>
		/// create new item for cachemanager.
		/// <see cref="System.Web.Caching"/> for more info on some parameters.
		/// </summary>
		/// <param name="strKey"></param>
		/// <param name="loader"><see cref="CacheManager.CacheItemLoader"/></param>
		/// <param name="tsAbsolute"></param>
		/// <param name="tsSliding"></param>
		/// <param name="priority"></param>
		public CacheRegistration( string strKey, CacheManager.CacheItemLoader loader, TimeSpan tsAbsolute, TimeSpan tsSliding, CacheItemPriority priority )
		{
			// set public read-only properties:
			_KeyName = strKey;
			_loader = loader;
			_tsAbsolute = tsAbsolute;
			_tsSliding = tsSliding;
			_priority = priority;

			// set internal read-only properties:

			// set private read-only properties:
			_cvCache = new CacheValue( this );
			_clSynch = new CacheLock( this );

			Trace( "Created" );
		}

		/// <summary>
		/// get value from cache, using synchronized access
		/// </summary>
		/// <returns></returns>
		public object GetValue()
		{
			if ( ! this.IsValid ) 
			{
				if ( CacheLock.UseLock )
				{
					// wait here for the requested cache item in case
					// something else is loading it
					if( this.Synch.Lock( "load" ) )
					{
						try
						{
							if ( ! this.IsValid ) 
								this.Cache.Load();
						} 
						finally
						{
							this.Synch.Release();
						}
					}
				}
				else
				{
					this.Cache.Load();
				}
			}
			return this.Cache.Value;
		}

		/// <summary>
		/// invalidate cache value
		/// </summary>
		/// <remarks>
		/// the cached item will be reloaded at next "get"
		/// </remarks>
		public void Invalidate()
		{
			this.Cache.Invalidate();
		}

		/// <summary>
		/// helper to write cachemanager trace messages
		/// </summary>
		/// <param name="strMessage"></param>
		private void Trace( string strMessage )
		{
			Trace( strMessage, null );
		}

		private delegate void TraceWriter( string strCategory, string strMessage, Exception exc );

		/// <summary>
		/// overload can specify trace.warn
		/// </summary>
		/// <param name="strMessage"></param>
		/// <param name="exc"></param>
		private void Trace( string strMessage, Exception exc )
		{
			// 2008.05.01, css: sometimes tracing is attempted when httpcontext
            // is not available.  for now, just skip (mostly just trying to see
            // this info in request / response cycle anyway.  where to put it, 
            // otherwise ?
            if (Context != null)
            {
                String strContext = Context.Request.ApplicationPath;
                String strSrc = "TopLevelNamespace";
                String strMsg = "CacheRegistration [" + ThreadId + "]" + "[" + KeyName + "] " + strMessage;
                TraceWriter tw = new TraceWriter(Context.Trace.Write);
                if (exc != null)
                {
                    tw = new TraceWriter(Context.Trace.Warn);

                    // write _all_ exception info to event log
                    EventLog el = new EventLog("Application", ".", strSrc);
                    Exception exes = exc;
                    while (exes != null)
                    {
                        strMsg += "\n" + exes.Message;
                        exes = exes.InnerException;
                    }

                    el.WriteEntry(strContext + " - " + strMsg, EventLogEntryType.Warning);
                    el.Close();
                    el = null;
                }
                tw(strSrc, strMsg, exc);
            }
		}

		/// <summary>
		/// base class for cacheregistration child classes; provides access to parent
		/// </summary>
		private abstract class CacheChild
		{
			/// <summary>
			/// (read only) reference to parent cacheregistration item
			/// </summary>
			protected CacheRegistration Parent
			{get { return _crParent; } }
			private CacheRegistration _crParent;

			/// <summary>
			/// create child class &amp; initialize parent reference
			/// </summary>
			/// <param name="crItem"></param>
			protected CacheChild( CacheRegistration crItem )
			{
				_crParent = crItem;
			}
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
			private Cache cache
			{ get { return Parent.Context.Cache; } }

			/// <summary>
			/// prior value in cache
			/// </summary>
			private object OldValue
			{ get { return _oldValue; } }
			private object _oldValue = null;
		
			/// <summary>
			/// check if an item is still in the cache
			/// </summary>
			internal bool IsValid
			{ get { return ( cache[ Parent.KeyName ] != null ); } }

			/// <summary>
			/// return cached item value
			/// </summary>
			internal object Value
			{ 
				get 
				{
					if ( this.IsValid )
						return cache.Get( Parent.KeyName );
					else
					{
						Parent.Trace( "Using old value" );
						return this.OldValue;
					}
				}
			}
		
			/// <summary>
			/// check if there is any value at all for this item (cache or oldvalue)
			/// </summary>
			/// <remarks>
			/// should only return true at app atartup
			/// </remarks>
			internal bool IsEmpty
			{ get { return ( ! this.IsValid && this.OldValue == null ); } }
		
			/// <summary>
			/// is value being loaded ?
			/// </summary>
			internal bool IsLoading
			{ get { return _isLoading; } }
			bool _isLoading = false;

			/// <summary>
			/// create cachevalue cache implementation wrapper
			/// </summary>
			/// <param name="crItem"></param>
			internal CacheValue( CacheRegistration crItem ) : base( crItem )  {}

			/// <summary>
			/// invalidate specified cache item
			/// </summary>
			/// <remarks>
			/// the cached item will be reloaded at next "get"
			/// </remarks>
			internal void Invalidate()
			{
				cache.Remove( Parent.KeyName );
				Parent.Trace( "Cache invalidated" );
			}

			/// <summary>
			/// call cache item loader and insert results into cache
			/// </summary>
			/// <remarks>
			/// synchronous
			/// </remarks>
			internal void Load()
			{
				Load( false );
			}

			/// <summary>
			/// load synch or asynch
			/// </summary>
			/// <remarks>
			/// asynch not working...
			/// </remarks>
			/// <param name="bAsynch"></param>
			private void Load( bool bAsynch )
			{
				try
				{
					this._isLoading = true;
					Parent.Trace( "Load started" );

					if ( ! bAsynch || this.OldValue == null )
						Insert( Parent.Loader() );
					else
						Parent.Loader.BeginInvoke( new AsyncCallback( Loaded ), Parent );
				} 
				catch ( Exception e )
				{
					string strMsg = "Load failed";
					Parent.Trace( strMsg , e );
					throw new Exception( strMsg, e );
				}
				finally
				{
					this._isLoading = false;
				}
			}

			/// <summary>
			/// get asynch cache loader results
			/// </summary>
			/// <param name="ar"></param>
			private void Loaded( IAsyncResult ar ) 
			{
				//CacheRegistration Parent = (CacheRegistration) ar.AsyncState;
				CacheManager.CacheItemLoader loader = (CacheManager.CacheItemLoader) ((AsyncResult) ar ).AsyncDelegate;
				CacheRegistration parent = (CacheRegistration) loader.Target;
				parent.Cache.Insert( loader.EndInvoke( ar ) );
			}

			/// <summary>
			/// insert value into cache
			/// </summary>
			/// <param name="oValue"></param>
			/// <exception cref="System.Exception">
			/// failure to insert cache value
			/// </exception>
			private void Insert( object oValue )
			{
				try
				{
					DateTime dtAbsolute = DateTime.Now.Add( Parent.Absolute );
					CacheItemRemovedCallback OnRemoveCallback = new CacheItemRemovedCallback( OnRemoved );
					this.cache.Insert( Parent.KeyName, oValue, null, dtAbsolute, Parent.Sliding, Parent.Priority, OnRemoveCallback );
					Parent.Trace( "Cache loaded" );
				}
				catch ( Exception exc )
				{
					string strMsg = "Insert failed";
					Parent.Trace( strMsg , exc );
					throw new Exception( strMsg, exc );
				}
			}

			/// <summary>
			/// process cache removal event; store old value for retrieval while loader is busy
			/// </summary>
			/// <param name="strKey"></param>
			/// <param name="objOldValue"></param>
			/// <param name="reason"></param>
			private void OnRemoved ( string strKey, object objOldValue, CacheItemRemovedReason reason )
			{
				// without oldvalue, all load calls are synchronous
				this._oldValue = objOldValue;
				Parent.Trace( "Cache item removed, reason: " + reason.ToString() );
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
			private ReaderWriterLock ItemRWL
			{ get{ return _itemRWL; } }
			private ReaderWriterLock _itemRWL;

			/// <summary>
			/// (waiting) thread count
			/// </summary>
			private int ThreadCount
			{
				get{ return _ThreadCount; }
				set{ _ThreadCount = value; }
			}
			private int _ThreadCount = 0;

			/// <summary>
			/// create cachelock for synchronization of cache item access
			/// </summary>
			/// <param name="crItem"></param>
			internal CacheLock( CacheRegistration crItem ) : base( crItem )
			{				
				_itemRWL = new ReaderWriterLock();			
			}

			/// <summary>
			/// get lock
			/// </summary>
			/// <param name="strReason">just for output to trace</param>
			/// <returns>true if lock acquired</returns>
			internal bool Lock( string strReason )
			{
				Parent.Trace( "Attempting to lock");
				if ( Parent.Cache.IsEmpty )
				{
					// if no value, wait until first loader is done
					// 2007.06.26, css: but no longer than WaitLockNumber waits !
					int i = 0; 
					while ( ! GetLock( WaitLockMillisec ) && i < WaitLockNumber )
					{
						Parent.Trace( "Waiting for " + strReason );
						i++;
					}
					if ( i == WaitLockNumber )
					{
						// this is really an exception case, but one that doesn't 
						// necessitate a stop condition or raising...
						// just record in the application event log
						String strMsg = "Ignoring prior " + strReason + " lock";
						Exception exc = new Exception( strMsg );
						Parent.Trace( strMsg, exc );
					}
					return true;
				}
				else if ( Parent.Cache.IsLoading )
				{
					// else drop through and use oldvalue while loader runs
					return false;
				}
				else
				{
					// first one here acquires lock, all others use oldvalue
					return GetLock( 1 );
				}
			}

			/// <summary>
			/// release lock
			/// </summary>
			/// <exception cref="System.ApplicationException">
			/// normally thrown if attempt to release lock not held.
			/// </exception>
			internal void Release()
			{
				try 
				{
					this.ItemRWL.ReleaseWriterLock();
					Parent.Trace( "Lock released" );
				}
				catch ( ApplicationException aeError )
				{
					Parent.Trace("Lock release error", (Exception) aeError );
				}
			}

			/// <summary>
			/// get lock within intTimeout milliseconds, return success or failure
			/// </summary>
			/// <param name="intTimeout"></param>
			/// <returns></returns>
			private bool GetLock( int intTimeout )
			{
				bool bSuccess = false;
				try
				{
					this.ItemRWL.AcquireWriterLock( intTimeout );
					bSuccess = true;
				}
				catch( ApplicationException e ) 
				{
				}
				return bSuccess;
			}
			
			/// <summary>
			/// accesses UseLock from CacheConfiguration section
			/// </summary>
			internal static bool UseLock
			{ get { return CacheConfiguration.Instance().CacheLock.UseLock; } }
			
			/// <summary>
			/// accesses WaitLockMillisec from CacheConfiguration section
			/// </summary>
			private static int WaitLockMillisec
			{ get { return CacheConfiguration.Instance().CacheLock.WaitLockMillisec; } }
			
			/// <summary>
			/// accesses WaitLockNumber from CacheConfiguration section
			/// </summary>
			private static int WaitLockNumber
			{ get { return CacheConfiguration.Instance().CacheLock.WaitLockNumber; } }

		}
	}
}
