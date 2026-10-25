using GiteaBot.Analysis;
using GiteaBot.Web.Models;

namespace GiteaBot;

/// <summary>
/// Offline self-test without Telegram/Gitea: verifies link detection and message formatting.
/// Usage: dotnet run -- --selftest
/// </summary>
public static class SelfTest
{
    private static readonly string[] Cases =
    [
        "https://github.com/majmccloud/TelegramBotFramework",
        "https://github.com/majmccloud",
        "github.com/majmccloud/TelegramBotFramework",
        "https://www.github.com/owner/repo",
        "https://github.com/owner/repo/issues/3",
        "https://github.com/owner/repo/tree/main/src/App.cs",
        "https://github.com/owner/repo/pull/1/files",
        "https://github.com/owner/repo/blob/abc123/README.md",
        "https://github.com/owner/repo.git",
        "https://github.com/owner/",
        "https://github.com/trending",
        "https://gist.github.com/owner/abc",
        "https://gitea.example.com/owner/repo",
        "https://github.com/owner/repo?x=1#frag"
    ];

    public static int Run()
    {
        Console.WriteLine("Self-test GitHubLinkAnalyzer");
        Console.WriteLine(new string('-', 70));

        var failures = 0;

        foreach (var input in Cases)
        {
            var link = GitHubLinkAnalyzer.Parse(input);

            if (link is null)
            {
                Console.WriteLine($"(no match)  {input}");
                continue;
            }

            Console.WriteLine($"{link.TypeLabel,-13} {link.DisplayName,-40} <- {input}");
        }

        Console.WriteLine();
        Console.WriteLine("Mehrere Links in einem Text:");
        foreach (var l in GitHubLinkAnalyzer.ExtractFromText(
                     "See https://github.com/majmccloud/TelegramBotFramework and also https://github.com/majmccloud – ignore the duplicate https://github.com/majmccloud/TelegramBotFramework."))
        {
            Console.WriteLine($"  {l.TypeLabel,-13} {l.DisplayName}");
        }

        Console.WriteLine();
        Console.WriteLine("Reserved owners (must be ignored):");
        foreach (var reserved in new[] { "https://github.com/trending", "https://github.com/orgs", "https://github.com/settings" })
        {
            var link = GitHubLinkAnalyzer.Parse(reserved);
            var ok = link is null;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {reserved} -> {(link is null ? "ignored" : link.DisplayName)}");
        }

        Console.WriteLine();
        Console.WriteLine("Non-GitHub domains (must be ignored):");
        foreach (var other in new[] { "https://gist.github.com/owner/abc", "https://gitlab.com/owner/repo", "https://dev.azure.com/x/y" })
        {
            var link = GitHubLinkAnalyzer.Parse(other);
            var ok = link is null;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {other} -> {(link is null ? "ignored" : link.DisplayName)}");
        }

        Console.WriteLine();
        Console.WriteLine("Formatting a Gitea message:");
        var sample = new Root
        {
            Title = "Sync completed",
            Message = "Repository majmccloud/TelegramBotFramework has been backed up.",
            Type = "repository",
            Timestamp = new DateTime(2024, 5, 1, 12, 34, 56, DateTimeKind.Utc)
        };
        Console.WriteLine(GiteaBot.Services.NotificationRouter.Format(sample));

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "Self-test passed." : $"Self-test finished with {failures} error(s).");
        return failures == 0 ? 0 : 1;
    }
}
