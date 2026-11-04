import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { beforeEach, test } from "node:test";
import { MAX_LEVELS } from "../src/limits.js";
import * as workerModule from "../src/worker.js";
import { RIGHT_ROW_1, makeCode } from "./make-code.js";

const worker = workerModule.default;

/** The real schema and SQL on a real SQLite, behind the few D1 calls the Worker makes. */
function fakeD1() {
  const database = new DatabaseSync(":memory:");
  database.exec(readFileSync(new URL("../schema.sql", import.meta.url), "utf8"));
  return {
    raw: database,
    prepare(sql) {
      const statement = database.prepare(sql);
      const bound = (...values) => ({
        all: async () => ({ results: statement.all(...values) }),
        first: async () => statement.get(...values) ?? null,
        run: async () => ({ meta: { changes: Number(statement.run(...values).changes) } })
      });
      return { ...bound(), bind: bound };
    }
  };
}

const twoRight = [RIGHT_ROW_1, RIGHT_ROW_1];
const CODE_A = makeCode({ moves: twoRight, name: "FIRST" });
const CODE_A_RENAMED = makeCode({ moves: twoRight, name: "COPYCAT" });
const CODE_B = makeCode({ target: 0x10, moves: [...twoRight, RIGHT_ROW_1], name: "SECOND" });

let env;
beforeEach(() => {
  env = { DB: fakeD1(), UPLOADS: { limit: async () => ({ success: true }) } };
});

const send = (method, body, path = "/api/levels") =>
  worker.fetch(new Request(`https://example.test${path}`, { method, body }), env);

async function expectReply(promise, status, body) {
  const response = await promise;
  assert.equal(response.status, status);
  assert.deepEqual(await response.json(), body);
}

test("the main module exports only its handler, or Cloudflare refuses to start it", () => {
  assert.deepEqual(Object.keys(workerModule), ["default"]);
});

test("an empty list, then uploads newest first, without the whitespace of the paste", async () => {
  await expectReply(send("GET"), 200, { codes: [] });
  await expectReply(send("POST", ` ${CODE_A.slice(0, 9)}\n${CODE_A.slice(9)} `), 201, { code: CODE_A });
  await expectReply(send("POST", CODE_B), 201, { code: CODE_B });
  await expectReply(send("GET"), 200, { codes: [CODE_B, CODE_A] });
});

test("the same puzzle again stays out, and the first upload keeps its name", async () => {
  await send("POST", CODE_A);
  await expectReply(send("POST", CODE_A_RENAMED), 409, { error: "Duplicate", code: CODE_A });
  await expectReply(send("POST", CODE_A), 409, { error: "Duplicate", code: CODE_A });
  await expectReply(send("GET"), 200, { codes: [CODE_A] });
});

test("the server does its own check and stores nothing bad", async () => {
  await expectReply(send("POST", "hello bits"), 400, { error: "NotACode" });
  await expectReply(send("POST", CODE_A.slice(0, -3)), 400, { error: "Damaged" });
  await expectReply(send("POST", makeCode({ moves: [RIGHT_ROW_1] })), 400, { error: "BadSolution" });
  await expectReply(send("POST", CODE_A + " ".repeat(3000)), 400, { error: "NotACode" });
  await expectReply(send("GET"), 200, { codes: [] });
});

test("too many uploads are refused before anything is stored", async () => {
  env.UPLOADS.limit = async () => ({ success: false });
  await expectReply(send("POST", CODE_A), 429, { error: "TooFast" });
  await expectReply(send("GET"), 200, { codes: [] });
});

test("a full page refuses new puzzles but still recognises known ones", async () => {
  await send("POST", CODE_A);
  const insert = env.DB.raw.prepare("INSERT INTO levels (code, puzzle) VALUES ('x', ?)");
  for (let i = 1; i < MAX_LEVELS; i++) {
    insert.run(String(i));
  }

  await expectReply(send("POST", CODE_B), 503, { error: "Full" });
  await expectReply(send("POST", CODE_A_RENAMED), 409, { error: "Duplicate", code: CODE_A });
});

test("other addresses and methods are refused", async () => {
  await expectReply(send("GET", undefined, "/api/other"), 404, { error: "NotFound" });
  await expectReply(send("DELETE"), 405, { error: "NotAllowed" });
});
