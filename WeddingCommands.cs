using Jotunn.Entities;
using Jotunn.Managers;

namespace WeddingTime
{
    internal static class WeddingCommands
    {
        internal static void Register()
        {
            var commands = CommandManager.Instance;
            commands.AddConsoleCommand(new WeddingTimeCommand());
            commands.AddConsoleCommand(new DoYouMarryMeCommand());
            commands.AddConsoleCommand(new IDoCommand());
            commands.AddConsoleCommand(new IDoNotCommand());
            commands.AddConsoleCommand(new DivorceCommand());
            commands.AddConsoleCommand(new AdminDivorceCommand());
            commands.AddConsoleCommand(new DebugMarriageCommand());
            commands.AddConsoleCommand(new DebugWeddingLevelCommand());
        }
    }

    internal sealed class WeddingTimeCommand : ConsoleCommand
    {
        public override string Name => "wedding_time";
        public override string Help => "Show your Wedding Bond level and shared in-game days remaining until the next level: /wedding_time";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length != 0)
            {
                context?.AddString("Usage: /wedding_time");
                return;
            }

            context?.AddString(WeddingProgression.GetLocalProgressSummary());
        }
    }

    internal sealed class DoYouMarryMeCommand : ConsoleCommand
    {
        public override string Name => "do_you_marry_me";
        public override string Help => "Propose marriage to a nearby player: /do_you_marry_me <player name>";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length == 0)
            {
                context?.AddString("Usage: /do_you_marry_me <player name>");
                return;
            }

            WeddingProposalService.Propose(string.Join(" ", args).Trim(), out var message);
            context?.AddString(message);
        }
    }

    internal sealed class IDoCommand : ConsoleCommand
    {
        public override string Name => "i_do";
        public override string Help => "Accept the pending marriage proposal: /i_do";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length != 0)
            {
                context?.AddString("Usage: /i_do");
                return;
            }

            WeddingProposalService.Respond(true, out var message);
            context?.AddString(message);
        }
    }

    internal sealed class IDoNotCommand : ConsoleCommand
    {
        public override string Name => "i_do_not";
        public override string Help => "Decline the pending marriage proposal: /i_do_not";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length != 0)
            {
                context?.AddString("Usage: /i_do_not");
                return;
            }

            WeddingProposalService.Respond(false, out var message);
            context?.AddString(message);
        }
    }

    internal sealed class DivorceCommand : ConsoleCommand
    {
        public override string Name => "divorce";
        public override string Help => "End your marriage: /divorce, then /divorce confirm. Use /divorce cancel to stop a pending divorce.";

        public override void Run(string[] args, Terminal context)
        {
            var action = args.Length == 0 ? string.Empty : args[0].ToLowerInvariant();
            if (args.Length > 1 || (action != string.Empty && action != "confirm" && action != "cancel"))
            {
                context?.AddString("Usage: /divorce | /divorce confirm | /divorce cancel");
                return;
            }

            string message;
            if (action == "confirm") WeddingDivorceService.Confirm(out message);
            else if (action == "cancel") WeddingDivorceService.Cancel(out message);
            else WeddingDivorceService.Arm(out message);
            context?.AddString(message);
        }
    }

    internal sealed class AdminDivorceCommand : ConsoleCommand
    {
        public override string Name => "wedding_admin_divorce";
        public override string Help => "Admin only: immediately end a player's marriage: wedding_admin_divorce <online player name | player ID>";

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length == 0)
            {
                context?.AddString("Usage: wedding_admin_divorce <online player name | player ID>");
                return;
            }

            WeddingDivorceService.AdminDivorce(string.Join(" ", args).Trim(), out var message);
            context?.AddString(message);
        }
    }
}
