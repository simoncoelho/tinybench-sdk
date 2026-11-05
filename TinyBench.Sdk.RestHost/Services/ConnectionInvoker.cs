using System.Reflection;
using System.Text.Json;
using TinyBench.Sdk.RestHost.Api;
using TinyBench.Sdk.RestHost.Driver;

namespace TinyBench.Sdk.RestHost.Services;

public sealed class ConnectionInvoker(LoadedDriver driver)
{
    public void Connect(IReadOnlyDictionary<string, JsonElement> parameters)
    {
        if (driver.ConnectMethod is null)
        {
            throw new TinyBenchRestException(
                "connect_not_defined",
                "The driver does not define a connect method.",
                StatusCodes.Status404NotFound);
        }

        Invoke(driver.ConnectMethod, parameters, "connect_failed");
    }

    public void Disconnect()
    {
        if (driver.DisconnectMethod is null)
        {
            throw new TinyBenchRestException(
                "disconnect_not_defined",
                "The driver does not define a disconnect method.",
                StatusCodes.Status404NotFound);
        }

        Invoke(driver.DisconnectMethod, new Dictionary<string, JsonElement>(), "disconnect_failed");
    }

    private void Invoke(MethodInfo method, IReadOnlyDictionary<string, JsonElement> parameters, string failureCode)
    {
        try
        {
            var arguments = method.GetParameters().Select(parameter =>
            {
                if (parameter.Name is not null && parameters.TryGetValue(parameter.Name, out var value))
                {
                    return TinyJsonInputs.Convert(parameter.ParameterType, value);
                }

                if (parameter.HasDefaultValue)
                {
                    return parameter.DefaultValue;
                }

                throw new InvalidOperationException($"Missing required connection parameter '{parameter.Name}'.");
            }).ToArray();

            method.Invoke(driver.Instance, arguments);
        }
        catch (Exception ex)
        {
            throw new TinyBenchRestException(failureCode, Unwrap(ex).Message, StatusCodes.Status400BadRequest);
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        if (ex is TargetInvocationException invocation && invocation.InnerException is not null)
        {
            return invocation.InnerException;
        }

        return ex;
    }
}
