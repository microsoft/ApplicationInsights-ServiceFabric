namespace Microsoft.ApplicationInsights.ServiceFabric
{
    using System;
    using System.Fabric;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.Extensibility;

    /// <summary>
    /// A telemetry initializer that will set component version based on the code package version of a Service Fabric Reliable Service.
    /// </summary>
    public class CodePackageVersionTelemetryInitializer : ITelemetryInitializer
    {
        /// <summary>
        /// The code package version for this component.
        /// </summary>
        private string codePackageVersion;

        /// <summary>
        /// Initializes component version of the telemetry item with the code package version from the Service Fabric runtime activation context.
        /// </summary>
        /// <param name="telemetry">The telemetry context to initialize.</param>
        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (telemetry.Context?.Component != null && string.IsNullOrEmpty(telemetry.Context.Component.Version))
            {
                if (string.IsNullOrEmpty(codePackageVersion))
                {
                    var activationContext = FabricRuntime.GetActivationContext();
                    codePackageVersion = activationContext.CodePackageVersion;
                }

                telemetry.Context.Component.Version = codePackageVersion;
            }
        }
    }
}