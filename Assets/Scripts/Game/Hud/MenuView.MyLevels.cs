using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.CustomLevels;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// The MY LEVELS screen: the player's own levels on the left, the chosen one on the right with PLAY,
    /// EDIT, COPY, DELETE and EXPORT. Arrows or the mouse wheel move through the list; Enter plays, E edits,
    /// N makes a new level, I imports a share code, X exports one, D copies, Delete (pressed twice) deletes,
    /// Esc goes back.
    /// </summary>
    public sealed partial class MenuView
    {
        const int VisibleRows = 9;
        const string NewLevelName = "MY LEVEL";
        const float DeleteConfirmTime = 3f;

        sealed class MyLevelRow
        {
            public RectTransform Root;
            public Image Ground;
            public TextMeshProUGUI Name, Status;
        }

        readonly MyLevelRow[] myRows = new MyLevelRow[VisibleRows];
        readonly Image[] myStart = new Image[N * N], myTarget = new Image[N * N];
        TextMeshProUGUI myCount, myName, myProof, myBest, myEmpty, myStartLabel, myTargetLabel;
        HudButton myPlay, myEdit, myDuplicate, myDelete, myNew, myImport, myExport;
        ShareDialog myShare;
        int mySelected, myTop, myEnteredFrame;
        float deleteArmedUntil;

        CustomLevelLibrary Library => game.CustomLevels;
        CustomLevel MySelected => mySelected >= 0 && mySelected < Library.levels.Count ? Library.levels[mySelected] : null;

        void BuildMyLevels(Screens s)
        {
            var root = s.MyLevels;
            var left = Outer(s, root, "Left", -1f, 0f);
            var right = Outer(s, root, "Right", 1f, 0f);

            Ui.Text(left, "Heading", 56, 36, 400, 48, displayBold, 34, Lcd.Ink, text: "MY LEVELS");
            myCount = Ui.Text(left, "Count", 56, 80, 640, 18, mono, 13, Lcd.Text2);
            HudButton.Create(right, "BACK", 1124, 38, 100, 44, display, 14, () => flow.Show(GameScreen.Title));

            const float x = 56f, top = 104f, w = 640f, h = 52f, pitch = 58f;
            for (int i = 0; i < VisibleRows; i++)
            {
                int visible = i;
                var rowRoot = Ui.Rect(left, $"Row {i + 1}", x, top + i * pitch, w, h);
                var row = new MyLevelRow
                {
                    Root = rowRoot,
                    Ground = Ui.Box(rowRoot, "Ground", 0, 0, w, h, 8f, Lcd.Cell),
                    Name = Ui.Text(rowRoot, "Name", 16, 0, 340, h, display, 16, Lcd.Ink),
                    Status = Ui.Text(rowRoot, "Status", w - 276, 0, 260, h, mono, 13, Lcd.Text2, TextAlignmentOptions.MidlineRight)
                };
                UiHotspot.Create(rowRoot, "Hotspot", 0, 0, w, h).Click = () =>
                {
                    Sfx.Play(Sound.Tick);
                    SelectMyLevel(myTop + visible);
                };
                myRows[i] = row;
            }
            myEmpty = Ui.Text(left, "Empty", x, top, w, 60, mono, 15, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "No levels yet. NEW starts one on an empty board.");

            myNew = HudButton.CreateSolid(left, "NEW", x, 636, 140, 44, display, 14, NewLevel);
            myImport = HudButton.Create(left, "IMPORT", x + 148, 636, 140, 44, display, 14, ImportLevel);
            Ui.Text(left, "Keys", x, 688, 1168, 20, mono, 13, Lcd.Text3,
                text: "Enter play · E edit · N new · I import · X export · D copy · Del delete · Esc back");

            const float px = 732f, pw = 492f;
            myName = Ui.Text(right, "Name", px, 104, pw, 36, display, 26, Lcd.Ink);
            myProof = Ui.Text(right, "Proof", px, 144, pw, 20, mono, 14, Lcd.Text2);
            myBest = Ui.Text(right, "Best", px, 166, pw, 20, mono, 14, Lcd.Text2);

            const float thumbY = 226f, cell = 20f, gap = 3f, thumbW = N * cell + (N - 1) * gap;
            myStartLabel = Ui.Text(right, "Start Label", px, thumbY - 26, thumbW, 20, display, 13, Lcd.Ink, text: "START");
            myTargetLabel = Ui.Text(right, "Target Label", px + thumbW + 40, thumbY - 26, thumbW, 20, display, 13, Lcd.Ink, text: "TARGET");
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                myStart[r * N + c] = Ui.Box(right, $"Start {r + 1},{c + 1}", px + c * (cell + gap), thumbY + r * (cell + gap), cell, cell, 4f, Lcd.Cell);
                myTarget[r * N + c] = Ui.Box(right, $"Target {r + 1},{c + 1}", px + thumbW + 40 + c * (cell + gap), thumbY + r * (cell + gap), cell, cell, 4f, Lcd.Cell);
            }

            myPlay = HudButton.CreateSolid(right, "PLAY", px, 440, pw, 52, display, 18, PlayMyLevel);
            float third = (pw - 16f) / 3f;
            myEdit = HudButton.Create(right, "EDIT", px, 500, third, 44, display, 14, EditMyLevel);
            myDuplicate = HudButton.Create(right, "COPY", px + third + 8f, 500, third, 44, display, 14, DuplicateMyLevel);
            myDelete = HudButton.Create(right, "DELETE", px + 2f * (third + 8f), 500, third, 44, display, 14, DeleteMyLevel);
            myExport = HudButton.Create(right, "EXPORT", px, 552, pw, 44, display, 14, ExportMyLevel);

            // Last, so it covers the screen.
            myShare = ShareDialog.Create(root, 1280f, 720f, display, mono);
        }

        void EnterMyLevels(bool arriving)
        {
            if (arriving)
            {
                myEnteredFrame = Time.frameCount;
                myShare.Close();
            }
            SelectMyLevel(mySelected);
        }

        void UpdateMyLevels(Keyboard kb)
        {
            if (myShare.IsOpen) return;
            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil) DisarmDelete();

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f) ScrollMyLevels(wheel > 0f ? -1 : 1);
            }

            // The key that came back here (Enter on the solved banner) must not also act on the list.
            if (Time.frameCount - myEnteredFrame <= 1) return;

            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) StepMyLevel(-1);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) StepMyLevel(1);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) PlayMyLevel();
            else if (kb.eKey.wasPressedThisFrame) EditMyLevel();
            else if (kb.nKey.wasPressedThisFrame) NewLevel();
            else if (kb.iKey.wasPressedThisFrame) ImportLevel();
            else if (kb.xKey.wasPressedThisFrame) ExportMyLevel();
            else if (kb.dKey.wasPressedThisFrame) DuplicateMyLevel();
            else if (kb.deleteKey.wasPressedThisFrame) DeleteMyLevel();
        }

        void StepMyLevel(int delta)
        {
            int count = Library.levels.Count;
            if (count == 0) return;
            int next = Mathf.Clamp(mySelected + delta, 0, count - 1);
            if (next != mySelected) Sfx.Play(Sound.Tick);
            SelectMyLevel(next);
        }

        void ScrollMyLevels(int delta)
        {
            int maxTop = Mathf.Max(0, Library.levels.Count - VisibleRows);
            myTop = Mathf.Clamp(myTop + delta, 0, maxTop);
            RefreshMyLevels();
        }

        void SelectMyLevel(int index)
        {
            int count = Library.levels.Count;
            mySelected = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            if (mySelected < myTop) myTop = mySelected;
            if (mySelected >= myTop + VisibleRows) myTop = mySelected - VisibleRows + 1;
            myTop = Mathf.Clamp(myTop, 0, Mathf.Max(0, count - VisibleRows));
            DisarmDelete();
            RefreshMyLevels();
        }

        /// <summary>Esc on the open share dialog closes it. False if it was not open.</summary>
        public bool CloseShare() => myShare != null && myShare.Close();

        void ImportLevel()
        {
            if (Library.IsFull)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            myShare.OpenImport(code =>
            {
                var entry = Library.Import(code, DateTime.UtcNow.Ticks, out var error);
                if (entry == null) return CustomLevelLibrary.ImportMessage(error);
                CustomLevelStore.Save(Library);
                SelectMyLevel(Library.levels.Count - 1);
                return null;
            });
        }

        void ExportMyLevel()
        {
            var entry = MySelected;
            if (entry == null || entry.proof == LevelProof.None) Sfx.Play(Sound.Blocked);
            else myShare.OpenExport(entry.level);
        }

        void NewLevel()
        {
            var entry = Library.Add(new LevelData { name = NewLevelName }, LevelProof.None, DateTime.UtcNow.Ticks);
            if (entry == null)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            CustomLevelStore.Save(Library);
            mySelected = Library.levels.Count - 1;
            flow.EditLevel(entry.level.id);
        }

        void PlayMyLevel()
        {
            var entry = MySelected;
            if (entry == null || !IsPlayable(entry))
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            flow.PlayCustom(entry, GameScreen.MyLevels);
        }

        /// <summary>A level whose start already is its target, or where nothing can move, has nothing to play.</summary>
        static bool IsPlayable(CustomLevel entry) => new LevelEdit(entry.level, entry.proof).Problems().Count == 0;

        void EditMyLevel()
        {
            var entry = MySelected;
            if (entry == null) Sfx.Play(Sound.Blocked);
            else flow.EditLevel(entry.level.id);
        }

        void DuplicateMyLevel()
        {
            var entry = MySelected;
            var copy = entry == null ? null : Library.Duplicate(entry.level.id, DateTime.UtcNow.Ticks);
            if (copy == null)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            CustomLevelStore.Save(Library);
            SelectMyLevel(Library.levels.Count - 1);
        }

        /// <summary>The first press arms the button; a second press within a few seconds deletes.</summary>
        void DeleteMyLevel()
        {
            var entry = MySelected;
            if (entry == null)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            if (deleteArmedUntil <= 0f)
            {
                deleteArmedUntil = Time.unscaledTime + DeleteConfirmTime;
                myDelete.Text = "SURE?";
                myDelete.Selected = true;
                return;
            }
            Library.Remove(entry.level.id);
            CustomLevelStore.Save(Library);
            SelectMyLevel(mySelected);
        }

        void DisarmDelete()
        {
            deleteArmedUntil = 0f;
            if (myDelete == null) return;
            myDelete.Text = "DELETE";
            myDelete.Selected = false;
        }

        void RefreshMyLevels()
        {
            var levels = Library.levels;
            int count = levels.Count;
            string range = count > VisibleRows ? $" · showing {myTop + 1}–{Mathf.Min(myTop + VisibleRows, count)}" : "";
            myCount.text = $"{count} of {CustomLevelLibrary.MaxLevels} levels{range}";
            myEmpty.gameObject.SetActive(count == 0);

            for (int i = 0; i < VisibleRows; i++)
            {
                var row = myRows[i];
                int index = myTop + i;
                bool shown = index < count;
                row.Root.gameObject.SetActive(shown);
                if (!shown) continue;
                bool selected = index == mySelected;
                row.Ground.color = selected ? Lcd.Ink : Lcd.Cell;
                row.Name.color = selected ? Lcd.Light : Lcd.Ink;
                row.Status.color = selected ? Lcd.Light : Lcd.Text2;
                row.Name.text = DisplayName(levels[index]);
                row.Status.text = ProofLine(levels[index]);
            }

            var entry = MySelected;
            bool any = entry != null;
            myPlay.Disabled = !any || !IsPlayable(entry);
            myEdit.Disabled = myDelete.Disabled = !any;
            myDuplicate.Disabled = !any || Library.IsFull;
            myNew.Disabled = myImport.Disabled = Library.IsFull;
            myExport.Disabled = !any || entry.proof == LevelProof.None;

            myName.text = any ? DisplayName(entry) : "";
            myProof.text = any ? ProofLine(entry) : "";
            myBest.text = any && entry.bestMoves > 0 ? $"your best: {entry.bestMoves} moves" : "";
            myStartLabel.enabled = myTargetLabel.enabled = any;
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                int k = r * N + c;
                myStart[k].enabled = myTarget[k].enabled = any;
                if (!any) continue;
                myStart[k].color = ThumbCell(entry.level, entry.level.start, r, c);
                myTarget[k].color = ThumbCell(entry.level, entry.level.target, r, c);
            }
        }

        static Color32 ThumbCell(LevelData level, int[] picture, int r, int c)
        {
            int m = 0x80 >> c;
            if ((level.walls[r] & m) != 0) return Lcd.Text3;
            return (picture[r] & m) != 0 ? Lcd.Ink : Lcd.Cell;
        }

        static string DisplayName(CustomLevel entry) =>
            string.IsNullOrEmpty(entry.level.name) ? "UNTITLED" : entry.level.name.ToUpperInvariant();

        static string ProofLine(CustomLevel entry)
        {
            switch (entry.proof)
            {
                case LevelProof.Searched: return $"par {entry.level.par} · shortest";
                case LevelProof.Replayed: return $"par {entry.level.par} · best so far";
                default: return "not proven";
            }
        }
    }
}
