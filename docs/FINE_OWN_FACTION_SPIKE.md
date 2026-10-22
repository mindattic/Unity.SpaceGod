# Fines paid to the player's own faction: spike findings (U.SPC-T245)

Question: a wanted player paying a fine to their own faction moves no money. What should happen to the
player's money, and how should fines work there?

## What happens today

`Law.PayFine(ship, f, ...)` does `ship.Faction.Treasury -= paid; f.Treasury += paid;`. The player has no
separate purse: "the purse" is `ship.Faction.Treasury`, which is `Player.Faction`'s treasury. So purse,
faction treasury and fleet account are already one account, and the three-way choice in the ticket
collapses. When `f == ship.Faction` (a jurisdiction or victim that is the player's own faction posted the
bounty) the debit and credit hit the same number and cancel: the bounty clears, standing resets, the hail
ends, and it costs nothing. Paying off the law is free for that faction's own pilots.

## Decision

- Keep one account. Do not introduce a separate purse or fleet account for this case.
- A fine paid to your own faction is a real cost but has no outside recipient: debit it from the treasury
  and do not credit it (it is burned, as court and administrative costs). The same insufficient-funds rule
  applies (no payment, nothing cleared if the treasury is below the fine).
- Everything else in `PayFine` is unchanged: bounty cleared, standing floor restored, hail dropped, scan
  cooldown set. The log line stays, saying the fine was paid to its own faction's courts.
- Rejected: leaving it free (makes crimes in one's own jurisdiction costless and a free standing reset);
  waiving the bounty outright (no pressure to avoid crime); crediting a notional "state" account (no such
  account exists, and it would only be a second spelling of the same number).

## Follow-up

Implementation is a small change in `Law.PayFine` (credit `f.Treasury` only when `f != ship.Faction`) plus
a LawTests case asserting the treasury falls by the fine; filed as U.SPC-T389.
