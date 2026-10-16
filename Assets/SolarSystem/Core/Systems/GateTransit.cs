using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// The one place a ship ever crosses between the two independent per-system simulations. Each
    /// system runs its own <see cref="World"/> (own bodies, factions, economy, war) with nothing
    /// shared between them, and every ship's id is a fixed index into its own fleet's list (ids are
    /// never reused or shuffled -- a destroyed hull stays in place, see <see cref="Ship.Destroyed"/>),
    /// so a ship can't simply move from one fleet's list to another's without desyncing every id after
    /// it. Instead the origin hull is retired in place (<see cref="Ship.Destroyed"/> and
    /// <see cref="Ship.GateDeparted"/>, so nothing draws a wreck for it) and a copy
    /// (<see cref="Ship.CloneForGateArrival"/>) is appended fresh to the destination's fleet, parked at
    /// the far gate -- indistinguishable to the player from the same ship carrying on. Anything it was
    /// mid-way through at home (a trade route, a patrol, a hyperlane ride) meant nothing there either,
    /// built from body indices that belong to a different catalogue entirely, so it arrives a free
    /// agent, able to wander and dock same as any independent ship (see <see cref="World.AddFreeShips"/>).
    /// </summary>
    public static class GateTransit
    {
        /// <summary>
        /// Sends <paramref name="ship"/> through <paramref name="gate"/> if it is close enough and the
        /// gate is paired: retires it in <paramref name="origin"/> and hands back the arriving copy
        /// already living in the destination system's fleet. Returns false (no-op, <paramref name="arrived"/>
        /// null) otherwise.
        /// </summary>
        public static bool TryPerform(JumpGate gate, DVec3 shipHelioKm, Ship ship, Fleet origin, double jd, out Ship arrived)
        {
            arrived = null;
            if (gate == null || ship == null || origin == null || !gate.CanActivate(shipHelioKm)) return false;

            var destination = gate.Destination;
            ship.Destroyed = true;
            ship.GateDeparted = true;

            var next = ship.CloneForGateArrival();
            next.Role = ShipRole.Free;
            next.Ports = destination.World.FreeShipPorts();

            var forward = gate.ArrivalForward;
            var up = Math.Abs(forward.Z) < 0.9 ? DVec3.UnitZ : DVec3.UnitX;
            var right = DVec3.Cross(forward, up).Normalized;
            next.Orbit = new ParkingOrbit
            {
                Body = destination.Catalog.Root.Index,
                RadiusKm = 0,
                Center = gate.ArrivalOffsetKm,
                U = right,
                V = forward,
                EpochJd = jd,
                PhaseAtEpoch = 0,
                RateRadPerDay = 0,
                Fixed = true,
            };
            next.Phase = ShipPhase.Parked;
            next.PhaseStartJd = jd;

            destination.World.Fleet.Receive(next);
            arrived = next;
            return true;
        }
    }
}
