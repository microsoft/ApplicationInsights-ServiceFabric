namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Builder;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Fabric;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// Service remoting client that wraps another service remoting client and adds correlation id and context propagation support. This allows
    /// traces the client and the service to log traces with the same relevant correlation id and context.
    /// </summary>
    internal class CorrelatingServiceRemotingClient : IServiceRemotingClient, IWrappingClient
    {
        private Uri serviceUri;
        private Lazy<DataContractSerializer> baggageSerializer;
        private TelemetryClient telemetryClient;
        private IMethodNameProvider methodNameProvider;

        /// <summary>
        /// Initializes the <see cref="CorrelatingServiceRemotingClient"/> object. It wraps the given inner client object for all the core
        /// remote call operation.
        /// </summary>
        /// <param name="innerClient">The client object which this client wraps.</param>
        /// <param name="serviceUri">The target Uri of the service which this client will call.</param>
        public CorrelatingServiceRemotingClient(IServiceRemotingClient innerClient, Uri serviceUri, IMethodNameProvider methodNameProvider)
        {
            if (innerClient == null)
            {
                throw new ArgumentNullException(nameof(innerClient));
            }
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            this.InnerClient = innerClient;
            this.serviceUri = serviceUri;
            this.baggageSerializer = new Lazy<DataContractSerializer>(() => new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>)));
            this.telemetryClient = new TelemetryClient();
            this.methodNameProvider = methodNameProvider;
        }

        /// <summary>
        /// Gets or Sets the Resolved service partition which was used when this client was created.
        /// </summary>
        public ResolvedServicePartition ResolvedServicePartition { get => this.InnerClient.ResolvedServicePartition; set => this.InnerClient.ResolvedServicePartition = value; }

        /// <summary>
        /// Gets or Sets the name of the listener in the replica or instance to which the client is connected to.
        /// </summary>
        public string ListenerName { get => this.InnerClient.ListenerName; set => this.InnerClient.ListenerName = value; }

        /// <summary>
        /// Gets or Sets the service endpoint to which the client is connected to.
        /// </summary>
        public ResolvedServiceEndpoint Endpoint { get => this.InnerClient.Endpoint; set => this.InnerClient.Endpoint = value; }

        /// <summary>
        /// Gets the inner client which this client wraps.
        /// </summary>
        public IServiceRemotingClient InnerClient { get; private set; }

        /// <summary>
        /// Sends a message to the service and gets a response back. The correlation id and context are sent along with
        /// the message as message headers.
        /// </summary>
        /// <param name="messageHeaders">Message headers.</param>
        /// <param name="requestBody">Message body.</param>
        /// <returns>Response body.</returns>
        public Task<byte[]> RequestResponseAsync(ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody)
        {
            return SendAndTrackRequestAsync(messageHeaders, requestBody, () => this.InnerClient.RequestResponseAsync(messageHeaders, requestBody));
        }

        /// <summary>
        /// Sends a one-way message to the service. The correlation id and context are sent along with the message
        /// as message headers.
        /// </summary>
        /// <param name="messageHeaders">Message headers.</param>
        /// <param name="requestBody">Message body.</param>
        public void SendOneWay(ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody)
        {
            SendAndTrackRequestAsync(messageHeaders, requestBody, () =>
            {
                this.InnerClient.SendOneWay(messageHeaders, requestBody);
                return Task.FromResult<byte[]>(null);
            }).Forget();
        }

        private async Task<byte[]> SendAndTrackRequestAsync(ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody, Func<Task<byte[]>> doSendRequest)
        {
            string methodName = this.methodNameProvider.GetMethodName(messageHeaders.InterfaceId, messageHeaders.MethodId);

            // Weird case, just use the numerical id as the method name
            if (string.IsNullOrEmpty(methodName))
            {
                methodName = messageHeaders.MethodId.ToString();
            }

            // Since service remoting doesn't really have an URL like HTTP URL, we will fake one up here containing the service URI with the interface id and
            // method id of the remote method
            string operationName = this.serviceUri.AbsoluteUri + "/" + methodName;

            // Call StartOperation, this will create a new activity with the current activity being the parent.
            var operation = telemetryClient.StartOperation<DependencyTelemetry>(operationName);
            operation.Telemetry.Type = ServiceRemotingLoggingStrings.ServiceRemotingTypeName;
            operation.Telemetry.Data = operationName;
            operation.Telemetry.Target = operationName;

            try
            {
                messageHeaders.AddHeader(ServiceRemotingLoggingStrings.ParentIdHeaderName, operation.Telemetry.Id);
                messageHeaders.AddHeader(ServiceRemotingLoggingStrings.RootIdHeaderName, operation.Telemetry.Context.Operation.Id);

                // We expect the baggage to not be there at all or just contain a few small items
                Activity currentActivity = Activity.Current;
                if (currentActivity.Baggage.Any())
                {
                    using (var ms = new MemoryStream())
                    {
                        var dictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(ms);
                        this.baggageSerializer.Value.WriteObject(dictionaryWriter, currentActivity.Baggage);
                        dictionaryWriter.Flush();
                        messageHeaders.AddHeader(ServiceRemotingLoggingStrings.CorrelationContextHeaderName, ms.GetBuffer());
                    }
                }

                byte[] result = await doSendRequest().ConfigureAwait(false);
                return result;
            }
            catch (Exception e)
            {                
                telemetryClient.TrackException(e);
                operation.Telemetry.Success = false;
                throw;
            }
            finally
            {
                // Stopping the operation, this will also pop the activity created by StartOperation off the activity stack.
                telemetryClient.StopOperation(operation);
            }
        }
    }
}
