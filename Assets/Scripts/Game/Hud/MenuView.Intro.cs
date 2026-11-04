using System.Collections;
using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The first title screen after startup: black (matching Unity's splash screen with the developer
    /// logo) lifts off the title, and the smiley's bits wake up column by column. A key or click skips
    /// it; reduced motion skips it altogether.
    /// </summary>
    public sealed partial class MenuView
    {
        sealed class TitleBit
        {
            public int Column;
            public Image Body, EyeL, EyeR;
        }

        readonly List<TitleBit> titleBits = new List<TitleBit>();

        const float RevealTime = 0.35f;

        Image reveal;
        Coroutine introRoutine;
        bool introRunning, introPlayed;

        static bool SkipPressed(Keyboard kb)
        {
            var p = Pointer.current;
            return kb.anyKey.wasPressedThisFrame || (p != null && p.press.wasPressedThisFrame);
        }

        void BuildReveal(RectTransform canvas)
        {
            // Full screen (not the 1280×720 frame), on top of every menu.
            reveal = Ui.Fill(canvas, "Reveal").gameObject.AddComponent<Image>();
            reveal.color = Color.black;
            reveal.raycastTarget = false;
            reveal.enabled = false;
        }

        void StartIntro()
        {
            introPlayed = true;
            if (game.Settings.reducedMotion || titleBits.Count == 0)
            {
                return;
            }

            introRoutine = StartCoroutine(Intro());
        }

        /// <summary>
        /// The black fades out while the bits pop in column by column, each column with its shift
        /// note; then they open their eyes and blink.
        /// </summary>
        IEnumerator Intro()
        {
            introRunning = true;
            reveal.enabled = true;
            const float start = 0.35f, step = 0.09f, pop = 0.2f, blink = 0.08f;
            float eyes = start + N * step + 0.3f, blinkAt = eyes + 0.6f, end = blinkAt + blink;
            int noted = -1;
            bool awake = false;
            for (float t = 0f; t < end; t += Time.unscaledDeltaTime)
            {
                reveal.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(t / RevealTime));
                int column = Mathf.Min(Mathf.FloorToInt((t - start) / step), N - 1);
                while (noted < column)
                {
                    noted++;
                    if (titleBits.Exists(b => b.Column == noted))
                    {
                        Sfx.PlayShift(Axis.Column, noted, true);
                    }
                }

                foreach (var b in titleBits)
                {
                    float k = Mathf.Clamp01((t - start - b.Column * step) / pop);
                    b.Body.color = k > 0f ? Lcd.Ink : Lcd.Background;
                    b.Body.rectTransform.localScale = Vector3.one * (k > 0f ? EaseOutBack(k) : 1f);
                }

                if (t >= eyes && !awake)
                {
                    awake = true;
                    Sfx.Play(Sound.Solved);
                }

                SetEyes(t >= eyes && !(t >= blinkAt && t < blinkAt + blink));
                yield return null;
            }

            introRoutine = null;
            FinishIntro();
        }

        /// <summary>Jumps the intro to its end (also when leaving the title screen).</summary>
        void FinishIntro()
        {
            if (introRoutine != null)
            {
                StopCoroutine(introRoutine);
            }

            introRoutine = null;
            introRunning = false;
            if (reveal != null)
            {
                reveal.enabled = false;
            }

            foreach (var b in titleBits)
            {
                b.Body.color = Lcd.Ink;
                b.Body.rectTransform.localScale = Vector3.one;
            }

            SetEyes(true);
        }

        void SetEyes(bool open)
        {
            foreach (var b in titleBits)
            {
                b.EyeL.enabled = b.EyeR.enabled = open;
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
