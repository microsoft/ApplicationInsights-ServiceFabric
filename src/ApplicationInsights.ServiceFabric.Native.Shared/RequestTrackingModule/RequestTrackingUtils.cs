using Microsoft.ApplicationInsights.DataContracts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.ApplicationInsights.ServiceFabric.Module
{
    /// <summary>
    /// Util methods to share some implemetation across service remoting V1 and V2.
    /// </summary>
    internal static class RequestTrackingUtils
    {
        private static DataContractSerializer _baggageSerializer = new DataContractSerializer(typeof(IEnumerable<KeyValuePair<string, string>>));

        /// <summary>
        /// Updates the given telemetry object with the provided methodName, parentId and property bag
        /// </summary>
        /// <param name="rt">Telemetry object.</param>
        /// <param name="methodName">method name for the request telemetry.</param>
        /// <param name="parentId">Correlation parent Id.</param>
        /// <param name="baggage">Correlation property bag.</param>
        public static void UpdateTelemetryBasedOnCorrelationContext(RequestTelemetry rt, string methodName, string parentId, IEnumerable<KeyValuePair<string, string>> baggage)
        {
            if (!string.IsNullOrEmpty(parentId))
            {
                rt.Context.Operation.ParentId = parentId;
                rt.Context.Operation.Id = GetOperationId(parentId);
            }

            rt.Name = methodName;

            if (baggage != null)
            {
                foreach (KeyValuePair<string, string> pair in baggage)
                {
                    if (!rt.Context.Properties.ContainsKey(pair.Key))
                    {
                        rt.Context.Properties.Add(pair.Key, pair.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Adds baggage to the current activity
        /// </summary>
        /// <param name="baggage"></param>
        public static void UpdateCurrentActivityBaggage(IEnumerable<KeyValuePair<string, string>> baggage)
        {
            if (baggage != null)
            {
                foreach (KeyValuePair<string, string> pair in baggage)
                {
                    Activity.Current.AddBaggage(pair.Key, pair.Value);
                }
            }
        }

        /// <summary>
        /// Gets the property bag from the current activity.
        /// </summary>
        /// <returns>Property bag as a byte array. Null if no property bag is found.</returns>
        public static byte[] GetBaggageFromActivity()
        {
            // We expect the baggage to not be there at all or just contain a few small items
            Activity currentActivity = Activity.Current;
            if (currentActivity != null && currentActivity.Baggage != null && currentActivity.Baggage.Any() == true)
            {
                using (var ms = new MemoryStream())
                {
                    var dictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(ms);
                    _baggageSerializer.WriteObject(dictionaryWriter, currentActivity.Baggage);
                    dictionaryWriter.Flush();
                    return ms.GetBuffer();
                }
            }
            return null;
        }

        /// <summary>
        /// Coverts the correlation baggage from serialized bytes to 
        /// </summary>
        /// <param name="correlationBytes">Baggage properties serialized as bytes</param>
        /// <returns>Deserialized properties.</returns>
        public static IEnumerable<KeyValuePair<string,string>> DeserializeBaggage(byte[] correlationBytes)
        {
            if (correlationBytes != null)
            {
                using (var baggageBytesStream = new MemoryStream(correlationBytes, writable: false))
                {
                    var dictionaryReader = XmlDictionaryReader.CreateBinaryReader(baggageBytesStream, XmlDictionaryReaderQuotas.Max);
                    return _baggageSerializer.ReadObject(dictionaryReader) as IEnumerable<KeyValuePair<string, string>>;
                }
            }

            return Enumerable.Empty<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// Gets the operation Id from the request Id: substring between '|' and first '.'.
        /// </summary>
        /// <param name="id">Id to get the operation id from.</param>
        private static string GetOperationId(string id)
        {
            // id MAY start with '|' and contain '.'. We return substring between them
            // ParentId MAY NOT have hierarchical structure and we don't know if initially rootId was started with '|',
            // so we must NOT include first '|' to allow mixed hierarchical and non-hierarchical request id scenarios
            int rootEnd = id.IndexOf('.');
            if (rootEnd < 0)
            {
                rootEnd = id.Length;
            }

            int rootStart = id[0] == '|' ? 1 : 0;
            return id.Substring(rootStart, rootEnd - rootStart);
        }
    }
}
