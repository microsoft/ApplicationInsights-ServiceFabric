namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System;
    using System.Collections.Generic;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.Extensibility;

#if NET45
    using System.Runtime.Remoting.Messaging;
#endif

    /// <summary>
    /// Telemetry initializer for service fabric. Adds service fabric specific context to outgoing telemetry.
    /// </summary>
    public partial class FabricTelemetryInitializer : ITelemetryInitializer
    {
        // If you update this - also update the same constant in src\ApplicationInsights.ServiceFabric.Native\Net45\FabricTelemetryInitializerExtension.cs
        private const string ServiceContextKeyName = "AI.SF.ServiceContext";

        private Dictionary<string, string> contextCollection;

        /// <summary>
        /// There are a few ways the context could be provided. This property makes it easy for the rest of the implemenatation to ignore all those cases. 
        /// </summary>
        private Dictionary<string, string> ApplicableServiceContext
        {
            get
            {
                if (this.contextCollection != null && this.contextCollection.Count > 0)
                {
                    return this.contextCollection;
                }

                return CallContext.LogicalGetData(ServiceContextKeyName) as Dictionary<string, string>;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricTelemetryInitializer"/> class.
        /// </summary>
        public FabricTelemetryInitializer()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricTelemetryInitializer"/> class.
        /// </summary>
        public FabricTelemetryInitializer(Dictionary<string, string> context)
        {
            // Clone the passed in context.
            this.contextCollection = new Dictionary<string, string>(context);
        }

        /// <summary>
        /// Adds service fabric context fields on the given telemetry object.
        /// </summary>
        /// <param name="telemetry">The telemetry item being sent through the AI sdk.</param>
        public void Initialize(ITelemetry telemetry)
        {
            try
            {
                var serviceContext = this.ApplicableServiceContext;
                if (serviceContext != null)
                {
                    foreach (var field in serviceContext)
                    {
                        if (!telemetry.Context.Properties.ContainsKey(field.Key))
                        {
                            telemetry.Context.Properties.Add(field.Key, field.Value);
                        }
                    }

                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName) && serviceContext.ContainsKey(KnownContextFieldNames.ServiceName))
                    {
                        telemetry.Context.Cloud.RoleName = serviceContext[KnownContextFieldNames.ServiceName];
                    }
                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                    {
                        if (serviceContext.ContainsKey(KnownContextFieldNames.InstanceId))
                        {
                            telemetry.Context.Cloud.RoleInstance = serviceContext[KnownContextFieldNames.InstanceId];
                        }
                        else if (serviceContext.ContainsKey(KnownContextFieldNames.ReplicaId))
                        {
                            telemetry.Context.Cloud.RoleInstance = serviceContext[KnownContextFieldNames.ReplicaId];
                        }
                    }
                }

                // Fallback to environment variables for setting role / instance names. We will rely on these environment variables exclusively for container lift and shift scenarios for now.
                // And for reliable services, when service context is neither provided directly nor through call context
                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
                {
                    telemetry.Context.Cloud.RoleName = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServiceName);
                }
                
                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
                {
                    telemetry.Context.Cloud.RoleName = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageName);
                }

                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                {
                    telemetry.Context.Cloud.RoleInstance = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageActivatonId) ?? Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageInstanceId);
                }

                if (!telemetry.Context.Properties.ContainsKey(KnownContextFieldNames.NodeName))
                {
                    string nodeName = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.NodeName);

                    if (!string.IsNullOrEmpty(nodeName))
                    {
                        telemetry.Context.Properties.Add(KnownContextFieldNames.NodeName, nodeName);
                    }
                }
            }
            catch
            {
                // Something went wrong trying to set these extra properties. We shouldn't fail though.
            }
        }

        // If you update this - also update the same constant in src\ApplicationInsights.ServiceFabric.Native\Net45\FabricTelemetryInitializerExtension.cs
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

        private class KnownEnvironmentVariableName
        {
            public const string ServiceName = "Fabric_ServiceName";
            public const string ServicePackageName = "Fabric_ServicePackageName";
            public const string ServicePackageInstanceId = "Fabric_ServicePackageInstanceId";
            public const string ServicePackageActivatonId = "Fabric_ServicePackageActivationId";
            public const string NodeName = "Fabric_NodeName";
        }
    }
}

