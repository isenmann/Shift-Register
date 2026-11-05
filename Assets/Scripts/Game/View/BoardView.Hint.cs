using System.Collections.Generic;
using ShiftRegister.Core;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// A hinted move shown by the bits themselves. The bits the move carries look the way they should go and wobble
    /// back and forth along it, each starting a little after the bit behind it, until the board changes. A bit the
    /// move would drop goes wide-eyed. A move that creates a bit on an empty part shows a faint bit to come where the
    /// new one will appear. Hinted bits stay awake, and open their eyes if they are at home.
    /// </summary>
    public sealed partial class BoardView
    {
        // Offsets in mockup pixels, times in seconds.
        const float HintWobbleReachPx = 4f, HintWobbleTime = 1f, HintWaveDelay = 0.1f, HintEaseInTime = 0.25f;
        // The wobble runs from a quarter of its reach behind the cell's centre to all of it ahead.
        const float HintSwayCentre = 0.375f, HintSwayRange = 0.625f;
        const float HintStretchPerPx = 0.015f, HintSquashPerPx = 0.009f;
        const float BitToComeAlpha = 0.5f;

        readonly List<Transform> bitsToCome = new List<Transform>();
        ulong hintBits;
        float hintStart;
        Axis hintAxis;
        Vector2 hintStep;

        /// <summary>How many faint bits to come the shown hint has.</summary>
        public int BitsToCome => bitsToCome.Count;

        /// <summary>True if this bit is showing a hinted move.</summary>
        public bool IsBitHinted(int row, int column) => BoardBits.GetCell(hintBits, row, column);

        /// <summary>Where a bit's body sits now, relative to its cell, in cell units.</summary>
        public Vector2 BitOffset(int row, int column) => cells[row, column].Bit.transform.localPosition;

        /// <summary>
        /// How far along the move a bit is this long after the hint started, in mockup pixels. It eases in, so the
        /// first swing doesn't jump, and <paramref name="delay"/> makes the wave.
        /// </summary>
        public static float HintWobble(float elapsed, float delay)
        {
            float time = elapsed - delay;
            if (time <= 0f)
            {
                return 0f;
            }

            float sway = HintSwayCentre - HintSwayRange * Mathf.Cos(2f * Mathf.PI * time / HintWobbleTime);
            return HintWobbleReachPx * sway * Mathf.Clamp01(time / HintEaseInTime);
        }

        /// <summary>The body's scale at a wobble offset: stretched along the move ahead, squashed behind.</summary>
        public static Vector3 HintScale(float offsetPx, Axis axis)
        {
            float along = 1f + offsetPx * HintStretchPerPx, across = 1f - offsetPx * HintSquashPerPx;
            return axis == Axis.Row ? new Vector3(along, across, 1f) : new Vector3(across, along, 1f);
        }

        /// <summary>
        /// Lets the bits show this move. False if nothing on the board can show it, so the caller marks it another way.
        /// </summary>
        public bool ShowMoveHint(Move move, ulong bits)
        {
            ClearMoveHint();
            if (cells == null || !rules.TryApply(bits, move, out var outcome))
            {
                return false;
            }

            ulong carried = MovingCells(move) & bits;
            ulong created = carried == 0 ? outcome.After & ~bits : 0;
            if (carried == 0 && created == 0)
            {
                return false;
            }

            hintBits = carried;
            hintStart = Time.time;
            hintAxis = move.Axis;
            hintStep = StepVector(move.Direction);
            WakeUp();

            for (int line = 0; line < BoardBits.Size; line++)
            {
                int rank = 0;
                for (int position = 0; position < BoardBits.Size; position++)
                {
                    // From the back of the line to its front, so the wave runs the way the move goes.
                    int along = hintStep.x > 0f || hintStep.y < 0f ? position : BoardBits.Size - 1 - position;
                    int row = move.Axis == Axis.Row ? line : along;
                    int column = move.Axis == Axis.Row ? along : line;
                    if (BoardBits.GetCell(carried, row, column))
                    {
                        var face = cells[row, column].Face;
                        face.HintLook = hintStep * StarePx;
                        face.HintWide = BoardBits.GetCell(outcome.LostCells, row, column);
                        face.HintDelay = rank++ * HintWaveDelay;
                    }
                    else if (BoardBits.GetCell(created, row, column))
                    {
                        AddBitToCome(row, column);
                    }
                }
            }

            return true;
        }

        /// <summary>Puts the hinted bits back to rest.</summary>
        public void ClearMoveHint()
        {
            if (cells != null && hintBits != 0)
            {
                for (int row = 0; row < BoardBits.Size; row++)
                {
                    for (int column = 0; column < BoardBits.Size; column++)
                    {
                        if (!BoardBits.GetCell(hintBits, row, column))
                        {
                            continue;
                        }

                        var cell = cells[row, column];
                        cell.Face.HintLook = Vector2.zero;
                        cell.Face.HintWide = false;
                        cell.Bit.transform.localPosition = Vector3.zero;
                        cell.Bit.transform.localScale = Vector3.one;
                    }
                }
            }

            hintBits = 0;
            foreach (var bit in bitsToCome)
            {
                DestroySafe(bit.gameObject);
            }

            bitsToCome.Clear();
        }

        /// <summary>One frame of the wobble. Public so tests can run it at a chosen time.</summary>
        public void TickHintWobble(float elapsed)
        {
            if (hintBits == 0 && bitsToCome.Count == 0)
            {
                return;
            }

            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (BoardBits.GetCell(hintBits, row, column))
                    {
                        var cell = cells[row, column];
                        // A lean still settling from the last move keeps the scale until it is done.
                        bool leaning = BoardBits.GetCell(leanCells, row, column);
                        PlaceWobbling(cell.Bit.transform, HintWobble(elapsed, cell.Face.HintDelay), !leaning);
                    }
                }
            }

            foreach (var bit in bitsToCome)
            {
                PlaceWobbling(bit, HintWobble(elapsed, 0f), true);
            }
        }

        void PlaceWobbling(Transform body, float offsetPx, bool scaled)
        {
            body.localPosition = hintStep * (offsetPx / CellPx);
            if (scaled)
            {
                body.localScale = HintScale(offsetPx, hintAxis);
            }
        }

        void AddBitToCome(int row, int column)
        {
            var color = (Color)Lcd.Ink;
            color.a = BitToComeAlpha;
            var eyeColor = (Color)Lcd.Light;
            eyeColor.a = BitToComeAlpha;
            var body = Renderer(cells[row, column].Ground.transform.parent, "Bit to come",
                ProceduralSprites.RoundedRect(CellPx, CellPx, RadiusPx), color, Layer.Bit);
            var eye = ProceduralSprites.RoundedRect(EyeWidth, EyeHeight, EyeRadius);
            var eyes = new[]
            {
                Renderer(body.transform, "Eye L", eye, eyeColor, Layer.Eyes),
                Renderer(body.transform, "Eye R", eye, eyeColor, Layer.Eyes)
            };
            ApplyEyes(eyes, hintStep * StarePx, 0f);
            bitsToCome.Add(body.transform);
        }
    }
}
