namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Builder;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.Reflection;

    /// <summary>
    /// Base class for <see cref="CorrelatingServiceProxyFactory"/> and <see cref="CorrelatingActorProxyFactory"/>.
    /// This base class provides common functionality such as the management of the method map providing
    /// mapping of ids to method names. 
    /// </summary>
    public abstract class CorrelatingProxyFactory : IMethodNameProvider
    {
        private ServiceContext serviceContext;
        private Dictionary<int, ServiceMethodDispatcherBase> methodMap;
        private HashSet<Type> processedTypes;
        private object thisLock;

        /// <summary>
        /// Instantiates the <see cref="CorrelatingProxyFactory"/> with the specified remoting factory and retrysettings.
        /// </summary>
        /// <param name="serviceContext">The service context for the calling service</param>
        public CorrelatingProxyFactory(ServiceContext serviceContext)
        {
            if (serviceContext == null)
            {
                throw new ArgumentNullException(nameof(serviceContext));
            }

            this.serviceContext = serviceContext;
            methodMap = new Dictionary<int, ServiceMethodDispatcherBase>();
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
                if (this.methodMap.TryGetValue(interfaceId, out ServiceMethodDispatcherBase methods))
                {
                    try
                    {
                        return methods.GetMethodName(methodId);
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Add all methods for the service interface implemented by the proxy to the method map.
        /// </summary>
        /// <param name="proxy">Proxy with methods on the service interface</param>
        protected void AddMethodsForProxy<TServiceInterface>(TServiceInterface proxy)
            where TServiceInterface : IService
        {
            lock (thisLock)
            {
                if (!this.processedTypes.Contains(typeof(TServiceInterface)))
                {
                    ServiceRemotingDispatcher dispatcher = new ServiceRemotingDispatcher(serviceContext, proxy);

                    // TODO: SF should expose method name without the need to use reflection
                    var methods = typeof(ServiceRemotingDispatcher)?.GetField("methodDispatcherMap", BindingFlags.Instance | BindingFlags.NonPublic)
                                        ?.GetValue(dispatcher) as IDictionary<int, ServiceMethodDispatcherBase>;

                    foreach (var pair in methods)
                    {
                        if (!this.methodMap.ContainsKey(pair.Key))
                        {
                            this.methodMap.Add(pair.Key, pair.Value);
                        }
                    }

                    processedTypes.Add(typeof(TServiceInterface));
                }
            }
        }
    }
}
