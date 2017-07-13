using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingDiagnosticSourceListener : IObserver<KeyValuePair<string, object>>, IDisposable
    {
        private TelemetryConfiguration _configuration;
        private string _effectiveProfileQueryEndpoint;
        private bool _setComponentCorrelationHttpHeaders;

        public ServiceRemotingDiagnosticSourceListener(TelemetryConfiguration configuration, string effectiveProfileQueryEndpoint, bool setComponentCorrelationHttpHeaders)
        {
            _configuration = configuration;
            _effectiveProfileQueryEndpoint = effectiveProfileQueryEndpoint;
            _setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
        }

        // Todo (nizarq): Are all of these useful?
        private const string DependencyErrorPropertyKey = "Error";
        private const string ServiceRemotingOutEventName = "Microsoft.ServiceFabric.Services.Remoting.V2.ServiceRemotingRequestOut";
        private const string ServiceRemotingOutStartEventName = "Microsoft.ServiceFabric.Services.Remoting.V2.ServiceRemotingRequestOut.Start";
        private const string ServiceRemotingOutStopEventName = "Microsoft.ServiceFabric.Services.Remoting.V2.ServiceRemotingOut.Stop";
        private const string ServiceRemotingExceptionEventName = "Microsoft.ServiceFabric.Services.Remoting.V2.Exception";

        private readonly IEnumerable<string> correlationDomainExclusionList;
        private readonly bool setComponentCorrelationHttpHeaders;
        private readonly ICorrelationIdLookupHelper correlationIdLookupHelper;
        private readonly TelemetryClient client;
        private readonly TelemetryConfiguration configuration;
        private readonly ServiceRemotingCoreDiagnosticSourceSubscriber subscriber;

        private readonly ConcurrentDictionary<string, Exception> pendingExceptions =
            new ConcurrentDictionary<string, Exception>();

        public ServiceRemotingDiagnosticSourceListener(
            TelemetryConfiguration configuration,
            string effectiveProfileQueryEndpoint,
            bool setComponentCorrelationHttpHeaders,
            IEnumerable<string> correlationDomainExclusionList,
            ICorrelationIdLookupHelper correlationIdLookupHelper)
        {
            this.client = new TelemetryClient(configuration);
            this.client.Context.GetInternalContext().SdkVersion = SdkVersionUtils.GetSdkVersion("rdddc:"); // rdddc represents remote dependency based on diagnostic source 

            this.configuration = configuration;
            this.setComponentCorrelationHttpHeaders = setComponentCorrelationHttpHeaders;
            this.correlationIdLookupHelper = correlationIdLookupHelper ?? new CorrelationIdLookupHelper(effectiveProfileQueryEndpoint);
            this.correlationDomainExclusionList = correlationDomainExclusionList ?? Enumerable.Empty<string>();

            this.subscriber = new ServiceRemotingCoreDiagnosticSourceSubscriber(this);
        }

        /// <summary>
        /// Notifies the observer that the provider has finished sending push-based notifications.
        /// <seealso cref="IObserver{T}.OnCompleted()"/>
        /// </summary>
        public void OnCompleted()
        {
        }

        /// <summary>
        /// Notifies the observer that the provider has experienced an error condition.
        /// <seealso cref="IObserver{T}.OnError(Exception)"/>
        /// </summary>
        /// <param name="error">An object that provides additional information about the error.</param>
        public void OnError(Exception error)
        {
        }

        public void OnNext(KeyValuePair<string, object> evnt)
        {
            switch (evnt.Key)
            {
                case ServiceRemotingOutStartEventName:
                    {
                        this.OnActivityStart((IServiceRemotingRequestMessage)evnt.Value);
                        break;
                    }

                case ServiceRemotingOutStopEventName:
                    {
                        object[] payload = evnt.Value as object[]; 

                        this.OnActivityStop(
                            (IServiceRemotingResponseMessage)payload[1],
                            (IServiceRemotingRequestMessage)payload[0],
                            // do we really need this the task status?
                            (TaskStatus)payload[2]);
                        break;
                    }

                case ServiceRemotingExceptionEventName:
                    {
                        object[] payload = evnt.Value as object[];

                        this.OnException(
                            (Exception)payload[1],
                            (IServiceRemotingRequestMessage)payload[0]);
                        break;
                    }
            }
        }

        public void Dispose()
        {
            if (this.subscriber != null)
            {
                this.subscriber.Dispose();
            }
        }

        //// netcoreapp 2.0 event

        /// <summary>
        /// Handler for Exception event, it is sent when request processing cause an exception (e.g. because of DNS or network issues)
        /// Stop event will be sent anyway with null response.
        /// </summary>
        internal void OnException(Exception exception, IServiceRemotingRequestMessage request)
        {
            Activity currentActivity = Activity.Current;
            if (currentActivity == null)
            {
                ServiceFabricSDKEventSource.Log.CurrentActivityIsNull();
                return;
            }

            ServiceFabricSDKEventSource.Log.ServiceRemotingDiagnosticSourceListenerException(currentActivity.Id);

            this.pendingExceptions.TryAdd(currentActivity.Id, exception);
            this.client.TrackException(exception);
        }

        //// netcoreapp 2.0 event

        /// <summary>
        /// Handler for Activity start event (outgoing request is about to be sent).
        /// </summary>
        internal void OnActivityStart(IServiceRemotingRequestMessage request)
        {
            var currentActivity = Activity.Current;
            if (currentActivity == null)
            {
                ServiceFabricSDKEventSource.Log.CurrentActivityIsNull();
                return;
            }

            ServiceFabricSDKEventSource.Log.ServiceRemotingDiagnosticSourceListenerStart(currentActivity.Id);

            this.InjectRequestHeaders(request, this.configuration.InstrumentationKey);
        }

        //// netcoreapp 2.0 event

        /// <summary>
        /// Handler for Activity stop event (response is received for the outgoing request).
        /// </summary>
        internal void OnActivityStop(IServiceRemotingResponseMessage response, IServiceRemotingRequestMessage request, TaskStatus requestTaskStatus)
        {
            Activity currentActivity = Activity.Current;
            if (currentActivity == null)
            {
                ServiceFabricSDKEventSource.Log.CurrentActivityIsNull();
                return;
            }

            ServiceFabricSDKEventSource.Log.HttpCoreDiagnosticSourceListenerStop(currentActivity.Id);

            DependencyTelemetry telemetry = new DependencyTelemetry();

            // properly fill dependency telemetry operation context: OperationCorrelationTelemetryInitializer initializes child telemetry
            telemetry.Context.Operation.Id = currentActivity.RootId;
            telemetry.Context.Operation.ParentId = currentActivity.ParentId;
            telemetry.Id = currentActivity.Id;
            foreach (var item in currentActivity.Baggage)
            {
                if (!telemetry.Context.Properties.ContainsKey(item.Key))
                {
                    telemetry.Context.Properties[item.Key] = item.Value;
                }
            }

            this.client.Initialize(telemetry);

            telemetry.Target = request.ServiceUri.ToString();
            telemetry.Type = "ServiceRemoting";
            telemetry.Duration = currentActivity.Duration;
            if (response != null)
            {
                this.ParseResponse(response, telemetry);
            }
            else
            {
                Exception exception;
                if (this.pendingExceptions.TryRemove(currentActivity.Id, out exception))
                {
                    telemetry.Context.Properties[DependencyErrorPropertyKey] = exception.GetBaseException().Message;
                }

                telemetry.ResultCode = requestTaskStatus.ToString();
                telemetry.Success = false;
            }

            this.client.Track(telemetry);
        }


        private void InjectRequestHeaders(IServiceRemotingRequestMessage request, string instrumentationKey, bool isLegacyEvent = false)
        {
            try
            {
                var currentActivity = Activity.Current;

                HttpRequestHeaders requestHeaders = request.Headers;
                if (requestHeaders != null && this.setComponentCorrelationHttpHeaders && !this.correlationDomainExclusionList.Contains(request.RequestUri.Host))
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(instrumentationKey) && !HttpHeadersUtilities.ContainsRequestContextKeyValue(requestHeaders, RequestResponseHeaders.RequestContextCorrelationSourceKey))
                        {
                            string sourceApplicationId;
                            if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(instrumentationKey, out sourceApplicationId))
                            {
                                HttpHeadersUtilities.SetRequestContextKeyValue(requestHeaders, RequestResponseHeaders.RequestContextCorrelationSourceKey, sourceApplicationId);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(e));
                    }

                    // Add the root ID
                    string rootId = currentActivity.RootId;
                    if (!string.IsNullOrEmpty(rootId) &&
                        !requestHeaders.Contains(RequestResponseHeaders.StandardRootIdHeader))
                    {
                        requestHeaders.Add(RequestResponseHeaders.StandardRootIdHeader, rootId);
                    }

                    // Add the parent ID
                    string parentId = currentActivity.Id;
                    if (!string.IsNullOrEmpty(parentId) &&
                        !requestHeaders.Contains(RequestResponseHeaders.StandardParentIdHeader))
                    {
                        requestHeaders.Add(RequestResponseHeaders.StandardParentIdHeader, parentId);
                        if (isLegacyEvent)
                        {
                            requestHeaders.Add(RequestResponseHeaders.RequestIdHeader, parentId);
                        }
                    }

                    if (isLegacyEvent)
                    {
                        // we expect baggage to be empty or contain a few items
                        using (IEnumerator<KeyValuePair<string, string>> e = currentActivity.Baggage.GetEnumerator())
                        {
                            if (e.MoveNext())
                            {
                                var baggage = new List<string>();
                                do
                                {
                                    KeyValuePair<string, string> item = e.Current;
                                    baggage.Add(new NameValueHeaderValue(item.Key, item.Value).ToString());
                                }
                                while (e.MoveNext());
                                request.Headers.Add(RequestResponseHeaders.CorrelationContextHeader, baggage);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(e));
            }
        }

        private void ParseResponse(IServiceRemotingResponseMessage response, DependencyTelemetry telemetry)
        {
            try
            {
                string targetApplicationId = HttpHeadersUtilities.GetRequestContextKeyValue(response.Headers, RequestResponseHeaders.RequestContextCorrelationTargetKey);
                if (!string.IsNullOrEmpty(targetApplicationId) && !string.IsNullOrEmpty(telemetry.Context.InstrumentationKey))
                {
                    // We only add the cross component correlation key if the key does not represent the current component.
                    string sourceApplicationId;
                    if (this.correlationIdLookupHelper.TryGetXComponentCorrelationId(telemetry.Context.InstrumentationKey, out sourceApplicationId) &&
                        targetApplicationId != sourceApplicationId)
                    {
                        telemetry.Type = RemoteDependencyConstants.AI;
                        telemetry.Target += " | " + targetApplicationId;
                    }
                }
            }
            catch (Exception e)
            {
                ServiceFabricSDKEventSource.Log.UnknownError(ExceptionUtilities.GetExceptionDetailString(e));
            }

            int statusCode = (int)response.StatusCode;
            telemetry.ResultCode = (statusCode > 0) ? statusCode.ToString(CultureInfo.InvariantCulture) : string.Empty;
            telemetry.Success = (statusCode > 0) && (statusCode < 400);
        }

        /// <summary>
        /// Diagnostic listener implementation that listens for events specific to outgoing dependency requests.
        /// </summary>
        private class ServiceRemotingCoreDiagnosticSourceSubscriber : IObserver<DiagnosticListener>, IDisposable
        {
            private readonly HttpCoreDiagnosticSourceListener httpDiagnosticListener;
            private readonly IDisposable listenerSubscription;
            private readonly bool isNetCore20HttpClient;

            private IDisposable eventSubscription;

            internal ServiceRemotingCoreDiagnosticSourceSubscriber(ServiceRemotingDiagnosticSourceListener listener)
            {
                this.httpDiagnosticListener = listener;

                var httpClientVersion = typeof(HttpClient).GetTypeInfo().Assembly.GetName().Version;
                this.isNetCore20HttpClient = httpClientVersion.CompareTo(new Version(4, 2)) >= 0;

                try
                {
                    this.listenerSubscription = DiagnosticListener.AllListeners.Subscribe(this);
                }
                catch (Exception ex)
                {
                    ServiceFabricSDKEventSource.Log.HttpCoreDiagnosticSubscriberFailedToSubscribe(ex.ToInvariantString());
                }
            }

            /// <summary>
            /// This method gets called once for each existing DiagnosticListener when this
            /// DiagnosticListener is added to the list of DiagnosticListeners
            /// (<see cref="System.Diagnostics.DiagnosticListener.AllListeners"/>). This method will
            /// also be called for each subsequent DiagnosticListener that is added to the list of
            /// DiagnosticListeners.
            /// <seealso cref="IObserver{T}.OnNext(T)"/>
            /// </summary>
            /// <param name="value">The DiagnosticListener that exists when this listener was added to
            /// the list, or a DiagnosticListener that got added after this listener was added.</param>
            public void OnNext(DiagnosticListener value)
            {
                if (value != null)
                {
                    // Comes from https://github.com/dotnet/corefx/blob/master/src/System.Net.Http/src/System/Net/Http/DiagnosticsHandlerLoggingStrings.cs#L12
                    if (value.Name == "HttpHandlerDiagnosticListener")
                    {
                        this.eventSubscription = value.Subscribe(
                            this.httpDiagnosticListener,
                            (evnt, r, _) =>
                            {
                                if (isNetCore20HttpClient)
                                {
                                    if (evnt == ServiceRemotingExceptionEventName)
                                    {
                                        return true;
                                    }

                                    if (!evnt.StartsWith(ServiceRemotingOutEventName, StringComparison.Ordinal))
                                    {
                                        return false;
                                    }

                                    if (evnt == ServiceRemotingOutEventName && r != null)
                                    {
                                        var request = (IServiceRemotingRequestMessage)r;
                                        return !this.applicationInsightsUrlFilter.IsApplicationInsightsUrl(request.RequestUri);
                                    }
                                }

                                return true;
                            });
                    }
                }
            }

            /// <summary>
            /// Notifies the observer that the provider has finished sending push-based notifications.
            /// <seealso cref="IObserver{T}.OnCompleted()"/>
            /// </summary>
            public void OnCompleted()
            {
            }

            /// <summary>
            /// Notifies the observer that the provider has experienced an error condition.
            /// <seealso cref="IObserver{T}.OnError(Exception)"/>
            /// </summary>
            /// <param name="error">An object that provides additional information about the error.</param>
            public void OnError(Exception error)
            {
            }

            public void Dispose()
            {
                if (this.eventSubscription != null)
                {
                    this.eventSubscription.Dispose();
                }

                if (this.listenerSubscription != null)
                {
                    this.listenerSubscription.Dispose();
                }
            }
        }

        // Todo (nizarq): Temporary
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingRequestMessage
    {
        /// <summary>
        /// 
        /// </summary>
        public Uri ServiceUri { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IServiceRemotingRequestMessageHeader GetHeader();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IServiceRemotingRequestMessageBody GetBody();

    }

    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingRequestMessageHeader
    {
        /// <summary>
        /// 
        /// </summary>
        int MethodId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        int InterfaceId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        string InvocationId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="headerName"></param>
        /// <param name="headerValue"></param>
        void AddHeader(string headerName, string headerValue);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="headerName"></param>
        /// <param name="headerValue"></param>
        /// <returns></returns>
        bool TryGetHeaderValue(string headerName, out string headerValue);


    }

    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingRequestMessageBody
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="position"></param>
        /// <param name="parameName"></param>
        /// <param name="parameter"></param>
        void SetParameter(int position, string parameName, object parameter);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="position"></param>
        /// <param name="parameName"></param>
        /// <param name="paramType"></param>
        /// <returns></returns>
        object GetParameter(int position, string parameName, Type paramType);
    }


    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingResponseMessage
    {
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IServiceRemotingResponseMessageHeader GetHeader();

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        IServiceRemotingResponseMessageBody GetBody();
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingResponseMessageBody
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="response"></param>
        void Set(object response);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="paramType"></param>
        /// <returns></returns>
        object Get(Type paramType);
    }

    /// <summary>
    /// 
    /// </summary>
    public interface IServiceRemotingResponseMessageHeader
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="headerName"></param>
        /// <param name="headerValue"></param>
        void AddHeader(string headerName, string headerValue);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="hasremoteexception"></param>
        /// <param name="headerValue"></param>
        /// <returns></returns>
        bool TryGetHeaderValue(string hasremoteexception, out string headerValue);
    }
}