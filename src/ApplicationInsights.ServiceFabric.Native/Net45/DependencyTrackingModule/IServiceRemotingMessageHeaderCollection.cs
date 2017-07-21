using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    interface IServiceRemotingMessageHeaderCollection
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
        /// <param name="headerName"></param>
        void RemoveHeader(string headerName);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="headerName"></param>
        /// <param name="headerValue"></param>
        /// <returns></returns>
        bool TryGetHeaderValue(string headerName, out string headerValue);
    }
}
