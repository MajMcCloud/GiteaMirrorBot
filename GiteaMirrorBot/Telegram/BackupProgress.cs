using GiteaBot.Analysis;
using GiteaBot.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types.Enums;

namespace GiteaBot.Telegram;

/// <summary>
/// Keeps the single status message ("⏳ In progress") alive during a running Gitea
/// operation. The operation consists of several HTTP round trips (create, catalog,
/// mirror job) and can therefore take considerably longer. Instead of flooding the group
/// with one message per step, the same message is updated through edits.
///
/// <para>
/// Rules:
/// <list type="bullet">
///   <item>Edits are sent at most once every <see cref="MinEditInterval"/> – otherwise
///   Telegram throttles and rejects edits that do not change the content.</item>
///   <item>A heartbeat refreshes only the elapsed time every <see cref="HeartbeatInterval"/>
///   so the message does not look stuck when Gitea is slow to respond.</item>
///   <item><see cref="FinishAsync"/> turns the status message into the final result.</item>
/// </list>
/// </para>
/// </summary>
public sealed class BackupProgress : IDisposable
{
    /// <summary>Minimum distance between two edits of the same message.</summary>
    public static readonly TimeSpan MinEditInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Interval of the elapsed-time refresh when nothing else happens.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);

    private readonly TopicSender _sender;
    private readonly PendingRequest _request;
    private readonly ILogger _log;

    private readonly PeriodicTimer _timer = new(HeartbeatInterval);
    private readonly CancellationTokenSource _loopCts = new();
    private readonly SemaphoreSlim _editGate = new(1, 1);

    private readonly List<string> _completed = [];
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private string _stage = "starting";
    private DateTime _lastEditUtc = DateTime.MinValue;
    private bool _closed;
    private Task? _heartbeat;

    private BackupProgress(TopicSender sender, PendingRequest request, ILogger log)
    {
        _sender = sender;
        _request = request;
        _log = log;
    }

    /// <summary>
    /// Sends the first status message and starts the heartbeat. Returns an instance
    /// whose <see cref="FinishAsync"/> should be called at the end.
    /// </summary>
    public static async Task<BackupProgress> StartAsync(
        TopicSender sender,
        PendingRequest request,
        ILogger log,
        string initialStage = "sending the request to Gitea")
    {
        var progress = new BackupProgress(sender, request, log);
        progress._stage = initialStage;

        var text = BuildText(request.Link, progress._completed, progress._stage, TimeSpan.Zero);

        var sent = await sender.SendAsync(
            request.ChatId,
            request.ThreadId,
            text,
            parseMode: ParseMode.Html,
            silent: true);

        if (sent is not null)
            request.StatusMessageId = sent.MessageId;

        progress._lastEditUtc = DateTime.UtcNow;
        progress._heartbeat = Task.Run(progress.RunHeartbeatAsync);

        return progress;
    }

    /// <summary>Marks the current step as done and starts the next one.</summary>
    public void Advance(string completedStep, string nextStage)
    {
        if (_closed)
            return;

        lock (_completed)
        {
            _completed.Add(completedStep);
        }

        _stage = nextStage;
        _ = PushAsync(force: true);
    }

    /// <summary>Renames the current step without marking anything as done.</summary>
    public void Stage(string stage)
    {
        if (_closed)
            return;

        _stage = stage;
        _ = PushAsync(force: true);
    }

    /// <summary>
    /// Replaces the status message with the final result. Returns <see langword="false"/>
    /// when the message could not be overwritten – the caller then sends it as a new message.
    /// </summary>
    public async Task<bool> FinishAsync(string finalText)
    {
        StopHeartbeat();

        if (_closed)
            return false;

        _closed = true;

        if (_request.StatusMessageId <= 0)
            return false;

        await _editGate.WaitAsync();

        try
        {
            return await _sender.EditAsync(_request.ChatId, _request.StatusMessageId, finalText);
        }
        finally
        {
            _editGate.Release();
        }
    }

    /// <summary>Deletes the status message (e.g. when the result is sent as a new message anyway).</summary>
    public async Task AbandonAsync()
    {
        StopHeartbeat();
        _closed = true;

        if (_request.StatusMessageId > 0)
            await _sender.DeleteAsync(_request.ChatId, _request.StatusMessageId);
    }

    // ------------------------------------------------------------------ Text

    /// <summary>
    /// Pure text generation without any Telegram part – which makes it testable.
    /// </summary>
    public static string BuildText(
        GitHubLink link,
        IReadOnlyList<string> completed,
        string stage,
        TimeSpan elapsed)
    {
        var lines = new List<string>
        {
            $"<b>⏳ In progress</b>",
            $"Target: <a href=\"{Html.Escape(link.CanonicalUrl)}\">{Html.Escape(link.DisplayName)}</a>",
            $"Type: {link.TypeLabel}"
        };

        foreach (var step in completed)
            lines.Add($"✅ {Html.Escape(step)}");

        lines.Add($"▶ {Html.Escape(stage)}");
        lines.Add($"running for {FormatElapsed(elapsed)}");

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return $"{Math.Max(0, (int)elapsed.TotalSeconds)} s";

        var minutes = (int)Math.Floor(elapsed.TotalMinutes);
        var seconds = elapsed.Seconds;

        return seconds == 0 ? $"{minutes} min" : $"{minutes} min {seconds} s";
    }

    // ------------------------------------------------------------------ Intern

    private async Task RunHeartbeatAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_loopCts.Token))
                await PushAsync(force: true);
        }
        catch (OperationCanceledException)
        {
            // regular shutdown
        }
        catch (ObjectDisposedException)
        {
            // timer was disposed during shutdown
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Could not update the status message.");
        }
    }

    private async Task PushAsync(bool force)
    {
        if (_closed || _request.StatusMessageId <= 0)
            return;

        if (!force && DateTime.UtcNow - _lastEditUtc < MinEditInterval)
            return;

        var text = BuildText(_request.Link, SnapshotCompleted(), _stage, DateTime.UtcNow - _startedUtc);

        if (!await _editGate.WaitAsync(TimeSpan.FromSeconds(2)))
            return;

        try
        {
            if (_closed)
                return;

            if (await _sender.EditAsync(_request.ChatId, _request.StatusMessageId, text))
                _lastEditUtc = DateTime.UtcNow;
        }
        finally
        {
            _editGate.Release();
        }
    }

    private string[] SnapshotCompleted()
    {
        lock (_completed)
        {
            return [.. _completed];
        }
    }

    private void StopHeartbeat()
    {
        try
        {
            _loopCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }

    public void Dispose()
    {
        StopHeartbeat();
        _editGate.Dispose();
        _loopCts.Dispose();
#pragma warning disable CA2216 // dispose the timer even though the dispose pattern is kept lean here
        _timer.Dispose();
#pragma warning restore CA2216
    }
}
