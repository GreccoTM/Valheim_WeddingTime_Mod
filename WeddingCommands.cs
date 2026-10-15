using System;
using Jotunn.Entities;

namespace WeddingTime
{
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

            var targetName = string.Join(" ", args).Trim();
            if (WeddingProposalService.Propose(targetName, out var message))
            {
                context?.AddString(message);
            }
            else
            {
                context?.AddString(message);
            }
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
}
