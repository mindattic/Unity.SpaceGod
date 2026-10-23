namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// The cockpit of a launched fighter or bomber, as an <see cref="IEmbodiable"/> (U.SPC-T422): one of a carrier's
    /// squadron, taken mid-battle and flown by the player (the X-wing run on the dreadnought). Taken, its pilot lets go:
    /// it turns at its own rate toward the stick, holds the throttle's share of its top speed, fires its guns down the
    /// crosshair and its torpedoes at the hull nearest the nose (<see cref="FighterAI.FlyManned"/>), and every gunner on
    /// the other side shoots at it for real. Its wingmen keep flying on it if it led them. Handed back, its pilot flies
    /// on from wherever it was left, picking a run of its own.
    /// </summary>
    public sealed class FighterSeat : IEmbodiable
    {
        public Battle Battle { get; }
        public BattleUnit Unit { get; }

        public FighterSeat(Battle battle, BattleUnit unit)
        {
            Battle = battle;
            Unit = unit;
        }

        public string Name
        {
            get
            {
                var f = Unit.Fighter;
                if (f == null) return Unit.Name;
                string craft = f.Bomber ? "bomber" : "fighter";
                return f.Carrier != null ? f.Carrier.Name + " " + craft + " " + (f.Squadron + 1) + "-" + (f.Slot + 1) : craft;
            }
        }

        public EmbodimentKind Kind => EmbodimentKind.Fighter;
        public Faction Faction => Unit.Faction;
        public IEmbodiable Host => null;
        public bool CanEmbody => Blocker == null;
        public bool Embodied => Battle.Flown.ContainsKey(Unit);

        public string Blocker
        {
            get
            {
                if (Unit.Kind != UnitKind.Fighter || Unit.Fighter == null) return "not a launched craft";
                if (Battle.Over) return "the fight is over";
                if (Unit.Dead) return "shot down";
                if (Unit.Docked) return "back aboard its carrier";
                if (!Unit.Present) return "gone";
                if (Unit.Captured) return "taken as a prize";
                return null;
            }
        }

        public EmbodimentPose PoseAt(double jd)
        {
            Unit.Basis(out _, out var up);
            return new EmbodimentPose(Battle.Body, Unit.Pos + Battle.Anchor, Unit.Forward, up);
        }

        /// <summary>The pilot lets go: the stick is the player's, holding the heading and speed the craft had.</summary>
        public void TakeCharge()
        {
            double top = Unit.Combat.Spec.MaxSpeed;
            Battle.Flown[Unit] = new FighterControl { Aim = Unit.Forward, Throttle = top > 0 ? System.Math.Min(1, Unit.Vel.Length / top) : 0 };
            Unit.PlayerControlled = true;
            Unit.PlayerFireHeld = false;
            Unit.PlayerSecondaryHeld = false;
            Unit.PlayerLockTarget = null;
            Unit.PlayerAim = Unit.Forward;
        }

        /// <summary>Stick, throttle (-1..1 eases it off or opens it up; it holds where it is left) and triggers.</summary>
        public void Control(in EmbodimentInput input, double dt)
        {
            if (!Battle.Flown.TryGetValue(Unit, out var stick)) return;
            if (input.Aim.LengthSquared > 1e-12) stick.Aim = input.Aim.Normalized;
            stick.Throttle = System.Math.Max(0, System.Math.Min(1, stick.Throttle + input.Throttle * ThrottlePerSecond * dt));
            stick.Secondary = input.Secondary;
            Unit.PlayerFireHeld = input.Primary;
            Unit.PlayerAim = stick.Aim;
        }

        /// <summary>How fast a held throttle key opens or closes the throttle: full range in two seconds.</summary>
        public const double ThrottlePerSecond = 0.5;

        /// <summary>Its pilot takes the stick back and flies a run of its own from here.</summary>
        public void HandBack()
        {
            Battle.Flown.Remove(Unit);
            Unit.PlayerControlled = false;
            Unit.PlayerFireHeld = false;
            Unit.PlayerSecondaryHeld = false;
            Unit.PlayerLockTarget = null;
            var f = Unit.Fighter;
            if (f == null) return;
            // Nothing snaps back to a stale run: a fresh approach from where it is, on a target it picks now.
            if (f.Phase != FighterPhase.Return)
            {
                f.Phase = FighterPhase.Approach;
                f.PhaseTime = 0;
            }
            f.RunEntry = DVec3.Zero;
            Unit.Target = null;
        }

        public override bool Equals(object obj) => obj is FighterSeat s && s.Unit == Unit && s.Battle == Battle;
        public override int GetHashCode() => Unit.GetHashCode();
    }
}
