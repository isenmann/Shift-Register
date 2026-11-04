// Share codes in JavaScript, for the browser and the Worker. A port of Core/ShareCode.cs and the parts of
// LevelRules, LevelEdit and Board it needs. The C# code is the reference: test/vectors.json holds its verdicts
// (made by Tools/ShareCodeVectors), so run "npm test" after changing either side.

const FLAG_WALLS = 1, FLAG_PINS = 2, FLAG_ROTATE = 4, FLAG_CHAIN = 8;
const FLAG_FILL_LEFT = 16, FLAG_FILL_RIGHT = 32, FLAG_GATES = 64, FLAG_GATES_ON_COLUMNS = 128;
const SIZE = 8, MAX_NAME_LENGTH = 16;
const AND = 0, OR = 1, XOR = 2, NOT = 3;
const ANY = 0, EXACTLY = 2;
const LEFT = 0, UP = 2;

// The same characters as char.IsWhiteSpace.
const WHITESPACE = /[\t-\r \x85\xA0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]/g;
const NAME_CHAR = /[A-Za-z0-9 \-.!?']/;

/** The player-facing words for each error, as in CustomLevelLibrary.ImportMessage. */
export const ERROR_MESSAGES = {
  NotACode: "That is not a share code.",
  UnknownVersion: "This code needs a newer version of the game.",
  Damaged: "This code is damaged or cut off.",
  InvalidLevel: "This code holds a level that breaks the rules.",
  BadSolution: "This code's solution does not solve its level."
};

/** Returns { code, level } or { error }. Nothing in a code is trusted: the rules are checked, the solution replayed. */
export function decodeShareCode(text) {
  const code = String(text ?? "").replace(WHITESPACE, "");
  if (code.length < 4 || code[0] !== "S" || code[1] !== "R" || code[3] !== "-") {
    return { error: "NotACode" };
  }

  if (code[2] !== "1") {
    return { error: /\p{Nd}/u.test(code[2]) ? "UnknownVersion" : "NotACode" };
  }

  const bytes = fromBase64Url(code.slice(4));
  if (!bytes) {
    return { error: "NotACode" };
  }

  if (bytes.length < 2) {
    return { error: "Damaged" };
  }

  const end = bytes.length - 2;
  if (((bytes[end] << 8) | bytes[end + 1]) !== crc16(bytes, end)) {
    return { error: "Damaged" };
  }

  const parsed = parse(bytes, end);
  if (parsed.error) {
    return parsed;
  }

  if (!isValid(parsed.level)) {
    return { error: "InvalidLevel" };
  }

  return replays(parsed.level, parsed.moves) ? { code, level: parsed.level } : { error: "BadSolution" };
}

/** Two levels with the same key are the same puzzle, whatever their name, solution or spelling of the code. */
export function puzzleKey(level) {
  const gates = level.gates.slice().sort((a, b) => a.row - b.row).map(gate => [gate.row, gate.op, gate.mask]);
  return JSON.stringify([
    level.start, level.target, level.walls, level.pins,
    level.rotateRows, level.chainTopRows, level.fillLeftRows, level.fillRightRows,
    gates, gates.length > 0 && level.gatesOnColumnMoves,
    level.lossRule, level.lossRule === ANY ? 0 : level.lossCount
  ]);
}

function crc16(bytes, count) {
  let crc = 0xFFFF;
  for (let i = 0; i < count; i++) {
    crc ^= bytes[i] << 8;
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
    }
  }

  return crc;
}

function fromBase64Url(text) {
  if (!/^[A-Za-z0-9_-]*$/.test(text) || text.length % 4 === 1) {
    return null;
  }

  const binary = atob(text.replace(/-/g, "+").replace(/_/g, "/"));
  // The last character can hide unused bits; only the one spelling of these bytes counts.
  const again = btoa(binary).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
  return again === text ? Uint8Array.from(binary, character => character.charCodeAt(0)) : null;
}

