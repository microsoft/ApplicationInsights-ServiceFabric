namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Remoting.V1.Client;

    /// <summary>
    /// Interface implemented by remoting clients to provide access to the inner client.
    /// </summary>
    internal interface IWrappingClient
    {
        IServiceRemotingClient InnerClient { get; }
    }
}
