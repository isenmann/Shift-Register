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
            public readonly Image[] Start = new Image[BoardSize * BoardSize], Target = new Image[BoardSize * BoardSize];
            public TextMeshProUGUI Count, Name, Proof, Best, Empty, StartLabel, TargetLabel;
            public HudButton Play, Edit, Duplicate, Delete, New, Import, Export;
            public ShareDialog Share;
        }

        /// <summary>Where one arrangement puts things; all in its own mockup pixels.</summary>
        sealed class MyLayout
        {
            public float FrameWidth, FrameHeight;
            public float HeadingX, HeadingY, HeadingWidth, HeadingHeight, HeadingSize;
            public float CountX, CountY, CountWidth;
            public float BackX, BackY, BackWidth, BackHeight;
            public float ListX, ListY, ListWidth, RowHeight, RowPitch, RowNameWidth;
            public int Rows;
            public float RowNameSize = 16f, StatusSize = 13f;
            public float PanelX, PanelWidth, NameY, NameHeight, NameSize, ProofY, BestY;
            public float LabelY, ThumbY, Cell, ThumbGap, ThumbSpace;
            public float PlayY, PlayHeight, Row2Y, Row3Y, ButtonHeight;
            /// <summary>Desktop keeps NEW and IMPORT under the list; phones have them with the other buttons.</summary>
            public bool Desktop;
        }

        const string NewLevelName = "MY LEVEL";
        const float DeleteConfirmTime = 3f;

        static readonly MyLayout MyDesktop = new MyLayout
        {
            Desktop = true,
            FrameWidth = 1280,
            FrameHeight = 720,
            HeadingX = 56,
            HeadingY = 36,
            HeadingWidth = 400,
            HeadingHeight = 48,
            HeadingSize = 34,
            CountX = 56,
            CountY = 80,
            CountWidth = 640,
            BackX = 1124,
            BackY = 38,
            BackWidth = 100,
            BackHeight = 44,
            ListX = 56,
            ListY = 104,
            ListWidth = 640,
            RowHeight = 52,
            RowPitch = 58,
            RowNameWidth = 340,
            Rows = 9,
            PanelX = 732,
            PanelWidth = 492,
            NameY = 104,
            NameHeight = 36,
            NameSize = 26,
            ProofY = 144,
            BestY = 166,
            LabelY = 200,
            ThumbY = 226,
            Cell = 20,
            ThumbGap = 3,
            ThumbSpace = 40,
            PlayY = 440,
            PlayHeight = 52,
            Row2Y = 500,
            Row3Y = 552,
            ButtonHeight = 44
        };

        static readonly MyLayout MyPhoneLandscape = new MyLayout
        {
            FrameWidth = 915,
            FrameHeight = 412,
            HeadingX = 16,
            HeadingY = 8,
            HeadingWidth = 220,
            HeadingHeight = 32,
            HeadingSize = 22,
            CountX = 236,
            CountY = 14,
            CountWidth = 190,
            BackX = 815,
            BackY = 8,
            BackWidth = 84,
            BackHeight = 40,
            ListX = 16,
            ListY = 48,
            ListWidth = 410,
            RowHeight = 52,
            RowPitch = 58,
            RowNameWidth = 220,
            Rows = 6,
            RowNameSize = 14,
            StatusSize = 12,
            PanelX = 442,
            PanelWidth = 457,
            NameY = 10,
            NameHeight = 26,
            NameSize = 18,
            ProofY = 36,
            BestY = 54,
            LabelY = 76,
            ThumbY = 96,
            Cell = 11,
            ThumbGap = 2,
            ThumbSpace = 24,
            PlayY = 218,
            PlayHeight = 52,
            Row2Y = 276,
            Row3Y = 330,
            ButtonHeight = 48
        };

        static readonly MyLayout MyPhonePortrait = new MyLayout
        {
            FrameWidth = 412,
            FrameHeight = 915,
            HeadingX = 16,
            HeadingY = 28,
            HeadingWidth = 280,
            HeadingHeight = 36,
            HeadingSize = 26,
            CountX = 16,
            CountY = 66,
            CountWidth = 380,
            BackX = 312,
            BackY = 24,
            BackWidth = 84,
            BackHeight = 44,
            ListX = 16,
            ListY = 92,
            ListWidth = 380,
            RowHeight = 52,
            RowPitch = 58,
            RowNameWidth = 210,
            Rows = 6,
            RowNameSize = 14,
            StatusSize = 12,
            PanelX = 16,
            PanelWidth = 380,
            NameY = 446,
            NameHeight = 28,
            NameSize = 18,
            ProofY = 476,
            BestY = 494,
            LabelY = 518,
            ThumbY = 540,
            Cell = 14,
            ThumbGap = 2,
            ThumbSpace = 40,
            PlayY = 684,
            PlayHeight = 60,
            Row2Y = 752,
            Row3Y = 810,
            ButtonHeight = 50
        };

        int mySelected, myTop, myEnteredFrame;
        float deleteArmedUntil, myDragY;
        bool myDragging;

        MyUi CurrentMyLevels => Current.MyLevelsUi;
        CustomLevelLibrary Library => game.CustomLevels;
        CustomLevel MySelected =>
            mySelected >= 0 && mySelected < Library.levels.Count ? Library.levels[mySelected] : null;

        void BuildMyLevels(Screens screens, MyLayout layout)
        {
            var root = screens.MyLevels;
            var left = Outer(screens, root, "Left", layout.Desktop || layout.FrameWidth > layout.FrameHeight ? -1f
                : 0f, 0f);
            var right = Outer(screens, root, "Right", layout.Desktop || layout.FrameWidth > layout.FrameHeight ? 1f
                : 0f, 0f);
            var ui = new MyUi { RowPitch = layout.RowPitch, Rows = new MyLevelRow[layout.Rows] };
            screens.MyLevelsUi = ui;

            Ui.Text(left, "Heading", layout.HeadingX, layout.HeadingY, layout.HeadingWidth, layout.HeadingHeight,
                displayBold, layout.HeadingSize, Lcd.Ink, text: "MY LEVELS");
            ui.Count = Ui.Text(left, "Count", layout.CountX, layout.CountY, layout.CountWidth, 18, mono, 13, Lcd.Text2);
            HudButton.Create(right, "BACK", layout.BackX, layout.BackY, layout.BackWidth, layout.BackHeight, display,
                14, () => flow.Show(GameScreen.Title));

            ui.List = Ui.Rect(left, "List", layout.ListX, layout.ListY, layout.ListWidth,
                layout.Rows * layout.RowPitch);
            for (int i = 0; i < layout.Rows; i++)
            {
                int visible = i;
                var rowRoot = Ui.Rect(ui.List, $"Row {i + 1}", 0, i * layout.RowPitch, layout.ListWidth,
                    layout.RowHeight);
                var row = new MyLevelRow
                {
                    Root = rowRoot,
                    Ground = Ui.Box(rowRoot, "Ground", 0, 0, layout.ListWidth, layout.RowHeight, 8f, Lcd.Cell),
                    Name = Ui.Text(rowRoot, "Name", 14, 0, layout.RowNameWidth, layout.RowHeight, display,
                    layout.RowNameSize, Lcd.Ink),
                    Status = Ui.Text(rowRoot, "Status", layout.ListWidth - 200f, 0, 186f, layout.RowHeight, mono,
                    layout.StatusSize, Lcd.Text2, TextAlignmentOptions.MidlineRight)
                };
                UiHotspot.Create(rowRoot, "Hotspot", 0, 0, layout.ListWidth, layout.RowHeight).Click = () =>
                {
                    Sfx.Play(Sound.Tick);
                    SelectMyLevel(myTop + visible);
                };
                ui.Rows[i] = row;
            }

            ui.Empty = Ui.Text(left, "Empty", layout.ListX, layout.ListY, layout.ListWidth, 60, mono, 15, Lcd.Text2,
                TextAlignmentOptions.TopLeft,
                "No levels yet. NEW starts one on an empty board.");
            ui.Empty.textWrappingMode = TextWrappingModes.Normal;

            ui.Name = Ui.Text(right, "Name", layout.PanelX, layout.NameY, layout.PanelWidth, layout.NameHeight, display,
                layout.NameSize, Lcd.Ink);
            ui.Proof = Ui.Text(right, "Proof", layout.PanelX, layout.ProofY, layout.PanelWidth, 18, mono, 13,
                Lcd.Text2);
            ui.Best = Ui.Text(right, "Best", layout.PanelX, layout.BestY, layout.PanelWidth, 18, mono, 13, Lcd.Text2);

            float thumbWidth = BoardSize * layout.Cell + (BoardSize - 1) * layout.ThumbGap;
            ui.StartLabel = Ui.Text(right, "Start Label", layout.PanelX, layout.LabelY, thumbWidth, 18, display, 13,
                Lcd.Ink, text: "START");
            ui.TargetLabel = Ui.Text(right, "Target Label", layout.PanelX + thumbWidth + layout.ThumbSpace,
                layout.LabelY, thumbWidth, 18, display, 13, Lcd.Ink, text: "TARGET");
            float radius = Mathf.Min(4f, layout.Cell / 4f);
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    float step = layout.Cell + layout.ThumbGap;
                    ui.Start[row * BoardSize + column] = Ui.Box(right, $"Start {row + 1},{column + 1}",
                        layout.PanelX + column * step, layout.ThumbY + row * step, layout.Cell, layout.Cell, radius,
                        Lcd.Cell);
                    ui.Target[row * BoardSize + column] = Ui.Box(right, $"Target {row + 1},{column + 1}",
                        layout.PanelX + thumbWidth + layout.ThumbSpace + column * step, layout.ThumbY + row * step,
                        layout.Cell, layout.Cell, radius, Lcd.Cell);
                }
            }

            ui.Play = HudButton.CreateSolid(right, "PLAY", layout.PanelX, layout.PlayY, layout.PanelWidth,
                layout.PlayHeight, display, 18, PlayMyLevel);
            float third = (layout.PanelWidth - 16f) / 3f;
            ui.Edit = HudButton.Create(right, "EDIT", layout.PanelX, layout.Row2Y, third, layout.ButtonHeight, display,
                14, EditMyLevel);
            ui.Duplicate = HudButton.Create(right, "COPY", layout.PanelX + third + 8f, layout.Row2Y, third,
                layout.ButtonHeight, display, 14, DuplicateMyLevel);
            ui.Delete = HudButton.Create(right, "DELETE", layout.PanelX + 2f * (third + 8f), layout.Row2Y, third,
                layout.ButtonHeight, display, 14, DeleteMyLevel);

            if (layout.Desktop)
            {
                ui.Export = HudButton.Create(right, "EXPORT", layout.PanelX, layout.Row3Y, layout.PanelWidth,
                    layout.ButtonHeight, display, 14, ExportMyLevel);
                ui.New = HudButton.CreateSolid(left, "NEW", layout.ListX, 636, 140, 44, display, 14, NewLevel);
                ui.Import = HudButton.Create(left, "IMPORT", layout.ListX + 148, 636, 140, 44, display, 14,
                    ImportLevel);
                Ui.Text(left, "Keys", layout.ListX, 688, 1168, 20, mono, 13, Lcd.Text3,
                    text: "Enter play · E edit · N new · I import · X export · D copy · Del delete · Esc back");
            }
            else
            {
                ui.New = HudButton.Create(right, "NEW", layout.PanelX, layout.Row3Y, third, layout.ButtonHeight,
                    display, 14, NewLevel);
                ui.Import = HudButton.Create(right, "IMPORT", layout.PanelX + third + 8f, layout.Row3Y, third,
                    layout.ButtonHeight, display, 14, ImportLevel);
                ui.Export = HudButton.Create(right, "EXPORT", layout.PanelX + 2f * (third + 8f), layout.Row3Y, third,
                    layout.ButtonHeight, display, 14, ExportMyLevel);
            }

            // Last, so it covers the screen.
            ui.Share = ShareDialog.Create(root, layout.FrameWidth, layout.FrameHeight, display, mono);
        }

        void EnterMyLevels(bool arriving)
        {
            if (arriving)
            {
                myEnteredFrame = Time.frameCount;
                myDragging = false;
                CurrentMyLevels.Share.Close();
            }

            SelectMyLevel(mySelected);
        }

        void UpdateMyLevels(Keyboard keyboard)
        {
            if (CurrentMyLevels.Share.IsOpen)
            {
                return;
            }

            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil)
            {
                DisarmDelete();
            }

            UpdateMyDrag();
            if (keyboard == null)
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f)
                {
                    ScrollMyLevels(wheel > 0f ? -1 : 1);
                }
            }

            // The key that came back here (Enter on the solved banner) must not also act on the list.
            if (Time.frameCount - myEnteredFrame <= 1)
            {
                return;
            }

            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                StepMyLevel(-1);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                StepMyLevel(1);
            }
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame
            || keyboard.spaceKey.wasPressedThisFrame)
            {
                PlayMyLevel();
            }
            else if (keyboard.eKey.wasPressedThisFrame)
            {
                EditMyLevel();
            }
            else if (keyboard.nKey.wasPressedThisFrame)
            {
                NewLevel();
            }
            else if (keyboard.iKey.wasPressedThisFrame)
            {
                ImportLevel();
            }
            else if (keyboard.xKey.wasPressedThisFrame)
            {
                ExportMyLevel();
            }
            else if (keyboard.dKey.wasPressedThisFrame)
            {
                DuplicateMyLevel();
            }
            else if (keyboard.deleteKey.wasPressedThisFrame)
            {
                DeleteMyLevel();
            }
        }

        /// <summary>Dragging up or down on the list scrolls it by whole rows.</summary>
        void UpdateMyDrag()
        {
            var pointer = Pointer.current;
            if (pointer == null)
            {
                return;
            }

            float y = pointer.position.ReadValue().y;
            if (pointer.press.wasPressedThisFrame)
            {
                myDragging = RectTransformUtility.RectangleContainsScreenPoint(CurrentMyLevels.List,
                    pointer.position.ReadValue(), Camera.main);
                myDragY = y;
            }

            if (!pointer.press.isPressed)
            {
                myDragging = false;
                return;
            }

            if (!myDragging)
            {
                return;
            }

            float pixelsPerRow = CurrentMyLevels.RowPitch * Screen.height / ScreenLayout.FrameHeightPx;
            int rows = (int)((y - myDragY) / pixelsPerRow);
            if (rows == 0)
            {
                return;
            }

            myDragY += rows * pixelsPerRow;
            ScrollMyLevels(rows); // the finger moves up: later rows come into view
        }

        void StepMyLevel(int delta)
        {
            int count = Library.levels.Count;
            if (count == 0)
            {
                return;
            }

            int next = Mathf.Clamp(mySelected + delta, 0, count - 1);
            if (next != mySelected)
            {
                Sfx.Play(Sound.Tick);
            }

            SelectMyLevel(next);
        }

        void ScrollMyLevels(int delta)
        {
            int maxTop = Mathf.Max(0, Library.levels.Count - CurrentMyLevels.Rows.Length);
            myTop = Mathf.Clamp(myTop + delta, 0, maxTop);
            RefreshMyLevels();
        }

        void SelectMyLevel(int index)
        {
            int count = Library.levels.Count;
            int visible = CurrentMyLevels.Rows.Length;
            mySelected = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            if (mySelected < myTop)
            {
                myTop = mySelected;
            }

            if (mySelected >= myTop + visible)
            {
                myTop = mySelected - visible + 1;
            }

            myTop = Mathf.Clamp(myTop, 0, Mathf.Max(0, count - visible));
            DisarmDelete();
            RefreshMyLevels();
        }

        /// <summary>Esc on the open share dialog closes it. False if it was not open.</summary>
        public bool CloseShare() => built && CurrentMyLevels.Share.Close();

        void ImportLevel()
        {
            if (Library.IsFull)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }

            CurrentMyLevels.Share.OpenImport(code =>
            {
                var entry = Library.Import(code, DateTime.UtcNow.Ticks, out var error);
                if (entry == null)
                {
                    return CustomLevelLibrary.ImportMessage(error);
                }

                CustomLevelStore.Save(Library);
                SelectMyLevel(Library.levels.Count - 1);
                return null;
            });
        }

        void ExportMyLevel()
        {
            var entry = MySelected;
            if (entry == null || entry.proof == LevelProof.None)
            {
                Sfx.Play(Sound.Blocked);
            }
            else
            {
                CurrentMyLevels.Share.OpenExport(entry.level);
            }
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

        /// <summary>
        /// A level whose start already is its target, or where nothing can move, has nothing to play.
        /// </summary>
        static bool IsPlayable(CustomLevel entry) => new LevelEdit(entry.level, entry.proof).Problems().Count == 0;

        void EditMyLevel()
        {
            var entry = MySelected;
            if (entry == null)
            {
                Sfx.Play(Sound.Blocked);
            }
            else
            {
                flow.EditLevel(entry.level.id);
            }
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
                CurrentMyLevels.Delete.Text = "SURE?";
                CurrentMyLevels.Delete.Selected = true;
                return;
            }

            Library.Remove(entry.level.id);
            CustomLevelStore.Save(Library);
            SelectMyLevel(mySelected);
        }

        void DisarmDelete()
        {
            deleteArmedUntil = 0f;
            CurrentMyLevels.Delete.Text = "DELETE";
            CurrentMyLevels.Delete.Selected = false;
        }

        void RefreshMyLevels()
        {
            var ui = CurrentMyLevels;
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
                if (!shown)
                {
                    continue;
                }

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
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    int cellIndex = row * BoardSize + column;
                    ui.Start[cellIndex].enabled = ui.Target[cellIndex].enabled = any;
                    if (!any)
                    {
                        continue;
                    }

                    ui.Start[cellIndex].color = ThumbCell(entry.level, entry.level.start, row, column);
                    ui.Target[cellIndex].color = ThumbCell(entry.level, entry.level.target, row, column);
                }
            }
        }

        static Color32 ThumbCell(LevelData level, int[] picture, int row, int column)
        {
            int mask = 0x80 >> column;
            if ((level.walls[row] & mask) != 0)
            {
                return Lcd.Text3;
            }

            return (picture[row] & mask) != 0 ? Lcd.Ink : Lcd.Cell;
        }

        static string DisplayName(CustomLevel entry) =>
            string.IsNullOrEmpty(entry.level.name) ? "UNTITLED" : entry.level.name.ToUpperInvariant();

        static string ProofLine(CustomLevel entry)
        {
            switch (entry.proof)
            {
                case LevelProof.Searched:
                    return $"par {entry.level.par} · shortest";
                case LevelProof.Replayed:
                    return $"par {entry.level.par} · best so far";
                default:
                    return "not proven";
            }
        }
    }
}
