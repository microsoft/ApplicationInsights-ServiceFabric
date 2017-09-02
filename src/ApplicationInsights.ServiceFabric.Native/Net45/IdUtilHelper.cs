namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Services.Runtime;
    using System;
    using System.Reflection;

    /// <summary>
    /// A static helper class that bridges the calls via reflection to service fabric runtime's internal class
    /// of IdUtil. That contains the essential logic for computing the method and interface ids.
    /// </summary>
    internal static class IdUtilHelper
    {
        private static Func<MethodInfo, int> computeIdFromMethodDelegate;
        private static Func<Type, int> computeIdFromTypeDelegate;
        private static Func<string, string, int> computeIdFromNamesDelegate;

        static IdUtilHelper()
        {
            try
            {
                Type idUtilType = typeof(ServiceRuntime).Assembly.GetType("Microsoft.ServiceFabric.Services.Common.IdUtil");

                // The ComputeId methods are all internal. However, we specify both NonPublic and Public, just in cases
                // service fabric changed them to become public in the future, then hopefully this code would still work.
                MethodInfo computeIdFromMethod = idUtilType?.GetMethod(
                    "ComputeId",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new Type[] { typeof(MethodInfo) },
                    null);
                MethodInfo computeIdFromType = idUtilType?.GetMethod(
                    "ComputeId",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new Type[] { typeof(Type) },
                    null);
                MethodInfo computeIdFromNames = idUtilType?.GetMethod(
                    "ComputeId",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new Type[] { typeof(string), typeof(string) },
                    null);

                if (computeIdFromMethod != null)
                {
                    computeIdFromMethodDelegate = (Func<MethodInfo, int>)Delegate.CreateDelegate(typeof(Func<MethodInfo, int>), computeIdFromMethod);
                }

                if (computeIdFromType != null)
                {
                    computeIdFromTypeDelegate = (Func<Type, int>)Delegate.CreateDelegate(typeof(Func<Type, int>), computeIdFromType);
                }

                if (computeIdFromNames != null)
                {
                    computeIdFromNamesDelegate = (Func<string, string, int>)Delegate.CreateDelegate(typeof(Func<string, string, int>), computeIdFromNames);
                }
            }
            catch (Exception)
            {
                // Can't let the static constructor brings down the process if anything happens.
                // At worst, we just don't get any method namese.
            }
        }

        internal static int ComputeId(MethodInfo methodInfo)
        {
            if (computeIdFromMethodDelegate != null)
            {
                return computeIdFromMethodDelegate(methodInfo);
            }

            return 0;
        }

        internal static int ComputeId(Type type)
        {
            if (computeIdFromTypeDelegate != null)
            {
                return computeIdFromTypeDelegate(type);
            }

            return 0;
        }

        internal static int ComputeId(string typeName, string typeNamespace)
        {
            if (computeIdFromNamesDelegate != null)
            {
                return computeIdFromNamesDelegate(typeName, typeNamespace);
            }

            return 0;
        }
    }
}