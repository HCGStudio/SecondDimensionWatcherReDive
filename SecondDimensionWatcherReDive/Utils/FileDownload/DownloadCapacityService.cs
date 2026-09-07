using System.Globalization;
using System.Text.Json;
using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

/// <summary>
/// Durable FIFO admission. Reservation commits before any remote submission;
/// both submission and cancellation hold the cross-instance budget lock.
/// </summary>
public sealed class DownloadCapacityService(
    IDownloadCapacityRepository repository,
    IAnimationInfoRepository animations,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IServiceProvider serviceProvider)
{
    public bool Enabled => configuration.GetValue("DownloadCapacity:Enabled", true);
    public long SafetyBytes => Math.Max(0, configuration.GetValue<long?>("DownloadCapacity:SafetyBytes") ?? 5L * 1024 * 1024 * 1024);
    public string SavePath => Path.GetFullPath(configuration["FileStore:Local"] ?? "./download");

    public async Task EnqueueAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginAsync(cancellationToken);
        var info = await animations.FindByIdAsync(itemId, cancellationToken)
                   ?? throw new KeyNotFoundException("The download no longer exists.");
        if (!info.IsDownloadTracked || info.DownloadCancellationId is not null)
            throw new InvalidOperationException("The download attempt is no longer active.");
        var existing = (await repository.ListAsync(cancellationToken)).FirstOrDefault(entry => entry.ItemId == itemId);
        if (existing is null || existing.DownloadAttemptId != info.DownloadAttemptId)
        {
            var now = DateTimeOffset.UtcNow;
            var expectedBytes = info.ReleaseSizeBytes;
            if ((expectedBytes is null or <= 0) && info.CachedDownloadData.Length > 0)
            {
                try { expectedBytes = Services.SyncFeed.ParseTorrentData(info.CachedDownloadData, info.DownloadUrl).PayloadSizeBytes; }
                catch (Exceptions.InvalidTorrentDataException) { /* Unknown payloads remain waiting. */ }
            }
            await repository.SaveAsync(new DownloadCapacityEntry(itemId, info.DownloadAttemptId, info.Title,
                info.AdditionalDownloadInfo, expectedBytes, 0, "Waiting", false,
                expectedBytes is > 0 ? "Waiting for capacity assessment." : "Size is unknown; automatic admission is disabled.",
                now, now), cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<DownloadCapacityEntry>> ListAsync(CancellationToken cancellationToken) =>
        repository.ListAsync(cancellationToken);

    public async Task<bool> ControlAsync(
        Guid itemId, string action, Func<CancellationToken, Task<bool>> remoteAction, CancellationToken cancellationToken)
    {
        await using var transaction = await repository.BeginAsync(cancellationToken);
        var entry = (await repository.ListAsync(cancellationToken)).FirstOrDefault(row => row.ItemId == itemId);
        // Waiting rows have never reserved capacity and cannot have submitted.
        // Reserved rows may have reached qBittorrent before acknowledgement.
        if (entry?.State != "Waiting" && !await remoteAction(cancellationToken)) return false;
        if (action == "cancel")
            await repository.RemoveAsync(itemId, cancellationToken);
        else if (entry is not null)
            await repository.SaveAsync(entry with { Paused = action == "pause", UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        if (!Enabled) return false;
        await using var transaction = await repository.BeginAsync(cancellationToken);
        await repository.RecoverTrackedAsync(cancellationToken);
        var entries = (await repository.ListAsync(cancellationToken)).ToList();
        if (entries.Count == 0) return false;
        var current = new Dictionary<Guid, Framework.DataRepository.AnimationInfo>();
        foreach (var entry in entries.ToArray())
        {
            var info = await animations.FindByIdAsync(entry.ItemId, cancellationToken);
            if (info is null || !info.IsDownloadTracked || info.IsDownloadFinished || info.DownloadAttemptId != entry.DownloadAttemptId)
            {
                await repository.RemoveAsync(entry.ItemId, cancellationToken);
                entries.Remove(entry);
            }
            else current[entry.ItemId] = info;
        }

        using var client = httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));
        var now = DateTimeOffset.UtcNow;
        Dictionary<string, RemoteTorrentInfo> remote;
        long available;
        try
        {
            // Read written amounts BEFORE free space. Concurrent writes then make
            // the estimate conservative, never create an optimistic balance.
            remote = new Dictionary<string, RemoteTorrentInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in entries.Select(row => row.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Chunk(50))
            {
                var torrents = await client.GetFromJsonAsync(
                    $"/api/v2/torrents/info?hashes={Uri.EscapeDataString(string.Join('|', batch))}",
                    QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray, cancellationToken)
                    ?? throw new IOException("The downloader did not return torrent status.");
                foreach (var torrent in torrents) remote[torrent.Hash] = torrent;
            }
            available = await GetAvailableBytesAsync(client, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            foreach (var entry in entries.Where(row => row.State == "Waiting"))
                await repository.SaveAsync(entry with { Reason = "Storage cannot be verified: " + exception.Message, UpdatedAt = now }, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!remote.TryGetValue(entry.Hash, out var torrent)) continue;
            // A remote task may outlive a process restart. Adopt its remaining
            // bytes before admitting any additional work.
            var total = torrent.TotalSize is > 0 ? torrent.TotalSize : entry.ExpectedBytes;
            var remaining = torrent.AmountLeft is > 0 ? torrent.AmountLeft.Value :
                torrent.Progress >= 1 ? 0 : total is > 0 ?
                    (long)Math.Ceiling(total.Value * (1 - Math.Clamp(torrent.Progress, 0, 1))) : long.MaxValue;
            entries[index] = entry with { State = "Submitted", RemainingBytes = Math.Max(0, remaining),
                Reason = "Capacity reserved for the remaining download.", UpdatedAt = now };
            await repository.SaveAsync(entries[index], cancellationToken);
        }

        var reserved = entries.FirstOrDefault(entry => entry.State == "Reserved" && !entry.Paused
            && current[entry.ItemId].DownloadCancellationId is null
            && now - entry.UpdatedAt >= TimeSpan.FromSeconds(10));
        if (reserved is not null)
        {
            var info = current[reserved.ItemId];
            var downloader = serviceProvider.GetRequiredService<RemoteTorrentDownloadClient>();
            var submitted = false;
            try
            {
                var accepted = await downloader.SubmitAdmittedAsync(info.Id, info.CachedDownloadData,
                    info.AdditionalDownloadInfo, cancellationToken);
                submitted = accepted;
                await repository.SaveAsync(reserved with {
                    State = accepted ? "Submitted" : "Failed",
                    RemainingBytes = accepted ? reserved.RemainingBytes : 0,
                    Reason = accepted ? "Submitted to downloader." : "Downloader rejected the torrent. Cancel and retry after correcting it.",
                    UpdatedAt = now }, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // An uncertain acknowledgement retains the full reservation.
                await repository.SaveAsync(reserved with { Reason = "Submission will be reconciled: " + exception.Message, UpdatedAt = now }, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            if (submitted)
                await downloader.SubmitQueryDownloadProgressAsync(info.Id, info.DownloadUrl, info.CachedDownloadData,
                    info.AdditionalDownloadInfo, cancellationToken);
            return true;
        }

        var waiting = entries.FirstOrDefault(entry => entry.State == "Waiting" && !entry.Paused
            && current[entry.ItemId].DownloadCancellationId is null && entry.ExpectedBytes is > 0);
        if (waiting is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }
        var remainingReservations = entries.Where(entry => entry.State is "Reserved" or "Submitted").Sum(entry => (decimal)entry.RemainingBytes);
        if (serviceProvider.GetService<ITranscodeCapacityBudget>() is { } transcoding)
            remainingReservations += await transcoding.GetRemainingBytesAsync(cancellationToken);
        var balance = Math.Max(0, (decimal)available - SafetyBytes - remainingReservations);
        var admitted = waiting.ExpectedBytes!.Value <= balance;
        await repository.SaveAsync(waiting with {
            State = admitted ? "Reserved" : "Waiting",
            RemainingBytes = admitted ? waiting.ExpectedBytes.Value : 0,
            Reason = admitted ? "Capacity reserved; preparing submission." :
                $"FIFO queue: requires {waiting.ExpectedBytes.Value} bytes; {balance:0} bytes available after safety reserve ({SafetyBytes} bytes) and active reservations. Resumes when capacity recovers.",
            // A new reservation can be dispatched on the next supervised iteration.
            UpdatedAt = admitted ? now.AddSeconds(-10) : now
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return admitted;
    }

    internal async Task<long> GetAvailableBytesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var preferences = JsonDocument.Parse(await client.GetStringAsync("/api/v2/app/preferences", cancellationToken));
        var root = preferences.RootElement;
        if (root.TryGetProperty("temp_path_enabled", out var temporary) && temporary.ValueKind == JsonValueKind.True)
            throw new IOException("A separate incomplete-download directory is enabled. Disable it to account for the actual write volume with the configured save path.");
        var localPath = configuration["DownloadCapacity:LocalVolumePath"];
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            if (!Directory.Exists(localPath)) throw new IOException("The configured shared download volume is not mounted.");
            var drive = FindDrive(CapacityVolume.CanonicalPath(localPath)) ?? throw new IOException("The shared download volume could not be located.");
            return drive.AvailableFreeSpace;
        }
        // Never substitute the application's own root filesystem for a remote disk.
        using var response = await client.GetAsync(
            $"/api/v2/app/getFreeSpaceAtPath?path={Uri.EscapeDataString(SavePath)}", cancellationToken);
        if (response.IsSuccessStatusCode && long.TryParse(await response.Content.ReadAsStringAsync(cancellationToken),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes >= 0)
            return bytes;
        // Older qBittorrent exposes only the default save volume. This fallback
        // is valid solely when the actual save directory is that default and no
        // separate incomplete-download directory is enabled.
        if (!root.TryGetProperty("save_path", out var path) || path.GetString()?.TrimEnd('/', '\\') != SavePath.TrimEnd('/', '\\'))
            throw new IOException("Configure DownloadCapacity:LocalVolumePath for the actual shared write volume, or use qBittorrent path-specific capacity reporting without a separate incomplete directory.");
        using var data = JsonDocument.Parse(await client.GetStringAsync("/api/v2/sync/maindata", cancellationToken));
        if (data.RootElement.TryGetProperty("server_state", out var server)
            && server.TryGetProperty("free_space_on_disk", out var free) && free.TryGetInt64(out bytes) && bytes >= 0)
            return bytes;
        throw new IOException("The downloader did not report usable free-space information.");
    }

    internal static DriveInfo? FindDrive(string path) => DriveInfo.GetDrives().Where(drive => drive.IsReady)
        .Where(drive => Path.GetFullPath(path) == drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar)
            || Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(drive.RootDirectory.FullName) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .OrderByDescending(drive => drive.RootDirectory.FullName.Length).FirstOrDefault();
}

public interface ITranscodeCapacityBudget
{
    Task<long> GetRemainingBytesAsync(CancellationToken cancellationToken);
}
