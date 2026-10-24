using Unity.SpaceGod.Core;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Interactive boarding (U.SPC-T468, docs/BOARDING.md). While piloting in a battle with a fight aboard, the piloted
    /// hull's own (defending) or one its faction's marines are in (boarding), B opens the boarding panel and the number
    /// keys give that side its standing orders over the compartments: 1 picks the next compartment, 2 sends the boarders
    /// for it or musters the defenders on it, 3 has them hold where they stand, 0 hands the side back to the resolver
    /// (the auto-resolve). The fight in each compartment is the resolver's weighted roll as ever. A hail still has the
    /// number keys first.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        /// <summary>The boarding panel is open (B, at the controls).</summary>
        public bool ShowBoarding { get; set; }

        /// <summary>The fight aboard the player can command, and on which side, as of the last battle frame (null for none).</summary>
        public (BattleUnit hull, BoardingRole role)? BoardingFight { get; private set; }

        /// <summary>The compartment picked on the boarding panel (-1 for none yet).</summary>
        public int BoardingPick { get; private set; } = -1;

        /// <summary>Whether the panel is up with a fight to command.</summary>
        public bool BoardingPanelOpen => ShowBoarding && IsPiloting && BoardingFight != null && BoardingFight.Value.hull.Combat.Interior.UnderBoarding;

        void UpdateBoardingCommand(Battle battle, BattleUnit unit)
        {
            var fight = BoardingCommand.FightFor(battle, unit);
            if (fight?.hull != BoardingFight?.hull) BoardingPick = -1;
            BoardingFight = fight;
            if (Input.ToggleBoarding)
            {
                if (fight == null) ShowToast("No fight aboard to command");
                ShowBoarding = fight != null && !ShowBoarding;
            }
            if (fight == null)
            {
                ShowBoarding = false;
                return;
            }
            if (!ShowBoarding) return;
            // A hail is answered first.
            if (unit.CargoDemandedBy != null || battle.AwaitingTermsFrom(unit.Side) != null) return;
            int key = Input.NumberKey;
            if (key < 0) return;
            var (hull, role) = fight.Value;
            var inside = hull.Combat.Interior;
            switch (key)
            {
                case 1:
                    BoardingPick = BoardingCommand.NextObjective(inside, BoardingPick);
                    if (BoardingPick >= 0) ShowToast("Picked: " + inside.Compartments[BoardingPick].Name);
                    break;
                case 2:
                    if (BoardingPick < 0) BoardingPick = BoardingCommand.NextObjective(inside, BoardingPick);
                    if (inside.Command(role, BoardingOrder.Push, BoardingPick)) ShowToast(Side(role) + BoardingCommand.Label(inside, role));
                    break;
                case 3:
                    if (inside.Command(role, BoardingOrder.Hold)) ShowToast(Side(role) + BoardingCommand.Label(inside, role));
                    break;
                case 0:
                    inside.AutoResolve(role);
                    ShowToast(Side(role) + BoardingCommand.Label(inside, role));
                    break;
            }
        }

        static string Side(BoardingRole role) => role == BoardingRole.Boarders ? "Our boarders: " : "Our marines: ";
    }
}
