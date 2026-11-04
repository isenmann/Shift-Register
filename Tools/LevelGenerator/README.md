# Level generator

Design harness for Shift Register levels. It compiles the game's core (`Assets/Scripts/Core`)
outside Unity and prints candidate levels with their solver par and solution. It is separate from
the RANDOM levels in the game, which `Assets/Scripts/Core/LevelGenerator.cs` makes at run time.

```
cd Tools/LevelGenerator
dotnet run -c Release -- 3      # chapter 3 (also 4, 5, 6; no argument = all)
```

- `Find(...)` scrambles a target picture with random lossless moves (always solvable) and keeps
  the first seed whose par is in range and whose solution uses the chapter's mechanic.
- `Forward(...)` builds a target from a start and an intended solution (gates, irreversible rules).
- Pictures: `#` bit, `X` wall, `P` pinned bit, `.` empty.
- Picked levels are written to `picked<chapter>.txt` (git-ignored); copy them into
  `Assets/Levels/ChapterN/*.json`, add them to `LevelCatalog.asset`, then run
  **Shift Register > Validate and Solve Levels** in Unity.
