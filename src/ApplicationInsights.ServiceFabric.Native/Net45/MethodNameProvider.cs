namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using System;
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
        private Dictionary<int, Dictionary<int, string>> idToMethodNameMap;
        private HashSet<Type> processedTypes;
        private object thisLock;

        /// <summary>
        /// Instantiates the <see cref="MethodNameProvider"/> with the specified remoting factory and retrysettings.
        /// </summary>
        public MethodNameProvider()
        {
            this.idToMethodNameMap = new Dictionary<int, Dictionary<int, string>>();
            processedTypes = new HashSet<Type>();
            thisLock = new object();
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
            lock (thisLock)
            {
                if (this.idToMethodNameMap.TryGetValue(interfaceId, out Dictionary<int, string> methodMap))
                {
                    if (methodMap.TryGetValue(methodId, out string methodName))
                    {
                        return methodName;
                    }
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
            lock (thisLock)
            {
                foreach (Type interfaceType in interfaces)
                {
                    // It's important that we don't reprocess types that we have seen before. Otherwise
                    // it would be too much overhead with each remoting call
                    if (this.processedTypes.Contains(interfaceType) ||
                        !baseInterfaceType.IsAssignableFrom(interfaceType))
                    {
                        continue;
                    }

                    int interfaceId = IdUtilHelper.ComputeId(interfaceType);
                    Dictionary<int, string> methodMap = null;

                    if (!this.idToMethodNameMap.TryGetValue(interfaceId, out methodMap))
                    {
                        methodMap = new Dictionary<int, string>();
                        this.idToMethodNameMap.Add(interfaceId, methodMap);
                    }

                    foreach (MethodInfo method in interfaceType.GetMethods())
                    {
                        methodMap[IdUtilHelper.ComputeId(method)] = method.Name;
                    }

                    this.processedTypes.Add(interfaceType);
                }
            }
        }
    }
}
