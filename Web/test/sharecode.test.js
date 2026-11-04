import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { decodeShareCode, puzzleKey } from "../public/sharecode.js";
import { DOWN_COLUMN_1, RIGHT_ROW_1, RIGHT_ROW_2, UP_COLUMN_3, makeCode } from "./make-code.js";

const vectorsPath = process.env.VECTORS ?? new URL("vectors.json", import.meta.url);
const vectors = JSON.parse(readFileSync(vectorsPath, "utf8"));

test("every verdict matches the C# code", () => {
  const seen = {};
  for (const vector of vectors) {
    const result = decodeShareCode(vector.code);
    const label = JSON.stringify(vector.code);
    assert.equal(result.error ?? "None", vector.error, label);
    seen[vector.error] = (seen[vector.error] ?? 0) + 1;
    if (vector.level) {
      assert.deepEqual(result.level, vector.level, label);
    }
  }

  // A verdict that never shows up would mean the vectors stopped testing it.
  for (const error of ["None", "NotACode", "UnknownVersion", "Damaged", "InvalidLevel", "BadSolution"]) {
    assert.ok(seen[error] > 0, `no vector with ${error}`);
  }
});

test("a code may hold whitespace, and comes back without it", () => {
  const code = makeCode({ moves: [RIGHT_ROW_1, RIGHT_ROW_1] });
  const pasted = ` ${code.slice(0, 8)}\n${code.slice(8)}\t`;
  assert.equal(decodeShareCode(pasted).code, code);
});

test("the puzzle key ignores name, solution and spelling", () => {
  const key = text => puzzleKey(decodeShareCode(text).level);
  const plain = key(makeCode({ moves: [RIGHT_ROW_1, RIGHT_ROW_1] }));
  assert.equal(key(makeCode({ moves: [RIGHT_ROW_1, RIGHT_ROW_1], name: "OTHER" })), plain);
  assert.equal(key(makeCode({ moves: [DOWN_COLUMN_1, RIGHT_ROW_2, RIGHT_ROW_2, UP_COLUMN_3] })), plain);
  // The walls flag with no walls at all spells the same puzzle differently.
  assert.equal(key(makeCode({ flags: 1, extra: [0], moves: [RIGHT_ROW_1, RIGHT_ROW_1] })), plain);
  assert.notEqual(key(makeCode({ target: 0x10, moves: [RIGHT_ROW_1, RIGHT_ROW_1, RIGHT_ROW_1] })), plain);
});

test("nothing odd gets through the name", () => {
  const { level } = decodeShareCode(makeCode({ moves: [RIGHT_ROW_1, RIGHT_ROW_1], name: "<b>hi</b> & a very long name" }));
  assert.equal(level.name, "bhib  a very lon");
});
