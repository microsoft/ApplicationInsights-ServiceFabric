namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.V1;
    using Microsoft.ServiceFabric.Services.Remoting.V1.Client;
    using System;
    using System.Fabric;

    /// <summary>
    /// Class for creating and wrapping the actor proxy factory. This class delegates all operations to the
    /// inner <see cref="ActorProxyFactory"/> but tracks all the interfaces for which proxies were created. 
    /// </summary>
    public class CorrelatingActorProxyFactory : IActorProxyFactory
    {
        private MethodNameProvider methodNameProvider;
        private ActorProxyFactory actorProxyFactory;

        /// <summary>
        /// Instantiates the <see cref="CorrelatingActorProxyFactory"/> with the specified remoting factory and retrysettings.
        /// </summary>
        /// <param name="serviceContext">The service context for the calling service</param>
        /// <param name="createActorRemotingClientFactory">
        /// Specifies the factory method that creates the remoting client factory. The remoting client factory got from this method
        /// is cached in the ActorProxyFactory.
        /// </param>
        /// <param name="retrySettings">Specifies the retry policy to use on exceptions seen when using the proxies created by this factory</param>
        public CorrelatingActorProxyFactory(ServiceContext serviceContext, Func<IServiceRemotingCallbackClient, IServiceRemotingClientFactory> createActorRemotingClientFactory = null, OperationRetrySettings retrySettings = null)
        {
            this.methodNameProvider = new MethodNameProvider(true /* threadSafe */);

            // Layer the factory structure so the hierarchy will look like this:
            // CorrelatingServiceProxyFactory
            //  --> ServiceProxyFactory
            //      --> CorrelatingServiceRemotingFactory
            //          --> <Factory created by createActorRemotingClientFactory>
            this.actorProxyFactory = new ActorProxyFactory(
                callbackClient => {
                    IServiceRemotingClientFactory innerClientFactory = createActorRemotingClientFactory(callbackClient);
                    return new CorrelatingServiceRemotingClientFactory(innerClientFactory, this.methodNameProvider);
                },
                retrySettings);
        }

        /// <summary>
        /// Creates a proxy to the actor object that implements an actor interface.
        /// </summary>
        /// <typeparam name="TActorInterface">The actor interface implemented by the remote actor object. The returned proxy
        /// object will implement this interface.</typeparam>
        /// <param name="actorId">Actor Id of the proxy actor object. Methods called on this proxy will result
        /// in requests being sent to the actor with this id.</param>
        /// <param name="applicationName">Name of the Service Fabric application that contains the actor service hosting
        /// the actor objects. This parameter can be null if the client is running as part
        /// of that same Service Fabric application. For more information, see Remarks.</param>
        /// <param name="serviceName">Name of the Service Fabric service as configured by Microsoft.ServiceFabric.Actors.Runtime.ActorServiceAttribute
        /// on the actor implementation. By default, the name of the service is derived from
        /// the name of the actor interface. However Microsoft.ServiceFabric.Actors.Runtime.ActorServiceAttribute
        /// is required when an actor implements more than one actor interfaces or an actor
        /// interface derives from another actor interface as the determination of the serviceName
        /// cannot be made automatically.</param>
        /// <param name="listenerName">By default an actor service has only one listener for clients to connect to and
        /// communicate with. However it is possible to configure an actor service with more
        /// than one listeners, the listenerName parameter specifies the name of the listener
        /// to connect to.</param>
        /// <returns>An actor proxy object that implements Microsoft.ServiceFabric.Actors.Client.IActorProxy
        /// and TActorInterface.</returns>
        public TActorInterface CreateActorProxy<TActorInterface>(ActorId actorId, string applicationName = null, string serviceName = null, string listenerName = null) where TActorInterface : IActor
        {
            TActorInterface proxy = this.actorProxyFactory.CreateActorProxy<TActorInterface>(actorId, applicationName, serviceName, listenerName);
            this.methodNameProvider.AddMethodsForProxyOrService(proxy.GetType().GetInterfaces(), typeof(IActor));
            return proxy;
        }

        /// <summary>
        /// Creates a proxy to the actor object that implements an actor interface.
        /// </summary>
        /// <typeparam name="TActorInterface">The actor interface implemented by the remote actor object. The returned proxy
        /// object will implement this interface.</typeparam>
        /// <param name="serviceUri">Uri of the actor service.</param>
        /// <param name="actorId">Actor Id of the proxy actor object. Methods called on this proxy will result
        /// in requests being sent to the actor with this id.</param>
        /// <param name="listenerName">By default an actor service has only one listener for clients to connect to and
        /// communicate with. However it is possible to configure an actor service with more
        /// than one listeners, the listenerName parameter specifies the name of the listener
        /// to connect to.</param>
        /// <returns>An actor proxy object that implements Microsoft.ServiceFabric.Actors.Client.IActorProxy
        /// and TActorInterface.</returns>
        public TActorInterface CreateActorProxy<TActorInterface>(Uri serviceUri, ActorId actorId, string listenerName = null) where TActorInterface : IActor
        {
            TActorInterface proxy = this.actorProxyFactory.CreateActorProxy<TActorInterface>(serviceUri, actorId, listenerName);
            this.methodNameProvider.AddMethodsForProxyOrService(proxy.GetType().GetInterfaces(), typeof(IActor));
            return proxy;
        }

        /// <summary>
        /// Create a proxy to the actor service that is hosting the specified actor id and
        /// implementing specified type of the service interface.
        /// </summary>
        /// <typeparam name="TServiceInterface">The service interface implemented by the actor service.</typeparam>
        /// <param name="serviceUri">Uri of the actor service to connect to.</param>
        /// <param name="actorId">Id of the actor. The created proxy will be connected to the partition of the
        /// actor service hosting actor with this id.</param>
        /// <param name="listenerName">By default an actor service has only one listener for clients to connect to and
        /// communicate with. However it is possible to configure an actor service with more
        /// than one listeners, the listenerName parameter specifies the name of the listener
        /// to connect to.</param>
        /// <returns>A service proxy object that implements Microsoft.ServiceFabric.Services.Remoting.Client.IServiceProxy
        /// and TServiceInterface.</returns>
        public TServiceInterface CreateActorServiceProxy<TServiceInterface>(Uri serviceUri, ActorId actorId, string listenerName = null) where TServiceInterface : IService
        {
            TServiceInterface proxy = this.actorProxyFactory.CreateActorServiceProxy<TServiceInterface>(serviceUri, actorId, listenerName);
            this.methodNameProvider.AddMethodsForProxyOrService(proxy.GetType().GetInterfaces(), typeof(IService));
            return proxy;
        }

        /// <summary>
        /// Create a proxy to the actor service that is hosting the specified actor id and
        /// implementing specified type of the service interface.
        /// </summary>
        /// <typeparam name="TServiceInterface">The service interface implemented by the actor service.</typeparam>
        /// <param name="serviceUri">Uri of the actor service to connect to.</param>
        /// <param name="partitionKey">The key of the actor service partition to connect to.</param>
        /// <param name="listenerName">By default an actor service has only one listener for clients to connect to and
        /// communicate with. However it is possible to configure an actor service with more
        /// than one listeners, the listenerName parameter specifies the name of the listener
        /// to connect to.</param>
        /// <returns>A service proxy object that implements Microsoft.ServiceFabric.Services.Remoting.Client.IServiceProxy
        /// and TServiceInterface.</returns>
        public TServiceInterface CreateActorServiceProxy<TServiceInterface>(Uri serviceUri, long partitionKey, string listenerName = null) where TServiceInterface : IService
        {
            TServiceInterface proxy = this.actorProxyFactory.CreateActorServiceProxy<TServiceInterface>(serviceUri, partitionKey, listenerName);
            this.methodNameProvider.AddMethodsForProxyOrService(proxy.GetType().GetInterfaces(), typeof(IService));
            return proxy;
        }       
    }
}
