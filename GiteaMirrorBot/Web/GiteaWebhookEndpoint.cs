using GiteaMirrorBot.Configuration;
using GiteaMirrorBot.Services;
using GiteaMirrorBot.Web.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace GiteaMirrorBot.Web;

/// <summary>
/// HTTP endpoint Gitea can post its notifications to (TA-11).
///   POST /api/gitea/webhook   -> Root { title, message, type, timestamp }
///   GET  /health              -> liveness
/// No authentication – the application runs locally (decision O-5).
/// </summary>
public static class GiteaWebhookEndpoint
{
    public static void MapWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost(AppSettings.WebhookPath, async (Root payload, NotificationRouter router, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var log = loggerFactory.CreateLogger("GiteaWebhook");

            log.LogInformation(
                "Gitea notification received: title={Title}, type={Type}, time={Timestamp}",
                payload.Title, payload.Type, payload.Timestamp);

            try
            {
                await router.RouteAsync(payload, ct);
                return Results.Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Could not forward the Gitea notification.");
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("GiteaWebhook")
        .WithTags("Gitea");

        app.MapGet("/health", () => Results.Ok(new { ok = true, time = DateTime.UtcNow }))
            .WithName("Health")
            .WithTags("System");
    }
}
