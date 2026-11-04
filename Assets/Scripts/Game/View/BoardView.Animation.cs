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
        [Header("Animation")]
        [SerializeField] bool animate = true;
        [SerializeField] float shiftTime = 0.13f;
        [SerializeField] float gateTime = 0.15f;
        [SerializeField] float flyTime = 0.55f;

        const int FloatBodyOrder = 8, FloatEyesOrder = 9;
        static readonly Color32 LostEye = new Color32(0xF1, 0xE3, 0xC8, 0xFF);

        sealed class Floater
        {
            public Transform T;
            public SpriteRenderer Body;
            public SpriteRenderer[] Eyes;
            public SpriteRenderer[] FlatEyes;
        }

        readonly struct Slide
        {
            public readonly Floater F;
            public readonly Vector2 From, To;
            public readonly float AlphaFrom, AlphaTo;

            public Slide(Floater f, Vector2 from, Vector2 to, float alphaFrom = 1f, float alphaTo = 1f)
            {
                F = f;
                From = from;
                To = to;
                AlphaFrom = alphaFrom;
                AlphaTo = alphaTo;
            }
        }

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
            foreach (var f in live)
            {
                f.T.gameObject.SetActive(false);
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

        IEnumerator Run(MoveOutcome o, bool solved, bool broken)
        {
            var m = o.Move;
            ulong moving = MovingCells(m) & o.Before;
            Vector2 step = StepVector(m.Direction) * ScreenLayout.Pitch;

            // 1. Shift: everything in the moved part slides one cell.
            ShowNow(o.Before & ~moving, o.After);
            var slides = new List<Slide>();
            ulong arrived = 0;
            for (int r = 0; r < BoardBits.Size; r++)
            {
                for (int c = 0; c < BoardBits.Size; c++)
                {
                    if (!BoardBits.GetCell(moving, r, c))
                    {
                        continue;
                    }

                    Vector2 p = CellCenter(r, c);
                    if ((o.LostCells & BoardBits.CellMask(r, c)) != 0)
                    {
                        StartCoroutine(FlyToBucket(Spawn(p), p, p + step));
                        continue;
                    }

                    var (r2, c2, wrap) = Destination(m, r, c);
                    arrived |= BoardBits.CellMask(r2, c2);
                    Vector2 q = CellCenter(r2, c2);
                    if (!wrap)
                    {
                        slides.Add(new Slide(Spawn(p), p, q));
                    }
                    else
                    {
                        // Wrap (rotate) or carry into the chained row: slide out, slide back in at the destination.
                        Vector2 outside = q - step * 0.6f;
                        slides.Add(new Slide(Spawn(p), p, p + step * 0.6f, 1f, 0f));
                        slides.Add(new Slide(Spawn(outside), outside, q, 0f, 1f));
                    }
                }
            }

            yield return Tween(shiftTime, t =>
            {
                float e = EaseOutCubic(t);
                foreach (var s in slides)
                {
                    s.F.T.localPosition = Vector2.Lerp(s.From, s.To, e);
                    SetAlpha(s.F, Mathf.Lerp(s.AlphaFrom, s.AlphaTo, t));
                }
            });
            foreach (var s in slides)
            {
                Release(s.F);
            }

            // Bits that came from nowhere (a 1-IN edge) pop in.
            ulong kept = o.Before & ~moving;
            ulong fresh = o.AfterShift & ~kept & ~arrived;
            ShowNow(o.AfterShift, o.After);
            if (fresh != 0)
            {
                yield return Pop(fresh, 0, gateTime);
            }

            // 2. Gates: changed cells pop in or shrink away.
            ulong appear = o.After & ~o.AfterShift;
            ulong vanish = o.AfterShift & ~o.After;
            if ((appear | vanish) != 0)
            {
                ShowNow(o.AfterShift & ~vanish, o.After);
                GatesSwitched?.Invoke(o);
                yield return Pop(appear, vanish, gateTime);
            }

            ShowNow(o.After, o.After);

            // 3. Wait for lost bits to land, then react.
            while (flights > 0)
            {
                yield return null;
            }

            Reacted?.Invoke(o, solved, broken);
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

        /// <summary>Static bits in <paramref name="appear"/> grow in; floaters at <paramref name="vanish"/> shrink away.</summary>
        IEnumerator Pop(ulong appear, ulong vanish, float duration)
        {
            var shrinking = new List<Floater>();
            for (int r = 0; r < BoardBits.Size; r++)
            {
                for (int c = 0; c < BoardBits.Size; c++)
                {
                    if (BoardBits.GetCell(vanish, r, c))
                    {
                        shrinking.Add(Spawn(CellCenter(r, c)));
                    }

                    if (BoardBits.GetCell(appear, r, c))
                    {
                        cells[r, c].Bit.transform.localScale = Vector3.zero;
                    }
                }
            }

            yield return Tween(duration, t =>
            {
                float grow = EaseOutBack(t);
                for (int r = 0; r < BoardBits.Size; r++)
                {
                    for (int c = 0; c < BoardBits.Size; c++)
                    {
                        if (BoardBits.GetCell(appear, r, c))
                        {
                            cells[r, c].Bit.transform.localScale = Vector3.one * grow;
                        }
                    }
                }

                foreach (var f in shrinking)
                {
                    f.T.localScale = Vector3.one * (1f - EaseOutCubic(t));
                }
            });
            foreach (var f in shrinking)
            {
                Release(f);
            }
        }

        IEnumerator FlyToBucket(Floater f, Vector2 from, Vector2 edge)
        {
            flights++;
            yield return Tween(shiftTime, t => f.T.localPosition = Vector2.Lerp(from, edge, EaseOutCubic(t)));

            SetLost(f, true);
            Vector3 bucketWorld = ScreenLayout.BucketCenter;
            Vector2 target = cellsRoot.InverseTransformPoint(bucketWorld);
            Vector2 control = (edge + target) / 2f + Vector2.up * 2.5f;
            yield return Tween(flyTime, t =>
            {
                float e = EaseInOutQuad(t);
                f.T.localPosition = Bezier(edge, control, target, e);
                f.T.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, e);
                SetAlpha(f, t < 0.8f ? 1f : 1f - (t - 0.8f) / 0.2f);
            });
            Release(f);
            flights--;
        }

        IEnumerator Shake(float duration)
        {
            yield return Tween(duration, t =>
                cellsRoot.localPosition = new Vector3(Mathf.Sin(t * 60f) * 0.09f * (1f - t), 0f, 0f));
            cellsRoot.localPosition = Vector3.zero;
        }

        /// <summary>A wave of hops across the board, column by column (the mockup's 8 px then 5 px hop).</summary>
        IEnumerator Hop()
        {
            hopping = true;
            const float duration = 0.5f, stagger = 0.035f;
            float total = duration + stagger * (BoardBits.Size - 1);
            yield return Tween(total, t =>
            {
                float time = t * total;
                for (int r = 0; r < BoardBits.Size; r++)
                {
                    for (int c = 0; c < BoardBits.Size; c++)
                    {
                        var bit = cells[r, c].Bit;
                        if (!bit.activeSelf)
                        {
                            continue;
                        }

                        float local = Mathf.Clamp01((time - c * stagger) / duration);
                        float h = local < 0.5f
                            ? Mathf.Sin(Mathf.PI * local / 0.5f) * 8f
                            : Mathf.Sin(Mathf.PI * (local - 0.5f) / 0.5f) * 5f;
                        bit.transform.localPosition = new Vector3(0f, h / CellPx, 0f);
                    }
                }
            });
            hopping = false;
        }

        /// <summary>Cells of the line part a move shifts (both rows for a chain).</summary>
        ulong MovingCells(Move m)
        {
            ulong mask = 0;
            if (m.Axis == Axis.Row)
            {
                int top = rules.ChainTopOf(m.Line);
                if (top >= 0)
                {
                    return (0xFFUL << (top * 8)) | (0xFFUL << ((top + 1) * 8));
                }

                var (first, last) = InputMapping.SegmentRange(rules, Axis.Row, m.Line, m.Segment);
                for (int c = first; c <= last; c++)
                {
                    mask |= BoardBits.CellMask(m.Line, c);
                }

                return mask;
            }

            var (top2, bottom) = InputMapping.SegmentRange(rules, Axis.Column, m.Line, m.Segment);
            for (int r = top2; r <= bottom; r++)
            {
                mask |= BoardBits.CellMask(r, m.Line);
            }

            return mask;
        }

        /// <summary>Where a (not lost) bit of the moved part ends up; wrap = it leaves one end and enters elsewhere.</summary>
        (int row, int column, bool wrap) Destination(Move m, int r, int c)
        {
            int d = m.TowardStart ? -1 : 1;
            if (m.Axis == Axis.Row)
            {
                int top = rules.ChainTopOf(r);
                if (top >= 0)
                {
                    int i = (r - top) * BoardBits.Size + c + d;
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

                    int nr = top + i / BoardBits.Size, nc = i % BoardBits.Size;
                    return (nr, nc, wrapped || nr != r);
                }

                var (first, last) = InputMapping.SegmentRange(rules, Axis.Row, r, m.Segment);
                int n = c + d;
                if (n < first)
                {
                    return (r, last, true);
                }

                if (n > last)
                {
                    return (r, first, true);
                }

                return (r, n, false);
            }

            var (top2, bottom) = InputMapping.SegmentRange(rules, Axis.Column, c, m.Segment);
            int nrow = r + d;
            if (nrow < top2)
            {
                return (bottom, c, true);
            }

            if (nrow > bottom)
            {
                return (top2, c, true);
            }

            return (nrow, c, false);
        }

        static Vector2 StepVector(Direction d) => d switch
        {
            Direction.Left => Vector2.left,
            Direction.Right => Vector2.right,
            Direction.Up => Vector2.up,
            _ => Vector2.down
        };

        Floater Spawn(Vector2 localPosition)
        {
            Floater f;
            if (pool.Count > 0)
            {
                f = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
            }
            else
            {
                f = CreateFloater();
            }

            f.T.gameObject.SetActive(true);
            f.T.localPosition = localPosition;
            f.T.localScale = Vector3.one;
            SetLost(f, false);
            SetAlpha(f, 1f);
            live.Add(f);
            return f;
        }

        void Release(Floater f)
        {
            if (!live.Remove(f))
            {
                return;
            }

            f.T.gameObject.SetActive(false);
            pool.Add(f);
        }

        Floater CreateFloater()
        {
            var go = new GameObject("Floating bit");
            var t = go.transform;
            t.SetParent(cellsRoot, false);
            var body = ProceduralSprites.RoundedRect(CellPx, CellPx, RadiusPx);
            var eye = ProceduralSprites.RoundedRect(EyeW, EyeH, EyeRadius);
            var flat = ProceduralSprites.RoundedRect(8f, 3f, 1f);
            var f = new Floater
            {
                T = t,
                Body = Renderer(t, "Body", body, Lcd.Ink, Layer.Bit),
                Eyes = new[]
                {
                    Renderer(t, "Eye L", eye, Lcd.Light, Layer.Eyes),
                    Renderer(t, "Eye R", eye, Lcd.Light, Layer.Eyes)
                },
                FlatEyes = new[]
                {
                    Renderer(t, "Flat Eye L", flat, LostEye, Layer.Eyes),
                    Renderer(t, "Flat Eye R", flat, LostEye, Layer.Eyes)
                }
            };
            f.Body.sortingOrder = FloatBodyOrder;
            f.Eyes[0].transform.localPosition = f.FlatEyes[0].transform.localPosition = new Vector2(-EyeX, EyeY) / CellPx;
            f.Eyes[1].transform.localPosition = f.FlatEyes[1].transform.localPosition = new Vector2(EyeX, EyeY) / CellPx;
            f.FlatEyes[0].transform.localPosition += new Vector3(0f, -1.5f / CellPx, 0f);
            f.FlatEyes[1].transform.localPosition += new Vector3(0f, -1.5f / CellPx, 0f);
            foreach (var e in f.Eyes)
            {
                e.sortingOrder = FloatEyesOrder;
            }

            foreach (var e in f.FlatEyes)
            {
                e.sortingOrder = FloatEyesOrder;
            }

            return f;
        }

        static void SetLost(Floater f, bool lost)
        {
            f.Body.color = WithAlpha(lost ? Lcd.Loss : Lcd.Ink, f.Body.color.a);
            foreach (var e in f.Eyes)
            {
                e.enabled = !lost;
            }

            foreach (var e in f.FlatEyes)
            {
                e.enabled = lost;
            }
        }

        static void SetAlpha(Floater f, float a)
        {
            f.Body.color = WithAlpha(f.Body.color, a);
            foreach (var e in f.Eyes)
            {
                e.color = WithAlpha(e.color, a);
            }

            foreach (var e in f.FlatEyes)
            {
                e.color = WithAlpha(e.color, a);
            }
        }

        static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        static IEnumerator Tween(float duration, Action<float> apply)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                apply(t / duration);
                yield return null;
            }

            apply(1f);
        }

        static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, float t) =>
            Vector2.Lerp(Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), t);

        static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
        static float EaseInOutQuad(float t) => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
