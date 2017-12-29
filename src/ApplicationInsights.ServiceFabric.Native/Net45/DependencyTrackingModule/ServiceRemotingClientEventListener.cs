using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing;
using Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities;
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
        private Lazy<DataContractSerializer> baggageSerializer;
        private ConcurrentDictionary<IServiceRemotingRequestMessage, IOperationHolder<DependencyTelemetry>> pendingTelemetry = new ConcurrentDictionary<IServiceRemotingRequestMessage, IOperationHolder<DependencyTelemetry>>();


        public ServiceRemotingClientEventListener(
            TelemetryConfiguration configuration,
            string effectiveProfileQueryEndpoint,
            bool setComponentCorrelationHttpHeaders,
            IEnumerable<string> correlationDomainExclusionList = null,  // todo (nizarq): see if we need this and next
            ICorrelationIdLookupHelper correlationIdLookupHelper = null)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("rdddc:"); // rdddc represents remote dependency based on diagnostic source 

            this.configuration = configuration;
            this.setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
            this.correlationIdLookupHelper = correlationIdLookupHelper ?? new CorrelationIdLookupHelper(effectiveProfileQueryEndpoint);
            this.correlationDomainExclusionList = correlationDomainExclusionList ?? Enumerable.Empty<string>();
            this.baggageSerializer = new Lazy<DataContractSerializer>(() => new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>)));

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
                    // Todo (nizarq) : Log
                    return;
                }

                IService service = (IService)sender;
                var request = eventArgs.Request;
                var serviceUri = eventArgs.ServiceUri;
                var methodName = eventArgs.MethodName;

                var messageHeaders = request.GetHeader();

                // Weird case, just use the numerical id as the method name
                if (string.IsNullOrEmpty(methodName))
                {
                    methodName = messageHeaders.MethodId.ToString();
                }

                // Call StartOperation, this will create a new activity with the current activity being the parent.
                // Since service remoting doesn't really have an URL like HTTP URL, we will do our best approximate that for
                // the Name, Type, Data, and Target properties
                var operation = client.StartOperation<DependencyTelemetry>(methodName);
                operation.Telemetry.Type = ServiceRemotingLoggingStrings.ServiceRemotingTypeName;
                operation.Telemetry.Data = serviceUri.AbsoluteUri + "/" + methodName;
                operation.Telemetry.Target = serviceUri.AbsoluteUri;

                // Todo (nizarq): There is a potential for this dictionary to grow crazy big.
                pendingTelemetry[request] = operation;

                if (!messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.ParentIdHeaderName, out byte[] parentIdHeaderValue) &&
                    !messageHeaders.TryGetHeaderValue(ServiceRemotingLoggingStrings.CorrelationContextHeaderName, out byte[] correlationContextHeaderValue))
                {
                    messageHeaders.AddHeader(ServiceRemotingLoggingStrings.ParentIdHeaderName, operation.Telemetry.Id);

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
                }

                InjectXComponentHeaders(messageHeaders, serviceUri);
            }
            catch
            {
                // We failed to add the header or activity, let's move on, let's not crash user's application because of that.
                // Todo (nizarq): Log this somewhere.
            }
        }

        private void ServiceRemotingClientEvents_ReceiveResponse(object sender, EventArgs e)
        {
            ServiceRemotingResponseEventArgs arg = e as ServiceRemotingResponseEventArgs;

            if (arg == null)
            {
                // Todo (nizarq): Log
                return;
            }

            var request = arg.Request;
            var response = arg.Response;

            IOperationHolder<DependencyTelemetry> dependencyOperation;
            if (pendingTelemetry.TryGetValue(request, out dependencyOperation))
            {
                // Todo (nizarq): Handle exception response.
                // Is there a way to tell whether remote sent an exception, so we add a failed request, or whether something went wrong before remote call was made so we add a exception telemetry.

                try
                {
                    string targetApplicationId = ServiceRemotingHeaderUtilities.GetRequestContextKeyValue(response.GetHeader(), RequestResponseHeaders.RequestContextCorrelationTargetKey);
                    if (!string.IsNullOrEmpty(targetApplicationId) && !string.IsNullOrEmpty(dependencyOperation.Telemetry.Context.InstrumentationKey))
                    {
                        // We only add the cross component correlation key if the key does not represent the current component.
                        string sourceApplicationId;
                        if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(dependencyOperation.Telemetry.Context.InstrumentationKey, out sourceApplicationId) &&
                            targetApplicationId != sourceApplicationId)
                        {
                            dependencyOperation.Telemetry.Type = ServiceRemotingLoggingStrings.ServiceRemotingTypeNameTracked;
                            dependencyOperation.Telemetry.Target += " | " + targetApplicationId;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(ex));
                }


                // Stopping the operation, this will also pop the activity created by StartOperation off the activity stack.
                client.StopOperation(dependencyOperation);
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
                        if (!string.IsNullOrEmpty(instrumentationKey) && !ServiceRemotingHeaderUtilities.ContainsRequestContextKeyValue(requestHeaders, RequestResponseHeaders.RequestContextCorrelationSourceKey))
                        {
                            string sourceApplicationId;
                            if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(instrumentationKey, out sourceApplicationId))
                            {
                                ServiceRemotingHeaderUtilities.SetRequestContextKeyValue(requestHeaders, RequestResponseHeaders.RequestContextCorrelationSourceKey, sourceApplicationId);
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