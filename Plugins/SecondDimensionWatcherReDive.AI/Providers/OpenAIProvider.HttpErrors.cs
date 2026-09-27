using System.Text;
using System.Text.Json;

namespace SecondDimensionWatcherReDive.AI.Providers;

public sealed partial class OpenAIProvider
{
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var message = new StringBuilder(
            $"OpenAI {operation} request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
        if (response.Headers.TryGetValues("x-request-id", out var requestIds))
            message.Append($" Request ID: {SanitizeErrorDetail(requestIds.FirstOrDefault() ?? "", apiKey, 256)}.");

        Exception? readException = null;
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCancellation.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            // Error responses can come from a proxy. Bound the read before parsing, and never
            // include an arbitrary HTML page or the entire JSON body in logs or the chat SSE error.
            const int maxBodyBytes = 16 * 1024;
            var buffer = new byte[maxBodyBytes + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(readCancellation.Token);
            var bytesRead = await stream.ReadAtLeastAsync(buffer, buffer.Length,
                throwOnEndOfStream: false, cancellationToken: readCancellation.Token);
            if (bytesRead > maxBodyBytes)
            {
                message.Append(" Upstream error body exceeded 16 KiB; details omitted.");
            }
            else if (bytesRead == 0)
            {
                message.Append(" Upstream returned an empty error body.");
            }
            else
            {
                using var document = JsonDocument.Parse(buffer.AsMemory(0, bytesRead));
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                        message.Append($" {SanitizeErrorDetail(error.GetString() ?? "", apiKey, 2048)}");
                    else if (error.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var field in new[] { "message", "type", "code", "param" })
                        {
                            if (error.TryGetProperty(field, out var value) &&
                                value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                                message.Append($" {field}: {SanitizeErrorDetail(value.ToString(), apiKey, 2048)};");
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            message.Append(" Upstream error body was not valid JSON; details omitted.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            message.Append(" Timed out reading the upstream error body.");
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            readException = exception;
            message.Append(" Could not read the upstream error body.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new HttpRequestException(message.ToString(), readException, response.StatusCode);
    }

    private static string SanitizeErrorDetail(string value, string? apiKey, int maxLength)
    {
        // Providers may echo a rejected credential in error.message. Redact before truncating.
        if (!string.IsNullOrEmpty(apiKey))
            value = value.Replace(apiKey, "[redacted]", StringComparison.Ordinal);
        value = string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character)).Trim();
        return value.Length > maxLength ? value[..maxLength] + "…" : value;
    }
}
