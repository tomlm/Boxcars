# Engine vs railroad purchase value

Status: analysis only, not scheduled. Goal: purchase recommendations (purchase bar and bots) that include engine
upgrades, with a fair comparison between buying a better engine and buying a railroad.

## Today

- `PurchaseRecommendationService` scores railroads only (Blended, Access, Connectivity, Monopoly, Strategy, LowPrice,
  TollBooth). Engines are never recommended.
- Bots see "Buy Express" / "Buy Superchief" as legal options, but the no-AI fallback only ever picks a railroad.

## Measure

Compare every purchase in the same unit: **extra cash per turn**, and **payback turns = price / extra cash per turn**.
Recommend whichever pays back fastest, provided the payback fits in the game left (estimate from how far the leader is
from winning cash; a player takes roughly 55-60 turns in a game).

## Simulation 1: planned trips (`PurchaseValueSimulation`)

Samples trips with the map's odds and prices them with the route planner. Unsold railroads are shared out among all
four players ("eventual" world); buying a railroad moves it from an opponent to the buyer. Income per turn =
(payoff - fees) / turns per trip + fees opponents pay the buyer.

**Flawed for engines**: each trip starts fresh, so it misses
1. bonusing out of an unfriendly destination (the next trip's first turn on an opponent's line), and
2. movement wasted on arrival (counted turns as dots / average roll, as if leftover movement carried over).

Both undervalue faster engines; it put Superchief's payback at ~35 turns. It also overrated some railroads
(N&W: 12.5 turns here, ~never in simulation 2). Useful mainly as a fast-ish railroad screen.

## Simulation 2: played turns (`PlayedPurchaseValueSimulation`)

Plays real turns for all four players: real dice, route planner, bonus moves after arrival, lay-ups, real fees,
purchases declined, winning cash out of reach. Ownership from game 4184. 400 turns of "Me" per variant; each figure
is uncertain by roughly +/-$500 (differences +/-$700). 7-13 minutes per variant (variants run in parallel).

"Trip net" = payoffs - fees paid by Me (independent of fees collected, which are the noisiest number).

| World | Engine | Trip net / turn | Gain vs Freight | Turns / trip | Arrivals that bonus on |
|---|---|---|---|---|---|
| Early (20 of 28 unsold) | Freight | $2,622 | | 3.51 | 3% |
| | Express | $3,229 | +$607 | 2.94 | 18% |
| | Superchief | $5,436 | **+$2,814** | 2.45 | 69% |
| Mid (9 unsold) | Freight | $2,556 | | 3.36 | 3% |
| | Express | $2,583 | +$27 | 3.48 | 18% |
| | Superchief | $4,427 | **+$1,871** | 2.41 | 66% |
| All sold | Freight | -$2,174 | | 3.81 | 4% |
| | Express | -$2,546 | -$372 | 3.31 | 17% |
| | Superchief | -$476 | **+$1,698** | 2.48 | 69% |

Railroads in the all-sold world (Freight, total cash per turn including fees collected, vs an opponent owning it):

- Buy NP ($14k): **+$1,705/turn, payback ~8 turns**
- Buy N&W ($12k): ~$0/turn, never pays back

### Findings

- **Superchief** pays back in ~14 turns early, ~21 mid, ~24 all sold. About 70% of its arrivals bonus straight into
  the next trip, so trips are a full turn shorter, and it pays fewer fees at unfriendly destinations. It beats most
  railroads; only the best railroad on offer (NP here) beats it.
- **Express** adds little: it only bonuses on doubles (1 in 6). Its gain (+$600 early, ~0 later) is within the noise.
- **Railroads vary enormously**: the right one pays back in under 10 turns, others never do.
- Superchief's gain fits **~$1,700 + $1,100 x (share of railroads unsold)** per turn, within ~$200 in all three worlds.

## Proposed feature (not started)

1. **Engine pick** on the purchase bar: Superchief when affordable and it keeps the cash reserve, with a reason like
   "+$2,500/turn, pays back in ~16 turns"; preferred over railroads unless one pays back sooner, and only if the payback
   fits in the game left. Express only as a labelled small gain when nothing better is on offer.
2. **Railroads scored in $/turn** too, so the comparison is fair. Needs a fast estimate calibrated against simulation 2
   on 8-10 railroads; access gain alone won't separate NP from N&W.
3. **Bots without the AI** fall back to the same pick.

## Running the simulations

Both are `[Fact(Skip = ...)]` in `tests/Boxcars.Engine.Tests/Unit/`. Remove the `Skip` and run, e.g.

```
dotnet test tests/Boxcars.Engine.Tests --filter "FullyQualifiedName~PlayedPurchaseValueSimulation" --logger "console;verbosity=detailed"
```

`PlayedPurchaseValueSimulation` reads `SIM_TURNS` (default 500) for turns per player; 10 makes a quick smoke test.
