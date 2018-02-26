using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.V2;
using Microsoft.ServiceFabric.Services.Remoting.V2.Client;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingClientEventListener : IDisposable
    {
        private readonly IEnumerable<string> correlationDomainExclusionList;
        private readonly bool setComponentCorrelationHttpHeaders;
        private readonly ICorrelationIdLookupHelper correlationIdLookupHelper;
        private readonly TelemetryClient client;
        private readonly TelemetryConfiguration configuration;
        private CacheBasedOperationHolder<DependencyTelemetry> pendingTelemetry = new CacheBasedOperationHolder<DependencyTelemetry>("aisfsdksrdependencies", 100 * 1000);


        public ServiceRemotingClientEventListener(
            TelemetryConfiguration configuration,
            string effectiveProfileQueryEndpoint,
            bool setComponentCorrelationHttpHeaders,
            IEnumerable<string> correlationDomainExclusionList = null,
            ICorrelationIdLookupHelper correlationIdLookupHelper = null)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("rddsr:");

            this.configuration = configuration;
            this.setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
            this.correlationIdLookupHelper = correlationIdLookupHelper ?? new CorrelationIdLookupHelper(effectiveProfileQueryEndpoint);
            this.correlationDomainExclusionList = correlationDomainExclusionList ?? Enumerable.Empty<string>();

            ServiceRemotingClientEvents.SendRequest += ServiceRemotingClientEvents_SendRequest;
            ServiceRemotingClientEvents.ReceiveResponse += ServiceRemotingClientEvents_ReceiveResponse;
        }

        private void ServiceRemotingClientEvents_SendRequest(object sender, EventArgs e)
        {
            try
            {
                ServiceRemotingRequestEventArgs eventArgs = e as ServiceRemotingRequestEventArgs;

                if (eventArgs == null)
                {
                    ServiceFabricSDKEventSource.Log.InvalidEventArgument((typeof(ServiceRemotingRequestEventArgs)).Name, e.GetType().Name);
                    return;
                }

                IService service = (IService)sender;
                var request = eventArgs.Request;
                var messageHeaders = request?.GetHeader();

                // If there are no header objects passed in, we don't do anything.
                if (messageHeaders == null)
                {
                    ServiceFabricSDKEventSource.Log.HeadersNotFound();
                    return;
                }

                var serviceUri = eventArgs.ServiceUri;
                var methodName = eventArgs.MethodName;

                // Weird case, just use the numerical id as the method name
                if (string.IsNullOrEmpty(methodName))
                {
                    methodName = messageHeaders.MethodId.ToString(CultureInfo.InvariantCulture);
                }

                // Call StartOperation, this will create a new activity with the current activity being the parent.
                // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
                // the Name, Type, Data, and Target properties
                var operation = client.StartOperation<DependencyTelemetry>(methodName);
                operation.Telemetry.Type = ServiceRemotingConstants.ServiceRemotingTypeName;
                operation.Telemetry.Data = serviceUri.AbsoluteUri + "/" + methodName;
                operation.Telemetry.Target = serviceUri.AbsoluteUri;

                pendingTelemetry.Store(request, operation);

                if (!messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.ParentIdHeaderName, out byte[] parentIdHeaderValue) &&
                    !messageHeaders.TryGetHeaderValue(ServiceRemotingConstants.CorrelationContextHeaderName, out byte[] correlationContextHeaderValue))
                {
                    messageHeaders.AddHeader(ServiceRemotingConstants.ParentIdHeaderName, operation.Telemetry.Id);
                    byte[] baggageFromActivity = RequestTrackingUtils.GetBaggageFromActivity();
                    if (baggageFromActivity != null)
                    {
                        messageHeaders.AddHeader(ServiceRemotingConstants.CorrelationContextHeaderName, baggageFromActivity);
                    }
               }

                InjectXComponentHeaders(messageHeaders, serviceUri);
            }
            catch(Exception ex)
            {
                // We failed to add the header or activity, let's move on, let's not crash user's application because of that.
                ServiceFabricSDKEventSource.Log.FailedToHandleEvent("ServiceRemotingClientEvents.SendRequest", ex.ToInvariantString());
            }
        }

        private void ServiceRemotingClientEvents_ReceiveResponse(object sender, EventArgs e)
        {
            try
            {
                ServiceRemotingResponseEventArgs successfulResponseArg = e as ServiceRemotingResponseEventArgs;
                ServiceRemotingFailedResponseEventArgs failedResponseArg = e as ServiceRemotingFailedResponseEventArgs;

                bool requestStateSuccessful;
                IServiceRemotingRequestMessage request;

                if (successfulResponseArg != null)
                {
                    // Successful Request
                    requestStateSuccessful = true;
                    request = successfulResponseArg.Request;
                }
                else if (failedResponseArg != null)
                {
                    requestStateSuccessful = false;
                    request = failedResponseArg.Request;
                }
                else
                {
                    ServiceFabricSDKEventSource.Log.InvalidEventArgument((typeof(ServiceRemotingResponseEventArgs)).Name, e.GetType().Name);
                    return;
                }

                IOperationHolder<DependencyTelemetry> dependencyOperation = pendingTelemetry.Get(request);
                if (dependencyOperation != null)
                {
                    pendingTelemetry.Remove(request);

                    // As of now, we are not encapsulated by the platform in IServiceRemotingResponseMessage. Hence we only deal with x-compnent headers in the case of successful call.
                    if (requestStateSuccessful)
                    {
                        UpdateTelemetryBasedOnXComponentResponseHeaders(successfulResponseArg.Response, dependencyOperation.Telemetry);
                    }

                    dependencyOperation.Telemetry.Success = requestStateSuccessful;
                    // Stopping the operation, this will also pop the activity created by StartOperation off the activity stack.
                    client.StopOperation(dependencyOperation);
                }
            }
            catch (Exception ex)
            {
                // We failed to add the header or activity, let's move on, let's not crash user's application because of that.
                ServiceFabricSDKEventSource.Log.FailedToHandleEvent("ServiceRemotingClientEvents.ReceiveResponse", ex.ToInvariantString());
            }
        }

        #region X-Component Correlation helper methods.
        private void UpdateTelemetryBasedOnXComponentResponseHeaders(IServiceRemotingResponseMessage response, DependencyTelemetry dependencyTelemetry)
        {
            if (response == null || response.GetHeader() == null)
            {
                return;
            }

            try
            {
                string targetApplicationId = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(response.GetHeader(), ServiceRemotingConstants.RequestContextCorrelationTargetKey);
                if (!string.IsNullOrEmpty(targetApplicationId) && !string.IsNullOrEmpty(dependencyTelemetry.Context.InstrumentationKey))
                {
                    // We only add the cross component correlation key if the key does not represent the current component.
                    string sourceApplicationId;
                    if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(dependencyTelemetry.Context.InstrumentationKey, out sourceApplicationId) &&
                        targetApplicationId != sourceApplicationId)
                    {
                        dependencyTelemetry.Target += " | " + targetApplicationId;
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(ex));
            }
        }

        private void InjectXComponentHeaders(IServiceRemotingRequestMessageHeader requestHeaders, Uri serviceUri)
        {
            try
            {
                var instrumentationKey = this.configuration.InstrumentationKey;
                if (requestHeaders != null && this.setComponentCorrelationHttpHeaders && !this.correlationDomainExclusionList.Contains(serviceUri.Host))
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(instrumentationKey) && !ServiceRemotingHeaderUtilities.ContainsRequestContextKeyValue(requestHeaders, ServiceRemotingConstants.RequestContextCorrelationSourceKey))
                        {
                            string sourceApplicationId;
                            if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(instrumentationKey, out sourceApplicationId))
                            {
                                ServiceRemotingHeaderUtilities.SetRequestContextKeyValue(requestHeaders, ServiceRemotingConstants.RequestContextCorrelationSourceKey, sourceApplicationId);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(e));
                    }
                }
            }
            catch (Exception e)
            {
                ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(e));
            }
        }
        #endregion

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    ServiceRemotingClientEvents.SendRequest -= ServiceRemotingClientEvents_SendRequest;
                    ServiceRemotingClientEvents.ReceiveResponse -= ServiceRemotingClientEvents_ReceiveResponse;
                    pendingTelemetry.Dispose();
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