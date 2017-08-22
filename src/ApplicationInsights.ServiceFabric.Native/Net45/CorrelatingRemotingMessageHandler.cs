namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ServiceFabric.Actors.Remoting.Runtime;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.Builder;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Fabric;
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
            this.methodMap = typeof(ServiceRemotingDispatcher)?.GetField("methodDispatcherMap", BindingFlags.Instance | BindingFlags.NonPublic)
                                ?.GetValue(this.innerHandler) as IDictionary<int, ServiceMethodDispatcherBase>;
        }

        private async Task<byte[]> HandleAndTrackRequestAsync(ServiceRemotingMessageHeaders messageHeaders, Func<Task<byte[]>> doHandleRequest)
        {
            // Create and prepare activity and RequestTelemetry objects to track this request.
            RequestTelemetry rt = new RequestTelemetry();

            if (messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.ParentIdHeaderName, out string parentId))
            {
                rt.Context.Operation.ParentId = parentId;
                rt.Context.Operation.Id = GetOperationId(parentId);
            }

            // Do our best effort in setting the request name. If we have the service context, add the service name. If
            // we have the method map, add the method name to it.
            if (this.serviceContext != null)
            {
                string methodName = null;
                if (this.methodMap != null && this.methodMap.TryGetValue(messageHeaders.InterfaceId, out ServiceMethodDispatcherBase method))
                {
                    try
                    {
                        methodName = method.GetMethodName(messageHeaders.MethodId);
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }

                // Weird case, just use the numerical id as the method name
                if (string.IsNullOrEmpty(methodName))
                {
                    methodName = messageHeaders.MethodId.ToString();
                }

                rt.Name = methodName;
            }

            if (messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.CorrelationContextHeaderName, out byte[] correlationBytes))
            {
                var baggageBytesStream = new MemoryStream(correlationBytes, writable: false);
                var dictionaryReader = XmlDictionaryReader.CreateBinaryReader(baggageBytesStream, XmlDictionaryReaderQuotas.Max);
                var baggage = this.baggageSerializer.Value.ReadObject(dictionaryReader) as IEnumerable<KeyValuePair<string, string>>;
                foreach (KeyValuePair<string, string> pair in baggage)
                {
                    rt.Context.Properties.Add(pair.Key, pair.Value);
                }
            }

            // Call StartOperation, this will create a new activity with the current activity being the parent.
            // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
            // the Name, Type, Data, and Target properties
            var operation = telemetryClient.StartOperation<RequestTelemetry>(rt);

            try
            {
                byte[] result = await doHandleRequest().ConfigureAwait(false);
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

        /// <summary>
        /// Gets the operation Id from the request Id: substring between '|' and first '.'.
        /// </summary>
        /// <param name="id">Id to get the operation id from.</param>
        private static string GetOperationId(string id)
        {
            // id MAY start with '|' and contain '.'. We return substring between them
            // ParentId MAY NOT have hierarchical structure and we don't know if initially rootId was started with '|',
            // so we must NOT include first '|' to allow mixed hierarchical and non-hierarchical request id scenarios
            int rootEnd = id.IndexOf('.');
            if (rootEnd < 0)
            {
                rootEnd = id.Length;
            }

            int rootStart = id[0] == '|' ? 1 : 0;
            return id.Substring(rootStart, rootEnd - rootStart);
        }
    }
}
