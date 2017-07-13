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