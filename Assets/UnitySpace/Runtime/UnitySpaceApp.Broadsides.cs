using Unity.SpaceGod.Core;

namespace Unity.SpaceGod
{
    /// <summary>
    /// The broadside game at a capital ship's helm (U.SPC-T425, see <see cref="Broadsides"/>). The trigger still calls the
    /// volley on the side the player is looking (U.SPC-T272); the number keys give the captain's orders, each to the arc
    /// the player is looking toward where it takes one: 1 that arc's battery to its next order (at will, hold fire,
    /// volley), 2 the shields angled to that arc (again to spread them evenly), 3 brace, 4 damage control's next focus,
    /// 5-8 the line into line ahead, line abreast, echelon or a screen, 9 engage at will, 0 every battery back to fire at will.
    /// The number keys answer a hail first, while one is open, then the boarding panel (B), while that is open.
    /// </summary>
    public sealed partial class UnitySpaceApp
    {
        void UpdateHelmCommand(Battle battle, BattleUnit unit)
        {
            if (PilotControlMode != PilotControl.Helm || !Broadsides.Commands(unit)) return;
            if (unit.CargoDemandedBy != null || battle.AwaitingTermsFrom(unit.Side) != null) return;
            // The boarding panel, while open, has the number keys (U.SPC-T468).
            if (BoardingPanelOpen) return;
            int key = Input.NumberKey;
            if (key < 0) return;
            var look = PilotLookDirection;
            var arc = Facings.Toward(unit, unit.Pos + look);
            var cmd = Broadsides.Of(unit);
            switch (key)
            {
                case 1:
                    var order = Broadsides.CycleOrder(unit, arc);
                    ShowToast(Cap(Facings.Label(arc)) + " battery: " + Broadsides.Label(order));
                    break;
                case 2:
                    var trim = Broadsides.TrimShields(unit, cmd.ShieldTrim == arc ? (Facing?)null : arc);
                    ShowToast(trim == null ? "Shields spread evenly" : "Shields angled to " + Facings.Label(trim.Value));
                    break;
                case 3:
                    ShowToast(Broadsides.Brace(unit) ? "Brace, brace, brace!" : "The crew is still getting back to their stations");
                    break;
                case 4:
                    ShowToast("Damage control: " + Broadsides.Label(Broadsides.CycleFocus(unit)) + " first");
                    break;
                case 5: OrderLine(battle, unit, FleetFormation.LineAhead); break;
                case 6: OrderLine(battle, unit, FleetFormation.LineAbreast); break;
                case 7: OrderLine(battle, unit, FleetFormation.Echelon); break;
                case 8: OrderLine(battle, unit, FleetFormation.Screen); break;
                case 9: OrderLine(battle, unit, FleetFormation.EngageAtWill); break;
                case 0:
                    Broadsides.SetAll(unit, BatteryOrder.FireAtWill);
                    ShowToast("All batteries: fire at will");
                    break;
            }
        }

        void OrderLine(Battle battle, BattleUnit flag, FleetFormation formation)
        {
            int heard = Broadsides.OrderFormation(battle, flag, formation);
            ShowToast(heard == 0 ? "No ships of the line to order" : "The line (" + heard + "): " + Broadsides.Label(formation));
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
