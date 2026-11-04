// In their own file because a Worker's main module may only export handlers.

// A code of the longest possible level is well under this, even pasted with line breaks.
export const MAX_BODY_LENGTH = 2048;
// Keeps a flood of generated levels from eating the free database quota.
export const MAX_LEVELS = 2000;
