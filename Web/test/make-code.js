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

export const RIGHT_ROW_1 = 0x40, DOWN_COLUMN_1 = 0xC0, RIGHT_ROW_2 = 0x48, UP_COLUMN_3 = 0x90;

/** Hand-made code: top-row picture start, picture target, these moves. */
export function makeCode({ flags = 0, extra = [], start = 0x80, target = 0x20, moves, name = "A" }) {
  const nameBytes = Array.from(name, character => character.charCodeAt(0));
  const body = [flags, 0, 1, start, 1, target, ...extra, moves.length, ...moves, nameBytes.length, ...nameBytes];
  const crc = crc16(body);
  const binary = String.fromCharCode(...body, crc >> 8, crc & 0xFF);
  return "SR1-" + btoa(binary).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}
