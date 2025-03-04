using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;


namespace Content.Server.Administration.Commands
{
    [AdminCommand(AdminFlags.Ban)]
    public sealed class BanCommand : IConsoleCommand
    {
        public string Command => "ban";
        public string Description => "Bans somebody";
        public string Help => $"Usage: {Command} <name or user ID> <reason> [duration in minutes, leave out or 0 for permanent ban] <kick true/false>]";

        public async void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player as IPlayerSession;
            var plyMgr = IoCManager.Resolve<IPlayerManager>();
            var locator = IoCManager.Resolve<IPlayerLocator>();
            var dbMan = IoCManager.Resolve<IServerDbManager>();
            var chatMan = IoCManager.Resolve<IChatManager>();

            string target;
            string reason;
            uint minutes;
            bool kick = false;

            switch (args.Length)
            {
                case 2:
                    target = args[0];
                    reason = args[1];
                    minutes = 0;
                    break;
                case 3:
                    target = args[0];
                    reason = args[1];

                    if (!ParseMinutes(shell, args[2], out minutes))
                    {
                        return;
                    }

                    break;

                case 4:
                    target = args[0];
                    reason = args[1];

                    if (!ParseMinutes(shell, args[2], out minutes))
                    {
                        return;
                    }

                    if(!bool.TryParse(args[3].ToLower(), out kick))
                    {
                        shell.WriteLine($"{args[3]} is not a valid boolean.\n{Help}");
                        return;
                    }

                    break;

                default:
                    shell.WriteLine($"Invalid amount of arguments.{Help}");
                    return;
            }

            var located = await locator.LookupIdByNameOrIdAsync(target);
            if (located == null)
            {
                shell.WriteError("Unable to find a player with that name.");
                return;
            }

            var targetUid = located.UserId;
            var targetHWid = located.LastHWId;
            var targetAddr = located.LastAddress;

            if (player != null && player.UserId == targetUid)
            {
                shell.WriteLine("You can't ban yourself!");
                return;
            }

            DateTimeOffset? expires = null;
            if (minutes > 0)
            {
                expires = DateTimeOffset.Now + TimeSpan.FromMinutes(minutes);
            }

            (IPAddress, int)? addrRange = null;
            if (targetAddr != null)
            {
                if (targetAddr.IsIPv4MappedToIPv6)
                    targetAddr = targetAddr.MapToIPv4();

                // Ban /64 for IPv4, /32 for IPv4.
                var cidr = targetAddr.AddressFamily == AddressFamily.InterNetworkV6 ? 64 : 32;
                addrRange = (targetAddr, cidr);
            }

            var banDef = new ServerBanDef(
                null,
                targetUid,
                addrRange,
                targetHWid,
                DateTimeOffset.Now,
                expires,
                reason,
                player?.UserId,
                null);

            await dbMan.AddServerBanAsync(banDef);

            var until = expires == null
                ? " навсегда."
                : $" до {expires}";

            var response = new StringBuilder($"Забанен {target} по причине \"{reason}\" {until}");

            shell.WriteLine(response.ToString());

            if (!plyMgr.TryGetSessionById(targetUid, out var targetPlayer))
                return;

            var message = $"Вы были забанены по причине \"{reason}\" {until}";
            chatMan.DispatchServerMessage(targetPlayer, message);

            if (kick)
            {
                targetPlayer.ConnectedClient.Disconnect(banDef.DisconnectMessage);
            }
        }

        public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var playerMgr = IoCManager.Resolve<IPlayerManager>();
                var options = playerMgr.ServerSessions.Select(c => c.Name).OrderBy(c => c).ToArray();
                return CompletionResult.FromHintOptions(options, "<name/user ID>");
            }

            if (args.Length == 2)
                return CompletionResult.FromHint("<reason>");

            if (args.Length == 3)
            {
                var durations = new CompletionOption[]
                {
                    new("0", "Permanent"),
                    new("1440", "1 day"),
                    new("10080", "1 week"),
                };

                return CompletionResult.FromHintOptions(durations, "[duration]");
            }

            if (args.Length == 4)
            {
                var kick = new CompletionOption[]
                {
                    new("true", "Kick"),
                    new("false", "Don't kick"),
                };

                return CompletionResult.FromHintOptions(kick, "[kick]");
            }

            return CompletionResult.Empty;
        }

        private bool ParseMinutes(IConsoleShell shell, string arg, out uint minutes)
        {
            if (!uint.TryParse(arg, out minutes))
            {
                shell.WriteLine($"{arg} is not a valid amount of minutes.\n{Help}");
                return false;
            }

            return true;
        }
    }
}
