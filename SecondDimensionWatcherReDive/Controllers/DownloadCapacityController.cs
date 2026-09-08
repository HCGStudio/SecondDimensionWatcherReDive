using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Authorize]
[Route("api/download-capacity")]
internal sealed class DownloadCapacityController(
    IDownloadCapacityRepository repository,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var entries = await repository.ListAsync(cancellationToken);
        var capacityEnabled = configuration.GetValue("DownloadCapacity:Enabled", true);
        return Ok(entries.Select(entry => ToExternal(entry, capacityEnabled)).ToList());
    }

    private static External.DownloadCapacityEntryResponse ToExternal(DownloadCapacityEntry entry, bool capacityEnabled)
    {
        var state = entry.State switch
        {
            "Waiting" => "Waiting",
            "Reserved" => "Reserved",
            "Submitted" => "Submitted",
            "Failed" => "Failed",
            _ => "Unknown"
        };
        // Persisted reasons contain internal diagnostics, including exception
        // messages. Only fixed reason codes cross the authenticated read API.
        var reasonCode = state switch
        {
            "Waiting" when entry.Reason.StartsWith("Storage cannot be verified: ", StringComparison.Ordinal)
                => capacityEnabled ? "storageUnavailable" : "downloaderUnavailable",
            "Waiting" when !capacityEnabled => "queuedForSubmission",
            "Waiting" when entry.ExpectedBytes is null or <= 0 => "unknownSize",
            "Waiting" when entry.Reason.StartsWith("FIFO queue: ", StringComparison.Ordinal)
                => "waitingForCapacity",
            "Waiting" when entry.Reason == "Remote task is absent; queued for resubmission."
                => "resubmitting",
            "Waiting" => "assessingCapacity",
            "Reserved" when entry.Reason.StartsWith("Submission will be reconciled: ", StringComparison.Ordinal)
                => "submissionUncertain",
            "Reserved" when !capacityEnabled => "queuedForSubmission",
            "Reserved" => "preparingSubmission",
            "Submitted" when entry.Reason == "Recovering an existing download reservation; awaiting remote reconciliation."
                => "reconciling",
            "Submitted" => "downloading",
            "Failed" => "submissionRejected",
            _ => "unknown"
        };
        return new External.DownloadCapacityEntryResponse(
            entry.ItemId, entry.Title, entry.ExpectedBytes is > 0 ? entry.ExpectedBytes : null,
            state, entry.Paused, reasonCode);
    }
}
