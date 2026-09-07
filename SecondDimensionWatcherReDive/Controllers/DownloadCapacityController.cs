using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Auth;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Authorize]
[Route("api/download-capacity")]
internal sealed class DownloadCapacityController(IDownloadCapacityRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken) =>
        Ok(await repository.ListAsync(cancellationToken));
}
