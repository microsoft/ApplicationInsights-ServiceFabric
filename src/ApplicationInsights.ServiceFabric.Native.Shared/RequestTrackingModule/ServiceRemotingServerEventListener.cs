using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
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
using Microsoft.ApplicationInsights.Extensibility.Implementation;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingServerEventListener : IDisposable
    {
        private TelemetryClient client;
        private TelemetryConfiguration configuration;
        private string effectiveProfileQueryEndpoint;
        private bool setComponentCorrelationHttpHeaders;
        private readonly ICorrelationIdLookupHelper correlationIdLookupHelper;
        private CacheBasedOperationHolder<RequestTelemetry> pendingTelemetry = new CacheBasedOperationHolder<RequestTelemetry>("aisfsdksrrequests", 100 * 1000);

        public ServiceRemotingServerEventListener(TelemetryConfiguration configuration, string effectiveProfileQueryEndpoint, bool setComponentCorrelationHttpHeaders, ICorrelationIdLookupHelper correlationIdLookupHelper = null)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("serviceremoting:");

            this.configuration = configuration;
            this.effectiveProfileQueryEndpoint = effectiveProfileQueryEndpoint;
            this.setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
            this.correlationIdLookupHelper = correlationIdLookupHelper ?? new CorrelationIdLookupHelper(effectiveProfileQueryEndpoint);

            ServiceRemotingServiceEvents.ReceiveRequest += ServiceRemotingServiceEvents_ReceiveRequest;
            ServiceRemotingServiceEvents.SendResponse += ServiceRemotingServiceEvents_SendResponse;
        }

        private void ServiceRemotingServiceEvents_ReceiveRequest(object sender, EventArgs e)
        {
            try
            {
                ServiceRemotingRequestEventArgs eventArgs = e as ServiceRemotingRequestEventArgs;

                if (eventArgs == null)
                {
                    ServiceFabricSDKEventSource.Log.InvalidEventArgument((typeof(ServiceRemotingRequestEventArgs)).Name, e.GetType().Name);
                    return;
                }

                var request = eventArgs.Request;
                var messageHeaders = request?.GetHeader();

                // If there are no header objects passed in, we don't do anything.
                if (messageHeaders == null)
                {
                    ServiceFabricSDKEventSource.Log.HeadersNotFound();
                    return;
                }

                string methodName = eventArgs.MethodName;

                // Create and prepare activity and RequestTelemetry objects to track this request.
                RequestTelemetry rt = new RequestTelemetry();

                messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.ParentIdHeaderName, out string parentId);
                messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.CorrelationContextHeaderName, out byte[] correlationBytes);

                var baggage = RequestTrackingUtils.DeserializeBaggage(correlationBytes);
                RequestTrackingUtils.UpdateTelemetryBasedOnCorrelationContext(rt, methodName, parentId, baggage);

                // Call StartOperation, this will create a new activity with the current activity being the parent.
                // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
                // the Name, Type, Data, and Target properties
                var operation = this.client.StartOperation<RequestTelemetry>(rt);

                RequestTrackingUtils.UpdateCurrentActivityBaggage(baggage);

                // Handle x-component correlation.
                if (string.IsNullOrEmpty(rt.Source) && messageHeaders != null)
                {
                    string telemetrySource = string.Empty;
                    string sourceAppId = null;

                    try
                    {
                        sourceAppId = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(messageHeaders, ServiceRemotingConstants.RequestContextCorrelationSourceKey);
                    }
                    catch (Exception ex)
                    {
                        ServiceFabricSDKEventSource.Log.GetCrossComponentCorrelationHeaderFailed(ex.ToInvariantString());
                    }

                    string currentComponentAppId = string.Empty;
                    bool foundMyAppId = false;
                    if (!string.IsNullOrEmpty(rt.Context.InstrumentationKey))
                    {
                        foundMyAppId = this.correlationIdLookupHelper.TryGetXComponentCorrelationId(rt.Context.InstrumentationKey, out currentComponentAppId); // The startOperation above takes care of setting the right instrumentation key by calling client.Initialize(rt);
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
                        sourceRoleName = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(messageHeaders, ServiceRemotingConstants.RequestContextSourceRoleNameKey);
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
                pendingTelemetry.Store(request, operation);
            }
            catch(Exception ex)
            {
                // We failed to read the header or generate activity, let's move on, let's not crash user's application because of that.
                ServiceFabricSDKEventSource.Log.FailedToHandleEvent("ServiceRemotingServiceEvents.ReceiveRequest", ex.ToInvariantString());
            }
        }

        private void ServiceRemotingServiceEvents_SendResponse(object sender, EventArgs e)
        {
            try
            {
                ServiceRemotingResponseEventArgs successfulResponseArg = e as ServiceRemotingResponseEventArgs;
                ServiceRemotingFailedResponseEventArgs failedResponseArg = e as ServiceRemotingFailedResponseEventArgs;

                IServiceRemotingRequestMessage request;
                bool requestStateSuccessful;

                if (successfulResponseArg != null)
                {
                    request = successfulResponseArg.Request;
                    requestStateSuccessful = true;
                }
                else if (failedResponseArg != null)
                {
                    request = failedResponseArg.Request;
                    requestStateSuccessful = false;
                }
                else
                {
                    ServiceFabricSDKEventSource.Log.InvalidEventArgument((typeof(ServiceRemotingResponseEventArgs)).Name, e.GetType().Name);
                    return;
                }

                IOperationHolder<RequestTelemetry> requestOperation = pendingTelemetry.Get(request);
                if (requestOperation != null)
                {
                    pendingTelemetry.Remove(request);

                    requestOperation.Telemetry.Success = requestStateSuccessful;

                    // Response code is a required field. But isn't applicable in service remoting.
                    // If we don't set it to anything the track call will set it to 200.
                    requestOperation.Telemetry.ResponseCode = ServiceRemotingConstants.NotApplicableResponseCode;
                                        
                    client.StopOperation(requestOperation);
                }

                if (requestStateSuccessful)
                {
                    try
                    {

                        // For now, the platform doesn't support headers for exception responses, so we will for now, not try to add x-component headers for failed responses.
                        var response = successfulResponseArg.Response;

                        var responseHeaders = response.GetHeader();

                        if (!string.IsNullOrEmpty(requestOperation.Telemetry.Context.InstrumentationKey)
                            && ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(responseHeaders, ServiceRemotingConstants.RequestContextCorrelationTargetKey) == null)
                        {
                            string correlationId;

                            if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(requestOperation.Telemetry.Context.InstrumentationKey, out correlationId))
                            {
                                ServiceRemotingHeaderUtilities.SetRequestContextKeyValue(responseHeaders, ServiceRemotingConstants.RequestContextCorrelationTargetKey, correlationId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ServiceFabricSDKEventSource.Log.SetCrossComponentCorrelationHeaderFailed(ex.ToInvariantString());
                    }
                }
            }
            catch(Exception ex)
            {
                ServiceFabricSDKEventSource.Log.FailedToHandleEvent("ServiceRemotingServiceEvents.SendResponse", ex.ToInvariantString());
            }
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
                    this.pendingTelemetry.Dispose();
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