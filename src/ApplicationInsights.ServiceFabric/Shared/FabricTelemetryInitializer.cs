namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System;
    using System.Collections.Generic;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.Extensibility;

#if !NETCORE
    using System.Runtime.Remoting.Messaging;
#endif

    /// <summary>
    /// Telemetry initializer for service fabric. Adds service fabric specific context to outgoing telemetry.
    /// </summary>
    public partial class FabricTelemetryInitializer : ITelemetryInitializer
    {
        private const string PackageActivationIdEnvVariableName = "Fabric_ServicePackageActivationId";

        // If you update this - also update the same constant in src\ApplicationInsights.ServiceFabric.Native\Net45\FabricTelemetryInitializerExtension.cs
        private const string ServiceContextKeyName = "ServiceContext";

        private Dictionary<string, string> contextCollection;

        /// <summary>
        /// There are a few ways the context could be provided. This property makes it easy for the rest of the implemenatation to ignore all those cases. 
        /// </summary>
        private Dictionary<string, string> ApplicableServiceContext
        {
            get
            {
#if NETCORE
                return null;
#else

                if (this.contextCollection != null)
                {
                    return this.contextCollection;
                }

                return CallContext.LogicalGetData(ServiceContextKeyName) as Dictionary<string, string>;
#endif
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FabricTelemetryInitializer"/> class.
        /// </summary>
        public FabricTelemetryInitializer()
        {
            string packageActivationId = Environment.GetEnvironmentVariable(PackageActivationIdEnvVariableName);
            if (!string.IsNullOrEmpty(packageActivationId))
            {
                // Exclusive hosting model. Let's try to build telemetry from environment variables if present.

                string serviceName = Environment.GetEnvironmentVariable(KnownContextFieldNames.ServiceName);
                string serviceTypeName = Environment.GetEnvironmentVariable(KnownContextFieldNames.ServiceTypeName);
                string partitionId = Environment.GetEnvironmentVariable(KnownContextFieldNames.PartitionId);
                string applicationName = Environment.GetEnvironmentVariable(KnownContextFieldNames.ApplicationName);
                string applicationTypeName = Environment.GetEnvironmentVariable(KnownContextFieldNames.ApplicationTypeName);
                string instanceId = Environment.GetEnvironmentVariable(KnownContextFieldNames.InstanceId);
                string replicaId = Environment.GetEnvironmentVariable(KnownContextFieldNames.ReplicaId);
                string nodeName = Environment.GetEnvironmentVariable(KnownContextFieldNames.NodeName);
                
                if (!string.IsNullOrEmpty(serviceName)
                    && !string.IsNullOrEmpty(serviceTypeName)
                    && !string.IsNullOrEmpty(applicationName)
                    && !string.IsNullOrEmpty(partitionId)
                    && !string.IsNullOrEmpty(applicationTypeName)
                    && !string.IsNullOrEmpty(nodeName)
                    && (!string.IsNullOrEmpty(replicaId) || !string.IsNullOrEmpty(instanceId)))
                {
                    this.contextCollection = new Dictionary<string, string>()
                    {
                        { KnownContextFieldNames.ServiceName, serviceName },
                        { KnownContextFieldNames.ServiceTypeName, serviceTypeName },
                        { KnownContextFieldNames.PartitionId, partitionId },
                        { KnownContextFieldNames.ApplicationName, applicationName },
                        { KnownContextFieldNames.ApplicationTypeName, applicationTypeName },
                        { KnownContextFieldNames.NodeName, nodeName }
                    };

                    if (!string.IsNullOrEmpty(replicaId))
                    {
                        this.contextCollection.Add(KnownContextFieldNames.ReplicaId, replicaId);
                    }

                    if (!string.IsNullOrEmpty(instanceId))
                    {
                        this.contextCollection.Add(KnownContextFieldNames.InstanceId, instanceId);
                    }
                }
            }
        }

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
                if (this.ApplicableServiceContext != null)
                {
                    foreach (var field in this.ApplicableServiceContext)
                    {
                        if (!telemetry.Context.Properties.ContainsKey(field.Key))
                        {
                            telemetry.Context.Properties.Add(field.Key, field.Value);
                        }
                    }

                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName) && this.ApplicableServiceContext.ContainsKey(KnownContextFieldNames.ServiceName))
                    {
                        telemetry.Context.Cloud.RoleName = this.ApplicableServiceContext[KnownContextFieldNames.ServiceName];
                    }
                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                    {
                        if (this.ApplicableServiceContext.ContainsKey(KnownContextFieldNames.InstanceId))
                        {
                            telemetry.Context.Cloud.RoleInstance = this.ApplicableServiceContext[KnownContextFieldNames.InstanceId];
                        }
                        else if (this.ApplicableServiceContext.ContainsKey(KnownContextFieldNames.ReplicaId))
                        {
                            telemetry.Context.Cloud.RoleInstance = this.ApplicableServiceContext[KnownContextFieldNames.ReplicaId];
                        }
                    }
                }

                // Fallback to environment variables for setting role / instance names. We will rely on these environment variables exclusively for container lift and shift scenarios for now.
                // And for reliable services, when service context is neither provided directly nor through call context
                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
                {
                    telemetry.Context.Cloud.RoleName = Environment.GetEnvironmentVariable(KnownContextFieldNames.ServicePackageName);
                }

                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                {
                    telemetry.Context.Cloud.RoleInstance = Environment.GetEnvironmentVariable(KnownContextFieldNames.ServicePackageActivatonId) ?? Environment.GetEnvironmentVariable(KnownContextFieldNames.ServicePackageInstanceId);
                }

                if (!telemetry.Context.Properties.ContainsKey(KnownContextFieldNames.NodeName))
                {
                    string nodeName = Environment.GetEnvironmentVariable(KnownContextFieldNames.NodeName);

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
            public const string ServiceName = "Fabric_ServiceName";
            public const string ServiceTypeName = "Fabrid_ServiceTypeName";
            public const string PartitionId = "Fabric_PartitionId";
            public const string ApplicationName = "Fabric_ApplicationName";
            public const string ApplicationTypeName = "Fabric_ApplicationTypeName";
            public const string InstanceId = "Fabric_InstanceId";
            public const string ReplicaId = "Fabric_ReplicaId";
            public const string NodeName = "Fabric_NodeName";
            public const string ServicePackageName = "Fabric_ServicePackageName";
            public const string ServicePackageInstanceId = "Fabric_ServicePackageInstanceId";
            public const string ServicePackageActivatonId = "Fabric_ServicePackageActivationId";
        }
    }
}

