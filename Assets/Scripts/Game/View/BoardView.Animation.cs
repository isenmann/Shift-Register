using System;
using System.Collections;
using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Input;
using UnityEngine;

namespace ShiftRegister.Game.View
{
    /// <summary>
    /// Move animation: the moved part slides one cell, wrapping/carried bits fade out and back in,
    /// lost bits turn red and fly into the bit bucket, gate changes pop, the board shakes when a
    /// rule breaks, and bits hop when the level is solved. The model is already updated; the view
    /// only catches up, and any new Show/Play snaps the running animation to its end.
    /// </summary>
    public sealed partial class BoardView
    {
        sealed class Floater
        {
            public Transform Transform;
            public SpriteRenderer Body;
            public SpriteRenderer[] Eyes;
            public SpriteRenderer[] FlatEyes;
        }

        readonly struct Slide
        {
            public readonly Floater Floater;
            public readonly Vector2 From, To;
            public readonly float AlphaFrom, AlphaTo;

            public Slide(Floater floater, Vector2 from, Vector2 to, float alphaFrom = 1f, float alphaTo = 1f)
            {
                Floater = floater;
                From = from;
                To = to;
                AlphaFrom = alphaFrom;
                AlphaTo = alphaTo;
            }
        }

        const int FloatBodyOrder = 8, FloatEyesOrder = 9;
        static readonly Color32 LostEye = new Color32(0xF1, 0xE3, 0xC8, 0xFF);

        [Header("Animation")]
        [SerializeField] bool animate = true;
        [SerializeField] float shiftTime = 0.13f;
        [SerializeField] float gateTime = 0.15f;
        [SerializeField] float flyTime = 0.55f;

        readonly List<Floater> pool = new List<Floater>();
        readonly List<Floater> live = new List<Floater>();
        ulong settledBits;
        int flights;
        bool busy, hopping;

        /// <summary>False for reduced motion: moves snap into place (the sound events still fire).</summary>
        public bool Animate
        {
            get => animate;
            set
            {
                animate = value;
                if (!value)
                {
                    StopAnimation();
                }
            }
        }

        /// <summary>True while a move animation runs (the solved hop does not count).</summary>
        public bool IsAnimating => busy;

        /// <summary>Raised when a move animation finishes or is cut short.</summary>
        public event Action Settled;

        /// <summary>Raised when a move's gates switch bits on or off (after the slide).</summary>
        public event Action<MoveOutcome> GatesSwitched;

        /// <summary>
        /// Raised when the board reacts to a move: lost bits have landed and the board shakes (broken)
        /// or hops (solved). Not raised when the animation is cut short by the next move.
        /// </summary>
        public event Action<MoveOutcome, bool, bool> Reacted;

        /// <summary>Animates a move that the model has already applied.</summary>
        public void Play(MoveOutcome outcome, bool solved, bool broken)
        {
            StopAnimation();
            settledBits = outcome.After;
            if (!animate || !Application.isPlaying || !isActiveAndEnabled || cells == null)
            {
                ShowNow(outcome.After, outcome.After);
                if (outcome.After != outcome.AfterShift)
                {
                    GatesSwitched?.Invoke(outcome);
                }

                Reacted?.Invoke(outcome, solved, broken);
                Settled?.Invoke();
                return;
            }

            busy = true;
            StartCoroutine(Run(outcome, solved, broken));
        }

        void StopAnimation()
        {
            if (!busy && !hopping && live.Count == 0)
            {
                return;
            }

            StopAllCoroutines();
            foreach (var floater in live)
            {
                floater.Transform.gameObject.SetActive(false);
            }

            pool.AddRange(live);
            live.Clear();
            flights = 0;
            hopping = false;
            if (cellsRoot != null)
            {
                cellsRoot.localPosition = Vector3.zero;
            }

            ShowNow(settledBits, settledBits);
            if (busy)
            {
                busy = false;
                Settled?.Invoke();
            }
        }

        void OnDisable()
        {
            // Coroutines die with the object; finish the animation so the board is never left half-way.
            if (busy || hopping || live.Count > 0)
            {
                StopAnimation();
            }
        }

        /// <summary>Called by Build: the old cells (and floaters under them) are about to be destroyed.</summary>
        void ResetAnimationState()
        {
            StopAllCoroutines();
            pool.Clear();
            live.Clear();
            flights = 0;
            busy = hopping = false;
        }