function parse(bytes, end) {
  let position = 0;
  const next = () => {
    if (position >= end) {
      throw new RangeError();
    }

    return bytes[position++];
  };
  const readRows = () => {
    const present = next(), rows = [];
    for (let row = 0; row < SIZE; row++) {
      rows.push(present & (1 << row) ? next() : 0);
    }

    return rows;
  };
  const readRowList = () => {
    const rowBits = next(), rows = [];
    for (let row = 0; row < SIZE; row++) {
      if (rowBits & (1 << row)) {
        rows.push(row);
      }
    }

    return rows;
  };
  const blank = () => new Array(SIZE).fill(0);

  const level = {};
  const moves = [];
  try {
    const flags = next(), loss = next();
    level.lossRule = loss & 3;
    level.lossCount = loss >> 2;
    level.gatesOnColumnMoves = (flags & FLAG_GATES_ON_COLUMNS) !== 0;
    level.start = readRows();
    level.target = readRows();
    level.walls = flags & FLAG_WALLS ? readRows() : blank();
    level.pins = flags & FLAG_PINS ? readRows() : blank();
    level.rotateRows = flags & FLAG_ROTATE ? readRowList() : [];
    level.chainTopRows = flags & FLAG_CHAIN ? readRowList() : [];
    level.fillLeftRows = flags & FLAG_FILL_LEFT ? readRowList() : [];
    level.fillRightRows = flags & FLAG_FILL_RIGHT ? readRowList() : [];
    level.gates = [];
    if (flags & FLAG_GATES) {
      const count = next();
      if (count > SIZE) {
        return { error: "InvalidLevel" };
      }

      for (let i = 0; i < count; i++) {
        const rowOp = next();
        level.gates.push({ row: rowOp & 7, op: rowOp >> 3, mask: next() });
      }
    }

    const moveCount = next();
    for (let i = 0; i < moveCount; i++) {
      moves.push(next());
    }

    const nameLength = next();
    let name = "", kept = 0;
    for (let i = 0; i < nameLength; i++) {
      const character = String.fromCharCode(next());
      if (kept < MAX_NAME_LENGTH && NAME_CHAR.test(character)) {
        name += character;
        kept++;
      }
    }

    level.name = name.trim();
  } catch (error) {
    if (error instanceof RangeError) {
      return { error: "Damaged" };
    }

    throw error;
  }

  if (position !== end) {
    return { error: "Damaged" };
  }

  level.par = moves.length;
  return { level, moves };
}

/** LevelRules.Validate and LevelEdit.AuthoredErrors. Rows are bytes and lossCount fits by construction. */
function isValid(level) {
  const { start, target, walls, pins, rotateRows, chainTopRows, fillLeftRows, fillRightRows, gates } = level;
  for (let row = 0; row < SIZE; row++) {
    if ((start[row] & walls[row]) || (target[row] & walls[row]) || (pins[row] & walls[row])
      || (pins[row] & ~start[row]) || (pins[row] & ~target[row])) {
      return false;
    }
  }

  const rotate = rotateRows.includes.bind(rotateRows);
  const fillLeft = fillLeftRows.includes.bind(fillLeftRows);
  const fillRight = fillRightRows.includes.bind(fillRightRows);
  if (fillLeftRows.some(row => (walls[row] & 0x80) || rotate(row))
    || fillRightRows.some(row => (walls[row] & 0x01) || rotate(row))) {
    return false;
  }

  const inChain = new Array(SIZE).fill(false);
  for (const top of chainTopRows) {
    const bottom = top + 1;
    if (top >= SIZE - 1 || inChain[top] || inChain[bottom] || walls[top] || walls[bottom]
      || rotate(top) !== rotate(bottom) || fillRight(top) || fillLeft(bottom)) {
      return false;
    }

    inChain[top] = inChain[bottom] = true;
  }

  const hasGate = new Array(SIZE).fill(false);
  for (const gate of gates) {
    if (gate.op > NOT || gate.mask === 0 || (gate.mask & walls[gate.row]) || hasGate[gate.row]
      || (fillLeft(gate.row) && (gate.mask & 0x80)) || (fillRight(gate.row) && (gate.mask & 0x01))) {
      return false;
    }

    hasGate[gate.row] = true;
  }

  return (level.lossRule & 3) !== 3;
}

/** Solver.Replays: every move must be legal, and the board must be solved exactly at the last one. */
function replays(level, moves) {
  if (moves.length === 0) {
    return false;
  }

  const rules = compile(level);
  const board = level.start.slice();
  let lost = 0;
  for (const packed of moves) {
    if (isSolved(level, board, lost) || isBroken(level, lost)) {
      return false; // Board.CanMove refuses moves after the end
    }

    const shiftLost = applyMove(rules, level, board, packed);
    if (shiftLost < 0) {
      return false;
    }

    lost += shiftLost;
  }

  return isSolved(level, board, lost);
}

function isBroken(level, lost) {
  return level.lossRule !== ANY && lost > level.lossCount;
}

function isSolved(level, board, lost) {
  if (board.some((row, index) => row !== level.target[index])) {
    return false;
  }

  return level.lossRule === EXACTLY ? lost === level.lossCount : !isBroken(level, lost);
}

function compile(level) {
  const rotate = level.rotateRows, left = level.fillLeftRows, right = level.fillRightRows;
  const rows = [], columns = [];
  const chainTop = new Array(SIZE).fill(-1);
  for (const top of level.chainTopRows) {
    chainTop[top] = chainTop[top + 1] = top;
  }

  for (let i = 0; i < SIZE; i++) {
    rows.push(buildLine(level.walls[i], level.pins[i] !== 0, rotate.includes(i), left.includes(i), right.includes(i)));
    columns.push(buildLine(getColumn(level.walls, i), getColumn(level.pins, i) !== 0, false, false, false));
  }

  return { rows, columns, chainTop };
}

