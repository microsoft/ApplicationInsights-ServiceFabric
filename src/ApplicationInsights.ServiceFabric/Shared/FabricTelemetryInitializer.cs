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
                // Populate telemetry context properties from the service context object
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
                }

                // Populate telemetry context properties from environment variables, but not overwriting properties
                // that have been populated from the service context. The environment variables are basically a fallback mechanism.
                AddPropertyFromEnvironmentVariable(KnownContextFieldNames.ServiceName, KnownEnvironmentVariableName.ServiceName, telemetry);
                AddPropertyFromEnvironmentVariable(KnownContextFieldNames.NodeName, KnownEnvironmentVariableName.NodeName, telemetry);
                AddPropertyFromEnvironmentVariable(KnownContextFieldNames.PartitionId, KnownEnvironmentVariableName.PartitionId, telemetry);
                AddPropertyFromEnvironmentVariable(KnownContextFieldNames.ApplicationName, KnownEnvironmentVariableName.ApplicationName, telemetry);

                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
                {
                    if (telemetry.Context.Properties.ContainsKey(KnownContextFieldNames.ServiceName))
                    {
                        telemetry.Context.Cloud.RoleName = telemetry.Context.Properties[KnownContextFieldNames.ServiceName];
                    }

                    // If we still don't have the role name, fall back to using the service package name as the role name.
                    // Not quite the same as role name, but better than nothing for differentiating different telemetry datapoint
                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleName))
                    {
                        telemetry.Context.Cloud.RoleName = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageName);
                    }
                }

                if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                {
                    if (telemetry.Context.Properties.ContainsKey(KnownContextFieldNames.InstanceId))
                    {
                        telemetry.Context.Cloud.RoleInstance = telemetry.Context.Properties[KnownContextFieldNames.InstanceId];
                    }
                    else if (telemetry.Context.Properties.ContainsKey(KnownContextFieldNames.ReplicaId))
                    {
                        telemetry.Context.Cloud.RoleInstance = telemetry.Context.Properties[KnownContextFieldNames.ReplicaId];
                    }

                    // If we still don't have the role instance name, fall back to using the environment package activation id or service package instance id.
                    // Not quite the same as role instance id, but better than nothing for differentiating different telemetry datapoint
                    if (string.IsNullOrEmpty(telemetry.Context.Cloud.RoleInstance))
                    {
                        telemetry.Context.Cloud.RoleInstance = Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageActivatonId) ?? Environment.GetEnvironmentVariable(KnownEnvironmentVariableName.ServicePackageInstanceId);
                    }
                }
            }
            catch
            {
                // Something went wrong trying to set these extra properties. We shouldn't fail though.
            }
        }

        /// <summary>
        /// Adds the property to the telemetry context, if it doesn't already exist, using the environment variable value. It's a no-op
        /// if the property with the <paramref name="contextFieldName"/> already exist in the telemetry context.
        /// </summary>
        /// <param name="contextFieldName">The name of context field property, as used by Service Fabric. This will be same name used in the telemetry context property dictionary</param>
        /// <param name="environmentVariableName">The name of the environment variable having the equivalent value</param>
        /// <param name="telemetry">The telemetry object whose property dictionary will be updated</param>
        private void AddPropertyFromEnvironmentVariable(string contextFieldName, string environmentVariableName, ITelemetry telemetry)
        {
            if (!telemetry.Context.Properties.ContainsKey(contextFieldName))
            {
                string value = Environment.GetEnvironmentVariable(environmentVariableName);
                if (!string.IsNullOrEmpty(value))
                {
                    telemetry.Context.Properties.Add(contextFieldName, value);
                }
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

        // Not all of these variables are currently read, but what we know currently are listed so it's easier to find them if we need them in the future
        private class KnownEnvironmentVariableName
        {
            public const string ServiceName = "Fabric_ServiceName";
            public const string ServicePackageName = "Fabric_ServicePackageName";
            public const string ServicePackageInstanceId = "Fabric_ServicePackageInstanceId";
            public const string ServicePackageActivatonId = "Fabric_ServicePackageActivationId";
            public const string NodeName = "Fabric_NodeName";
            public const string PartitionId = "Fabric_Id";
            public const string ApplicationName = "Fabric_ApplicationName";
            public const string CodePackageName = "Fabric_CodePackageName";
            public const string ConfigurationIdentifier = "Fabric_Epoch";
        }
    }
}

