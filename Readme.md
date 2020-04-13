# Cache Management

This is a small collection of code accumulated over the years, from other projects.

Note: none of this is for async/await, at least at this point (2020.04).

To use: add as singletons in `Startup.cs`.

TODO: These should really use a common interface...

## Cache Manager

Uses `Lazy<T>` to deal with multiple concurrent attempts to load a cache item.

Might be better to just use https://github.com/alastairtree/LazyCache (find on NuGet), which is what the original concept [falafel](https://medium.com/falafel-software/working-with-system-runtime-caching-memorycache-9f8548172ccd) from evolved into. 

One advantage here is that getting the value uses generics.  It's also simpler to  operate.

This one is more useable for ad-hoc caching that CacheRegister.

## Cache Register

Much earlier code that manages a collection of cache item control entries (`CacheRegistration`s) that each manage their own locking, cache options, and value factory.  `CacheRegistration` will return the previously cached value, if it has one, while the value is being reloaded, so there is no waiting, if timing is a bit more critical than freshness for the time it takes to reload.

Trace level logging is available, if enabled for the logger.

This approach was originally designed to load common app-wide values at start-up, that were then accesed via the registry.  Consumers do not need to know how the values were produced.

Updated for current c#, and .NET Core 3.1.
