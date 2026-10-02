using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Fido2NetLib;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record PasskeySummary(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
internal sealed record PasskeySettingsResponse(bool HasPassword, IReadOnlyList<PasskeySummary> Passkeys);
internal sealed record PasskeyOptionsResponse(Guid CeremonyId, JsonElement PublicKey);
internal sealed record PasskeyLoginOptionsRequest([StringLength(64)] string? Username);
internal sealed record PasskeyRegistrationRequest(
    Guid CeremonyId,
    [Required] AuthenticatorAttestationRawResponse Credential,
    [Required, StringLength(64, MinimumLength = 1)] string Name);
internal sealed record PasskeyAssertionRequest(
    Guid CeremonyId,
    [Required] AuthenticatorAssertionRawResponse Credential,
    [StringLength(256)] string? RefreshToken = null,
    [StringLength(128)] string? DeviceName = null);
