using System;
using System.Collections;
using System.Web;

namespace TopLevelNamespace.Utility.CacheManager
{
	/// <summary>
	/// Common application-wide functionality for cached item management.
	/// </summary>
	public class CacheManager
	{

		/// <summary>
		/// delegate called to load registered cache item
		/// </summary>
		public delegate object CacheItemLoader();

		/// <summary>
		/// all registered cache items to manage (key, CacheRegistration)
		/// </summary>
		private static Hashtable htRegisteredCacheItems = new Hashtable();

		public CacheManager() {} 

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
		/// <param name="crItem"></param>
		public static void RegisterItem( CacheRegistration crItem )
		{
			htRegisteredCacheItems.Add( crItem.KeyName, crItem );
			HttpContext.Current.Trace.Write( "CacheManager", "Cache item registered: " + crItem.KeyName );
		}

		/// <summary>
		/// retrieve a managed cache registration
		/// </summary>
		/// <param name="strKey"></param>
		/// <returns></returns>
		public static CacheRegistration GetRegistrationItem( string strKey )
		{
			return (CacheRegistration) htRegisteredCacheItems[ strKey ];
		}

		/// <summary>
		/// unregister an existing managed item
		/// </summary>
		/// <param name="strKey"></param>
		public static void UnregisterItem( string strKey )
		{
			htRegisteredCacheItems.Remove( strKey );
		}

		/// <summary>
		/// check if a cache registration exists for a key
		/// </summary>
		/// <param name="strKey"></param>
		/// <returns></returns>
		public static bool IsRegistered( string strKey )
		{
			return htRegisteredCacheItems.ContainsKey( strKey );
		}
		
		/// <summary>
		/// check if an item is still in the cache
		/// </summary>
		/// <param name="strKey"></param>
		/// <returns></returns>
		public static bool IsValid( string strKey )
		{
			return GetRegistrationItem( strKey ).IsValid;
		}

		/// <summary>
		/// get value of previously registered item, re-loading if necessary
		/// </summary>
		/// <param name="strKey"></param>
		/// <returns></returns>
		public static object GetItem( string strKey )
		{
			return GetItem( GetRegistrationItem( strKey ) );
		}

		/// <summary>
		/// get value of item described by a CacheRegistration object
		/// </summary>
		/// <remarks>
		/// useful for handling (unregistered) instance loaders (as opposed to static ones)
		/// </remarks>
		/// <param name="crItem"></param>
		/// <returns></returns>
		public static object GetItem( CacheRegistration crItem )
		{
			return crItem.GetValue();
		}

		/// <summary>
		/// invalidate specified cache item; no arg = invalidate all
		/// </summary>
		/// <remarks>
		/// the cached item will be reloaded at next "get"
		/// </remarks>
		/// <param name="strKey"></param>
		public static void Invalidate( string strKey )
		{
			GetRegistrationItem( strKey ).Invalidate();
		}

		/// <summary>
		/// invalidate all registered cached items
		/// </summary>
		public static void Invalidate()
		{
			foreach ( string strKey in htRegisteredCacheItems.Keys )
				Invalidate( strKey );
		}

	}
}
