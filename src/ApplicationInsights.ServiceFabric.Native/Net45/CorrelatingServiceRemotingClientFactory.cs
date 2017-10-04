namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Client;
    using Microsoft.ServiceFabric.Services.Communication.Client;
    using Microsoft.ServiceFabric.Services.Remoting.V1.Client;
    using System;
    using System.Fabric;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Base class for remoting client factory for constructing clients that can call other Service Fabric services. Clients created by this factory pass correlation ids and relevant information
    /// to the callee so diagnostic traces can be tagged with the relevant ids. This factory wraps and use an instance of <see cref="IServiceRemotingClientFactory"/> for most of the
    /// underying functionality. <see cref="CorrelatingServiceRemotingClientFactory"/> calls <see cref="IServiceRemotingClientFactory"/> to create an inner client, which
    /// handles the main call transport and will be wrapped by a <see cref="CorrelatingServiceRemotingClient"/> object.
    /// </summary>
    internal class CorrelatingServiceRemotingClientFactory : IServiceRemotingClientFactory
    {
        private IServiceRemotingClientFactory innerClientFactory;
        private IMethodNameProvider methodNameProvider;

        /// <summary>
        /// Initializes the factory. It wraps another client factory as its inner client factory to perform many of its core operations.
        /// </summary>
        /// <param name="innerClientFactory">The client factory that this factory wraps.</param>
        /// <param name="methodNameProvider">The provider that helps resolve method ids into method names.</param>
        public CorrelatingServiceRemotingClientFactory(IServiceRemotingClientFactory innerClientFactory, IMethodNameProvider methodNameProvider)
        {
            if (innerClientFactory == null)
            {
                throw new ArgumentNullException(nameof(innerClientFactory));
            }

            this.innerClientFactory = innerClientFactory;
            this.innerClientFactory.ClientConnected += this.ClientConnected;
            this.innerClientFactory.ClientDisconnected += this.ClientDisconnected;
            this.methodNameProvider = methodNameProvider;
        }

        /// <summary>
        /// Event handler that is fired when the Communication client connects to the service endpoint.
        /// </summary>
        public event EventHandler<CommunicationClientEventArgs<IServiceRemotingClient>> ClientConnected;

        /// <summary>
        /// Event handler that is fired when the Communication client disconnects from the service endpoint.
        /// </summary>
        public event EventHandler<CommunicationClientEventArgs<IServiceRemotingClient>> ClientDisconnected;

        /// <summary>
        /// Resolves a partition of the specified service containing one or more communication
        /// listeners and returns a client to communicate to the endpoint corresponding to
        /// the given listenerName. The endpoint of the service is of the form - {"Endpoints":{"Listener1":"Endpoint1","Listener2":"Endpoint2"
        /// ...}}
        /// </summary>
        /// <param name="serviceUri">Uri of the service to resolve.</param>
        /// <param name="partitionKey">Key that identifies the partition to resolve.</param>
        /// <param name="targetReplicaSelector">Specifies which replica in the partition identified by the partition key, the client should connect to.</param>
        /// <param name="listenerName">Specifies which listener in the endpoint of the chosen replica, to which the client should connect to.</param>
        /// <param name="retrySettings">Specifies the retry policy that should be used for exceptions that occur when creating the client.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A System.Threading.Tasks.Task that represents outstanding operation. The result of the Task is the <see cref="CorrelatingServiceRemotingClient"/> object.</returns>
        public async Task<IServiceRemotingClient> GetClientAsync(Uri serviceUri, ServicePartitionKey partitionKey, TargetReplicaSelector targetReplicaSelector, 
            string listenerName, OperationRetrySettings retrySettings, CancellationToken cancellationToken)
        {
            var innerClient = await this.innerClientFactory.GetClientAsync(serviceUri, partitionKey, targetReplicaSelector, listenerName, retrySettings, cancellationToken).ConfigureAwait(false);
            return new CorrelatingServiceRemotingClient(innerClient, serviceUri, methodNameProvider);
        }

        /// <summary>
        /// Resolves a partition of the specified service containing one or more communication
        /// listeners and returns a client to communicate to the endpoint corresponding to
        /// the given listenerName. The endpoint of the service is of the form - {"Endpoints":{"Listener1":"Endpoint1","Listener2":"Endpoint2"
        /// ...}}
        /// </summary>
        /// <param name="previousRsp">Previous ResolvedServicePartition value.</param>
        /// <param name="targetReplicaSelector">Specifies which replica in the partition identified by the partition key, the client should connect to.</param>
        /// <param name="listenerName">Specifies which listener in the endpoint of the chosen replica, to which the client should connect to.</param>
        /// <param name="retrySettings">Specifies the retry policy that should be used for exceptions that occur when creating the client.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A System.Threading.Tasks.Task that represents outstanding operation. The result of the Task is the <see cref="CorrelatingServiceRemotingClient"/> object.</returns>
        public async Task<IServiceRemotingClient> GetClientAsync(ResolvedServicePartition previousRsp, TargetReplicaSelector targetReplicaSelector, string listenerName, OperationRetrySettings retrySettings, CancellationToken cancellationToken)
        {
            var innerClient = await this.innerClientFactory.GetClientAsync(previousRsp, targetReplicaSelector, listenerName, retrySettings, cancellationToken).ConfigureAwait(false);
            return new CorrelatingServiceRemotingClient(innerClient, previousRsp.ServiceName, methodNameProvider);
        }

        /// <summary>
        /// Handles the exceptions that occur in the CommunicationClient when sending a message to the Service.
        /// </summary>
        /// <param name="client">Communication client.</param>
        /// <param name="exceptionInformation">Information about exception that happened while communicating with the service.</param>
        /// <param name="retrySettings">Specifies the retry policy that should be used for handling the reported exception.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A System.Threading.Tasks.Task that represents outstanding operation. The result
        /// of the Task is a Microsoft.ServiceFabric.Services.Communication.Client.OperationRetryControl
        /// object that provides information on retry policy for this exception.</returns>
        public Task<OperationRetryControl> ReportOperationExceptionAsync(IServiceRemotingClient client, ExceptionInformation exceptionInformation, OperationRetrySettings retrySettings, CancellationToken cancellationToken)
        {
            IServiceRemotingClient effectiveClient = (client as IWrappingClient)?.InnerClient ?? client;
            return this.innerClientFactory.ReportOperationExceptionAsync(effectiveClient, exceptionInformation, retrySettings, cancellationToken);
        }
    }
}
