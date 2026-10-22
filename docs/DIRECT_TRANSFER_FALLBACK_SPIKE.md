# Direct-transfer fallback: spike findings (U.SPC-T246)

Question: a slow boat planned for a direct transfer orbit across systems (`Ship.PlannedDirect`) can fail to
solve it at departure and then flies a costly straight flip-and-burn instead of taking the hyperlanes.
Should it re-plan (hyperlanes, or a later window), and what does that cost?

## What happens today

1. `PlanWindow` (via `EstimateTrip(..., out direct)`) decides a conic straight across beats the lanes' tolls,
   sets `PlannedConicDepartJd`/`PlannedConicDays` and `PlannedDirect = true`.
2. At the window, `Depart` reads `direct = PlannedDirect && UsesConics(ship)`, clears the flag, and skips
   `Lanes.Route` (so `ship.Journey` stays null). It calls `Burn(..., allowConic: true)` straight to the destination.
3. `Burn` plans the flip-and-burn first, then `TryConic` weighs a conic against it. `TryConic` returns null when
   `HohmannDaysFor` is not positive, when the planned window judged the burn the better buy, when no solved
   flight time beats the flip-and-burn's `LegCost`, when `SolveConic` fails at the real (drifted) departure,
   or when the arc is not `Clear` of the bodies on the way.
4. On null, the flip-and-burn stands. It was only ever the comparison baseline; for a cross-system leg it is
   the most expensive way to cross (delta-v ~ 2*sqrt(a*d) plus gravity loss), and the lanes were skipped,
   so the lane route that the planner priced as the alternative is never considered.

## Can it happen?

Yes: the window is planned on estimates (`BestConic`, coarse grid, `earliestJd + wait`) and re-solved at
departure with the live orbit; `Clear` is only checked then, and `Depart`'s own `allowConic` pass solves
for exactly the tabled flight times. Any mismatch (a moved body, a clearance failure, a parking-orbit phase
that no longer closes) drops it to the flip-and-burn.

## Recommendation

Yes, fall back to the hyperlanes, not to a later window.

- The planner already priced both: the lanes were the runner-up. Using them costs the tolls plus ramp burns
  the planner accepted as the alternative, bounded and known, and is deterministic.
- Re-planning a later window needs the window search again (`PlanWindow`, up to `MaxWindowWaitDays` of idling
  with the hull's `TimeValueOf` ticking) and can fail the same way; it risks a loop. Not worth it.
- A flip-and-burn across a system is the one outcome the planner never intended.

Rule: in `Fleet.Depart`, when `direct` was set and, after the `Burn` returns, `ship.Conic == null`, and a
lane `Route` exists, set `ship.Journey` to that route and `Burn` again to the first hop's ramp (as the
non-direct path does). If no route exists the flip-and-burn stands.

## Cost

- Code: about 10 lines in `Depart`, one extra `Plan` pass in the rare failure case; refund nothing (propellant is
  charged in `Burn` via `ChargePropellant`, so the discarded first plan must be refunded with `RefundUnflown`,
  or the re-plan done before the charge: split the charge out or call `Burn` once with a flag).
- Gameplay: the affected hull pays lane tolls and ramp time instead of a large unplanned propellant bill (may
  strand a low tank), so it matches what its trip estimate already told its owner.
- Test: a Fleet test forcing `TryConic` to fail (a tiny keep-out clearance or a zero Hohmann case) on a
  `PlannedDirect` cross-system ship and asserting `Journey != null` / `Phase == Lane` after the ramp.

Follow-up: U.SPC-T390.
