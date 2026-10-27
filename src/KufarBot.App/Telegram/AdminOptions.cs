using System;
using System.Linq;

namespace KufarBot.App.Telegram;

/// <summary>eph-12: Telegram user IDs allowed to run the /status admin command.</summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public long[] UserIds { get; set; } = Array.Empty<long>();

    public bool IsAdmin(long userId) => UserIds.Contains(userId);
}
