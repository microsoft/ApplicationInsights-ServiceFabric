namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using System.Threading.Tasks;

    /// <summary>
    /// Class with extension helper methods for working with the Task class
    /// </summary>
    internal static class TaskExtensions
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "task")]
        public static void Forget(this Task task) { }

    }
}
