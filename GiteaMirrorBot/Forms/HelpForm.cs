using GiteaMirrorBot.Services;
using Telegram.Bot.Types.Enums;
using TelegramBotBase.Base;
using TelegramBotBase.Form;

namespace GiteaMirrorBot.Forms;

/// <summary>Short usage description for private chats – sent once when the form opens.</summary>
public class HelpForm : FormBase
{
    public HelpForm()
    {
        Opened += async (_, _) =>
        {
            var s = BotServices.Current.Settings;

            var text =
                "<b>GiteaMirrorBot</b>" + Environment.NewLine +
                "Post a GitHub link in the group, for example:" + Environment.NewLine +
                "• https://github.com/owner/repo → backs up the repository" + Environment.NewLine +
                "• https://github.com/owner → backs up the organization" + Environment.NewLine +
                Environment.NewLine +
                "The bot then asks via a button whether the backup should be queued through Gitea." + Environment.NewLine +
                Environment.NewLine +
                $"Group: <code>{s.Telegram.GroupId}</code>" + Environment.NewLine +
                $"Gitea: <code>{s.Gitea.BaseUrl}</code>";

            await Device.Send(text, parseMode: ParseMode.Html);
        };
    }
}
