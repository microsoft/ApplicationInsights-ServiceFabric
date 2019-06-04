namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using System;
    using System.Fabric;

    /// <summary>
    /// Class for creating and wrapping the service proxy factory. This class delegates all operations to the
    /// inner <see cref="Microsoft.ServiceFabric.Services.Remoting.V1.Client.ServiceProxyFactory"/> but tracks all the interfaces for which proxies were created.
    /// </summary>
    public class CorrelatingServiceProxyFactory : IServiceProxyFactory
    {
        private MethodNameProvider methodNameProvider;
        private ServiceProxyFactory serviceProxyFactory;

        /// <summary>
        /// Instantiates the <see cref="CorrelatingServiceProxyFactory"/> with the specified remoting factory and retrysettings.
        /// </summary>
        /// <param name="serviceContext">The service context for the calling service</param>
        /// <param name="createServiceRemotingClientFactory">
        /// Specifies the factory method that creates the remoting client factory. The remoting client factory got from this method
        /// is cached in the ServiceProxyFactory.
        /// </param>
        /// <param name="retrySettings">Specifies the retry policy to use on exceptions seen when using the proxies created by this factory</param>
        public CorrelatingServiceProxyFactory(ServiceContext serviceContext, Func<Microsoft.ServiceFabric.Services.Remoting.V1.IServiceRemotingCallbackClient, Microsoft.ServiceFabric.Services.Remoting.V1.Client.IServiceRemotingClientFactory> createServiceRemotingClientFactory = null, OperationRetrySettings retrySettings = null)
        {
            this.methodNameProvider = new MethodNameProvider(true /* threadSafe */);

            // Layer the factory structure so the hierarchy will look like this:
            // CorrelatingServiceProxyFactory
            //  --> ServiceProxyFactory
            //      --> CorrelatingServiceRemotingFactory
            //          --> <Factory created by createServcieRemotingClientFactory>
            this.serviceProxyFactory = new ServiceProxyFactory(
                callbackClient => {
                    Microsoft.ServiceFabric.Services.Remoting.V1.Client.IServiceRemotingClientFactory innerClientFactory = createServiceRemotingClientFactory(callbackClient);
                    return new CorrelatingServiceRemotingClientFactory(innerClientFactory, this.methodNameProvider);
                },
                retrySettings);
        }

        /// <summary>
        /// Creates a proxy to communicate to the specified service using the remoted interface TServiceInterface that 
        /// the service implements.
        /// <typeparam name="TServiceInterface">Interface that is being remoted</typeparam>
        /// <param name="serviceUri">Uri of the Service.</param>
        /// <param name="partitionKey">The Partition key that determines which service partition is responsible for handling requests from this service proxy</param>
        /// <param name="targetReplicaSelector">Determines which replica or instance of the service partition the client should connect to.</param>
        /// <param name="listenerName">This parameter is Optional if the service has a single communication listener. The endpoints from the service
        /// are of the form {"Endpoints":{"Listener1":"Endpoint1","Listener2":"Endpoint2" ...}}. When the service exposes multiple endpoints, this parameter
        /// identifies which of those endpoints to use for the remoting communication.
        /// </param>
        /// <returns>The proxy that implement the interface that is being remoted. The returned object also implement <see cref="T:Microsoft.ServiceFabric.Services.Remoting.Client.IServiceProxy" /> interface.</returns>
        /// </summary>
        public TServiceInterface CreateServiceProxy<TServiceInterface>(Uri serviceUri, ServicePartitionKey partitionKey = null, TargetReplicaSelector targetReplicaSelector = TargetReplicaSelector.Default, string listenerName = null) where TServiceInterface : IService
        {
            TServiceInterface proxy = this.serviceProxyFactory.CreateServiceProxy<TServiceInterface>(serviceUri, partitionKey, targetReplicaSelector, listenerName);
            this.methodNameProvider.AddMethodsForProxyOrService(proxy.GetType().GetInterfaces(), typeof(IService));
            return proxy;
        }

        /// <summary>
        /// Creates a proxy to communicate to the specified service using the remoted interface TServiceInterface that 
        /// the service implements.
        /// <typeparam name="TServiceInterface">Interface that is being remoted</typeparam>
        /// <param name="serviceUri">Uri of the Service.</param>
        /// <param name="partitionKey">The Partition key that determines which service partition is responsible for handling requests from this service proxy</param>
        /// <param name="targetReplicaSelector">Determines which replica or instance of the service partition the client should connect to.</param>
        /// <param name="listenerName">This parameter is Optional if the service has a single communication listener. The endpoints from the service
        /// are of the form {"Endpoints":{"Listener1":"Endpoint1","Listener2":"Endpoint2" ...}}. When the service exposes multiple endpoints, this parameter
        /// identifies which of those endpoints to use for the remoting communication.
        /// </param>
        /// <returns>The proxy that implement the interface that is being remoted. The returned object also implement <see cref="T:Microsoft.ServiceFabric.Services.Remoting.Client.IServiceProxy" /> interface.</returns>
        /// </summary>
        public TServiceInterface CreateNonIServiceProxy<TServiceInterface>(Uri serviceUri, ServicePartitionKey partitionKey = null, TargetReplicaSelector targetReplicaSelector = TargetReplicaSelector.Default, string listenerName = null)
        {
            throw new NotSupportedException();
        }
    }
}