function buildLine(walls, locked, rotate, fillStart, fillEnd) {
  const line = { segments: [], starts: [], ends: [], locked, rotate, fillStart, fillEnd };
  let current = 0, first = 0, last = 0;
  const close = () => {
    if (current !== 0) {
      line.segments.push(current);
      line.starts.push(first);
      line.ends.push(last);
    }

    current = 0;
  };
  for (let i = 0; i < SIZE; i++) {
    const bit = 0x80 >> i;
    if (walls & bit) {
      close();
      continue;
    }

    if (current === 0) {
      first = bit;
    }

    current |= bit;
    last = bit;
  }

  close();
  return line;
}

/** Column byte with the top row as its highest bit. */
function getColumn(rows, column) {
  let value = 0;
  for (let row = 0; row < SIZE; row++) {
    if (rows[row] & (0x80 >> column)) {
      value |= 0x80 >> row;
    }
  }

  return value;
}

function setColumn(rows, column, value) {
  const mask = 0x80 >> column;
  for (let row = 0; row < SIZE; row++) {
    rows[row] = value & (0x80 >> row) ? rows[row] | mask : rows[row] & ~mask;
  }
}

/** Applies a packed move to the board in place. Returns the bits lost, or -1 if the move is illegal. */
function applyMove(rules, level, board, packed) {
  const direction = packed >> 6, line = (packed >> 3) & 7, segment = packed & 7;
  const towardStart = direction === LEFT || direction === UP;
  if (direction > 1) {
    return moveColumn(rules, level, board, line, segment, towardStart);
  }

  const top = rules.chainTop[line];
  if (top >= 0) {
    if (rules.rows[top].locked || rules.rows[top + 1].locked || segment >= rules.rows[top].segments.length) {
      return -1;
    }

    const lost = shiftChain(rules, board, top, towardStart);
    applyGates(level, board, top);
    applyGates(level, board, top + 1);
    return lost;
  }

  const row = rules.rows[line];
  if (row.locked || segment >= row.segments.length) {
    return -1;
  }

  const shifted = shiftLine(board[line], row, segment, towardStart);
  board[line] = shifted.value;
  applyGates(level, board, line);
  return shifted.lost;
}

function moveColumn(rules, level, board, column, segment, towardStart) {
  const line = rules.columns[column];
  if (line.locked || segment >= line.segments.length) {
    return -1;
  }

  const shifted = shiftLine(getColumn(board, column), line, segment, towardStart);
  setColumn(board, column, shifted.value);
  if (level.gatesOnColumnMoves) {
    for (let row = 0; row < SIZE; row++) {
      if (line.segments[segment] & (0x80 >> row)) {
        applyGates(level, board, row);
      }
    }
  }

  return shifted.lost;
}

function shiftLine(value, line, segment, towardStart) {
  const mask = line.segments[segment], inSegment = value & mask;
  let shifted, out;
  if (towardStart) {
    out = inSegment & line.starts[segment];
    shifted = (inSegment << 1) & mask;
    if (out && line.rotate) {
      shifted |= line.ends[segment];
    }

    if (line.fillEnd && (mask & 0x01)) {
      shifted |= 0x01;
    }
  } else {
    out = inSegment & line.ends[segment];
    shifted = (inSegment >> 1) & mask;
    if (out && line.rotate) {
      shifted |= line.starts[segment];
    }

    if (line.fillStart && (mask & 0x80)) {
      shifted |= 0x80;
    }
  }

  return { value: ((value & ~mask) | shifted) & 0xFF, lost: out && !line.rotate ? 1 : 0 };
}

/** Rows top and top + 1 as one 16-bit register. Returns the bits lost. */
function shiftChain(rules, board, top, towardStart) {
  let value = (board[top] << 8) | board[top + 1], lost = 0;
  const rotate = rules.rows[top].rotate;
  if (towardStart) {
    const out = value & 0x8000;
    value = (value << 1) & 0xFFFF;
    if (out && rotate) {
      value |= 0x0001;
    } else if (out) {
      lost = 1;
    }

    if (rules.rows[top + 1].fillEnd) {
      value |= 0x0001;
    }
  } else {
    const out = value & 0x0001;
    value >>= 1;
    if (out && rotate) {
      value |= 0x8000;
    } else if (out) {
      lost = 1;
    }

    if (rules.rows[top].fillStart) {
      value |= 0x8000;
    }
  }

  board[top] = value >> 8;
  board[top + 1] = value & 0xFF;
  return lost;
}

function applyGates(level, board, row) {
  const gates = level.gates.filter(gate => gate.row === row);
  if (gates.length === 0) {
    return;
  }

  let value = board[row];
  for (const gate of gates) {
    if (gate.op === AND) {
      value &= gate.mask;
    } else if (gate.op === OR) {
      value |= gate.mask;
    } else if (gate.op === XOR) {
      value ^= gate.mask;
    } else if (value & gate.mask) {
      value = ~value;
    }
  }

  board[row] = ((value & ~level.walls[row]) | level.pins[row]) & 0xFF;
}
