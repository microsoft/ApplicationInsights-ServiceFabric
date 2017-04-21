namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.Globalization;
    using System.Runtime.Remoting.Messaging;
    using Microsoft.ApplicationInsights.Extensibility;

    public partial class FabricTelemetryInitializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FabricTelemetryInitializer"/> class.
        /// </summary>
        /// <param name="context">a service context object.</param>
        public FabricTelemetryInitializer(ServiceContext context)
        {
            this.contextCollection = GetContextContractDictionaryFromServiceContext(context);
        }

        /// <summary>
        /// This static method is a helper method that anyone can invoke to set the call context.
        /// This provides a way for the user to add a single line of code at the entry point and get collected telemetry augmented with service fabric specific fields.
        /// </summary>
        /// <param name="context">A service context object.</param>
        public static void SetServiceCallContext(ServiceContext context)
        {
            // The call initializes TelemetryConfiguration that will create and Intialize modules.
            TelemetryConfiguration configuration = TelemetryConfiguration.Active;

            CallContext.LogicalSetData(ServiceContextKeyName, GetContextContractDictionaryFromServiceContext(context));
        }
        
        /// <summary>
        /// Converts the context object to the loose dictionary based contract this initializer depends on for data.
        /// </summary>
        /// <param name="context">An object of type <see cref="ServiceContext" />.</param>
        /// <returns>A dictionary that encapsulates the given context.</returns>
        private static Dictionary<string, string> GetContextContractDictionaryFromServiceContext(ServiceContext context)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            if (context != null)
            {
                result.Add(KnownContextFieldNames.ServiceName, context.ServiceName.ToString());
                result.Add(KnownContextFieldNames.ServiceTypeName, context.ServiceTypeName);
                result.Add(KnownContextFieldNames.PartitionId, context.PartitionId.ToString());
                result.Add(KnownContextFieldNames.ApplicationName, context.CodePackageActivationContext.ApplicationName);
                result.Add(KnownContextFieldNames.ApplicationTypeName, context.CodePackageActivationContext.ApplicationTypeName);
                result.Add(KnownContextFieldNames.NodeName, context.NodeContext.NodeName);
                if (context is StatelessServiceContext)
                {
                    result.Add(KnownContextFieldNames.InstanceId, context.ReplicaOrInstanceId.ToString(CultureInfo.InvariantCulture));
                }

                if (context is StatefulServiceContext)
                {
                    result.Add(KnownContextFieldNames.ReplicaId, context.ReplicaOrInstanceId.ToString(CultureInfo.InvariantCulture));
                }
            }

            return result;
        }
    }
}
