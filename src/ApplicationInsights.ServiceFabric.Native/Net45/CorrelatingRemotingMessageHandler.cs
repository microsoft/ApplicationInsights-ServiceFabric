namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ServiceFabric.Actors.Remoting.Runtime;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Builder;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Fabric;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// Service remoting handler that wraps a service and parses correlation id and context, if they have been passed by the caller as
    /// message headers. This allows traces the client and the service to log traces with the same relevant correlation id and context.
    /// </summary>
    public class CorrelatingRemotingMessageHandler : IServiceRemotingMessageHandler
    {
        private Lazy<DataContractSerializer> baggageSerializer;

        private IServiceRemotingMessageHandler innerHandler;
        private TelemetryClient telemetryClient;
        private IDictionary<int, ServiceMethodDispatcherBase> methodMap;
        private ServiceContext serviceContext;

        /// <summary>
        /// Initializes the <see cref="CorrelatingRemotingMessageHandler"/> object. It wraps the given service for all the core
        /// operations for servicing the request.
        /// </summary>
        public CorrelatingRemotingMessageHandler(ServiceContext serviceContext, IService service)
        {
            this.innerHandler = new ServiceRemotingDispatcher(serviceContext, service);
            this.serviceContext = serviceContext;
            Initialize();
        }

        /// <summary>
        /// Initializes the <see cref="CorrelatingRemotingMessageHandler"/> object. It wraps the given actor service for all the core
        /// operations for servicing the request.
        /// </summary>
        public CorrelatingRemotingMessageHandler(ActorService actorService)
        {
            this.innerHandler = new ActorServiceRemotingDispatcher(actorService);
            this.serviceContext = actorService.Context;
            Initialize();
        }

        /// <summary>
        /// Handles a one way message from the client. It consumes the correlation id and context from the message headers, if any.
        /// </summary>
        /// <param name="requestContext">Request context - contains additional information about the request.</param>
        /// <param name="messageHeaders">Request message headers.</param>
        /// <param name="requestBody">Request message body.</param>
        public void HandleOneWay(IServiceRemotingRequestContext requestContext, ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody)
        {
            HandleAndTrackRequestAsync(messageHeaders, () => 
            {
                this.innerHandler.HandleOneWay(requestContext, messageHeaders, requestBody);
                return Task.FromResult<byte[]>(null);
            }).Forget();
        }

        /// <summary>
        /// Handles a message from the client that requires a response from the service. It consumes the correlation id and
        /// context from the message headers, if any.
        /// </summary>
        /// <param name="requestContext">Request context - contains additional information about the request.</param>
        /// <param name="messageHeaders">Request message headers.</param>
        /// <param name="requestBody">Request message body</param>
        /// <returns></returns>
        public Task<byte[]> RequestResponseAsync(IServiceRemotingRequestContext requestContext, ServiceRemotingMessageHeaders messageHeaders, byte[] requestBody)
        {
            return HandleAndTrackRequestAsync(messageHeaders, () => this.innerHandler.RequestResponseAsync(requestContext, messageHeaders, requestBody));
        }

        private void Initialize()
        {
            this.telemetryClient = new TelemetryClient();
            this.baggageSerializer = new Lazy<DataContractSerializer>(() => new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>)));

            // TODO: SF should expose method name without the need to use reflection
            this.methodMap = typeof(ServiceRemotingDispatcher).GetField("methodDispatcherMap", BindingFlags.Instance | BindingFlags.NonPublic)
                                .GetValue(this.innerHandler) as IDictionary<int, ServiceMethodDispatcherBase>;
        }

        private async Task<byte[]> HandleAndTrackRequestAsync(ServiceRemotingMessageHeaders messageHeaders, Func<Task<byte[]>> doHandleRequest)
        {
            // Create a new activity object representing the activity coming from the caller. This won't be the activity
            // used to track our further operations, but it will just serve as the parent activity. After it's restored
            // with data from the wire, we call start to put it into the activity stack so it's in effect.
            var activity = new Activity(ServiceRemotingLoggingStrings.InboundRequestActivityName);
            if (messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.RequestIdHeaderName, out string requestId))
            {
                activity.SetParentId(requestId);

                if (messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.CorrelationContextHeaderName, out byte[] correlationBytes))
                {
                    var baggageBytesStream = new MemoryStream(correlationBytes, writable: false);
                    var dictionaryReader = XmlDictionaryReader.CreateBinaryReader(baggageBytesStream, XmlDictionaryReaderQuotas.Max);
                    var baggage = this.baggageSerializer.Value.ReadObject(dictionaryReader) as IEnumerable<KeyValuePair<string, string>>;
                    foreach (KeyValuePair<string, string> pair in baggage)
                    {
                        activity.AddBaggage(pair.Key, pair.Value);
                    }

                }
            }
            activity.Start();

            // Create and prepare activity and RequestTelemetry objects to track this request.
            try
            {
                RequestTelemetry rt = new RequestTelemetry();
                rt.Id = activity.Id;
                rt.Context.Operation.Id = activity.RootId;
                rt.Context.Operation.ParentId = activity.ParentId;

                rt.Properties.Add(nameof(ServiceContext.ServiceName), this.serviceContext.ServiceName.ToString());
                rt.Properties.Add(nameof(ServiceContext.PartitionId), this.serviceContext.PartitionId.ToString());
                rt.Properties.Add(nameof(ServiceContext.ReplicaOrInstanceId), this.serviceContext.ReplicaOrInstanceId.ToString(CultureInfo.InvariantCulture));

                if (this.methodMap != null && this.methodMap.TryGetValue(messageHeaders.InterfaceId, out ServiceMethodDispatcherBase method))
                {
                    try
                    {
                        string requestName = this.serviceContext.ServiceName + "/" + method.GetMethodName(messageHeaders.MethodId);
                        rt.Name = requestName;
                    }
                    catch (KeyNotFoundException) { }
                }

                // Starts the operation. This also creates a new activity, and it's a child activity of the activity that we
                // previous restored earlier as the parent.
                var operation = telemetryClient.StartOperation<RequestTelemetry>(rt);

                bool success = true;
                try
                {
                    byte[] result = await doHandleRequest();
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
                    rt.Success = success;

                    // Stopping the operation, this will also pop the activity created by StartOperation off the activity stack.
                    telemetryClient.StopOperation(operation);
                }
            }
            finally
            {
                // Stopping the operation, this will also pop the activity we created to represent the parent activity.
                activity.Stop();
            }
        }
    }
}
