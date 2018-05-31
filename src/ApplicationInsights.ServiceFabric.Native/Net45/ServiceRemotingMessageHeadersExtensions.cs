namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    using Microsoft.ServiceFabric.Actors.Remoting.V1.Builder;
    using Microsoft.ServiceFabric.Services.Remoting.V1;
    using System;
    using System.Reflection;
    using System.Text;

    /// <summary>
    /// Class with extension helper methods for working with headers in a service remoting message
    /// </summary>
    internal static class ServiceRemotingMessageHeadersExtensions
    {
        private static class ActorMessageHeadersHelper
        {
            private static Func<byte[], object> deserializeMethodDelegate;
            private static FieldInfo interfaceIdField;
            private static FieldInfo methodIdField;

            static ActorMessageHeadersHelper()
            {
                try
                {
                    Type actorMessageHeadersType = typeof(ActorMethodDispatcherBase).Assembly.GetType("Microsoft.ServiceFabric.Actors.Remoting.V1.ActorMessageHeaders");
                    MethodInfo method = actorMessageHeadersType?.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                    if (method != null)
                    {
                        deserializeMethodDelegate = (Func<byte[], object>)Delegate.CreateDelegate(typeof(Func<byte[], object>), method);
                    }

                    interfaceIdField = actorMessageHeadersType?.GetField("InterfaceId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    methodIdField = actorMessageHeadersType?.GetField("MethodId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch (Exception)
                {
                    // Can't let the static constructor brings down the process if anything happens.
                    // At worst, we just don't get any method namese.
                }
            }
            
            public static bool TryGetIds(byte[] headerBytes, out int methodId, out int interfaceId)
            {
                methodId = 0;
                interfaceId = 0;

                if (deserializeMethodDelegate != null)
                {
                    object actorMessageHeaders = deserializeMethodDelegate(headerBytes);
                    if (actorMessageHeaders != null)
                    {
                        interfaceId = (int)interfaceIdField?.GetValue(actorMessageHeaders);
                        methodId = (int)methodIdField?.GetValue(actorMessageHeaders);
                    }
                }

                return methodId != 0 && interfaceId != 0;
            }
        }

        public static bool ContainsHeader(this ServiceRemotingMessageHeaders messageHeaders, string headerName)
        {
            return messageHeaders.TryGetHeaderValue(headerName, out byte[] headerValueBytes);
        }

        public static bool TryGetHeaderValue(this ServiceRemotingMessageHeaders messageHeaders, string headerName, out string headerValue)
        {
            headerValue = null;
            if (!messageHeaders.TryGetHeaderValue(headerName, out byte[] headerValueBytes))
            {
                return false;
            }

            headerValue = Encoding.UTF8.GetString(headerValueBytes);
            return true;
        }

        public static void AddHeader(this ServiceRemotingMessageHeaders messageHeaders, string headerName, string value)
        {
            messageHeaders.AddHeader(headerName, Encoding.UTF8.GetBytes(value));
        }
               

        public static bool TryGetActorMethodAndInterfaceIds(this ServiceRemotingMessageHeaders messageHeaders, out int methodId, out int interfaceId)
        {
            methodId = 0;
            interfaceId = 0;

            // If a call is directed on the actor, it should have the ActorMessageHeader header containing details about the actor call.
            byte[] actorHeaderBytes;
            if (!messageHeaders.TryGetHeaderValue("ActorMessageHeader", out actorHeaderBytes))
            {
                return false;
            }

            return ActorMessageHeadersHelper.TryGetIds(actorHeaderBytes, out methodId, out interfaceId);
        }
    }
}
