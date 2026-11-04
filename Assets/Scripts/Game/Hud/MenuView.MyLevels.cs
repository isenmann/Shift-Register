using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.CustomLevels;
using ShiftRegister.Game.View;
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
    /// Esc goes back. Three arrangements like the other menus; on phones, dragging the list scrolls it.
    /// </summary>
    public sealed partial class MenuView
    {
        const string NewLevelName = "MY LEVEL";
        const float DeleteConfirmTime = 3f;

        sealed class MyLevelRow
        {
            public RectTransform Root;
            public Image Ground;
            public TextMeshProUGUI Name, Status;
        }

        /// <summary>The MY LEVELS screen in one arrangement.</summary>
        sealed class MyUi
        {
            public MyLevelRow[] Rows;
            public RectTransform List;
            public float RowPitch;
            public readonly Image[] Start = new Image[N * N], Target = new Image[N * N];
            public TextMeshProUGUI Count, Name, Proof, Best, Empty, StartLabel, TargetLabel;
            public HudButton Play, Edit, Duplicate, Delete, New, Import, Export;
            public ShareDialog Share;
        }

        /// <summary>Where one arrangement puts things; all in its own mockup pixels.</summary>
        sealed class MyLayout
        {
            public float FrameW, FrameH;
            public float HeadingX, HeadingY, HeadingW, HeadingH, HeadingSize;
            public float CountX, CountY, CountW;
            public float BackX, BackY, BackW, BackH;
            public float ListX, ListY, ListW, RowH, RowPitch, RowNameW;
            public int Rows;
            public float RowNameSize = 16f, StatusSize = 13f;
            public float PanelX, PanelW, NameY, NameH, NameSize, ProofY, BestY;
            public float LabelY, ThumbY, Cell, ThumbGap, ThumbSpace;
            public float PlayY, PlayH, Row2Y, Row3Y, ButtonH;
            /// <summary>Desktop keeps NEW and IMPORT under the list; phones have them with the other buttons.</summary>
            public bool Desktop;
        }

        int mySelected, myTop, myEnteredFrame;
        float deleteArmedUntil, myDragY;
        bool myDragging;

        MyUi My => Cur.My;
        CustomLevelLibrary Library => game.CustomLevels;
        CustomLevel MySelected => mySelected >= 0 && mySelected < Library.levels.Count ? Library.levels[mySelected] : null;

        static readonly MyLayout MyDesktop = new MyLayout
        {
            Desktop = true, FrameW = 1280, FrameH = 720,
            HeadingX = 56, HeadingY = 36, HeadingW = 400, HeadingH = 48, HeadingSize = 34,
            CountX = 56, CountY = 80, CountW = 640,
            BackX = 1124, BackY = 38, BackW = 100, BackH = 44,
            ListX = 56, ListY = 104, ListW = 640, RowH = 52, RowPitch = 58, RowNameW = 340, Rows = 9,
            PanelX = 732, PanelW = 492, NameY = 104, NameH = 36, NameSize = 26, ProofY = 144, BestY = 166,
            LabelY = 200, ThumbY = 226, Cell = 20, ThumbGap = 3, ThumbSpace = 40,
            PlayY = 440, PlayH = 52, Row2Y = 500, Row3Y = 552, ButtonH = 44
        };

        static readonly MyLayout MyPhoneLandscape = new MyLayout
        {
            FrameW = 915, FrameH = 412,
            HeadingX = 16, HeadingY = 8, HeadingW = 220, HeadingH = 32, HeadingSize = 22,
            CountX = 236, CountY = 14, CountW = 190,
            BackX = 815, BackY = 8, BackW = 84, BackH = 40,
            ListX = 16, ListY = 48, ListW = 410, RowH = 52, RowPitch = 58, RowNameW = 220, Rows = 6,
            RowNameSize = 14, StatusSize = 12,
            PanelX = 442, PanelW = 457, NameY = 10, NameH = 26, NameSize = 18, ProofY = 36, BestY = 54,
            LabelY = 76, ThumbY = 96, Cell = 11, ThumbGap = 2, ThumbSpace = 24,
            PlayY = 218, PlayH = 52, Row2Y = 276, Row3Y = 330, ButtonH = 48
        };

        static readonly MyLayout MyPhonePortrait = new MyLayout
        {
            FrameW = 412, FrameH = 915,
            HeadingX = 16, HeadingY = 28, HeadingW = 280, HeadingH = 36, HeadingSize = 26,
            CountX = 16, CountY = 66, CountW = 380,
            BackX = 312, BackY = 24, BackW = 84, BackH = 44,
            ListX = 16, ListY = 92, ListW = 380, RowH = 52, RowPitch = 58, RowNameW = 210, Rows = 6,
            RowNameSize = 14, StatusSize = 12,
            PanelX = 16, PanelW = 380, NameY = 446, NameH = 28, NameSize = 18, ProofY = 476, BestY = 494,
            LabelY = 518, ThumbY = 540, Cell = 14, ThumbGap = 2, ThumbSpace = 40,
            PlayY = 684, PlayH = 60, Row2Y = 752, Row3Y = 810, ButtonH = 50
        };

        void BuildMyLevels(Screens s, MyLayout k)
        {
            var root = s.MyLevels;
            var left = Outer(s, root, "Left", k.Desktop || k.FrameW > k.FrameH ? -1f : 0f, 0f);
            var right = Outer(s, root, "Right", k.Desktop || k.FrameW > k.FrameH ? 1f : 0f, 0f);
            var ui = new MyUi { RowPitch = k.RowPitch, Rows = new MyLevelRow[k.Rows] };
            s.My = ui;

            Ui.Text(left, "Heading", k.HeadingX, k.HeadingY, k.HeadingW, k.HeadingH, displayBold, k.HeadingSize, Lcd.Ink, text: "MY LEVELS");
            ui.Count = Ui.Text(left, "Count", k.CountX, k.CountY, k.CountW, 18, mono, 13, Lcd.Text2);
            HudButton.Create(right, "BACK", k.BackX, k.BackY, k.BackW, k.BackH, display, 14, () => flow.Show(GameScreen.Title));

            ui.List = Ui.Rect(left, "List", k.ListX, k.ListY, k.ListW, k.Rows * k.RowPitch);
            for (int i = 0; i < k.Rows; i++)
            {
                int visible = i;
                var rowRoot = Ui.Rect(ui.List, $"Row {i + 1}", 0, i * k.RowPitch, k.ListW, k.RowH);
                var row = new MyLevelRow
                {
                    Root = rowRoot,
                    Ground = Ui.Box(rowRoot, "Ground", 0, 0, k.ListW, k.RowH, 8f, Lcd.Cell),
                    Name = Ui.Text(rowRoot, "Name", 14, 0, k.RowNameW, k.RowH, display, k.RowNameSize, Lcd.Ink),
                    Status = Ui.Text(rowRoot, "Status", k.ListW - 200f, 0, 186f, k.RowH, mono, k.StatusSize, Lcd.Text2, TextAlignmentOptions.MidlineRight)
                };
                UiHotspot.Create(rowRoot, "Hotspot", 0, 0, k.ListW, k.RowH).Click = () =>
                {
                    Sfx.Play(Sound.Tick);
                    SelectMyLevel(myTop + visible);
                };
                ui.Rows[i] = row;
            }
            ui.Empty = Ui.Text(left, "Empty", k.ListX, k.ListY, k.ListW, 60, mono, 15, Lcd.Text2, TextAlignmentOptions.TopLeft,
                "No levels yet. NEW starts one on an empty board.");
            ui.Empty.textWrappingMode = TextWrappingModes.Normal;

            ui.Name = Ui.Text(right, "Name", k.PanelX, k.NameY, k.PanelW, k.NameH, display, k.NameSize, Lcd.Ink);
            ui.Proof = Ui.Text(right, "Proof", k.PanelX, k.ProofY, k.PanelW, 18, mono, 13, Lcd.Text2);
            ui.Best = Ui.Text(right, "Best", k.PanelX, k.BestY, k.PanelW, 18, mono, 13, Lcd.Text2);

            float thumbW = N * k.Cell + (N - 1) * k.ThumbGap;
            ui.StartLabel = Ui.Text(right, "Start Label", k.PanelX, k.LabelY, thumbW, 18, display, 13, Lcd.Ink, text: "START");
            ui.TargetLabel = Ui.Text(right, "Target Label", k.PanelX + thumbW + k.ThumbSpace, k.LabelY, thumbW, 18, display, 13, Lcd.Ink, text: "TARGET");
            float radius = Mathf.Min(4f, k.Cell / 4f);
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                float step = k.Cell + k.ThumbGap;
                ui.Start[r * N + c] = Ui.Box(right, $"Start {r + 1},{c + 1}", k.PanelX + c * step, k.ThumbY + r * step, k.Cell, k.Cell, radius, Lcd.Cell);
                ui.Target[r * N + c] = Ui.Box(right, $"Target {r + 1},{c + 1}", k.PanelX + thumbW + k.ThumbSpace + c * step, k.ThumbY + r * step, k.Cell, k.Cell, radius, Lcd.Cell);
            }

            ui.Play = HudButton.CreateSolid(right, "PLAY", k.PanelX, k.PlayY, k.PanelW, k.PlayH, display, 18, PlayMyLevel);
            float third = (k.PanelW - 16f) / 3f;
            ui.Edit = HudButton.Create(right, "EDIT", k.PanelX, k.Row2Y, third, k.ButtonH, display, 14, EditMyLevel);
            ui.Duplicate = HudButton.Create(right, "COPY", k.PanelX + third + 8f, k.Row2Y, third, k.ButtonH, display, 14, DuplicateMyLevel);
            ui.Delete = HudButton.Create(right, "DELETE", k.PanelX + 2f * (third + 8f), k.Row2Y, third, k.ButtonH, display, 14, DeleteMyLevel);

            if (k.Desktop)
            {
                ui.Export = HudButton.Create(right, "EXPORT", k.PanelX, k.Row3Y, k.PanelW, k.ButtonH, display, 14, ExportMyLevel);
                ui.New = HudButton.CreateSolid(left, "NEW", k.ListX, 636, 140, 44, display, 14, NewLevel);
                ui.Import = HudButton.Create(left, "IMPORT", k.ListX + 148, 636, 140, 44, display, 14, ImportLevel);
                Ui.Text(left, "Keys", k.ListX, 688, 1168, 20, mono, 13, Lcd.Text3,
                    text: "Enter play · E edit · N new · I import · X export · D copy · Del delete · Esc back");
            }
            else
            {
                ui.New = HudButton.Create(right, "NEW", k.PanelX, k.Row3Y, third, k.ButtonH, display, 14, NewLevel);
                ui.Import = HudButton.Create(right, "IMPORT", k.PanelX + third + 8f, k.Row3Y, third, k.ButtonH, display, 14, ImportLevel);
                ui.Export = HudButton.Create(right, "EXPORT", k.PanelX + 2f * (third + 8f), k.Row3Y, third, k.ButtonH, display, 14, ExportMyLevel);
            }

            // Last, so it covers the screen.
            ui.Share = ShareDialog.Create(root, k.FrameW, k.FrameH, display, mono);
        }

        void EnterMyLevels(bool arriving)
        {
            if (arriving)
            {
                myEnteredFrame = Time.frameCount;
                myDragging = false;
                My.Share.Close();
            }
            SelectMyLevel(mySelected);
        }

        void UpdateMyLevels(Keyboard kb)
        {
            if (My.Share.IsOpen) return;
            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil) DisarmDelete();
            UpdateMyDrag();
            if (kb == null) return;

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

        /// <summary>Dragging up or down on the list scrolls it by whole rows.</summary>
        void UpdateMyDrag()
        {
            var p = Pointer.current;
            if (p == null) return;
            float y = p.position.ReadValue().y;
            if (p.press.wasPressedThisFrame)
            {
                myDragging = RectTransformUtility.RectangleContainsScreenPoint(My.List, p.position.ReadValue(), Camera.main);
                myDragY = y;
            }
            if (!p.press.isPressed)
            {
                myDragging = false;
                return;
            }
            if (!myDragging) return;

            float pixelsPerRow = My.RowPitch * Screen.height / ScreenLayout.FrameHeightPx;
            int rows = (int)((y - myDragY) / pixelsPerRow);
            if (rows == 0) return;
            myDragY += rows * pixelsPerRow;
            ScrollMyLevels(rows); // the finger moves up: later rows come into view
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
            int maxTop = Mathf.Max(0, Library.levels.Count - My.Rows.Length);
            myTop = Mathf.Clamp(myTop + delta, 0, maxTop);
            RefreshMyLevels();
        }

        void SelectMyLevel(int index)
        {
            int count = Library.levels.Count;
            int visible = My.Rows.Length;
            mySelected = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            if (mySelected < myTop) myTop = mySelected;
            if (mySelected >= myTop + visible) myTop = mySelected - visible + 1;
            myTop = Mathf.Clamp(myTop, 0, Mathf.Max(0, count - visible));
            DisarmDelete();
            RefreshMyLevels();
        }

        /// <summary>Esc on the open share dialog closes it. False if it was not open.</summary>
        public bool CloseShare() => built && My.Share.Close();

        void ImportLevel()
        {
            if (Library.IsFull)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }
            My.Share.OpenImport(code =>
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
            else My.Share.OpenExport(entry.level);
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
                My.Delete.Text = "SURE?";
                My.Delete.Selected = true;
                return;
            }
            Library.Remove(entry.level.id);
            CustomLevelStore.Save(Library);
            SelectMyLevel(mySelected);
        }

        void DisarmDelete()
        {
            deleteArmedUntil = 0f;
            My.Delete.Text = "DELETE";
            My.Delete.Selected = false;
        }

        void RefreshMyLevels()
        {
            var ui = My;
            var levels = Library.levels;
            int count = levels.Count, visible = ui.Rows.Length;
            string range = count > visible ? $" · {myTop + 1}–{Mathf.Min(myTop + visible, count)}" : "";
            ui.Count.text = $"{count} of {CustomLevelLibrary.MaxLevels} levels{range}";
            ui.Empty.gameObject.SetActive(count == 0);

            for (int i = 0; i < visible; i++)
            {
                var row = ui.Rows[i];
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
            ui.Play.Disabled = !any || !IsPlayable(entry);
            ui.Edit.Disabled = !any;
            ui.Delete.Disabled = !any;
            ui.Duplicate.Disabled = !any || Library.IsFull;
            ui.New.Disabled = Library.IsFull;
            ui.Import.Disabled = Library.IsFull;
            ui.Export.Disabled = !any || entry.proof == LevelProof.None;

            ui.Name.text = any ? DisplayName(entry) : "";
            ui.Proof.text = any ? ProofLine(entry) : "";
            ui.Best.text = any && entry.bestMoves > 0 ? $"your best: {Ui.Moves(entry.bestMoves)}" : "";
            ui.StartLabel.enabled = ui.TargetLabel.enabled = any;
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
            {
                int k = r * N + c;
                ui.Start[k].enabled = ui.Target[k].enabled = any;
                if (!any) continue;
                ui.Start[k].color = ThumbCell(entry.level, entry.level.start, r, c);
                ui.Target[k].color = ThumbCell(entry.level, entry.level.target, r, c);
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
