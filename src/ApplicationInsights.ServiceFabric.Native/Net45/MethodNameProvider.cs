namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Reflection;

    /// <summary>
    /// Provider for looking up method names by an interface id and a method id. It has methods allowing proxies
    /// or service objects to be examined, and its interfaces and methods will be added to the map. The ids are
    /// calculated using the same logic as service fabric runtime, so the method ids used in service remoting can
    /// be mapped back to method names. 
    /// </summary>
    internal class MethodNameProvider : IMethodNameProvider
    {
        private IDictionary<int, Dictionary<int, string>> idToMethodNameMap;
        private bool useConcurrentDictionary;

        /// <summary>
        /// Instantiates the <see cref="MethodNameProvider"/> with the specified remoting factory and retrysettings.
        /// </summary>
        /// <param name="threadSafe">Whether this method name provider needs to be thread safe or not with respect to concurrent reads and writes.</param>
        public MethodNameProvider(bool threadSafe)
        {
            this.useConcurrentDictionary = threadSafe;
            if (threadSafe)
            {
                this.idToMethodNameMap = new ConcurrentDictionary<int, Dictionary<int, string>>();
            }
            else
            {
                this.idToMethodNameMap = new Dictionary<int, Dictionary<int, string>>();
            }
        }

        /// <summary>
        /// Look up the given interface id and method id and returns the method name.
        /// This only works for interface types for which it had created proxies.
        /// </summary>
        /// <param name="interfaceId"></param>
        /// <param name="methodId"></param>
        /// <returns></returns>
        public string GetMethodName(int interfaceId, int methodId)
        {
            if (this.idToMethodNameMap.TryGetValue(interfaceId, out Dictionary<int, string> methodMap))
            {
                if (methodMap.TryGetValue(methodId, out string methodName))
                {
                    return methodName;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds methods for the given interfaces which derives from the given base interface type.
        /// </summary>
        /// <param name="interfaces">The interfaces to be added.</param>
        /// <param name="baseInterfaceType">The base interface type as a filter where only interfaces that derives for this base interface type are added.</param>
        public void AddMethodsForProxyOrService(IEnumerable<Type> interfaces, Type baseInterfaceType)
        {
            foreach (Type interfaceType in interfaces)
            {
                if (!baseInterfaceType.IsAssignableFrom(interfaceType))
                {
                    continue;
                }

                int interfaceId = IdUtilHelper.ComputeId(interfaceType);

                // Add if it's not there, don't add if it's there already
                if (!this.idToMethodNameMap.TryGetValue(interfaceId, out Dictionary<int, string> methodMap))
                {
                    // Since idToMethodNameMap can be accessed by multiple threads, it is important to make sure
                    // the inner dictionary has everything added, before this is added to idToMethodNameMap. The
                    // inner dictionary will never be thread safe and it doesn't need to be, as long as it always
                    // is effectively "read-only". If the order is reverse, you risk having another thread trying
                    // to fetch a method from it prematurely.
                    methodMap = new Dictionary<int, string>();
                    foreach (MethodInfo method in interfaceType.GetMethods())
                    {
                        methodMap[IdUtilHelper.ComputeId(method)] = method.Name;
                    }

                    // If multiple threads are trying to set this entry, the last one wins, and this is ok to have
                    // since this method map should always look the same once it's constructed.
                    this.idToMethodNameMap[interfaceId] = methodMap;
                }
            }
        }
    }
}
