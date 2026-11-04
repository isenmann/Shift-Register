# Shared levels page

A public page where players upload Shift Register share codes and browse everyone's levels. It runs on Cloudflare
Workers with a D1 database, on the free plan. Only the code is stored, plus a key that spots repeated puzzles. The
browser works out names, boards and par from the code when the page opens.

| Path | What it is |
| --- | --- |
| `public/` | The page, served as it is. `sharecode.js` is shared with the Worker. |
| `src/worker.js` | `GET /api/levels` lists the codes, `POST /api/levels` adds one (the body is the code). |
| `schema.sql` | The database table. |
| `test/` | `vectors.json` holds the verdicts of the game's C# code, made by `Tools/ShareCodeVectors`. |

The game's C# code is the reference for share codes. After changing `Core/ShareCode.cs` or the rules, rerun
`dotnet run` in `Tools/ShareCodeVectors`, then `npm test`.

## Run it

Tested with Node 26. Nothing to install: `npx` fetches Wrangler, Cloudflare's tool, when needed.

```sh
npm test       # no network or account needed
npm run dev    # local copy with a local database, at http://localhost:8787
```

## Put it online

1. In `public/index.html`, replace `ITCH_URL` with the game's itch.io address.
2. Sign in and create the database, then copy the `database_id` it prints into `wrangler.jsonc`:

   ```sh
   npx wrangler@4 login
   npx wrangler@4 d1 create shift-register-levels
   ```

3. Create the table and publish. The last command prints the page's address:

   ```sh
   npx wrangler@4 d1 execute shift-register-levels --remote --file=schema.sql
   npm run deploy
   ```

The database id is not a secret, so it can be committed.

## Look after it

Limits: 5 uploads a minute per visitor (`wrangler.jsonc`) and 2000 levels in total (`MAX_LEVELS` in `src/limits.js`).

Remove a level, for example one with a rude name:

```sh
npx wrangler@4 d1 execute shift-register-levels --remote --command "DELETE FROM levels WHERE code = 'SR1-...'"
```

To empty the page after a flood of uploads, use `DELETE FROM levels`.