        IEnumerator Run(MoveOutcome outcome, bool solved, bool broken)
        {
            var move = outcome.Move;
            ulong moving = MovingCells(move) & outcome.Before;
            Vector2 step = StepVector(move.Direction) * ScreenLayout.Pitch;

            // 1. Shift: everything in the moved part slides one cell.
            ShowNow(outcome.Before & ~moving, outcome.After);
            var slides = new List<Slide>();
            ulong arrived = 0;
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (!BoardBits.GetCell(moving, row, column))
                    {
                        continue;
                    }

                    Vector2 startPosition = CellCenter(row, column);
                    if ((outcome.LostCells & BoardBits.CellMask(row, column)) != 0)
                    {
                        StartCoroutine(FlyToBucket(Spawn(startPosition), startPosition, startPosition + step));
                        continue;
                    }

                    var (targetRow, targetColumn, wrap) = Destination(move, row, column);
                    arrived |= BoardBits.CellMask(targetRow, targetColumn);
                    Vector2 endPosition = CellCenter(targetRow, targetColumn);
                    if (!wrap)
                    {
                        slides.Add(new Slide(Spawn(startPosition), startPosition, endPosition));
                    }
                    else
                    {
                        // Wrap (rotate) or carry into the chained row: slide out, slide back in at the destination.
                        Vector2 outside = endPosition - step * 0.6f;
                        slides.Add(new Slide(Spawn(startPosition), startPosition, startPosition + step * 0.6f, 1f, 0f));
                        slides.Add(new Slide(Spawn(outside), outside, endPosition, 0f, 1f));
                    }
                }
            }

            yield return Tween(shiftTime, progress =>
            {
                float eased = EaseOutCubic(progress);
                foreach (var slide in slides)
                {
                    slide.Floater.Transform.localPosition = Vector2.Lerp(slide.From, slide.To, eased);
                    SetAlpha(slide.Floater, Mathf.Lerp(slide.AlphaFrom, slide.AlphaTo, progress));
                }
            });
            foreach (var slide in slides)
            {
                Release(slide.Floater);
            }

            // Bits that came from nowhere (a 1-IN edge) pop in.
            ulong kept = outcome.Before & ~moving;
            ulong fresh = outcome.AfterShift & ~kept & ~arrived;
            ShowNow(outcome.AfterShift, outcome.After);
            if (fresh != 0)
            {
                yield return Pop(fresh, 0, gateTime);
            }

            // 2. Gates: changed cells pop in or shrink away.
            ulong appear = outcome.After & ~outcome.AfterShift;
            ulong vanish = outcome.AfterShift & ~outcome.After;
            if ((appear | vanish) != 0)
            {
                ShowNow(outcome.AfterShift & ~vanish, outcome.After);
                GatesSwitched?.Invoke(outcome);
                yield return Pop(appear, vanish, gateTime);
            }

            ShowNow(outcome.After, outcome.After);

            // 3. Wait for lost bits to land, then react.
            while (flights > 0)
            {
                yield return null;
            }

            Reacted?.Invoke(outcome, solved, broken);
            if (broken)
            {
                yield return Shake(0.3f);
            }

