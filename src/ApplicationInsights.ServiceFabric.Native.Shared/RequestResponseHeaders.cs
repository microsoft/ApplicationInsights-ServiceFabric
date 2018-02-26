using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal class ServiceRemotingConstants
    {
        /// <summary>
        /// Request-Context header.
        /// </summary>
        public const string RequestContextHeader = "Request-Context";

        /// <summary>
        /// Source key in the request context header that is added by an application while making http requests and retrieved by the other application when processing incoming requests.
        /// </summary>
        public const string RequestContextCorrelationSourceKey = "appId";

        /// <summary>
        /// Target key in the request context header that is added to the response and retrieved by the calling application when processing incoming responses.
        /// </summary>
        public const string RequestContextCorrelationTargetKey = "appId"; // Although the name of Source and Target key is the same - appId. Conceptually they are different and hence, we intentionally have two constants here. Makes for better reading of the code.

        /// <summary>
        /// Source-RoleName key in the request context header that is added by an application while making http requests and retrieved by the other application when processing incoming requests.
        /// </summary>
        public const string RequestContextSourceRoleNameKey = "roleName";

        /// <summary>
        /// Parent Id header name
        /// </summary>
        public const string ParentIdHeaderName = "Microsoft.ApplicationInsights.ServiceFabric.ParentId";

        /// <summary>
        /// Correlation context header name.
        /// </summary>
        public const string CorrelationContextHeaderName = "Microsoft.ApplicationInsights.ServiceFabric.CorrelationContext";

        /// <summary>
        /// Dependency type value for service remoting dependency.
        /// </summary>
        public const string ServiceRemotingTypeName = "ServiceFabricServiceRemoting";

        /// <summary>
        /// Response codes do not apply to service fabric, but are mandatory fields in application insights. We will use the following string as the response code.
        /// </summary>
        public const string NotApplicableResponseCode = "Not Applicable";
    }
}
