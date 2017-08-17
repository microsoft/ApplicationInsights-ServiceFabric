namespace Microsoft.ApplicationInsights.ServiceFabric.Remoting.Activities
{
    internal interface IMethodNameProvider
    {
        string GetMethodName(int interfaceId, int methodId);
    }
}
