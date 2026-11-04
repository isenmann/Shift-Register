# Level format

One JSON file per level, listed in play order by `LevelCatalog.asset`.
After editing levels run **Shift Register > Validate and Solve Levels**: it checks every level,
solves it, and writes `par` and `solution` back into the file (nothing else in the file changes).

Rows are numbers 0–255, row 0 is the top row, and the leftmost cell is 128
(`[0, 102, 255, ...]`: row 1 = `01100110`). Row indices in lists are 0-based.
Only `start` and `target` are required; everything else defaults to off.

| Field | Meaning |
| --- | --- |
| `id`, `name` | Shown as "2-3 · HEART". Ids must be unique. |
| `start`, `target` | 8 row numbers each. |
| `walls` | 8 row numbers marking wall cells. Walls split rows and columns into parts that shift on their own. |
| `pins` | 8 row numbers marking pinned bits (also set in `start` and `target`). Locks their row and column. |
| `rotateRows` | Rows that wrap around, e.g. `[2, 5]`. |
| `chainTopRows` | `[r]` chains rows r and r+1 into one 16-bit register. |
| `fillLeftRows`, `fillRightRows` | Rows with a 1-IN edge on that side. |
| `gates` | `{ "row": 1, "op": 1, "mask": 128 }`, applied in order after the row moves. `op`: 0 AND, 1 OR, 2 XOR, 3 NOT pad. |
| `gatesOnColumnMoves` | `true`: gates also apply to rows a column move passes through. |
| `lossRule`, `lossCount` | `lossRule` 0 any loss, 1 at most `lossCount` bits ("lose 0 bits"), 2 exactly `lossCount` bits. |
| `par`, `solution` | Written by the tool. |

## Player levels

Levels from the in-game editor (MY LEVELS) use the same format but are not files here. They live in
PlayerPrefs under `ShiftRegister.CustomLevels.v1` with ids starting `custom-`, and never count toward
stars or unlocks. A level is proven either by the solver (par is the true minimum) or by beating it in
test play (par is the best solve so far).

Share codes are `SR1-` plus base64url bytes (`Assets/Scripts/Core/ShareCode.cs`). A code carries its
solution, which is replayed on import, so a code that does not solve its own level is refused. Authored
levels also keep to one gate per row and no gate on a 1-IN cell (`LevelEdit.AuthoredErrors`), because
the board draws only one rule per cell. Built-in levels keep to this too; the rules do not enforce it.
