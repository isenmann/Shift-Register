import { decodeShareCode, puzzleKey } from "../public/sharecode.js";
import { MAX_BODY_LENGTH, MAX_LEVELS } from "./limits.js";

export default {
  async fetch(request, env) {
    if (new URL(request.url).pathname !== "/api/levels") {
      return reply(404, { error: "NotFound" });
    }

    if (request.method === "GET") {
      return list(env);
    }

    return request.method === "POST" ? upload(request, env) : reply(405, { error: "NotAllowed" });
  }
};

async function list(env) {
  const { results } = await env.DB.prepare("SELECT code FROM levels ORDER BY rowid DESC").all();
  return reply(200, { codes: results.map(row => row.code) });
}

async function upload(request, env) {
  const visitor = request.headers.get("CF-Connecting-IP") ?? "unknown";
  const { success } = await env.UPLOADS.limit({ key: visitor });
  if (!success) {
    return reply(429, { error: "TooFast" });
  }

  if (Number(request.headers.get("Content-Length")) > MAX_BODY_LENGTH) {
    return reply(400, { error: "NotACode" });
  }

  const body = await request.text();
  if (body.length > MAX_BODY_LENGTH) {
    return reply(400, { error: "NotACode" });
  }

  // The browser checked this too, but anyone can send anything to this address.
  const { code, level, error } = decodeShareCode(body);
  if (error) {
    return reply(400, { error });
  }

  const puzzle = puzzleKey(level);
  const { meta } = await env.DB.prepare(
    `INSERT INTO levels (code, puzzle)
     SELECT ?1, ?2 WHERE (SELECT COUNT(*) FROM levels) < ?3
     ON CONFLICT (puzzle) DO NOTHING`
  ).bind(code, puzzle, MAX_LEVELS).run();
  if (meta.changes > 0) {
    return reply(201, { code });
  }

  const first = await env.DB.prepare("SELECT code FROM levels WHERE puzzle = ?1").bind(puzzle).first();
  return first ? reply(409, { error: "Duplicate", code: first.code }) : reply(503, { error: "Full" });
}

function reply(status, body) {
  return Response.json(body, { status });
}
