using System;
using System.Globalization;
using System.Runtime.Caching;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ServiceFabric.Services.Remoting.V2;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal sealed class CacheBasedOperationHolder<T> : IDisposable
    {
        /// <summary>
        /// The memory cache instance used to hold items. MemoryCache.Default is not used as it is shared 
        /// across application and can potentially collide with customer application.
        /// </summary>
        private readonly MemoryCache memoryCache;

        /// <summary>
        /// The cache item policy which identifies the expiration time.
        /// </summary>
        private readonly CacheItemPolicy cacheItemPolicy;

        /// <summary>
        /// Creates an object of type <see cref="CacheBasedOperationHolder{T}"/>
        /// </summary>
        /// <param name="cacheName">Identifier for the cache.</param>
        /// <param name="expirationInMilliSecs">Expiration time in miliseconds.</param>
        public CacheBasedOperationHolder(string cacheName, long expirationInMilliSecs)
        {
            this.cacheItemPolicy = new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMilliseconds(expirationInMilliSecs) };
            this.memoryCache = new MemoryCache(cacheName);
        }

        /// <summary>
        /// Gets the IOperationHolder object against the given key.
        /// </summary>
        /// <param name="key">The request against which the operation holder is stored.</param>
        /// <returns>The operation holder against the key. Null if not found.</returns>
        public IOperationHolder<T> Get(IServiceRemotingRequestMessage key)
        {
            IOperationHolder<T> result = null;
            var cacheItem = this.memoryCache.GetCacheItem(key.GetHashCode().ToString(CultureInfo.InvariantCulture));
            if (cacheItem != null)
            {
                result = (IOperationHolder<T>)cacheItem.Value;
            }

            return result;
        }

        /// <summary>
        /// Removes the entry for the given request.
        /// </summary>
        /// <param name="key">The request against which the operation holder is stored.</param>
        /// <returns>True if able to remove, false otherwise.</returns>
        public bool Remove(IServiceRemotingRequestMessage key)
        {
            return this.memoryCache.Remove(key.GetHashCode().ToString(CultureInfo.InvariantCulture)) != null;
        }

        /// <summary>
        /// Adds operationHold in MemoryCache. DO NOT call it for the request that already exists in the cache.
        /// This is a known Memory Cache race-condition issue when items with same id are added concurrently
        /// and MemoryCache leaks memory. It should be fixed sometime AFTER .NET 4.7.1.
        /// </summary>
        public void Store(IServiceRemotingRequestMessage key, IOperationHolder<T> operationHolder)
        {
            if (operationHolder == null)
            {
                throw new ArgumentNullException("operationHolder");
            }

            // it might be possible to optimize by preventing the long to string conversion
            this.memoryCache.Set(key.GetHashCode().ToString(CultureInfo.InvariantCulture), operationHolder, this.cacheItemPolicy);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.memoryCache.Dispose();
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
