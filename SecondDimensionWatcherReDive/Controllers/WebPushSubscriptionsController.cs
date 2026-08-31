using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Repositories;
using SecondDimensionWatcherReDive.Utils.Http;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/notifications/web-push")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
internal sealed class WebPushSubscriptionsController(
    IWebPushSubscriptionRepository subscriptions,
    OutboundAddressPolicy outboundAddressPolicy,
    IConfiguration configuration) : ControllerBase
{
    private const int MaximumEndpointLength = 2048;

    [HttpGet("config")]
    public ActionResult<WebPushConfigurationResponse> GetConfiguration() =>
        Ok(new WebPushConfigurationResponse(
            configuration.GetValue<bool>("Notifications:WebPush:Enabled"),
            configuration["Notifications:WebPush:VapidPublicKey"] ?? string.Empty));

    [HttpGet("subscriptions")]
    public async Task<ActionResult<IReadOnlyList<WebPushSubscriptionSummary>>> GetSubscriptionsAsync(
        CancellationToken cancellationToken)
    {
        var items = await subscriptions.GetAllAsync(cancellationToken);
        return Ok(items.Select(ToSummary).ToList());
    }

    [HttpPost("subscriptions")]
    public async Task<ActionResult<WebPushSubscriptionSummary>> RegisterAsync(
        [FromBody] RegisterWebPushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Notifications:WebPush:Enabled")
            || string.IsNullOrWhiteSpace(
                configuration["Notifications:WebPush:VapidPublicKey"])
            || string.IsNullOrWhiteSpace(
                configuration["Notifications:WebPush:VapidPrivateKey"]))
            return Conflict(new { message = "Enable and configure Web Push first." });

        if (!TryNormalizeEndpoint(request.Endpoint, out var endpoint, out var endpointUri))
            return ValidationError(
                "endpoint",
                "The endpoint must be an absolute HTTPS push-service URL of at most 2048 characters.");
        if (request.Keys is null
            || !IsValidKey(request.Keys.P256dh, expectedLength: 65, requiredPrefix: 0x04)
            || !IsValidKey(request.Keys.Auth, expectedLength: 16, requiredPrefix: null))
            return ValidationError("keys", "The browser subscription keys are invalid.");

        try
        {
            // Push endpoints are bearer capabilities supplied by a browser. Apply
            // the same DNS/IP policy used for other outbound requests at both
            // registration time and again through the pinned sending handler.
            await outboundAddressPolicy.ValidateUriAsync(endpointUri!, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var saved = await subscriptions.UpsertAsync(
                new WebPushSubscription(
                    Guid.NewGuid(),
                    endpoint!,
                    request.Keys.P256dh!,
                    request.Keys.Auth!,
                    now,
                    now,
                    null,
                    null,
                    null),
                cancellationToken);
            return Ok(ToSummary(saved));
        }
        catch (OutboundRequestBlockedException)
        {
            return ValidationError("endpoint", "The push-service endpoint is not allowed.");
        }
        catch (WebPushSubscriptionLimitExceededException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("subscriptions/{id:guid}")]
    public async Task<IActionResult> RemoveAsync(
        [FromRoute] Guid id,
        CancellationToken cancellationToken) =>
        await subscriptions.RemoveAsync(id, cancellationToken)
            ? NoContent()
            : NotFound();

    [HttpPost("subscriptions/remove-current")]
    public async Task<IActionResult> RemoveCurrentAsync(
        [FromBody] RemoveWebPushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeEndpoint(request.Endpoint, out var endpoint, out _))
            return ValidationError("endpoint", "The push-service endpoint is invalid.");
        await subscriptions.RemoveByEndpointAsync(endpoint!, cancellationToken);
        return NoContent();
    }

    private static WebPushSubscriptionSummary ToSummary(WebPushSubscription subscription) => new(
        subscription.Id,
        new Uri(subscription.Endpoint).GetLeftPart(UriPartial.Authority),
        subscription.CreatedAt,
        subscription.UpdatedAt,
        subscription.LastSuccessAt,
        subscription.LastFailureAt,
        subscription.LastError);

    private ActionResult ValidationError(string key, string message)
    {
        ModelState.AddModelError(key, message);
        return ValidationProblem(ModelState);
    }

    private static bool TryNormalizeEndpoint(
        string? value,
        out string? endpoint,
        out Uri? uri)
    {
        endpoint = null;
        uri = null;
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumEndpointLength
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.IdnHost)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        endpoint = uri.AbsoluteUri;
        return true;
    }

    private static bool IsValidKey(
        string? value,
        int expectedLength,
        byte? requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            return false;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(normalized);
            if (bytes.Length != expectedLength
                || (requiredPrefix.HasValue && bytes[0] != requiredPrefix.Value))
                return false;
            if (requiredPrefix == 0x04)
            {
                using var key = ECDiffieHellman.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = bytes[1..33],
                        Y = bytes[33..65]
                    }
                });
            }
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
