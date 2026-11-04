import { decodeShareCode, puzzleKey, ERROR_MESSAGES } from "./sharecode.js";

const API = "/api/levels";
const SVG = "http://www.w3.org/2000/svg";
const MESSAGES = {
  ...ERROR_MESSAGES,
  TooFast: "That was a lot of uploads. Wait a minute and try again.",
  Full: "The page is full for now. Try again later."
};

const element = id => document.getElementById(id);
const list = element("list");
const more = element("more");

/** Newest first: { level, row } per shared level loaded so far. */
let entries = [];
let selected = -1;
/** Where the next page starts, as the server said. Null before the first page and after the last. */
let next = null;
let loading = false;

const displayName = level => (level.name || "UNTITLED").toUpperCase();
const proofLine = level => `par ${level.par} · best so far`;

function mechanics(level) {
  const names = [];
  const add = (present, name) => {
    if (present) {
      names.push(name);
    }
  };
  add(level.walls.some(Boolean), "WALLS");
  add(level.pins.some(Boolean), "PINS");
  add(level.rotateRows.length, "WRAP");
  add(level.chainTopRows.length, "CHAIN");
  add(level.fillLeftRows.length || level.fillRightRows.length, "1-IN");
  add(level.gates.length, "GATES");
  add(level.lossRule === 1, level.lossCount === 0 ? "LOSE NO BITS" : `LOSE AT MOST ${level.lossCount}`);
  add(level.lossRule === 2, `LOSE EXACTLY ${level.lossCount}`);
  return names.length > 0 ? names : ["PLAIN BOARD"];
}

function cellKind(level, picture, row, column) {
  const mask = 0x80 >> column;
  if (level.walls[row] & mask) {
    return "wall";
  }

  return picture[row] & mask ? "on" : "off";
}

/** One SVG per row, not one element per cell: there can be thousands of rows. */
function miniBoard(level) {
  const paths = { on: "", off: "", wall: "" };
  for (let row = 0; row < 8; row++) {
    for (let column = 0; column < 8; column++) {
      paths[cellKind(level, level.target, row, column)] += `M${column * 5} ${row * 5}h4v4h-4z`;
    }
  }

  const svg = document.createElementNS(SVG, "svg");
  svg.setAttribute("viewBox", "0 0 39 39");
  svg.setAttribute("aria-hidden", "true");
  for (const kind of Object.keys(paths)) {
    const path = document.createElementNS(SVG, "path");
    path.setAttribute("class", kind);
    path.setAttribute("d", paths[kind]);
    svg.append(path);
  }

  return svg;
}

function makeRow(entry) {
  const row = document.createElement("button");
  row.type = "button";
  row.className = "row";
  row.tabIndex = -1;
  const title = document.createElement("span");
  title.className = "title";
  title.textContent = displayName(entry.level);
  const status = document.createElement("span");
  status.className = "status";
  status.textContent = proofLine(entry.level);
  row.append(miniBoard(entry.level), title, status);
  row.addEventListener("click", () => select(entries.indexOf(entry)));
  return row;
}

function fillBoard(target, level, picture) {
  target.replaceChildren();
  for (let row = 0; row < 8; row++) {
    for (let column = 0; column < 8; column++) {
      const cell = document.createElement("i");
      const kind = cellKind(level, picture, row, column);
      if (kind !== "off") {
        cell.className = kind;
      }

      target.append(cell);
    }
  }
}

function showCount() {
  element("count").textContent = entries.length === 0 ? "No levels yet. Paste a share code above to add the first one."
    : "Newest first";
}

function select(index, focus = false) {
  if (entries[selected]) {
    entries[selected].row.setAttribute("aria-current", "false");
    entries[selected].row.tabIndex = -1;
  }

  selected = index;
  const entry = entries[index];
  element("detail").hidden = !entry;
  if (!entry) {
    return;
  }

  const { level, row } = entry;
  row.setAttribute("aria-current", "true");
  row.tabIndex = 0;
  row.scrollIntoView({ block: "nearest" });
  if (focus) {
    row.focus();
  }

  element("name").textContent = displayName(level);
  element("proof").textContent = proofLine(level);
  element("code").textContent = level.code;
  element("chips").replaceChildren(...mechanics(level).map(name => {
    const chip = document.createElement("span");
    chip.className = "chip";
    chip.textContent = name;
    return chip;
  }));
  fillBoard(element("start"), level, level.start);
  fillBoard(element("target"), level, level.target);
  element("copy").textContent = "COPY CODE";
}

