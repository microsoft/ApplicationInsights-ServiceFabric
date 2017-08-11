namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    internal static class ServiceRemotingLoggingStrings
    {
        public const string InboundRequestActivityName = "Microsoft.ServiceFabric.Remoting.RemotingRequestIn";
        public const string RequestIdHeaderName = "Request-Id";
        public const string CorrelationContextHeaderName = "Correlation-Context";
        public const string OutboundRequestActivityName = "Microsoft.ServiceFabric.Remoting.RemotingRequestOut";
    }
}
