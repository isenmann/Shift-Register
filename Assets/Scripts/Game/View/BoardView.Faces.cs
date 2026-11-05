using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// The bits' small reactions to a move. Moved bits lean along the shift, then settle with a small squash, and
    /// look where they are going. Bits beside the moved part glance at it; if a bit falls, they stare at the edge
    /// it left. While the player thinks, one bit blinks now and then, and after a long pause they fall asleep one
    /// at a time until any input wakes them. Bits on a target cell are at home and relax into a squint. Bits that
    /// a move of the selected lines would push off the edge peek over it, and the one an arrow or swipe is about
    /// to drop goes wide-eyed. A solved level gets happy eyes until the board changes. A body at rest never leaves
    /// its cell or changes size, so the picture stays easy to read; only a hint the player asked for moves it
    /// (see BoardView.Hint.cs).
    /// </summary>
    public sealed partial class BoardView
    {
        sealed class Face
        {
            public Vector2 Look, LookTarget;
            public float LookFrom, LookUntil, WideUntil;
            public float Wide;
            /// <summary>0 = eyes open, 1 = closed to a thin line.</summary>
            public float Lid;
            public float BlinkLeft, WakeDelay;
            public bool Asleep;
            /// <summary>0 = wide awake, 1 = at home. Empty cells keep their target, so a new bit fits.</summary>
            public float Squint, SquintTarget;
            /// <summary>Where this bit looks when no reaction is running: over the edge it could fall from.</summary>
            public Vector2 PeekLook;
            /// <summary>The move being aimed would drop this bit.</summary>
            public bool PeekWide;
            /// <summary>Where this bit looks to show a hinted move; zero when it isn't showing one.</summary>
            public Vector2 HintLook;
            /// <summary>The hinted move would drop this bit.</summary>
            public bool HintWide;
            /// <summary>How long after the bit behind it this bit starts to wobble.</summary>
            public float HintDelay;
        }

        // Timings in seconds, offsets in mockup pixels.
        const float LeanTime = 0.29f, LeanAmount = 0.14f, LeanCrossRatio = 0.7f;
        const float TravelLookTime = 0.3f, LookPx = 2.5f, StarePx = 2.8f;
        const float GlanceTime = 0.5f, GlanceMaxDelay = 0.13f, StareTime = 0.7f, StareMaxDelay = 0.13f;
        const float FaceRate = 14f, FaceSnap = 0.01f;
        // A wide eye is 7×10.5 px instead of 6×8.
        const float WideWidthPx = 1f, WideHeightPx = 2.5f;
        // A closed eye is an 8×2 px line.
        const float ClosedWidthPx = 2f, ClosedHeightPx = -6f;
        const float LidCloseRate = 42f, LidOpenRate = 24f;
        // A squinting eye is 6×4.5 px and sits a little lower.
        const float SquintHeightPx = -3.5f, SquintDropPx = 1.5f, SquintRate = 7f;
        const float HappyEyeSize = 14f, HappyEyeStroke = 2.6f;
        const float BlinkTime = 0.11f, BlinkGapMin = 2.6f, BlinkGapMax = 5f;
        const float NapDelay = 10f, NapStep = 0.3f, WakeMaxDelay = 0.12f;

        static readonly Direction[] AllDirections =
        {
            Direction.Left, Direction.Right, Direction.Up, Direction.Down
        };

        readonly System.Random faceRandom = new System.Random();
        ulong leanCells;
        float leanStart;
        Axis leanAxis;
        bool bitLife = true;
        bool idleLife;
        /// <summary>A solved or broken board waits for the player: nobody naps through that.</summary>
        bool napHeld;
        float idleTime, napIn;
        float blinkIn = BlinkGapMin;
        // What the peeks were worked out for, so they are only worked out again when it changes.
        bool peekKnown;
        ulong peekBits;
        int peekRow, peekColumn;
        Move? peekAimed;

        /// <summary>The BIT LIFE option. Off: every bit keeps a plain face, and moves slide as plain squares.</summary>
        public bool BitLife
        {
            get => bitLife;
            set
            {
                if (bitLife != value)
                {
                    bitLife = value;
                    ClearFaces();
                }
            }
        }

        /// <summary>
        /// True while the player can act on the board: bits blink and nap. Off in the level editor and behind panels.
        /// </summary>
        public bool IdleLife
        {
            get => idleLife;
            set
            {
                idleLife = value;
                WakeUp();
                if (!value)
                {
                    ClearPeeks();
                }
            }
        }

        /// <summary>
        /// True if bits on a target cell squint. Off in the level editor, where the picture is still being drawn.
        /// Takes effect at the next <see cref="Show"/>.
        /// </summary>
        public bool SquintAtHome { get; set; } = true;

        /// <summary>How much a bit is squinting: 0 = wide awake, 1 = at home.</summary>
        public float EyeSquint(int row, int column) => cells[row, column].Face.Squint;

        public bool IsBitAsleep(int row, int column) => cells[row, column].Face.Asleep;

        /// <summary>True once the pause is long enough for a nap, so the rest of the screen can hush too.</summary>
        public bool IsDozing => idleTime >= NapDelay;

        /// <summary>True if a bit shows the happy eyes of a solved level.</summary>
        public bool IsBitHappy(int row, int column) => cells[row, column].HappyEyes[0].enabled;

        void SetHappy(bool happy)
        {
            if (cells == null)
            {
                return;
            }

            happy &= bitLife;
            foreach (var cell in cells)
            {
                for (int i = 0; i < cell.Eyes.Length; i++)
                {
                    cell.Eyes[i].enabled = !happy;
                    cell.HappyEyes[i].enabled = happy;
                }
            }
        }

        /// <summary>True if a bit is looking over the edge it could fall from.</summary>
        public bool IsBitPeeking(int row, int column) => cells[row, column].Face.PeekLook != Vector2.zero;

        /// <summary>True if the move being aimed would drop this bit.</summary>
        public bool IsBitAlarmed(int row, int column) => cells[row, column].Face.PeekWide;

        /// <summary>
        /// Says what the player has selected. Edge bits that a move of the cursor's row or column would drop look
        /// over the edge, and those that <paramref name="aimed"/> would drop go wide-eyed. A negative
        /// <paramref name="row"/> means no cursor lines. Cheap to call every frame.
        /// </summary>
        public void SetPeek(Board board, int row, int column, Move? aimed)
        {
            if (cells == null || !bitLife)
            {
                return;
            }

            if (peekKnown && peekBits == board.Bits && peekRow == row && peekColumn == column
                && Nullable.Equals(peekAimed, aimed))
            {
                return;
            }

            ClearPeeks();
            peekKnown = true;
            peekBits = board.Bits;
            peekRow = row;
            peekColumn = column;
            peekAimed = aimed;
            if (row >= 0)
            {
                foreach (var direction in AllDirections)
                {
                    if (InputMapping.TryCursorMove(rules, row, column, direction, out var move))
                    {
                        MarkPeeks(board, move, false);
                    }
                }
            }

            if (aimed.HasValue)
            {
                MarkPeeks(board, aimed.Value, true);
            }
        }

        /// <summary>The rules decide who would fall, so a wrapping row or a wall never raises a false alarm.</summary>
        void MarkPeeks(Board board, Move move, bool alarmed)
        {
            if (!board.CanMove(move) || !rules.TryApply(board.Bits, move, out var outcome) || outcome.LostCells == 0)
            {
                return;
            }

            Vector2 direction = StepVector(move.Direction);
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (!BoardBits.GetCell(outcome.LostCells, row, column))
                    {
                        continue;
                    }

                    var face = cells[row, column].Face;
                    if (alarmed)
                    {
                        face.PeekLook = direction * StarePx;
                        face.PeekWide = true;
                    }
                    else
                    {
                        face.PeekLook += direction * LookPx; // a corner bit looks both ways
                    }
                }
            }
        }

        void ClearPeeks()
        {
            peekKnown = false;
            if (cells == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                cell.Face.PeekLook = Vector2.zero;
                cell.Face.PeekWide = false;
            }
        }

        /// <summary>How closed a bit's eyes are: 0 = open, 1 = closed.</summary>
        public float EyeLid(int row, int column) => cells[row, column].Face.Lid;

        /// <summary>The player did something: restarts the idle clock and wakes every sleeping bit.</summary>
        public void WakeUp()
        {
            idleTime = 0f;
            if (cells == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                if (cell.Face.Asleep)
                {
                    cell.Face.Asleep = false;
                    cell.Face.WakeDelay = (float)faceRandom.NextDouble() * WakeMaxDelay;
                }
            }
        }

        /// <summary>The body's scale this long after a shift starts: stretched first, then a damped squash.</summary>
        public static Vector3 LeanScale(float elapsed, Axis axis)
        {
            float progress = elapsed / LeanTime;
            float amount = progress <= 0f || progress >= 1f
                ? 0f
                : Mathf.Sin(progress * Mathf.PI * 2f) * (1f - progress) * LeanAmount;
            float along = 1f + amount, across = 1f - amount * LeanCrossRatio;
            return axis == Axis.Row ? new Vector3(along, across, 1f) : new Vector3(across, along, 1f);
        }

        /// <summary>
        /// Which way a bit at this cell looks to see the moved part: zero if it is part of it or not right beside it.
        /// </summary>
        public static Vector2 GlanceDirection(ulong moving, Axis axis, int row, int column)
        {
            if (BoardBits.GetCell(moving, row, column))
            {
                return Vector2.zero;
            }

            if (axis == Axis.Row)
            {
                if (row > 0 && BoardBits.GetCell(moving, row - 1, column))
                {
                    return Vector2.up;
                }

                return row < BoardBits.Size - 1 && BoardBits.GetCell(moving, row + 1, column)
                    ? Vector2.down
                    : Vector2.zero;
            }

            if (column > 0 && BoardBits.GetCell(moving, row, column - 1))
            {
                return Vector2.left;
            }

            return column < BoardBits.Size - 1 && BoardBits.GetCell(moving, row, column + 1)
                ? Vector2.right
                : Vector2.zero;
        }

        /// <summary>Starts the glances and stares of a move. They also run when moves snap into place.</summary>
        void StartReactions(MoveOutcome outcome)
        {
            var move = outcome.Move;
            ulong moving = MovingCells(move);
            Vector2 step = StepVector(move.Direction);
            bool spilled = outcome.LostCells != 0;
            float now = Time.time;
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (!BoardBits.GetCell(outcome.After, row, column))
                    {
                        continue;
                    }

                    var face = cells[row, column].Face;
                    Vector2 glance = GlanceDirection(moving, move.Axis, row, column);
                    bool inMovedPart = BoardBits.GetCell(moving, row, column);
                    if (spilled && (inMovedPart || glance != Vector2.zero))
                    {
                        float from = now + (float)faceRandom.NextDouble() * StareMaxDelay;
                        face.LookTarget = step * StarePx;
                        face.LookFrom = from;
                        face.LookUntil = face.WideUntil = from + StareTime;
                    }
                    else if (glance != Vector2.zero)
                    {
                        float from = now + (float)faceRandom.NextDouble() * GlanceMaxDelay;
                        face.LookTarget = glance * LookPx;
                        face.LookFrom = from;
                        face.LookUntil = from + GlanceTime;
                    }
                }
            }
        }

        /// <summary>Hands the lean and the travel look over from the slide to the bits that arrived.</summary>
        void StartArrival(ulong arrived, float[,] squints, Axis axis, Vector2 step, float moveStart, bool spilled)
        {
            if (!bitLife)
            {
                return;
            }

            leanCells = arrived;
            leanStart = moveStart;
            leanAxis = axis;
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (!BoardBits.GetCell(arrived, row, column))
                    {
                        continue;
                    }

                    // The squint came along with the bit, and eases from there to what the new cell asks for.
                    var face = cells[row, column].Face;
                    face.Squint = squints[row, column];
                    if (!spilled) // otherwise they are already staring at the edge
                    {
                        face.Look = face.LookTarget = step * LookPx;
                        face.LookFrom = moveStart;
                        face.LookUntil = moveStart + TravelLookTime;
                    }

                    ApplyEyes(cells[row, column].Eyes, face.Look, face.Wide, face.Lid, face.Squint);
                }
            }
        }

        /// <summary>Runs after the move animation's coroutines, so these scales win over ShowNow's resets.</summary>
        void LateUpdate() => TickFaces(Time.deltaTime);

        /// <summary>One frame of the faces. Public so tests can run it without play mode.</summary>
        public void TickFaces(float deltaTime)
        {
            if (cells == null)
            {
                return;
            }

            UpdateIdleLife(deltaTime);

            float now = Time.time;
            float blend = 1f - Mathf.Exp(-deltaTime * FaceRate);
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var cell = cells[row, column];
                    var face = cell.Face;
                    bool started = now >= face.LookFrom;
                    bool reacting = started && now < face.LookUntil;
                    bool hinted = face.HintLook != Vector2.zero;
                    Vector2 peek = face.Asleep ? Vector2.zero : face.PeekLook;
                    Vector2 look = reacting ? face.LookTarget : hinted ? face.HintLook : peek;
                    bool alarmed = (face.PeekWide && !face.Asleep) || face.HintWide;
                    float squint = hinted ? 0f : face.SquintTarget;
                    float wide = alarmed || (started && now < face.WideUntil) ? 1f : 0f;
                    bool closed = face.Asleep || face.BlinkLeft > 0f || face.WakeDelay > 0f;
                    float lid = closed ? 1f : 0f;
                    face.BlinkLeft = Mathf.Max(0f, face.BlinkLeft - deltaTime);
                    face.WakeDelay = Mathf.Max(0f, face.WakeDelay - deltaTime);
                    if (face.Look == look && face.Wide == wide && face.Lid == lid && face.Squint == squint)
                    {
                        continue;
                    }

                    face.Look = Vector2.Lerp(face.Look, look, blend);
                    face.Wide = Mathf.Lerp(face.Wide, wide, blend);
                    float lidRate = closed ? LidCloseRate : LidOpenRate;
                    face.Lid = Mathf.Lerp(face.Lid, lid, 1f - Mathf.Exp(-deltaTime * lidRate));
                    face.Squint = Mathf.Lerp(face.Squint, squint, 1f - Mathf.Exp(-deltaTime * SquintRate));
                    if ((face.Look - look).sqrMagnitude < FaceSnap * FaceSnap)
                    {
                        face.Look = look;
                    }

                    if (Mathf.Abs(face.Wide - wide) < FaceSnap)
                    {
                        face.Wide = wide;
                    }

                    if (Mathf.Abs(face.Lid - lid) < FaceSnap)
                    {
                        face.Lid = lid;
                    }

                    if (Mathf.Abs(face.Squint - squint) < FaceSnap)
                    {
                        face.Squint = squint;
                    }

                    ApplyEyes(cell.Eyes, face.Look, face.Wide, face.Lid, face.Squint);
                }
            }

            UpdateLean(now);
            TickHintWobble(now - hintStart);
        }

        /// <summary>The idle clock: one bit blinks every few seconds, and a long pause sends them to sleep.</summary>
        void UpdateIdleLife(float deltaTime)
        {
            if (!idleLife || !bitLife)
            {
                return;
            }

            idleTime += deltaTime;
            blinkIn -= deltaTime;
            if (blinkIn <= 0f)
            {
                blinkIn = Mathf.Lerp(BlinkGapMin, BlinkGapMax, (float)faceRandom.NextDouble());
                if (!busy && TryPickAwakeBit(out var blinker))
                {
                    blinker.Face.BlinkLeft = BlinkTime;
                }
            }

            if (napHeld || idleTime < NapDelay)
            {
                napIn = 0f;
                return;
            }

            napIn -= deltaTime;
            if (napIn <= 0f)
            {
                napIn = NapStep;
                if (TryPickAwakeBit(out var sleeper, skipHinted: true))
                {
                    sleeper.Face.Asleep = true;
                }
            }
        }

        /// <summary>A random bit on the board that is awake.</summary>
        bool TryPickAwakeBit(out Cell picked, bool skipHinted = false)
        {
            picked = null;
            int seen = 0;
            foreach (var cell in cells)
            {
                bool skipped = skipHinted && cell.Face.HintLook != Vector2.zero;
                if (cell.Bit.activeSelf && !cell.Face.Asleep && !skipped && faceRandom.Next(++seen) == 0)
                {
                    picked = cell;
                }
            }

            return picked != null;
        }

        void UpdateLean(float now)
        {
            if (leanCells == 0)
            {
                return;
            }

            float elapsed = now - leanStart;
            bool done = elapsed >= LeanTime;
            Vector3 scale = done ? Vector3.one : LeanScale(elapsed, leanAxis);
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (BoardBits.GetCell(leanCells, row, column))
                    {
                        cells[row, column].Bit.transform.localScale = scale;
                    }
                }
            }

            if (done)
            {
                leanCells = 0;
            }
        }

        /// <summary>Puts every bit back to its resting face (a new picture has no reactions to finish).</summary>
        void ClearFaces()
        {
            leanCells = 0;
            idleTime = 0f;
            napHeld = false;
            ClearMoveHint();
            ClearPeeks();
            SetHappy(false);
            if (cells == null)
            {
                return;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var cell = cells[row, column];
                    var face = cell.Face;
                    face.Look = face.LookTarget = Vector2.zero;
                    face.LookFrom = face.LookUntil = face.WideUntil = 0f;
                    face.Wide = face.Lid = face.BlinkLeft = face.WakeDelay = 0f;
                    face.Asleep = false;
                    // At once, not eased: an undo or reset must not make the whole board squint again.
                    face.Squint = face.SquintTarget =
                        bitLife && SquintAtHome && BoardBits.GetCell(rules.Target, row, column) ? 1f : 0f;
                    ApplyEyes(cell.Eyes, Vector2.zero, 0f, 0f, face.Squint);
                }
            }
        }

        /// <summary>
        /// Places a bit's two eyes (left, right); <paramref name="look"/> is in mockup pixels. Staring or closed eyes
        /// win over a squint.
        /// </summary>
        static void ApplyEyes(SpriteRenderer[] eyes, Vector2 look, float wide, float lid = 0f, float squint = 0f)
        {
            float relaxed = squint * (1f - wide) * (1f - lid);
            var scale = new Vector3(1f + (wide * WideWidthPx + lid * ClosedWidthPx) / EyeWidth,
                1f + (wide * WideHeightPx + lid * ClosedHeightPx + relaxed * SquintHeightPx) / EyeHeight, 1f);
            for (int i = 0; i < eyes.Length; i++)
            {
                float side = i == 0 ? -EyeX : EyeX;
                float height = EyeY + look.y - relaxed * SquintDropPx;
                eyes[i].transform.localPosition = new Vector2(side + look.x, height) / CellPx;
                eyes[i].transform.localScale = scale;
            }
        }
    }
}
