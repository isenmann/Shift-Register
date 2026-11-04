using ShiftRegister.Core;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// The bits' small reactions to a move. Moved bits lean along the shift, then settle with a small squash, and
    /// look where they are going. Bits beside the moved part glance at it; if a bit falls, they stare at the edge
    /// it left. A body at rest never leaves its cell or changes size, so the picture stays easy to read.
    /// </summary>
    public sealed partial class BoardView
    {
        sealed class Face
        {
            public Vector2 Look, LookTarget;
            public float LookFrom, LookUntil, WideUntil;
            public float Wide;
        }

        // Timings in seconds, offsets in mockup pixels.
        const float LeanTime = 0.29f, LeanAmount = 0.14f, LeanCrossRatio = 0.7f;
        const float TravelLookTime = 0.3f, LookPx = 2.5f, StarePx = 2.8f;
        const float GlanceTime = 0.5f, GlanceMaxDelay = 0.13f, StareTime = 0.7f, StareMaxDelay = 0.13f;
        const float FaceRate = 14f, FaceSnap = 0.01f;
        // A wide eye is 7×10.5 px instead of 6×8.
        const float WideWidthPx = 1f, WideHeightPx = 2.5f;

        readonly System.Random faceRandom = new System.Random();
        ulong leanCells;
        float leanStart;
        Axis leanAxis;

        /// <summary>The body's scale at this time after a shift starts: stretched first, then a damped squash.</summary>
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
        void StartArrival(ulong arrived, Axis axis, Vector2 step, float moveStart, bool spilled)
        {
            leanCells = arrived;
            leanStart = moveStart;
            leanAxis = axis;
            if (spilled)
            {
                return; // they are already staring at the edge
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (!BoardBits.GetCell(arrived, row, column))
                    {
                        continue;
                    }

                    var face = cells[row, column].Face;
                    face.Look = face.LookTarget = step * LookPx;
                    face.LookFrom = moveStart;
                    face.LookUntil = moveStart + TravelLookTime;
                    ApplyEyes(cells[row, column].Eyes, face.Look, face.Wide);
                }
            }
        }

        /// <summary>Runs after the move animation's coroutines, so these scales win over the ones ShowNow resets.</summary>
        void LateUpdate()
        {
            if (cells == null)
            {
                return;
            }

            float now = Time.time;
            float blend = 1f - Mathf.Exp(-Time.deltaTime * FaceRate);
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    var cell = cells[row, column];
                    var face = cell.Face;
                    bool started = now >= face.LookFrom;
                    Vector2 look = started && now < face.LookUntil ? face.LookTarget : Vector2.zero;
                    float wide = started && now < face.WideUntil ? 1f : 0f;
                    if (face.Look == look && face.Wide == wide)
                    {
                        continue;
                    }

                    face.Look = Vector2.Lerp(face.Look, look, blend);
                    face.Wide = Mathf.Lerp(face.Wide, wide, blend);
                    if ((face.Look - look).sqrMagnitude < FaceSnap * FaceSnap)
                    {
                        face.Look = look;
                    }

                    if (Mathf.Abs(face.Wide - wide) < FaceSnap)
                    {
                        face.Wide = wide;
                    }

                    ApplyEyes(cell.Eyes, face.Look, face.Wide);
                }
            }

            UpdateLean(now);
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
            if (cells == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                var face = cell.Face;
                face.Look = face.LookTarget = Vector2.zero;
                face.LookFrom = face.LookUntil = face.WideUntil = 0f;
                face.Wide = 0f;
                ApplyEyes(cell.Eyes, Vector2.zero, 0f);
            }
        }

        /// <summary>Places a bit's two eyes (left, right); <paramref name="look"/> is in mockup pixels.</summary>
        static void ApplyEyes(SpriteRenderer[] eyes, Vector2 look, float wide)
        {
            var scale = new Vector3(1f + wide * WideWidthPx / EyeWidth, 1f + wide * WideHeightPx / EyeHeight, 1f);
            for (int i = 0; i < eyes.Length; i++)
            {
                float side = i == 0 ? -EyeX : EyeX;
                eyes[i].transform.localPosition = new Vector2(side + look.x, EyeY + look.y) / CellPx;
                eyes[i].transform.localScale = scale;
            }
        }
    }
}
