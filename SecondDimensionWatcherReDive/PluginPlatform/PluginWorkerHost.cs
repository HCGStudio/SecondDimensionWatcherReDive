using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace SecondDimensionWatcherReDive.PluginPlatform;

public interface IPluginWorkerBridge
{
    string Request(string capability, string payloadJson);
}

internal static class PluginWorkerHost
{
    public const string WorkerArgument = "--plugin-worker";

    public static bool IsWorkerInvocation(string[] args)
        => args.Length == 1 && string.Equals(args[0], WorkerArgument, StringComparison.Ordinal);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var line = await Console.In.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException("Missing worker invocation.");
            var invocation = JsonSerializer.Deserialize(
                                 line,
                                 PluginWorkerJsonContext.Default.PluginWorkerInvocation)
                             ?? throw new InvalidDataException("Invalid worker invocation.");
            Execute(invocation);
            return 0;
        }
        catch (Exception exception)
        {
            WriteMessage(new PluginWorkerMessage
            {
                Type = "error",
                Error = SanitizeError(exception.Message)
            });
            return 1;
        }
    }

    private static void Execute(PluginWorkerInvocation invocation)
    {
        var heapMiB = Math.Clamp(invocation.MaximumHeapMegabytes, 16, 512);
        var constraints = new V8RuntimeConstraints
        {
            MaxOldSpaceSize = heapMiB,
            MaxArrayBufferAllocation = checked((nuint)heapMiB * 1024 * 1024 / 2)
        };
        using var runtime = new V8Runtime(constraints)
        {
            MaxHeapSize = checked((nuint)Math.Max(8, heapMiB - 8) * 1024 * 1024),
            MaxStackUsage = 2 * 1024 * 1024,
            EnableInterruptPropagation = true,
            HeapSizeViolationPolicy = V8RuntimeViolationPolicy.Interrupt
        };
        using var engine = runtime.CreateScriptEngine(
            V8ScriptEngineFlags.DisableGlobalMembers | V8ScriptEngineFlags.HideHostExceptions);
        var bridgeName = $"__sdwBridge_{Guid.NewGuid():N}";
        engine.AddRestrictedHostObject<IPluginWorkerBridge>(bridgeName, new PluginWorkerBridge());
        engine.Execute("sdw-sdk.js", $$"""
            'use strict';
            globalThis.sdw = ((bridge) => {
              return Object.freeze({
                request(capability, payload) {
                  const response = JSON.parse(bridge.Request(String(capability), JSON.stringify(payload ?? {})));
                  if (!response.Ok) throw new Error(response.Error || 'Capability request was denied.');
                  return response.Result;
                }
              });
            })(globalThis[{{JsonSerializer.Serialize(bridgeName)}}]);
            delete globalThis[{{JsonSerializer.Serialize(bridgeName)}}];
            """);
        engine.Execute("plugin.js", invocation.Script);

        var handlerJson = JsonSerializer.Serialize(invocation.Handler);
        var inputJson = JsonSerializer.Serialize(invocation.Input.GetRawText());
        var configurationJson = JsonSerializer.Serialize(invocation.Configuration.GetRawText());
        var maximumResponseBytes = Math.Clamp(invocation.MaximumResponseBytes, 1024, 8 * 1024 * 1024);
        var expression = $$"""
            (() => {
              if (!globalThis.sdwPlugin || typeof globalThis.sdwPlugin.handlers !== 'object')
                throw new Error('Plugin must define globalThis.sdwPlugin.handlers.');
              const handler = globalThis.sdwPlugin.handlers[{{handlerJson}}];
              if (typeof handler !== 'function')
                throw new Error('Plugin handler is not defined: ' + {{handlerJson}});
              const value = handler(JSON.parse({{inputJson}}), Object.freeze(JSON.parse({{configurationJson}})));
              if (value && typeof value.then === 'function')
                throw new Error('Async JavaScript handlers are not supported; use synchronous sdw.request calls.');
              return JSON.stringify(value === undefined ? null : value);
            })()
            """;
        var serialized = Convert.ToString(engine.Evaluate(expression), System.Globalization.CultureInfo.InvariantCulture)
                         ?? "null";
        if (System.Text.Encoding.UTF8.GetByteCount(serialized) > maximumResponseBytes)
            throw new InvalidDataException("Plugin result exceeds the configured response limit.");
        using var document = JsonDocument.Parse(serialized);
        WriteMessage(new PluginWorkerMessage
        {
            Type = "result",
            Result = document.RootElement.Clone()
        });
    }

    private static void WriteMessage(PluginWorkerMessage message)
    {
        Console.Out.WriteLine(JsonSerializer.Serialize(message, PluginWorkerJsonContext.Default.PluginWorkerMessage));
        Console.Out.Flush();
    }

    private static string SanitizeError(string message)
        => message.Length <= 1_024 ? message : message[..1_024];

    private sealed class PluginWorkerBridge : IPluginWorkerBridge
    {
        public string Request(string capability, string payloadJson)
        {
            if (capability.Length > 64 || payloadJson.Length > 2 * 1024 * 1024)
                throw new InvalidDataException("Capability request is too large.");
            using var payloadDocument = JsonDocument.Parse(payloadJson);
            var id = Guid.NewGuid().ToString("N");
            WriteMessage(new PluginWorkerMessage
            {
                Type = "capability",
                Id = id,
                Capability = capability,
                Payload = payloadDocument.RootElement.Clone()
            });

            var responseLine = Console.In.ReadLine();
            if (string.IsNullOrWhiteSpace(responseLine)) throw new IOException("Capability broker disconnected.");
            var response = JsonSerializer.Deserialize(
                               responseLine,
                               PluginWorkerJsonContext.Default.PluginWorkerMessage)
                           ?? throw new InvalidDataException("Invalid capability response.");
            if (!string.Equals(response.Id, id, StringComparison.Ordinal))
                throw new InvalidDataException("Capability response id does not match request.");
            if (response.Type == "capability-error")
                return JsonSerializer.Serialize(new PluginWorkerBridgeResponse
                {
                    Ok = false,
                    Error = response.Error ?? "Capability request was denied."
                }, PluginWorkerJsonContext.Default.PluginWorkerBridgeResponse);
            if (response.Type != "capability-result" || response.Result is null)
                throw new InvalidDataException("Invalid capability response type.");
            return JsonSerializer.Serialize(new PluginWorkerBridgeResponse
            {
                Ok = true,
                Result = response.Result.Value.Clone()
            }, PluginWorkerJsonContext.Default.PluginWorkerBridgeResponse);
        }
    }
}
