import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { decodeShareCode, puzzleKey } from "../public/sharecode.js";

const vectorsPath = process.env.VECTORS ?? new URL("vectors.json", import.meta.url);
const vectors = JSON.parse(readFileSync(vectorsPath, "utf8"));

function crc16(bytes) {
  let crc = 0xFFFF;
  for (const byte of bytes) {
    crc ^= byte << 8;
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
    }
  }

  return crc;
}

/** Hand-made code: top-row picture start, picture target, these moves. */
function makeCode({ flags = 0, extra = [], start = 0x80, target = 0x20, moves, name = "A" }) {
  const nameBytes = Array.from(name, character => character.charCodeAt(0));
  const body = [flags, 0, 1, start, 1, target, ...extra, moves.length, ...moves, nameBytes.length, ...nameBytes];
  const crc = crc16(body);
  const binary = String.fromCharCode(...body, crc >> 8, crc & 0xFF);
  return "SR1-" + btoa(binary).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

const RIGHT_ROW_1 = 0x40, DOWN_COLUMN_1 = 0xC0, RIGHT_ROW_2 = 0x48, UP_COLUMN_3 = 0x90;

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
