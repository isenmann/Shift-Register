-- The code is all that is kept. The key only stops the same puzzle coming back under another name,
-- and the implicit rowid gives the order of arrival.
CREATE TABLE IF NOT EXISTS levels (
  code TEXT NOT NULL,
  puzzle TEXT NOT NULL UNIQUE
);
