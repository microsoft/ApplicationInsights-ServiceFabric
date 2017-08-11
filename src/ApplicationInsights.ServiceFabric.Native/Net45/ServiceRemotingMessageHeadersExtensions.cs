namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Remoting;
    using System.Text;

    /// <summary>
    /// Class with extension helper methods for working with headers in a service remoting message
    /// </summary>
    internal static class ServiceRemotingMessageHeadersExtensions
    {
        public static bool TryGetHeaderValue(this ServiceRemotingMessageHeaders messageHeaders, string headerName, out string headerValue)
        {
            headerValue = null;
            if (!messageHeaders.TryGetHeaderValue(headerName, out byte[] headerValueBytes))
            {
                return false;
            }

            headerValue = Encoding.UTF8.GetString(headerValueBytes);
            return true;
        }

        public static void AddHeader(this ServiceRemotingMessageHeaders messageHeaders, string headerName, string value)
        {
            messageHeaders.AddHeader(headerName, Encoding.UTF8.GetBytes(value));
        }
    }
}
