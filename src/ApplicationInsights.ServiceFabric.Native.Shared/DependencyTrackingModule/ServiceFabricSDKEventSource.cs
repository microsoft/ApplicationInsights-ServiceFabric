using System;
using System.Diagnostics.Tracing;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal sealed partial class ServiceFabricSDKEventSource : EventSource
    {
        public static readonly ServiceFabricSDKEventSource Log = new ServiceFabricSDKEventSource();

        private ServiceFabricSDKEventSource()
        {
            this.ApplicationName = this.GetApplicationName();
        }

        public string ApplicationName { [NonEvent]get; [NonEvent]private set; }

        [Event(
            1,
            Keywords = Keywords.UserActionable,
            Message = "Failed to retrieve App ID for the current application insights resource. Make sure the configured instrumentation key is valid. Error: {0}",
            Level = EventLevel.Warning)]
        public void FetchAppIdFailed(string exception, string appDomainName = "Incorrect")
        {
            this.WriteEvent(1, exception, this.ApplicationName);
        }

        [Event(
            2,
            Message = "Current Activity is null",
            Level = EventLevel.Error)]
        public void CurrentActivityIsNull(string appDomainName = "Incorrect")
        {
            this.WriteEvent(2, this.ApplicationName);
        }

        [Event(
            3,
            Keywords = Keywords.Diagnostics,
            Message = "Microsoft.ServiceFabric.Services.Remoting.V2.Exception id = '{0}'",
            Level = EventLevel.Verbose)]
        public void ServiceRemotingDiagnosticSourceListenerException(string id, string appDomainName = "Incorrect")
        {
            this.WriteEvent(3, id, this.ApplicationName);
        }

        [Event(
            4,
            Keywords = Keywords.Diagnostics,
            Message = "Microsoft.ServiceFabric.Services.Remoting.V2.ServiceRemotingRequestOut.Start id = '{0}'",
            Level = EventLevel.Verbose)]
        public void ServiceRemotingDiagnosticSourceListenerStart(string id, string appDomainName = "Incorrect")
        {
            this.WriteEvent(4, id, this.ApplicationName);
        }

        [Event(
            5,
            Keywords = Keywords.Diagnostics,
            Message = "Microsoft.ServiceFabric.Services.Remoting.V2.ServiceRemotingOut.Stop id = '{0}'",
            Level = EventLevel.Verbose)]
        public void HttpCoreDiagnosticSourceListenerStop(string id, string appDomainName = "Incorrect")
        {
            this.WriteEvent(30, id, this.ApplicationName);
        }

        [Event(
            6,
            Keywords = Keywords.Diagnostics,
            Message = "Unknown error occurred.",
            Level = EventLevel.Warning)]
        public void UnknownError(string exception, string appDomainName = "Incorrect")
        {
            this.WriteEvent(6, exception, this.ApplicationName);
        }

        [Event(
            7,
            Keywords = Keywords.Diagnostics,
            Message = "ServiceRemotingDiagnosticSubscriber failed to subscribe. Error details '{0}'",
            Level = EventLevel.Error)]
        public void ServiceRemotingDiagnosticSubscriberFailedToSubscribe(string error, string appDomainName = "Incorrect")
        {
            this.WriteEvent(7, error, this.ApplicationName);
        }

        [Event(
            8,
            Keywords = Keywords.Diagnostics,
            Message = "Failed to determine cross component correlation header. Error: {0}",
            Level = EventLevel.Warning)]
        public void GetCrossComponentCorrelationHeaderFailed(string error, string appDomainName = "Incorrect")
        {
            this.WriteEvent(8, error, this.ApplicationName);
        }

        [Event(
            9,
            Keywords = Keywords.Diagnostics,
            Message = "Failed to determine role name header. Error: {0}",
            Level = EventLevel.Warning)]
        public void GetComponentRoleNameHeaderFailed(string error, string appDomainName = "Incorrect")
        {
            this.WriteEvent(9, error, this.ApplicationName);
        }

        [Event(
            10,
            Keywords = Keywords.Diagnostics,
            Message = "Failed to add cross component correlation header. Error: {0}",
            Level = EventLevel.Warning)]
        public void SetCrossComponentCorrelationHeaderFailed(string error, string appDomainName = "Incorrect")
        {
            this.WriteEvent(10, error, this.ApplicationName);
        }

        [Event(
            11,
            Keywords = Keywords.Diagnostics,
            Message = "Invalid Event Argument type. Expected: {0}, Received: {1}",
            Level = EventLevel.Error)]
        public void InvalidEventArgument(string expectedType, string receivedType, string appDomainName = "Incorrect")
        {
            this.WriteEvent(11, expectedType, receivedType, this.ApplicationName);
        }

        [Event(
            12,
            Keywords = Keywords.Diagnostics,
            Message = "No headers detected for the request.",
            Level = EventLevel.Warning)]
        public void HeadersNotFound(string appDomainName = "Incorrect")
        {
            this.WriteEvent(12, this.ApplicationName);
        }

        [Event(
            13,
            Keywords = Keywords.Diagnostics,
            Message = "Failed to handle event {0}. Excepton: {1}",
            Level = EventLevel.Warning)]
        public void FailedToHandleEvent(string eventName, string error, string appDomainName = "Incorrect")
        {
            this.WriteEvent(13, eventName, error, this.ApplicationName);
        }

        [NonEvent]
        private string GetApplicationName()
        {
            string name;
            try
            {
                name = AppDomain.CurrentDomain.FriendlyName;
            }
            catch (Exception exp)
            {
                name = "Undefined " + exp;
            }

            return name;
        }
    }

    /// <summary>
    /// Keywords for the <see cref="ServiceFabricSDKEventSource"/>.
    /// </summary>
    public sealed class Keywords
    {
        /// <summary>
        /// Key word for user actionable events.
        /// </summary>
        public const EventKeywords UserActionable = (EventKeywords)0x1;

        /// <summary>
        /// Key word for diagnostics events.
        /// </summary>
        public const EventKeywords Diagnostics = (EventKeywords)0x2;
    }

}