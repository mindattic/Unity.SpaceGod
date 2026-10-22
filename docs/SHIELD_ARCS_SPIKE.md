# Shield arc selection: spike findings (U.SPC-T244)

Question: shield arcs are chosen by which part of the hull was hit, not the direction the shot came from.
Should a hit land on the arc facing the incoming shot?

## How an arc is chosen today

Every path asks `ShipCombat.GeneratorFor(yaw)` where `yaw` is the bearing of a *hit point* from the hull's own
bow (`BattleUnit.YawDegOf(at)`, or the same atan2 on a local point in `Collisions.ApplyDamage`).
What differs is what the hit point is:

| Source | Hit point `at` | Arc chosen | Matches incoming direction? |
|---|---|---|---|
| Beams, aimed rounds (`ResolveShot`, `StepAimedRound`) | First entry of the ray into the hull's hit sphere (`FirstHostileAlong`) | The side the shot enters | Yes, by construction |
| Collisions / ramming (`ApplyImpact`) | Contact point | Struck side | Yes |
| Boarding breacher (`~2065`) | `t.Pos + toward-attacker * 0.3 R` | Side facing the attacker | Yes |
| Missiles, torpedoes (`StepMissiles`) | `aim`: the position of the *targeted hardpoint* (`PointOf(TargetSub.Def)`), or hull centre | Arc covering that hardpoint | **No** |
| Boarding pods (`PodHit`) | Same `aim` | Same | **No** |

So the premise holds only for guided munitions. A missile aimed at a starboard engine detonates at the
starboard engine, so it is judged against the starboard arc even when it flew in from the port side
and passed through the port arc on the way. With `aim == t.Pos` (no hardpoint left) the yaw is `atan2(0,0) = 0`,
so the bow arc takes every such hit regardless of direction: a plain bug in the same family.

Notes:
- Missile detonation is a swept test against `aim` with radius `0.6 R + 0.05`, so the warhead is already inside the
  hull volume (radius 0.6 R, deeper than the arc surface) when it goes off. Geometrically it has crossed a shield arc.
- `PickSubsystem` weights a shield generator by whether it still has charge (2.5 vs 0.5), but it is not
  affected by approach side.
- `ApplyDamage` also takes its armour/subsystem split from the targeted `sub`, independent of the arc; that
  part is fine (a missile hunting a specific hardpoint should still hurt it once shields are gone).

## Recommendation

Yes, for missiles, torpedoes and pods: the arc should be the one on the side the munition came in from.
Beams and rounds need no change. Do not change the targeting side: the warhead still homes on its hardpoint
and still damages it once through.

Rule: shield arc = `GeneratorFor(YawDegOf(m.Pos_at_entry))`, with entry taken as the missile's position the step
it first comes within the hull radius (or, simpler and equivalent for a homing round,
`target.Pos + (m.Pos - target.Pos).Normalized * R` evaluated before the final step's overshoot).
If the arc on that side is up and charged, the warhead is soaked there; if collapsed or shot away, it
proceeds to the hardpoint as now.

## Gameplay impact

- Positive: flanking and facing now matter for missiles as they already do for lasers. A ship that keeps its
  strongest or freshest arc toward the enemy, or rolls a damaged arc away, gets rewarded.
- Fixes a quirk where a missile "teleports" its shield check to the far side and can be stopped by (or bypass)
  the wrong arc, and removes the always-bow-arc bug for hull-targeted missiles.
- Balance: missiles that target a hardpoint behind a collapsed arc on the *opposite* side lose their free pass;
  missiles that target a hardpoint behind a full arc on the opposite side lose their (accidental) shield-bypass
  when the near arc is down. Net effect depends on fleet facing; expect a modest nerf to missile-heavy
  fleets against well-oriented ships and a buff to missiles from the weak side. Re-tune `MissileLauncher` damage only if playtests show a swing.
- `PodHit` bounce rule (pods bounce off any charged arc) gets the same fix; pods that approach a collapsed
  arc and aim at a hardpoint behind a charged one will now go through, which is the intent.
- VFX: `FxKind.ShieldHit` takes `at` and the generator index. Using the entry point puts the bubble on the
  side the warhead came from rather than inside the hull on the far side, which looks more correct.

## Code impact (small)

1. `Battle.StepMissiles`: compute `entry` (point on the hull sphere toward `m.Pos`) and pass it as a new
   `shieldAt` argument to `ApplyDamage`/`PodHit`, keeping `aim` for subsystem selection.
2. `Battle.ApplyDamage(target, sub, damage, at)`: add an optional `DVec3? shieldAt = null`; use
   `shieldAt ?? at` for `GeneratorFor` and the `ShieldHit` emit. All other callers unchanged.
3. `PodHit`: use the same entry yaw for the generator check; the `Embed`/`Breach` placement keeps using `at`.
4. Guard the degenerate case (`to == 0`): fall back to the missile's velocity reversed.
5. Tests (Tests/Core.Tests/CombatTests.cs, GunneryTests.cs): a missile aimed at a starboard hardpoint arriving
   from port drains the port generator; with port collapsed it reaches the hardpoint; a hull-targeted missile no
   longer always hits the bow arc.

No changes needed in `Collisions`, `ShipView`, `BattleFx` or the HUD ring. Estimated effort: under half a day.
Follow-up implementation ticket: U.SPC-T380.
