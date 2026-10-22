# Adventure rewards: who pays? Spike findings (U.SPC-T283)

Question: `EventDirector.Pay` does `purse.Treasury += r.Credits` and takes the credits from nobody, although the
code comments say escape-pod and derelict-survivor rescues are "paid by their owners" and a patron pays.
Should the patron/owners pay (capped at what they hold), or stay new money?

## Today

- `Pay` credits the engaged faction only. Money is created.
- The parallel path, `Crews` pod rescue (`Crews.cs` ~288), already does it properly:
  `min(people * RescueRewardPerHead, owners.Treasury)` is debited from the owners and credited to the rescuer.
  So the same rescue pays differently depending on which system resolves it.
- Rewards carry `ReputationWith` = the patron for stranded ship, derelict, escape pod, pirate-hunter bounty,
  hauling and law opportunities. Anomaly and some fixed contracts have no patron.

## Balance impact

Rewards run 25k to 300k credits (survivors 15k each, pods per head). Faction treasuries start at
`SocietySpec.Treasury = 4e8`, and pirate clans hold 1e7-1e8. A reward is therefore at most about 0.1% of a
society's purse; debiting it is invisible to the AI economy. Only a poor patron (a depleted pirate hunter
clan, a faction after wars) would notice, and the cap handles that case. The player's income is unchanged
except when the patron is broke, where the payout shrinks (the same behaviour as pod rescue today).
Money creation of this size is not currently a measurable inflation source either; the value of fixing it is
consistency and a closed money supply, not balance.

## Decision

1. Rewards with a patron (`ReputationWith != null` and the patron is not the payee and not the player's own
   faction) are **paid by the patron, capped at its treasury**: `paid = min(Credits, patron.Treasury)`,
   debited from the patron, credited to the purse, and `op.Reward.Credits` rewritten to `paid` so the outcome
   text shows what actually moved (as `CargoCrates` already is).
2. Rewards with no patron (anomaly survey, fixed contracts) stay **new money**: they stand for the player's
   survey data or the galaxy at large, and there is nobody to charge.
3. Pirate patrons (`IsPirate`) pay like anyone else, from their clan purse, so ransoms and bounties are not
   free either.
4. Reuse one helper with `Crews` rescue so the two paths cannot drift again.

## Follow-up (not done in this spike)

Implement the debit in `EventDirector.Pay`, add a test that the patron's treasury falls by the reward and that
a broke patron pays only what it holds, and that a patronless reward leaves every other treasury untouched.
