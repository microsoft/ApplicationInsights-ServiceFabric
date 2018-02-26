namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.ServiceFabric.Module;
    using Microsoft.ServiceFabric.Actors;
    using Microsoft.ServiceFabric.Actors.Remoting.V1.Runtime;
    using Microsoft.ServiceFabric.Actors.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting;
    using Microsoft.ServiceFabric.Services.Remoting.V1;
    using Microsoft.ServiceFabric.Services.Remoting.V1.Runtime;
    using System;
    using System.Collections.Generic;
    using System.Fabric;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Threading.Tasks;
    using System.Xml;

    /// <summary>
    /// Service remoting handler that wraps a service and parses correlation id and context, if they have been passed by the caller as
    /// message headers. This allows traces the client and the service to log traces with the same relevant correlation id and context.
    /// </summary>
    public class CorrelatingRemotingMessageHandler : IServiceRemotingMessageHandler, IDisposable
    {
        private IServiceRemotingMessageHandler innerHandler;
        private TelemetryClient telemetryClient;
        private MethodNameProvider methodNameProvider;
        private bool isActorService;

        /// <summary>
        /// Initializes the <see cref="CorrelatingRemotingMessageHandler"/> object. It wraps the given service for all the core
        /// operations for servicing the request.
        /// </summary>
        /// <param name="service">The service whose remoting messages this handler should handle.</param>
        /// <param name="serviceContext">The context object for the service.</param>
        public CorrelatingRemotingMessageHandler(ServiceContext serviceContext, IService service)
        {
            this.InitializeCommonFields();
            this.innerHandler = new ServiceRemotingDispatcher(serviceContext, service);

            // Populate our method name provider with methods from the IService interfaces
            this.methodNameProvider.AddMethodsForProxyOrService(service.GetType().GetInterfaces(), typeof(IService));
        }

        /// <summary>
        /// Initializes the <see cref="CorrelatingRemotingMessageHandler"/> object. It wraps the given actor service for all the core
        /// operations for servicing the request.
        /// </summary>
        /// <param name="actorService">The actor service whose remoting messages this handler should handle.</param>
        public CorrelatingRemotingMessageHandler(ActorService actorService)
        {
            this.InitializeCommonFields();
            this.innerHandler = new ActorServiceRemotingDispatcher(actorService);
            this.isActorService = true;

            // Populate our method name provider with methods from the ActorService interfaces, and the Actor interfaces
            this.methodNameProvider.AddMethodsForProxyOrService(actorService.GetType().GetInterfaces(), typeof(IService));
            this.methodNameProvider.AddMethodsForProxyOrService(actorService.ActorTypeInformation.InterfaceTypes, typeof(IActor));
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

        private void InitializeCommonFields()
        {
            this.telemetryClient = new TelemetryClient();
            this.methodNameProvider = new MethodNameProvider(false /* threadSafe */);
        }

        private async Task<byte[]> HandleAndTrackRequestAsync(ServiceRemotingMessageHeaders messageHeaders, Func<Task<byte[]>> doHandleRequest)
        {
            // Create and prepare activity and RequestTelemetry objects to track this request.
            RequestTelemetry rt = new RequestTelemetry() { ResponseCode = ServiceRemotingConstants.NotApplicableResponseCode };

            messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.ParentIdHeaderName, out string parentId);

            // Do our best effort in setting the request name.
            string methodName = null;

            if (this.isActorService && messageHeaders.TryGetActorMethodAndInterfaceIds(out int methodId, out int interfaceId))
            {
                methodName = this.methodNameProvider.GetMethodName(interfaceId, methodId);

                // Weird case, we couldn't find the method in the map. Just use the numerical id as the method name
                if (string.IsNullOrEmpty(methodName))
                {
                    methodName = methodId.ToString();
                }
            }
            else
            {
                methodName = this.methodNameProvider.GetMethodName(messageHeaders.InterfaceId, messageHeaders.MethodId);

                // Weird case, we couldn't find the method in the map. Just use the numerical id as the method name
                if (string.IsNullOrEmpty(methodName))
                {
                    methodName = messageHeaders.MethodId.ToString();
                }
            }

            byte[] correlationBytes = messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.CorrelationContextHeaderName, out correlationBytes) ? correlationBytes : null;
            var baggage = RequestTrackingUtils.DeserializeBaggage(correlationBytes);
            
            RequestTrackingUtils.UpdateTelemetryBasedOnCorrelationContext(rt, methodName, parentId, baggage);

            // Call StartOperation, this will create a new activity with the current activity being the parent.
            // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
            // the Name, Type, Data, and Target properties
            var operation = telemetryClient.StartOperation<RequestTelemetry>(rt);

            RequestTrackingUtils.UpdateCurrentActivityBaggage(baggage);

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

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        /// <summary>
        /// Overridden by derived class to dispose and clean up resources used by <see cref="CorrelatingRemotingMessageHandler"/>.
        /// </summary>
        /// <param name="disposing">Whether it should dispose managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    IDisposable disposableHandler = this.innerHandler as IDisposable;
                    if (disposableHandler != null)
                    {
                        disposableHandler.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        /// <summary>
        /// Dispose and clean up resources used by <see cref="CorrelatingRemotingMessageHandler"/>
        /// </summary>
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion // IDisposable Support
    }
}
