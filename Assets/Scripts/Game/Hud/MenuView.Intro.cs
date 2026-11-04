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
            public Image Body, LeftEye, RightEye;
        }

        readonly List<TitleBit> titleBits = new List<TitleBit>();

        const float RevealTime = 0.35f;

        Image reveal;
        Coroutine introRoutine;
        bool introRunning, introPlayed;

        static bool SkipPressed(Keyboard keyboard)
        {
            var pointer = Pointer.current;
            return keyboard.anyKey.wasPressedThisFrame || (pointer != null && pointer.press.wasPressedThisFrame);
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
            float eyes = start + BoardSize * step + 0.3f, blinkAt = eyes + 0.6f, end = blinkAt + blink;
            int noted = -1;
            bool awake = false;
            for (float elapsed = 0f; elapsed < end; elapsed += Time.unscaledDeltaTime)
            {
                reveal.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(elapsed / RevealTime));
                int column = Mathf.Min(Mathf.FloorToInt((elapsed - start) / step), BoardSize - 1);
                while (noted < column)
                {
                    noted++;
                    if (titleBits.Exists(titleBit => titleBit.Column == noted))
                    {
                        Sfx.PlayShift(Axis.Column, noted, true);
                    }
                }

                foreach (var titleBit in titleBits)
                {
                    float popProgress = Mathf.Clamp01((elapsed - start - titleBit.Column * step) / pop);
                    titleBit.Body.color = popProgress > 0f ? Lcd.Ink : Lcd.Background;
                    titleBit.Body.rectTransform.localScale = Vector3.one * (popProgress > 0f ? EaseOutBack(popProgress)
                        : 1f);
                }

                if (elapsed >= eyes && !awake)
                {
                    awake = true;
                    Sfx.Play(Sound.Solved);
                }

                SetEyes(elapsed >= eyes && !(elapsed >= blinkAt && elapsed < blinkAt + blink));
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

            foreach (var titleBit in titleBits)
            {
                titleBit.Body.color = Lcd.Ink;
                titleBit.Body.rectTransform.localScale = Vector3.one;
            }

            SetEyes(true);
        }

        void SetEyes(bool open)
        {
            foreach (var titleBit in titleBits)
            {
                titleBit.LeftEye.enabled = titleBit.RightEye.enabled = open;
            }
        }

        static float EaseOutBack(float progress)
        {
            const float overshoot = 1.70158f, overshootPlusOne = overshoot + 1f;
            float remaining = progress - 1f;
            return 1f + overshootPlusOne * remaining * remaining * remaining + overshoot * remaining * remaining;
        }
    }
}
