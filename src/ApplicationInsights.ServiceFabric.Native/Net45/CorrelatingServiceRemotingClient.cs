namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ServiceFabric.Services.Remoting;
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
    public class CorrelatingServiceRemotingClient : IServiceRemotingClient, IWrappingClient
    {
        private Uri serviceUri;
        private Lazy<DataContractSerializer> baggageSerializer;
        private TelemetryClient telemetryClient;
        private ServiceContext serviceContext;
        private ITelemetryInitializer fabricTelemetryInitializer;

        /// <summary>
        /// Initializes the <see cref="CorrelatingServiceRemotingClient"/> object. It wraps the given inner client object for all the core
        /// remote call operation.
        /// </summary>
        /// <param name="innerClient">The client object which this client wraps.</param>
        /// <param name="serviceUri">The target Uri of the service which this client will call.</param>
        public CorrelatingServiceRemotingClient(IServiceRemotingClient innerClient, Uri serviceUri)
            : this(innerClient, serviceUri, null)
        {
        }

        /// <summary>
        /// Initializes the <see cref="CorrelatingServiceRemotingClient"/> object. It wraps the given inner client object for all the core
        /// remote call operation. It allows passing in a ServiceContext object associated with the caller so telemetry can be logged with
        /// service fabric properties.
        /// </summary>
        /// <param name="innerClient">The client object which this client wraps.</param>
        /// <param name="serviceUri">The target Uri of the service which this client will call.</param>
        /// <param name="clientServiceContext">The service context of the caller creating this client object. Pass in null if there isn't one.</param>
        public CorrelatingServiceRemotingClient(IServiceRemotingClient innerClient, Uri serviceUri, ServiceContext clientServiceContext)
        {
            if (innerClient == null)
            {
                throw new ArgumentNullException(nameof(innerClient));
            }
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            this.serviceContext = clientServiceContext;
            this.fabricTelemetryInitializer = FabricTelemetryInitializerExtension.CreateFabricTelemetryInitializer(this.serviceContext);
            this.InnerClient = innerClient;
            this.serviceUri = serviceUri;
            this.baggageSerializer = new Lazy<DataContractSerializer>(() => new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>)));
            this.telemetryClient = new TelemetryClient();
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
            DependencyTelemetry dt = new DependencyTelemetry();
            dt.Name = ServiceRemotingLoggingStrings.OutboundRequestActivityName;

            // Determine if there is currently an activity. If there is, initializes the dependency telemetry with the activity information,
            // which helps set the right context for the dependency telemetry
            Activity currentActivity = Activity.Current;
            if (currentActivity != null)
            {
                dt.Id = currentActivity.Id;
                dt.Context.Operation.Id = currentActivity.RootId;
                dt.Context.Operation.ParentId = currentActivity.ParentId;
            }

            this.fabricTelemetryInitializer.Initialize(dt);

            // Call StartOperation, this will create a new activity with the current activity being the parent. This
            // new activity also inherits most of the properties/attributes carried by the DependencyTelemetry object.
            var operation = telemetryClient.StartOperation<DependencyTelemetry>(dt);

            bool success = true;
            try
            {
                // After the operation, the activity stack, if any, will have been changed. We need
                // to update what our current activity is.
                currentActivity = Activity.Current;
                if (currentActivity != null)
                {
                    messageHeaders.AddHeader(ServiceRemotingLoggingStrings.RequestIdHeaderName, currentActivity.Id);

                    // We expect the baggage to not be there at all or just contain a few small items
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
                }

                byte[] result = await doSendRequest();
                return result;
            }
            catch (Exception e)
            {
                success = false;
                telemetryClient.TrackException(e);
                throw;
            }
            finally
            {
                dt.Success = success;

                // Stopping the operation, this will also pop the activity created by StartOperation off the activity stack.
                telemetryClient.StopOperation(operation);
            }
        }
    }
}
