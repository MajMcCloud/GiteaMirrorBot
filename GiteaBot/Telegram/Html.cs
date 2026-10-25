namespace GiteaBot.Telegram;

/// <summary>
/// HTML is the parse mode of every bot message. Links, repository names and Gitea
/// notifications may contain characters that would form HTML tags.
/// </summary>
public static class Html
{
    public static string Escape(string? value)
        => string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
