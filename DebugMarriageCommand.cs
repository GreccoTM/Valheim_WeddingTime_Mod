using Jotunn.Entities;

namespace WeddingTime
{
    internal sealed class DebugMarriageCommand : ConsoleCommand
    {
        public override string Name => "wedding_debug_married";
        public override string Help => "Toggle the local marriage state for testing: wedding_debug_married on|off";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length != 1 || (args[0] != "on" && args[0] != "off"))
            {
                context?.AddString("Usage: wedding_debug_married on|off (requires devcommands). Equip the Utility-slot Wedding Ring first.");
                return;
            }

            var enabled = args[0] == "on";
            WeddingBond.SetDebugMarriage(enabled);
            context?.AddString(enabled
                ? "Debug marriage enabled. With the Wedding Ring equipped, the temporary level 1 Wedding Bond should appear."
                : "Debug marriage disabled; the temporary effect has been removed.");
        }
    }

    internal sealed class DebugWeddingLevelCommand : ConsoleCommand
    {
        public override string Name => "wedding_debug_level";
        public override string Help => "Preview a Wedding Bond level locally: wedding_debug_level 1-10|off";
        public override bool IsCheat => true;

        public override void Run(string[] args, Terminal context)
        {
            if (args.Length != 1)
            {
                context?.AddString("Usage: wedding_debug_level 1-10|off (requires devcommands).");
                return;
            }

            if (args[0].Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                WeddingProgression.SetDebugLevel(0);
                context?.AddString("Wedding Bond level preview disabled; using the saved progression.");
                return;
            }

            if (!int.TryParse(args[0], out var level) || level < 1 || level > 10)
            {
                context?.AddString("Usage: wedding_debug_level 1-10|off (requires devcommands).");
                return;
            }

            WeddingProgression.SetDebugLevel(level);
            context?.AddString($"Wedding Bond preview set to level {level}; this does not change saved progression.");
        }
    }
}
