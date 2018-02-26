using Microsoft.ServiceFabric.Services.Remoting.V2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    internal static class ServiceRemotingHeaderUtilities
    {

        #region Request Header Extension Methods
        internal static void AddHeader(this IServiceRemotingRequestMessageHeader messageHeaders, string headerName, string value)
        {
            messageHeaders.AddHeader(headerName, Encoding.UTF8.GetBytes(value));
        }

        internal static bool TryGetHeaderValue(this IServiceRemotingRequestMessageHeader messageHeaders, string headerName, out string headerValue)
        {
            headerValue = null;
            if (!messageHeaders.TryGetHeaderValue(headerName, out byte[] headerValueBytes))
            {
                return false;
            }

            headerValue = Encoding.UTF8.GetString(headerValueBytes);
            return true;
        }
        #endregion

        #region Response Header Extension Methods
        internal static void AddHeader(this IServiceRemotingResponseMessageHeader messageHeaders, string headerName, string value)
        {
            messageHeaders.AddHeader(headerName, Encoding.UTF8.GetBytes(value));
        }

        internal static bool TryGetHeaderValue(this IServiceRemotingResponseMessageHeader messageHeaders, string headerName, out string headerValue)
        {
            headerValue = null;
            if (!messageHeaders.TryGetHeaderValue(headerName, out byte[] headerValueBytes))
            {
                return false;
            }

            headerValue = Encoding.UTF8.GetString(headerValueBytes);
            return true;
        }
        #endregion

        #region Request-Context (used for x-component correlation among other things) utility methods 
        internal static string GetRequestContextKeyValue(IServiceRemotingRequestMessageHeader headers, string keyName)
        {
            return GetHeaderKeyValue(new ServiceRemotingRequestHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName);
        }

        internal static bool ContainsRequestContextKeyValue(IServiceRemotingRequestMessageHeader headers, string keyName)
        {
            return ContainsHeaderKeyValue(new ServiceRemotingRequestHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName);
        }

        internal static void SetRequestContextKeyValue(IServiceRemotingRequestMessageHeader headers, string keyName, string keyValue)
        {
            SetHeaderKeyValue(new ServiceRemotingRequestHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName, keyValue);
        }

        internal static string GetRequestContextKeyValue(IServiceRemotingResponseMessageHeader headers, string keyName)
        {
            return GetHeaderKeyValue(new ServiceRemotingResponseHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName);
        }

        internal static bool ContainsRequestContextKeyValue(IServiceRemotingResponseMessageHeader headers, string keyName)
        {
            return ContainsHeaderKeyValue(new ServiceRemotingResponseHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName);
        }

        internal static void SetRequestContextKeyValue(IServiceRemotingResponseMessageHeader headers, string keyName, string keyValue)
        {
            SetHeaderKeyValue(new ServiceRemotingResponseHeaderWrapper(headers), ServiceRemotingConstants.RequestContextHeader, keyName, keyValue);
        }
        #endregion

        #region Helper Methods
        private static IEnumerable<string> GetHeaderValues(IServiceRemotingMessageHeaderCollection headers, string headerName)
        {
            string result;
            if (headers == null || !headers.TryGetHeaderValue(headerName, out result))
            {
                return Enumerable.Empty<string>();
            }

            return result.Split(',');
        }

        private static bool ContainsHeaderKeyValue(IServiceRemotingMessageHeaderCollection headers, string headerName, string keyName)
        {
            return !string.IsNullOrEmpty(GetHeaderKeyValue(headers, headerName, keyName));
        }

        private static string GetHeaderKeyValue(IServiceRemotingMessageHeaderCollection headers, string headerName, string keyName)
        {
            IEnumerable<string> headerValues = GetHeaderValues(headers, headerName);
            return GetHeaderKeyValue(headerValues, keyName);
        }

        private static void SetHeaderKeyValue(IServiceRemotingMessageHeaderCollection headers, string headerName, string keyName, string keyValue)
        {
            if (headers == null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            string[] headerValues = GetHeaderValues(headers, headerName).ToArray();

            headers.AddHeader(headerName, UpdateHeaderWithKeyValue(headerValues, keyName, keyValue));
        }

        /// <summary>
        /// Given the provided list of header value strings, return a list of key name/value pairs
        /// with the provided keyName and keyValue. If the initial header value strings contains
        /// the key name, then the original key value should be replaced with the provided key
        /// value. If the initial header value strings don't contain the key name, then the key
        /// name/value pair should be added to the list and returned.
        /// </summary>
        /// <param name="headerValues">The existing header values that the key/value pair should be added to.</param>
        /// <param name="keyName">The name of the key to add.</param>
        /// <param name="keyValue">The value of the key to add.</param>
        /// <returns>The result of setting the provided key name/value pair into the provided headerValues.</returns>
        private static string UpdateHeaderWithKeyValue(IEnumerable<string> headerValues, string keyName, string keyValue)
        {
            string[] newHeaderKeyValue = new[] { string.Format(CultureInfo.InvariantCulture, "{0}={1}", keyName.Trim(), keyValue.Trim()) };
            IEnumerable<string> headerList = headerValues == null || !headerValues.Any()
                ? newHeaderKeyValue
                : headerValues
                    .Where((string headerValue) =>
                    {
                        int equalsSignIndex = headerValue.IndexOf('=');
                        return equalsSignIndex == -1 || TrimSubstring(headerValue, 0, equalsSignIndex) != keyName;
                    })
                    .Concat(newHeaderKeyValue);

            return string.Join(", ", headerList.ToArray());
        }

        /// <summary>
        /// Get the key value from the provided HttpHeader value that is set up as a comma-separated list of key value pairs. Each key value pair is formatted like (key)=(value).
        /// </summary>
        /// <param name="headerValues">The header values that may contain key name/value pairs.</param>
        /// <param name="keyName">The name of the key value to find in the provided header values.</param>
        /// <returns>The first key value, if it is found. If it is not found, then null.</returns>
        private static string GetHeaderKeyValue(IEnumerable<string> headerValues, string keyName)
        {
            if (headerValues != null)
            {
                foreach (string keyNameValue in headerValues)
                {
                    string[] keyNameValueParts = keyNameValue.Trim().Split('=');
                    if (keyNameValueParts.Length == 2 && keyNameValueParts[0].Trim() == keyName)
                    {
                        return keyNameValueParts[1].Trim();
                    }
                }
            }

            return null;
        }

        private static string TrimSubstring(string value, int startIndex, int endIndex)
        {
            int firstNonWhitespaceIndex = -1;
            int last = -1;
            for (int firstSearchIndex = startIndex; firstSearchIndex < endIndex; ++firstSearchIndex)
            {
                if (!char.IsWhiteSpace(value[firstSearchIndex]))
                {
                    firstNonWhitespaceIndex = firstSearchIndex;

                    // Found the first non-whitespace character index, now look for the last.
                    for (int lastSearchIndex = endIndex - 1; lastSearchIndex >= startIndex; --lastSearchIndex)
                    {
                        if (!char.IsWhiteSpace(value[lastSearchIndex]))
                        {
                            last = lastSearchIndex;
                            break;
                        }
                    }

                    break;
                }
            }

            return firstNonWhitespaceIndex == -1 ? null : value.Substring(firstNonWhitespaceIndex, last - firstNonWhitespaceIndex + 1);
        }
        #endregion

        #region private helper interface / classes
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
            /// <param name="headerValue"></param>
            /// <returns></returns>
            bool TryGetHeaderValue(string headerName, out string headerValue);
        }

        private class ServiceRemotingRequestHeaderWrapper : IServiceRemotingMessageHeaderCollection
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

            public bool TryGetHeaderValue(string headerName, out string headerValue)
            {
                return this.header.TryGetHeaderValue(headerName, out headerValue);
            }
        }

        private class ServiceRemotingResponseHeaderWrapper : IServiceRemotingMessageHeaderCollection
        {
            IServiceRemotingResponseMessageHeader header;

            public ServiceRemotingResponseHeaderWrapper(IServiceRemotingResponseMessageHeader header)
            {
                this.header = header;
            }

            public void AddHeader(string headerName, string headerValue)
            {
                this.header.AddHeader(headerName, headerValue);
            }

            public bool TryGetHeaderValue(string headerName, out string headerValue)
            {
                return this.header.TryGetHeaderValue(headerName, out headerValue);
            }
        }
        #endregion
    }
}
