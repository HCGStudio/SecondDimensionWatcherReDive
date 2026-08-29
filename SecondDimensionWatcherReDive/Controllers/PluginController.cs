using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Controllers.External;
using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.PluginPlatform;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/plugins")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class PluginController(
    IPluginManager manager,
    IJavaScriptPluginLoader packageLoader) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<External.InstalledPlugin>>> GetAll(CancellationToken cancellationToken)
        => Ok((await manager.GetAllAsync(cancellationToken)).Select(plugin => plugin.ToExternal()).ToArray());

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<External.PluginPackagePreview>> Preview(
        [FromForm] IFormFile package,
        CancellationToken cancellationToken)
    {
        if (package.Length == 0) return BadRequest(Error("empty_package", "A non-empty plugin package is required."));
        try
        {
            await using var stream = package.OpenReadStream();
            return Ok((await packageLoader.PreviewPackageAsync(stream, package.FileName, cancellationToken)).ToExternal());
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or JsonException)
        {
            return BadRequest(Error("invalid_package", exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(Error("plugin_preview_capacity_reached", exception.Message));
        }
    }

    [HttpPost("preview-remote")]
    public ActionResult PreviewRemote([FromBody] RemotePluginInstallRequest request)
        => StatusCode(StatusCodes.Status403Forbidden, Error(
            "remote_install_disabled",
            $"Remote JavaScript installation is disabled. Download '{request.Url}' through a trusted administrative channel, verify its provenance, then upload it for checksum, signature and capability review."));

    [HttpPost("install")]
    public async Task<ActionResult<External.PluginInstallResult>> Install(
        [FromBody] InstallPluginRequest request,
        CancellationToken cancellationToken)
        => await ExecuteMutationAsync(async () => (await packageLoader.InstallPackageAsync(
            request.PreviewToken,
            request.ExpectedSha256,
            request.ApprovedCapabilities.ToDomain(),
            cancellationToken)).ToExternal());

    [HttpPost("{id}/upgrade")]
    public async Task<ActionResult<External.PluginInstallResult>> Upgrade(
        string id,
        [FromBody] InstallPluginRequest request,
        CancellationToken cancellationToken)
        => await ExecuteMutationAsync(async () => (await manager.UpgradeAsync(
            id,
            request.PreviewToken,
            request.ExpectedSha256,
            request.ApprovedCapabilities.ToDomain(),
            cancellationToken)).ToExternal());

    [HttpPost("{id}/enable")]
    public async Task<ActionResult> Enable(string id, CancellationToken cancellationToken)
        => await ExecuteEmptyMutationAsync(() => manager.EnableAsync(id, cancellationToken));

    [HttpPost("{id}/disable")]
    public async Task<ActionResult> Disable(string id, CancellationToken cancellationToken)
        => await ExecuteEmptyMutationAsync(() => manager.DisableAsync(id, cancellationToken));

    [HttpPut("{id}/configuration")]
    public async Task<ActionResult> UpdateConfiguration(
        string id,
        [FromBody] UpdatePluginConfigurationRequest request,
        CancellationToken cancellationToken)
        => await ExecuteEmptyMutationAsync(() => manager.UpdateConfigurationAsync(
            id,
            request.Configuration,
            cancellationToken));

    [HttpDelete("{id}")]
    public async Task<ActionResult> Uninstall(
        string id,
        CancellationToken cancellationToken,
        [FromQuery] bool deleteData = false)
        => await ExecuteEmptyMutationAsync(() => manager.UninstallAsync(id, deleteData, cancellationToken));

    private async Task<ActionResult<T>> ExecuteMutationAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return Ok(await operation());
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(Error("plugin_not_found", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(Error("invalid_plugin_request", exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, Error("capability_or_trust_denied", exception.Message));
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException)
        {
            return Conflict(Error("plugin_operation_rejected", exception.Message));
        }
    }

    private async Task<ActionResult> ExecuteEmptyMutationAsync(Func<Task> operation)
    {
        var result = await ExecuteMutationAsync(async () =>
        {
            await operation();
            return true;
        });
        return result.Result ?? NoContent();
    }

    private static PluginOperationError Error(string code, string message) => new(code, message);
}