function toEntry(code) {
  const { level } = decodeShareCode(code);
  return level ? { level: { ...level, code } } : null; // a stored code from a newer game stays out of the list
}

function say(text, bad = false) {
  const message = element("message");
  message.textContent = text;
  message.className = bad ? "message bad" : "message";
}

/** Loads the next page once the button at the end of the list comes near the visible part. */
const nearEnd = new IntersectionObserver(changes => {
  if (changes.some(change => change.isIntersecting)) {
    loadMore();
  }
}, { root: list, rootMargin: "0px 0px 300px 0px" });

async function loadMore() {
  if (loading) {
    return;
  }

  loading = true;
  more.disabled = true;
  more.textContent = "LOADING...";
  nearEnd.unobserve(more);
  try {
    const response = await fetch(next === null ? API : `${API}?before=${next}`);
    if (!response.ok) {
      throw new Error(String(response.status));
    }

    const page = await response.json();
    // A level shown at the top after a duplicate upload can arrive again here, further down the list.
    const fresh = page.codes.filter(code => !entries.some(entry => entry.level.code === code))
      .map(toEntry).filter(Boolean);
    for (const entry of fresh) {
      entry.row = makeRow(entry);
    }

    more.before(...fresh.map(entry => entry.row));
    entries.push(...fresh);
    next = page.next;
    more.hidden = next === null;
    showCount();
    if (selected < 0 && entries.length > 0) {
      select(0);
    }

    if (next !== null) {
      nearEnd.observe(more); // reports at once if the button is still near, so a short page cannot stall
    }
  } catch {
    if (entries.length === 0) {
      element("count").textContent = "The levels could not be loaded.";
    }

    more.textContent = "TRY AGAIN"; // by click only: retrying by itself would hammer a server in trouble
    more.disabled = false;
  } finally {
    loading = false;
  }
}

/** The level the server knows, added to the top of the list if this page has not seen it yet. */
function showLevel(code) {
  const entry = toEntry(code);
  const key = puzzleKey(entry.level);
  const known = entries.findIndex(other => puzzleKey(other.level) === key);
  if (known >= 0) {
    select(known);
    return entries[known];
  }

  entry.row = makeRow(entry);
  list.prepend(entry.row);
  if (selected >= 0) {
    selected++;
  }

  entries.unshift(entry);
  showCount();
  select(0);
  return entry;
}

async function upload(event) {
  event.preventDefault();
  const input = element("input");
  if (!input.value.trim()) {
    say("Paste a share code first.", true);
    input.focus();
    return;
  }

  const { error } = decodeShareCode(input.value);
  if (error) {
    say(MESSAGES[error], true);
    input.focus();
    return;
  }

  element("send").disabled = true;
  try {
    const response = await fetch(API, { method: "POST", body: input.value });
    const result = await response.json();
    if (response.status === 201) {
      const { level } = showLevel(result.code);
      input.value = "";
      say(`${displayName(level)} is on the page. Thanks for sharing!`);
    } else if (result.error === "Duplicate") {
      const { level } = showLevel(result.code);
      say(`This puzzle is already here as ${displayName(level)}.`, true);
    } else {
      say(MESSAGES[result.error] ?? "The upload failed. Try again in a moment.", true);
    }
  } catch {
    say("The upload failed. Try again in a moment.", true);
  } finally {
    element("send").disabled = false;
  }
}

async function copy() {
  const button = element("copy");
  try {
    await navigator.clipboard.writeText(entries[selected].level.code);
    button.textContent = "COPIED";
  } catch {
    getSelection().selectAllChildren(element("code"));
    button.textContent = "CODE SELECTED";
  }
}

list.addEventListener("keydown", event => {
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    const next = selected + (event.key === "ArrowDown" ? 1 : -1);
    if (entries[next]) {
      select(next, true);
    }
  }
});
element("form").addEventListener("submit", upload);
element("copy").addEventListener("click", copy);
more.addEventListener("click", loadMore);
loadMore();
