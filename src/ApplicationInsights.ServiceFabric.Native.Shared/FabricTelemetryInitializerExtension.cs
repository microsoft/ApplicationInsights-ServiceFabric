namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System.Collections.Generic;
    using System.Fabric;
    using System.Globalization;
    using Microsoft.ApplicationInsights.Extensibility;

#if NET45
    using System.Runtime.Remoting.Messaging;
#endif

    /// <summary>
    /// Provides extended functionality related to the ServiceFabricTelemetryInitializer specifically targetted at Service Fabric Native applications.
    /// </summary>
    public static class FabricTelemetryInitializerExtension
    {
        // If you update this - also update the same constant in src\ApplicationInsights.ServiceFabric\Shared\FabricTelemetryInitializer.cs
        private const string ServiceContextKeyName = "AI.SF.ServiceContext";

        /// <summary>
        /// Creates an instance of the FabricTelemetryInitializer based on the Service Context passed in.
        /// </summary>
        /// <param name="context">a service context object.</param>
        /// <returns></returns>
        public static FabricTelemetryInitializer CreateFabricTelemetryInitializer(ServiceContext context)
        {
            return new FabricTelemetryInitializer(GetContextContractDictionaryFromServiceContext(context));
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

        // If you update this - also update the same constant in src\ApplicationInsights.ServiceFabric\Shared\FabricTelemetryInitializer.cs
        private class KnownContextFieldNames
        {
            public const string ServiceName = "ServiceFabric.ServiceName";
            public const string ServiceTypeName = "ServiceFabric.ServiceTypeName";
            public const string PartitionId = "ServiceFabric.PartitionId";
            public const string ApplicationName = "ServiceFabric.ApplicationName";
            public const string ApplicationTypeName = "ServiceFabric.ApplicationTypeName";
            public const string NodeName = "ServiceFabric.NodeName";
            public const string InstanceId = "ServiceFabric.InstanceId";
            public const string ReplicaId = "ServiceFabric.ReplicaId";
        }
    }
}
