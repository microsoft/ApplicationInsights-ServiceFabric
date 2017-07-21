using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingRequestHeaderWrapper : IServiceRemotingMessageHeaderCollection
    {
        IServiceRemotingRequestMessageHeader header;

        public ServiceRemotingRequestHeaderWrapper(IServiceRemotingRequestMessageHeader header)
        {
            this.header = header;
        }

        public void AddHeader(string headerName, string headerValue)
        {
            this.header.AddHeader(headerName, headerValue);
        }

        public void RemoveHeader(string headerName)
        {
            this.header.RemoveHeader(headerName);
        }

        public bool TryGetHeaderValue(string headerName, out string headerValue)
        {
            return this.header.TryGetHeaderValue(headerName, out headerValue);
        }
    }
}
