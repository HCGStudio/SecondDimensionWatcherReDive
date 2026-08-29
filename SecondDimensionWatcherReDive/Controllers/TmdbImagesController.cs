using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/images/tmdb")]
internal sealed class TmdbImagesController(
    ITmdbImageProxyService imageProxy,
    IOptions<TmdbImageProxyOptions> options) : ControllerBase
{
    [HttpGet("{size}/{fileName}")]
    public async Task<IActionResult> GetAsync(
        string size,
        string fileName,
        CancellationToken cancellationToken)
    {
        var result = await imageProxy.GetAsync(size, fileName, cancellationToken);
        if (result.Status == TmdbImageFetchStatus.InvalidPath)
            return Error(StatusCodes.Status400BadRequest, "tmdb_image_path_invalid");
        if (result.Status == TmdbImageFetchStatus.NotFound)
            return Error(StatusCodes.Status404NotFound, "tmdb_image_not_found");
        if (result.Status != TmdbImageFetchStatus.Success || result.Content is null)
            return Error(StatusCodes.Status502BadGateway, "tmdb_image_unavailable");

        var maxAge = Math.Max(0, (long)options.Value.ClientCacheDuration.TotalSeconds);
        Response.Headers.CacheControl = $"public, max-age={maxAge}";
        Response.Headers.ETag = result.Content.ETag;
        Response.Headers.XContentTypeOptions = "nosniff";

        if (MatchesIfNoneMatch(result.Content.ETag))
            return StatusCode(StatusCodes.Status304NotModified);

        return File(result.Content.Bytes, result.Content.ContentType);
    }

    private ObjectResult Error(int statusCode, string code)
    {
        Response.Headers.CacheControl = "no-store";
        var problem = new ProblemDetails
        {
            Status = statusCode
        };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }

    private bool MatchesIfNoneMatch(string etag) =>
        Request.Headers.IfNoneMatch
            .SelectMany(value => value?.Split(',') ?? [])
            .Select(value => value.Trim())
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
}
