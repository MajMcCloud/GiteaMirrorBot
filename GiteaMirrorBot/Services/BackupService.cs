using GiteaBot.Analysis;
using GiteaBot.Gitea;
using GiteaBot.Telegram;
using Microsoft.Extensions.Logging;

namespace GiteaBot.Services;

/// <summary>
/// Orchestrates the backup operation (TA-08/TA-09):
/// 1. ask Gitea to create the record  (/api/sync/repository | /api/sync/organization)
/// 2. take the id from the sync response (Gitea returns it on 200 and on 409) and trigger
///    the mirror job (/api/job/mirror-repo | /api/job/mirror-org). If the body does not
///    contain an id, the operation falls back to the catalog lookup (/api/github/repositories).
/// 3. delete the question message -> report the result in the same topic.
/// While steps 1 and 2 run, an "In progress" message is kept up to date
/// (<see cref="BackupProgress"/>) so the group can see that something is happening.
/// </summary>
public sealed class BackupService
{
    private readonly GiteaApiClient _gitea;
    private readonly GiteaCatalogService _catalog;
    private readonly TopicSender _sender;
    private readonly PendingRequestStore _store;
    private readonly ILogger<BackupService> _log;

    public BackupService(
        GiteaApiClient gitea,
        GiteaCatalogService catalog,
        TopicSender sender,
        PendingRequestStore store,
        ILogger<BackupService> log)
    {
        _gitea = gitea;
        _catalog = catalog;
        _sender = sender;
        _store = store;
        _log = log;
    }

    /// <summary>"Backup" button: queue the job in Gitea, run the mirror job, delete the question, report.</summary>
    public async Task ConfirmAsync(PendingRequest request, CancellationToken ct = default)
    {
        using var progress = await BackupProgress.StartAsync(_sender, request, _log);

        GiteaResult sync;
        GiteaResult mirror;

        try
        {
            // 1.) Create the record
            sync = await _gitea.SyncAsync(request.Link, ct);

            if (sync.Success)
                progress.Advance($"created via {EndpointName(request.Link)}", "preparing mirror job");
            else if (sync.IsConflict)
                progress.Advance($"already existed ({EndpointName(request.Link)})", "preparing mirror job");
            else
                progress.Advance($"creation failed ({EndpointName(request.Link)})", "building the result");

            // Creating the record changed the catalog -> without a cache reset the new id would be missing.
            _catalog.Invalidate();

            // 2.) Mirror job
            // A 409 also carries the stored id – already mirrored targets are simply queued again.
            mirror = sync.Success || sync.IsConflict
                ? await RunMirrorJobAsync(request.Link, sync, progress, ct)
                : new GiteaResult(false, 0, "No mirror job – creation failed.");
        }
        catch (Exception ex)
        {
            // Without this guard the status message would hang in the group as "in progress" forever.
            _log.LogError(ex, "Backup aborted: {Target}", request.Link.DisplayName);

            sync = new GiteaResult(false, 0, $"Operation aborted: {ex.Message}");
            mirror = new GiteaResult(false, 0, "not run");
        }

        // 3.) The question message always disappears (points 5/6 of the requirements).
        await _sender.DeleteAsync(request.ChatId, request.QuestionMessageId);
        _store.Remove(request.Id);

        var text = BuildResultText(request, sync, mirror);

        // Write the result into the existing status message: the group keeps the
        // trail (which steps ran) and does not get a second message.
        if (!await progress.FinishAsync(text))
        {
            await progress.AbandonAsync();
            await _sender.SendAsync(request.ChatId, request.ThreadId, text);
        }

        _log.LogInformation(
            "Backup finished: {Target} -> create {Sync}, mirror job {Mirror}",
            request.Link.DisplayName, sync.Success, mirror.Success);
    }

    /// <summary>"Cancel" button: delete our own question message, no further message.</summary>
    public async Task CancelAsync(PendingRequest request)
    {
        await _sender.DeleteAsync(request.ChatId, request.QuestionMessageId);

        // Also remove a status message that may still be standing (e.g. from an earlier attempt).
        if (request.StatusMessageId > 0)
            await _sender.DeleteAsync(request.ChatId, request.StatusMessageId);

        _store.Remove(request.Id);
        _log.LogInformation("Backup declined: {Target}", request.Link.DisplayName);
    }

