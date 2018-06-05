#if !NET45
namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// .Net core does not have a CallContext implementation. So we are wrapping async local and providing an alternative implementation.
    /// </summary>
    public static class CallContext
    {
        private static ConcurrentDictionary<string, AsyncLocal<object>> _collection = new ConcurrentDictionary<string, AsyncLocal<object>>();

        /// <summary>
        /// Stores the given object as an async local object in our collection.
        /// </summary>
        /// <param name="name">The call context name.</param>
        /// <param name="data">The object to store.</param>
        public static void LogicalSetData(string name, object data)
        {
            _collection.GetOrAdd(name, (param) => new AsyncLocal<object>()).Value = data;
        }

        /// <summary>
        /// Retrieves the object stored against the given name.
        /// </summary>
        /// <param name="name">The name against which item was stored.</param>
        /// <returns>The object retrieved.</returns>
        public static object LogicalGetData(string name)
        {
            if (_collection.TryGetValue(name, out AsyncLocal<object> data))
            {
                return data.Value;
            }

            return null;
        }
    }
}
#endif