// In their own file because a Worker's main module may only export handlers.

// A code of the longest possible level is well under this, even pasted with line breaks.
export const MAX_BODY_LENGTH = 2048;
// Keeps a flood of generated levels from eating the free database quota. Checked against the highest
// rowid, which reads one row where counting would read them all, so it is a count of arrivals: removing
// an older level frees no room, but emptying the table starts again from 1.
export const MAX_LEVELS = 20000;
// Levels per request. Every request reads about this many rows, however long the list has become.
export const PAGE_SIZE = 50;