            busy = false;
            Settled?.Invoke();
            if (solved)
            {
                StartCoroutine(Hop());
            }
        }

        /// <summary>
        /// Static bits in <paramref name="appear"/> grow in; floaters at <paramref name="vanish"/> shrink away.
        /// </summary>
        IEnumerator Pop(ulong appear, ulong vanish, float duration)
        {
            var shrinking = new List<Floater>();
            for (int row = 0; row < BoardBits.Size; row++)
            {
                for (int column = 0; column < BoardBits.Size; column++)
                {
                    if (BoardBits.GetCell(vanish, row, column))
                    {
                        shrinking.Add(Spawn(CellCenter(row, column)));
                    }

                    if (BoardBits.GetCell(appear, row, column))
                    {
                        cells[row, column].Bit.transform.localScale = Vector3.zero;
                    }
                }
            }

            yield return Tween(duration, progress =>
            {
                float grow = EaseOutBack(progress);
                for (int row = 0; row < BoardBits.Size; row++)
                {
                    for (int column = 0; column < BoardBits.Size; column++)
                    {
                        if (BoardBits.GetCell(appear, row, column))
                        {
                            cells[row, column].Bit.transform.localScale = Vector3.one * grow;
                        }
                    }
                }

                foreach (var floater in shrinking)
                {
                    floater.Transform.localScale = Vector3.one * (1f - EaseOutCubic(progress));
                }
            });
            foreach (var floater in shrinking)
            {
                Release(floater);
            }
        }

        IEnumerator FlyToBucket(Floater floater, Vector2 from, Vector2 edge)
        {
            flights++;
            yield return Tween(shiftTime,
                progress => floater.Transform.localPosition = Vector2.Lerp(from, edge, EaseOutCubic(progress)));

            SetLost(floater, true);
            Vector3 bucketWorld = ScreenLayout.BucketCenter;
            Vector2 target = cellsRoot.InverseTransformPoint(bucketWorld);
            Vector2 control = (edge + target) / 2f + Vector2.up * 2.5f;
            yield return Tween(flyTime, progress =>
            {
                float eased = EaseInOutQuad(progress);
                floater.Transform.localPosition = Bezier(edge, control, target, eased);
                floater.Transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, eased);
                SetAlpha(floater, progress < 0.8f ? 1f : 1f - (progress - 0.8f) / 0.2f);
            });
            Release(floater);
            flights--;
        }

        IEnumerator Shake(float duration)
        {
            yield return Tween(duration, progress =>
                cellsRoot.localPosition = new Vector3(Mathf.Sin(progress * 60f) * 0.09f * (1f - progress), 0f, 0f));
            cellsRoot.localPosition = Vector3.zero;
        }

        /// <summary>A wave of hops across the board, column by column (the mockup's 8 px then 5 px hop).</summary>
        IEnumerator Hop()
        {
            hopping = true;
            const float duration = 0.5f, stagger = 0.035f;
            float total = duration + stagger * (BoardBits.Size - 1);
            yield return Tween(total, progress =>
            {
                float time = progress * total;
                for (int row = 0; row < BoardBits.Size; row++)
                {
                    for (int column = 0; column < BoardBits.Size; column++)
                    {
                        var bit = cells[row, column].Bit;
                        if (!bit.activeSelf)
                        {
                            continue;
                        }

                        float local = Mathf.Clamp01((time - column * stagger) / duration);
                        float height = local < 0.5f
                            ? Mathf.Sin(Mathf.PI * local / 0.5f) * 8f
                            : Mathf.Sin(Mathf.PI * (local - 0.5f) / 0.5f) * 5f;
                        bit.transform.localPosition = new Vector3(0f, height / CellPx, 0f);
                    }
                }
            });
            hopping = false;
        }

        /// <summary>Cells of the line part a move shifts (both rows for a chain).</summary>
        ulong MovingCells(Move move)
        {
            ulong mask = 0;
            if (move.Axis == Axis.Row)
            {
                int top = rules.ChainTopOf(move.Line);
                if (top >= 0)
                {
                    return (0xFFUL << (top * 8)) | (0xFFUL << ((top + 1) * 8));
                }

                var (first, last) = InputMapping.SegmentRange(rules, Axis.Row, move.Line, move.Segment);
                for (int column = first; column <= last; column++)
                {
                    mask |= BoardBits.CellMask(move.Line, column);
                }

                return mask;
            }

            var (segmentTop, bottom) = InputMapping.SegmentRange(rules, Axis.Column, move.Line, move.Segment);
            for (int row = segmentTop; row <= bottom; row++)
            {
                mask |= BoardBits.CellMask(row, move.Line);
            }

            return mask;
        }

        /// <summary>
        /// Where a (not lost) bit of the moved part ends up; wrap = it leaves one end and enters elsewhere.
        /// </summary>
        (int row, int column, bool wrap) Destination(Move move, int row, int column)
        {
            int step = move.TowardStart ? -1 : 1;
            if (move.Axis == Axis.Row)
            {
                int top = rules.ChainTopOf(row);
                if (top >= 0)
                {
                    int i = (row - top) * BoardBits.Size + column + step;
                    bool wrapped = false;
                    if (i < 0)
                    {
                        i = 2 * BoardBits.Size - 1;
                        wrapped = true;
                    }
                    else if (i >= 2 * BoardBits.Size)
                    {
                        i = 0;
                        wrapped = true;
                    }

                    int newRow = top + i / BoardBits.Size, newColumn = i % BoardBits.Size;
                    return (newRow, newColumn, wrapped || newRow != row);
                }

                var (first, last) = InputMapping.SegmentRange(rules, Axis.Row, row, move.Segment);
                int next = column + step;
                if (next < first)
                {
                    return (row, last, true);
                }

                if (next > last)
                {
                    return (row, first, true);
                }

                return (row, next, false);
            }

            var (segmentTop, bottom) = InputMapping.SegmentRange(rules, Axis.Column, column, move.Segment);
            int nextRow = row + step;
            if (nextRow < segmentTop)
            {
                return (bottom, column, true);
            }

            if (nextRow > bottom)
            {
                return (segmentTop, column, true);
            }

            return (nextRow, column, false);
        }

        static Vector2 StepVector(Direction direction) => direction switch
        {
            Direction.Left => Vector2.left,
            Direction.Right => Vector2.right,
            Direction.Up => Vector2.up,
            _ => Vector2.down
        };

        Floater Spawn(Vector2 localPosition)
        {
            Floater floater;
            if (pool.Count > 0)
            {
                floater = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                floater = CreateFloater();
            }

            floater.Transform.gameObject.SetActive(true);
            floater.Transform.localPosition = localPosition;
            floater.Transform.localScale = Vector3.one;
            SetLost(floater, false);
            SetAlpha(floater, 1f);
            live.Add(floater);
            return floater;
        }

        void Release(Floater floater)
        {
            if (!live.Remove(floater))
            {
                return;
            }

            floater.Transform.gameObject.SetActive(false);
            pool.Add(floater);
        }

        Floater CreateFloater()
        {
            var gameObject = new GameObject("Floating bit");
            var parent = gameObject.transform;
            parent.SetParent(cellsRoot, false);
            var body = ProceduralSprites.RoundedRect(CellPx, CellPx, RadiusPx);
            var eye = ProceduralSprites.RoundedRect(EyeWidth, EyeHeight, EyeRadius);
            var flat = ProceduralSprites.RoundedRect(8f, 3f, 1f);
            var floater = new Floater
            {
                Transform = parent,
                Body = Renderer(parent, "Body", body, Lcd.Ink, Layer.Bit),
                Eyes = new[]
                {
                    Renderer(parent, "Eye L", eye, Lcd.Light, Layer.Eyes),
                    Renderer(parent, "Eye R", eye, Lcd.Light, Layer.Eyes)
                },
                FlatEyes = new[]
                {
                    Renderer(parent, "Flat Eye L", flat, LostEye, Layer.Eyes),
                    Renderer(parent, "Flat Eye R", flat, LostEye, Layer.Eyes)
                }
            };
            floater.Body.sortingOrder = FloatBodyOrder;
            floater.Eyes[0].transform.localPosition = floater.FlatEyes[0].transform.localPosition = new Vector2(-EyeX,
                EyeY) / CellPx;
            floater.Eyes[1].transform.localPosition = floater.FlatEyes[1].transform.localPosition = new Vector2(EyeX,
                EyeY) / CellPx;
            floater.FlatEyes[0].transform.localPosition += new Vector3(0f, -1.5f / CellPx, 0f);
            floater.FlatEyes[1].transform.localPosition += new Vector3(0f, -1.5f / CellPx, 0f);
            foreach (var eyeRenderer in floater.Eyes)
            {
                eyeRenderer.sortingOrder = FloatEyesOrder;
            }

            foreach (var eyeRenderer in floater.FlatEyes)
            {
                eyeRenderer.sortingOrder = FloatEyesOrder;
            }

            return floater;
        }

        static void SetLost(Floater floater, bool lost)
        {
            floater.Body.color = WithAlpha(lost ? Lcd.Loss : Lcd.Ink, floater.Body.color.a);
            foreach (var eyeRenderer in floater.Eyes)
            {
                eyeRenderer.enabled = !lost;
            }

            foreach (var eyeRenderer in floater.FlatEyes)
            {
                eyeRenderer.enabled = lost;
            }
        }

        static void SetAlpha(Floater floater, float alpha)
        {
            floater.Body.color = WithAlpha(floater.Body.color, alpha);
            foreach (var eyeRenderer in floater.Eyes)
            {
                eyeRenderer.color = WithAlpha(eyeRenderer.color, alpha);
            }

            foreach (var eyeRenderer in floater.FlatEyes)
            {
                eyeRenderer.color = WithAlpha(eyeRenderer.color, alpha);
            }
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        static IEnumerator Tween(float duration, Action<float> apply)
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                apply(elapsed / duration);
                yield return null;
            }

            apply(1f);
        }

        static Vector2 Bezier(Vector2 start, Vector2 control, Vector2 end, float progress) =>
            Vector2.Lerp(Vector2.Lerp(start, control, progress), Vector2.Lerp(control, end, progress), progress);

        static float EaseOutCubic(float progress) => 1f - Mathf.Pow(1f - progress, 3f);
        static float EaseInOutQuad(float progress) =>
            progress < 0.5f ? 2f * progress * progress : 1f - Mathf.Pow(-2f * progress + 2f, 2f) / 2f;

        static float EaseOutBack(float progress)
        {
            const float overshoot = 1.70158f, overshootPlusOne = overshoot + 1f;
            return 1f + overshootPlusOne * Mathf.Pow(progress - 1f, 3f) + overshoot * Mathf.Pow(progress - 1f, 2f);
        }
    }
}