    /// <summary>
    /// Mirrors the record that was just created (or already existed) right away.
    /// The id comes straight from the sync response; only when it is missing there,
    /// the repository catalog is consulted. If both fail, the created record stays.
    /// </summary>
    private async Task<GiteaResult> RunMirrorJobAsync(GitHubLink link, GiteaResult sync, BackupProgress progress, CancellationToken ct)
    {
        var direct = GiteaApiClient.ParseSyncResponse(sync.RawBody);
        var directId = link.Kind == LinkKind.Repository ? direct?.Repository?.Id : direct?.Organization?.Id;

        if (!string.IsNullOrWhiteSpace(directId))
        {
            progress.Advance(link.Kind == LinkKind.Repository
                ? $"repository id {directId} from the sync response"
                : $"organization id {directId} from the sync response", "triggering mirror job");

            return link.Kind == LinkKind.Repository
                ? await _gitea.MirrorRepositoriesAsync([directId], ct)
                : await _gitea.MirrorOrganizationsAsync([directId], ct);
        }

        // Fallback: the sync response carried no id -> look it up in the catalog.
        _log.LogInformation("Sync response without id for {Target} – FALLING BACK to catalog lookup", link.DisplayName);

        var plan = await _catalog.PlanAsync(link, ct);

        if (!plan.Resolved)
        {
            _log.LogWarning("Mirror job skipped: {Target} – {Reason}", link.DisplayName, plan.Reason);
            progress.Advance($"no mirror job: {plan.Reason}", "building the result");
            return new GiteaResult(false, 0, plan.Reason);
        }

        if (!plan.HasWork)
        {
            progress.Advance("no mirror job: no id in the catalog", "building the result");
            return new GiteaResult(false, 0, "No id found in the catalog.");
        }

        if (plan.OrganizationIds.Count > 0)
        {
            _log.LogInformation("Org mirror job (fallback): {Target} -> {Count} org id(s)", link.DisplayName, plan.OrganizationIds.Count);
            progress.Advance($"{plan.OrganizationIds.Count} organization id(s) found in the catalog", "triggering mirror job");
            return await _gitea.MirrorOrganizationsAsync(plan.OrganizationIds, ct);
        }

        _log.LogInformation("Repo mirror job (fallback): {Target} -> id {Id}", link.DisplayName, plan.RepositoryIds[0]);
        progress.Advance($"repository id {plan.RepositoryIds[0]} found in the catalog", "triggering mirror job");
        return await _gitea.MirrorRepositoriesAsync(plan.RepositoryIds, ct);
    }

    private static string BuildResultText(PendingRequest request, GiteaResult sync, GiteaResult mirror)
    {
        var link = request.Link;
        var headline = (sync.Success, sync.IsConflict, mirror.Success) switch
        {
            (true, _, true) => $"✅ Backed up",
            (true, _, false) => $"⚠️ Created, mirror job pending",
            (false, true, true) => $"✅ Already in Gitea – mirrored",
            (false, true, false) => $"ℹ️ Already in Gitea, mirror job pending",
            _ => $"⚠️ Backup failed",
        };

        var lines = new List<string>
        {
            $"<b>{headline}</b>",
            $"Type: {link.TypeLabel}",
            $"GitHub: <a href=\"{Html.Escape(link.CanonicalUrl)}\">{Html.Escape(link.DisplayName)}</a>",
            $"Create ({EndpointName(link)}): {Html.Escape(sync.Message)}"
        };

        var mirrorPath = link.Kind == LinkKind.Repository
            ? GiteaApiClient.MirrorRepositoryJobEndpoint
            : GiteaApiClient.MirrorOrganizationJobEndpoint;

        lines.Add($"Mirror job ({mirrorPath}): {Html.Escape(mirror.Message)}");

        if (!string.IsNullOrWhiteSpace(request.RequestedBy))
            lines.Add($"Requested by: {Html.Escape(request.RequestedBy)}");

        return string.Join(Environment.NewLine, lines);
    }

    private static string EndpointName(GitHubLink link)
        => link.Kind == LinkKind.Repository
            ? GiteaApiClient.RepositoryEndpoint
            : GiteaApiClient.OrganizationEndpoint;
}
