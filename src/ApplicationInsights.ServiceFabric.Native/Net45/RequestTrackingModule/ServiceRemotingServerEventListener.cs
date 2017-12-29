using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities;
using Microsoft.ApplicationInsights.ServiceFabric.Module;
using Microsoft.ServiceFabric.Services.Remoting.V2;
using Microsoft.ServiceFabric.Services.Remoting.V2.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing;
using System.Collections.Concurrent;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingServerEventListener : IDisposable
    {
        private TelemetryClient client;
        private TelemetryConfiguration configuration;
        private string effectiveProfileQueryEndpoint;
        private bool setComponentCorrelationHttpHeaders;
        private readonly ICorrelationIdLookupHelper correlationIdLookupHelper;
        private readonly Lazy<DataContractSerializer> baggageSerializer;
        private ConcurrentDictionary<IServiceRemotingRequestMessage, IOperationHolder<RequestTelemetry>> pendingTelemetry = new ConcurrentDictionary<IServiceRemotingRequestMessage, IOperationHolder<RequestTelemetry>>();

        public ServiceRemotingServerEventListener(TelemetryConfiguration configuration, string effectiveProfileQueryEndpoint, bool setComponentCorrelationHttpHeaders, ICorrelationIdLookupHelper correlationIdLookupHelper = null)
        {
            this.client = new TelemetryClient(configuration);
            this.configuration = configuration;
            this.effectiveProfileQueryEndpoint = effectiveProfileQueryEndpoint;
            this.setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
            this.correlationIdLookupHelper = correlationIdLookupHelper ?? new CorrelationIdLookupHelper(effectiveProfileQueryEndpoint);
            this.baggageSerializer = new Lazy<DataContractSerializer>(() => new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>)));


            ServiceRemotingServiceEvents.ReceiveRequest += ServiceRemotingServiceEvents_ReceiveRequest;
            ServiceRemotingServiceEvents.SendResponse += ServiceRemotingServiceEvents_SendResponse;
        }

        private void ServiceRemotingServiceEvents_ReceiveRequest(object sender, EventArgs e)
        {
            ServiceRemotingRequestEventArgs eventArgs = e as ServiceRemotingRequestEventArgs;

            if (eventArgs == null)
            {
                // Todo (nizarq): Log
                return;
            }

            var request = eventArgs.Request;
            var messageHeaders = request.GetHeader();
            string methodName = eventArgs.MethodName;

            // Create and prepare activity and RequestTelemetry objects to track this request.
            RequestTelemetry rt = new RequestTelemetry();
            this.client.Initialize(rt);

            if (messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.ParentIdHeaderName, out string parentId))
            {
                rt.Context.Operation.ParentId = parentId;
                rt.Context.Operation.Id = GetOperationId(parentId);
            }

            rt.Name = methodName;

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

            if (string.IsNullOrEmpty(rt.Source) && messageHeaders != null)
            {
                string telemetrySource = string.Empty;
                string sourceAppId = null;

                try
                {
                    sourceAppId = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(messageHeaders, RequestResponseHeaders.RequestContextCorrelationSourceKey);
                }
                catch (Exception ex)
                {
                    ServiceFabricSDKEventSource.Log.GetCrossComponentCorrelationHeaderFailed(ex.ToInvariantString());
                }

                string currentComponentAppId = string.Empty;
                bool foundMyAppId = false;
                if (!string.IsNullOrEmpty(rt.Context.InstrumentationKey))
                {
                    foundMyAppId = this.correlationIdLookupHelper.TryGetXComponentCorrelationId(rt.Context.InstrumentationKey, out currentComponentAppId);
                }

                // If the source header is present on the incoming request,
                // and it is an external component (not the same ikey as the one used by the current component),
                // then populate the source field.
                if (!string.IsNullOrEmpty(sourceAppId)
                    && foundMyAppId
                    && sourceAppId != currentComponentAppId)
                {
                    telemetrySource = sourceAppId;
                }

                string sourceRoleName = null;

                try
                {
                    sourceRoleName = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(messageHeaders, RequestResponseHeaders.RequestContextSourceRoleNameKey);
                }
                catch (Exception ex)
                {
                    ServiceFabricSDKEventSource.Log.GetComponentRoleNameHeaderFailed(ex.ToInvariantString());
                }

                if (!string.IsNullOrEmpty(sourceRoleName))
                {
                    if (string.IsNullOrEmpty(telemetrySource))
                    {
                        telemetrySource = "roleName:" + sourceRoleName;
                    }
                    else
                    {
                        telemetrySource += " | roleName:" + sourceRoleName;
                    }
                }

                rt.Source = telemetrySource;
            }

            // Call StartOperation, this will create a new activity with the current activity being the parent.
            // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
            // the Name, Type, Data, and Target properties
            var operation = this.client.StartOperation<RequestTelemetry>(rt);

            // Todo (nizarq): There is a potential for this dictionary to grow crazy big.
            pendingTelemetry[request] = operation;
        }

        private void ServiceRemotingServiceEvents_SendResponse(object sender, EventArgs e)
        {
            ServiceRemotingResponseEventArgs arg = e as ServiceRemotingResponseEventArgs;

            if (arg == null)
            {
                // Todo (nizarq): Log
                return;
            }

            var request = arg.Request;
            var response = arg.Response;

            var responseHeaders = response.GetHeader();

            // Todo (nizarq): Determine whether response was success or failure.

            IOperationHolder<RequestTelemetry> requestOperation;
            if (pendingTelemetry.TryGetValue(request, out requestOperation))
            {
                client.StopOperation(requestOperation);
            }

            try
            {
                if (!string.IsNullOrEmpty(requestOperation.Telemetry.Context.InstrumentationKey)
                    && ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(responseHeaders, RequestResponseHeaders.RequestContextCorrelationTargetKey) == null)
                {
                    string correlationId;

                    if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(requestOperation.Telemetry.Context.InstrumentationKey, out correlationId))
                    {
                        ServiceRemotingHeaderUtilities.SetRequestContextKeyValue(responseHeaders, RequestResponseHeaders.RequestContextCorrelationTargetKey, correlationId);
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceFabricSDKEventSource.Log.SetCrossComponentCorrelationHeaderFailed(ex.ToInvariantString());
            }
        }

        // Todo (nizarq): Refactor out to common class - V1 based CorrelatingRemotingMessageHandler.cs also contains this
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

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    ServiceRemotingServiceEvents.ReceiveRequest -= ServiceRemotingServiceEvents_ReceiveRequest;
                    ServiceRemotingServiceEvents.SendResponse -= ServiceRemotingServiceEvents_SendResponse;
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}