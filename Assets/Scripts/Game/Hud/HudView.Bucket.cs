using System;
using System.Collections.Generic;
using ShiftRegister.Core;
using ShiftRegister.Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The residents of the bit bucket: lost bits stay visible as a small red pile that fills from the bottom. The
    /// card only has room for so many, so the pile stops growing when it is full and the count stays exact. Now and
    /// then one resident looks around, and they all glance at the board when another bit lands.
    /// </summary>
    public sealed partial class HudView
    {
        sealed class Resident
        {
            public Image Body;
            public RectTransform LeftEye, RightEye;
            public Vector2 LeftRest, RightRest;
            public Vector2 Look, LookTarget;
            public float LookUntil;
        }

        sealed class Pile
        {
            public readonly List<Resident> Residents = new List<Resident>();
            public int Columns, Rows, Shown;
        }

        const float ResidentLookPx = 1.5f, ResidentLookRate = 10f;
        const float ResidentLookTime = 0.9f, ResidentGlanceTime = 0.5f;
        const float ResidentGapMin = 3.5f, ResidentGapMax = 6.5f;

        readonly System.Random residentRandom = new System.Random();
        float nextResidentLook;

        /// <summary>How many residents a pile shows: the count on the card stays exact when there are more.</summary>
        public static int ResidentsShown(int lost, int columns, int rows) => Math.Min(lost, columns * rows);

        /// <param name="bottom">The mockup y of the pile's bottom edge; it grows upwards from there.</param>
        void BuildPile(Layout layout, Transform frame, float x, float bottom, int columns, int rows, float size,
            float pitch)
        {
            var pile = new Pile { Columns = columns, Rows = rows };
            float radius = size * 8f / 46f;
            float eyeWidth = Mathf.Max(3f, size * 8f / 46f), eyeHeight = Mathf.Max(1.5f, size * 3f / 46f);
            float eyeX = size * 8f / 46f, eyeY = size * 0.5f - size * 5f / 46f - eyeHeight / 2f;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float top = bottom - size - row * pitch;
                    string name = $"Resident {pile.Residents.Count + 1}";
                    var body = Ui.Box(frame, name, x + column * pitch, top, size, size, radius, Lcd.Loss);
                    var left = Ui.Box(body.transform, "Eye L", size / 2f - eyeX - eyeWidth / 2f, eyeY, eyeWidth,
                        eyeHeight, 0.5f, Lcd.LostEye).rectTransform;
                    var right = Ui.Box(body.transform, "Eye R", size / 2f + eyeX - eyeWidth / 2f, eyeY, eyeWidth,
                        eyeHeight, 0.5f, Lcd.LostEye).rectTransform;
                    body.gameObject.SetActive(false);
                    pile.Residents.Add(new Resident
                    {
                        Body = body,
                        LeftEye = left,
                        RightEye = right,
                        LeftRest = left.anchoredPosition,
                        RightRest = right.anchoredPosition
                    });
                }
            }

            layout.Pile = pile;
        }

        /// <summary>Text above the pile takes room: a phone's rule line or a broken rule leaves fewer rows.</summary>
        static int PileRows(Layout layout, LevelRules rules, bool broken)
        {
            bool textTakesRoom = broken || (layout.Phone && rules.LossRule != LossRule.Any);
            return textTakesRoom ? Mathf.Max(1, layout.Pile.Rows / 2) : layout.Pile.Rows;
        }

        void RefreshPile(Layout layout, LevelRules rules, int lost, bool broken)
        {
            var pile = layout.Pile;
            if (pile == null)
            {
                return;
            }

            pile.Shown = game.Settings.bitLife
                ? ResidentsShown(lost, pile.Columns, PileRows(layout, rules, broken))
                : 0;
            for (int i = 0; i < pile.Residents.Count; i++)
            {
                var body = pile.Residents[i].Body;
                body.gameObject.SetActive(i < pile.Shown);
                body.rectTransform.localScale = Vector3.one;
            }
        }

        /// <summary>A bit just landed: the newest resident pops, and the others look over at the board.</summary>
        void OnBitsLanded()
        {
            var pile = Active.Pile;
            if (pile == null || pile.Shown == 0)
            {
                return;
            }

            StartCoroutine(Bump(pile.Residents[pile.Shown - 1].Body.rectTransform));
            float now = Time.time;
            for (int i = 0; i < pile.Shown; i++)
            {
                pile.Residents[i].LookTarget = TowardBoard;
                pile.Residents[i].LookUntil = now + ResidentGlanceTime;
            }
        }

        static Vector2 TowardBoard => ScreenLayout.Portrait ? Vector2.down : Vector2.left;

        /// <summary>
        /// One resident looks around every few seconds. Off while the board's bits nap: a long think stays quiet.
        /// </summary>
        void UpdatePile()
        {
            var pile = Active.Pile;
            if (pile == null || pile.Shown == 0)
            {
                return;
            }

            float now = Time.time;
            if (now >= nextResidentLook)
            {
                nextResidentLook = now + Mathf.Lerp(ResidentGapMin, ResidentGapMax,
                    (float)residentRandom.NextDouble());
                if (game.BoardView.IdleLife && !game.BoardView.IsDozing)
                {
                    var resident = pile.Residents[residentRandom.Next(pile.Shown)];
                    resident.LookTarget = residentRandom.Next(2) == 0 ? TowardBoard : -TowardBoard;
                    resident.LookUntil = now + ResidentLookTime;
                }
            }

            float blend = 1f - Mathf.Exp(-Time.deltaTime * ResidentLookRate);
            for (int i = 0; i < pile.Shown; i++)
            {
                var resident = pile.Residents[i];
                Vector2 target = now < resident.LookUntil ? resident.LookTarget * ResidentLookPx : Vector2.zero;
                if (resident.Look == target)
                {
                    continue;
                }

                resident.Look = (resident.Look - target).sqrMagnitude < 0.0001f
                    ? target
                    : Vector2.Lerp(resident.Look, target, blend);
                resident.LeftEye.anchoredPosition = resident.LeftRest + resident.Look;
                resident.RightEye.anchoredPosition = resident.RightRest + resident.Look;
            }
        }
    }
}
