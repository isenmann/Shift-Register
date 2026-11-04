using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.CustomLevels;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.Input;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.LevelEditor
{
    /// <summary>
    /// The level editor screen (layout A of the editor canvas) on its own canvas: the desktop arrangement,
    /// or on phones and tablets a landscape one (B3 proportions). An upright phone gets a message to rotate
    /// instead; a running search goes on behind it. The board stays where the game draws it and is painted directly:
    /// click or drag, or arrows and Space. Tab swaps
    /// start and target, 1–5 pick a tool, 6 changes the gate tool's operator, Z / Y undo and redo, Enter
    /// solves, T test plays, X exports a share code, Esc cancels a search or leaves. Each row's options sit beside it:
    /// 1-IN toggles in the arrow
    /// button slots, then wrap, gate operator and the link to the next row.
    /// </summary>
    public sealed class EditorView : MonoBehaviour
    {
        [SerializeField] GameController game;
        [SerializeField] ScreenFlow flow;
        [SerializeField] Camera cam;
        [Tooltip("Silkscreen Regular: buttons and titles.")]
        [SerializeField] TMP_FontAsset display;
        [Tooltip("JetBrains Mono Regular.")]
        [SerializeField] TMP_FontAsset mono;

        const int BoardSize = BoardBits.Size;
        const double SolveBudgetMs = 8.0;
        const float AutosaveDelay = 2f;
        const float CaretBlink = 0.53f;
        static readonly PaintTool[] ToolOrder = { PaintTool.Bit, PaintTool.Wall, PaintTool.Pin, PaintTool.Gate,
            PaintTool.Erase };
        static readonly string[] ToolLabels = { "BIT  1", "WALL  2", "PIN  3", "GATE  4", "ERASE  5" };
        static readonly LossRule[] LossOrder = { LossRule.Any, LossRule.AtMost, LossRule.Exactly };
        static readonly string[] LossLabels = { "ANY", "MAX", "EXACT" };

        EditorSession session;
        string levelId;
        bool keepSession;
        LevelData shownBoard;
        int row = 4, column = 3, enabledFrame = -1;
        bool painting, dirty;
        float saveAt;

        RectTransform frame, rotateMessage, left, right;
        float progressWidth = 188f;
        TMP_InputField nameField;
        TextMeshProUGUI nameText;
        Image nameCaret;
        int caretShownAt = -1;
        float caretMovedTime;
        HudButton startTab, targetTab, solveButton, testPlayButton, exportButton, undoButton, redoButton;
        ShareDialog share;
        readonly HudButton[] toolButtons = new HudButton[ToolOrder.Length];
        HudButton opButton, lossDown, lossUp;
        readonly HudButton[] fillLeft = new HudButton[BoardSize], fillRight = new HudButton[BoardSize],
            wrap = new HudButton[BoardSize], gate = new HudButton[BoardSize];
        readonly HudButton[] link = new HudButton[BoardSize - 1];
        readonly HudButton[] lossTabs = new HudButton[LossOrder.Length];
        TextMeshProUGUI lossCount;
        Image columnsTick;
        readonly Image[] miniBoard = new Image[BoardSize * BoardSize];
        TextMeshProUGUI otherName, paintLabel, ghostLabel, statusText, proofTitle, proofText;
        Image statusDot;
        RectTransform progressBox, progressFill;

        /// <summary>The level the screen opens next.</summary>
        public void SetLevel(string id) => levelId = id;

        /// <summary>The session being edited, for checks; null until the screen first opens.</summary>
        public EditorSession Session => session;

        void Awake()
        {
            Ui.RenderWithMainCamera(GetComponent<Canvas>(), 10);
            Build();
        }

        void OnEnable()
        {
            enabledFrame = Time.frameCount;
            game.BoardLent = true;
            ScreenLayout.LayoutChanged += ApplyFrame;
            ApplyFrame();
            if (session != null)
            {
                ResumeAfterTestPlay();
            }
            else
            {
                Open();
            }
        }

        void OnDisable()
        {
            ScreenLayout.LayoutChanged -= ApplyFrame;
            game.BoardLent = false;
            if (session == null)
            {
                return;
            }

            session.EndPaint();
            session.CancelSolve();
            share.Close();
            CommitName();
            Save();
            if (keepSession)
            {
                return; // test play comes back here, with the undo history
            }

            session.Edit.Changed -= OnEdited;
            session = null;
            shownBoard = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && session != null)
            {
                Save();
            }
        }

        void OnApplicationQuit()
        {
            if (session != null)
            {
                Save();
            }
        }

        void ApplyFrame()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            }

            bool upright = ScreenLayout.Portrait;
            frame.gameObject.SetActive(!upright);
            rotateMessage.gameObject.SetActive(upright);
            game.BoardView.gameObject.SetActive(!upright);
            if (upright)
            {
                painting = false;
                session?.EndPaint();
                return;
            }

            left.anchoredPosition = new Vector2(-ScreenLayout.OuterShiftPx, 0f);
            right.anchoredPosition = new Vector2(ScreenLayout.OuterShiftPx, 0f);
            game.PlaceBoard();
        }

        // ---------------------------------------------------------------- level

        void Open()
        {
            var entry = game.CustomLevels.Get(levelId);
            if (entry == null)
            {
                throw new InvalidOperationException($"No custom level '{levelId}' to edit.");
            }

            session = new EditorSession(entry.level, entry.proof);
            session.Edit.Changed += OnEdited;
            shownBoard = null;
            dirty = false;
            nameField.SetTextWithoutNotify(session.Edit.Level.name.ToUpperInvariant());
            Refresh();
        }

        /// <summary>Back from test play: a win may have proven the level or lowered its par.</summary>
        void ResumeAfterTestPlay()
        {
            keepSession = false;
            shownBoard = null;
            var entry = game.CustomLevels.Get(levelId);
            var level = session.Edit.Level;
            if (entry != null && (entry.proof != session.Edit.Proof || entry.level.par != level.par)
                && Move.TryParseList(entry.level.solution, out var moves))
            {
                session.Edit.SetSolution(moves, entry.proof);
            }

            Refresh();
        }

        void TestPlay()
        {
            if (session.Edit.Problems().Count > 0)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }

            session.EndPaint();
            session.CancelSolve();
            CommitName();
            Save();
            keepSession = true;
            flow.PlayCustom(game.CustomLevels.Get(levelId), GameScreen.Editor);
        }

        void OnEdited()
        {
            dirty = true;
            saveAt = Time.unscaledTime + AutosaveDelay;
            Refresh();
        }

        void Save()
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;
            game.CustomLevels.Update(session.Edit.Level, session.Edit.Proof, DateTime.UtcNow.Ticks);
            CustomLevelStore.Save(game.CustomLevels);
        }

        void CommitName() => session.Edit.SetName(nameField.text);

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (session == null)
            {
                return;
            }

            if (session.IsSolving && session.Tick(SolveBudgetMs))
            {
                OnSolveFinished();
            }
            else if (session.IsSolving)
            {
                RefreshProof();
            }

            if (ScreenLayout.Portrait)
            {
                if (dirty && Time.unscaledTime >= saveAt)
                {
                    Save();
                }

                return;
            }

            HandlePointer();
            HandleKeyboard();
            game.Controls.HighlightCell(row, column);
            RefreshCaret();

            if (dirty && Time.unscaledTime >= saveAt && !painting)
            {
                Save();
            }
        }

        void OnSolveFinished()
        {
            Sfx.Play(session.Outcome == SolveOutcome.None ? Sound.Solved : session.Outcome == SolveOutcome.NoSolution
                ? Sound.Broken
                : Sound.Blocked);
            dirty = true;
            Refresh();
        }

        void HandlePointer()
        {
            var pointer = Pointer.current;
            if (pointer == null || cam == null)
            {
                return;
            }

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Feed(pointer.position.ReadValue(), pointer.press.isPressed, pointer.press.wasPressedThisFrame, overUi);
        }

        /// <summary>
        /// One frame of pointer input. Public so play-mode checks can drive it without input devices.
        /// </summary>
        public void Feed(Vector2 screen, bool down, bool justPressed, bool overUi)
        {
            if (session == null || cam == null)
            {
                return;
            }

            if (!down && painting)
            {
                painting = false;
                session.EndPaint();
                return;
            }

            if (Time.frameCount == enabledFrame)
            {
                return; // the click that opened the screen must not paint
            }

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            Vector2 local = game.Controls.transform.InverseTransformPoint(world);
            bool onCell = BoardControls.HitCell(local, out int hitRow, out int hitColumn);

            if (painting)
            {
                if (onCell)
                {
                    PaintTo(hitRow, hitColumn);
                }

                return;
            }

            if (overUi || !onCell)
            {
                return;
            }

            row = hitRow;
            column = hitColumn;
            if (!justPressed)
            {
                return;
            }

            painting = true;
            session.BeginPaint(hitRow, hitColumn);
            Sfx.Play(Sound.Tick);
        }

        void PaintTo(int paintRow, int paintColumn)
        {
            if (paintRow == row && paintColumn == column)
            {
                return;
            }

            row = paintRow;
            column = paintColumn;
            session.PaintTo(paintRow, paintColumn);
            Sfx.Play(Sound.Tick);
        }

        void HandleKeyboard()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || Time.frameCount == enabledFrame || Ui.IsTyping || share.IsOpen)
            {
                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            {
                MoveCursor(0, -1);
            }
            else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)
            {
                MoveCursor(0, 1);
            }
            else if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame)
            {
                MoveCursor(-1, 0);
            }
            else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame)
            {
                MoveCursor(1, 0);
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                session.BeginPaint(row, column);
                session.EndPaint();
                Sfx.Play(Sound.Tick);
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                SwapLayer();
            }

            for (int i = 0; i < ToolOrder.Length; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    PickTool(ToolOrder[i]);
                }
            }

            if (keyboard.digit6Key.wasPressedThisFrame)
            {
                CycleOp();
            }

            if (keyboard.yKey.wasPressedThisFrame || (keyboard.zKey.wasPressedThisFrame && keyboard.shiftKey.isPressed))
            {
                Redo();
            }
            else if (keyboard.zKey.wasPressedThisFrame)
            {
                Undo();
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                ToggleSolve();
            }

            if (keyboard.tKey.wasPressedThisFrame)
            {
                TestPlay();
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                Export();
            }
        }

        void MoveCursor(int rowDelta, int columnDelta)
        {
            int newRow = Mathf.Clamp(row + rowDelta, 0, BoardSize - 1),
                newColumn = Mathf.Clamp(column + columnDelta, 0, BoardSize - 1);
            if (newRow == row && newColumn == column)
            {
                return;
            }

            row = newRow;
            column = newColumn;
            Sfx.Play(Sound.Tick);
        }

        void SwapLayer()
        {
            session.SwapLayer();
            Sfx.Play(Sound.Click);
            Refresh();
        }

        void SetLayer(EditLayer layer)
        {
            session.Layer = layer;
            Refresh();
        }

        void PickTool(PaintTool tool)
        {
            session.Tool = tool;
            Refresh();
        }

        void CycleOp()
        {
            if (session.Tool != PaintTool.Gate)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }

            session.Op = EditorSession.NextOp(session.Op);
            Refresh();
        }

        void CycleRowGate(int row)
        {
            var gate = session.Edit.GateOn(row);
            if (gate != null)
            {
                session.Edit.SetGateOp(row, EditorSession.NextOp(gate.op));
            }
        }

        void StepLoss(int delta)
        {
            var level = session.Edit.Level;
            session.Edit.SetLoss(level.lossRule, level.lossCount + delta);
        }

        void Undo()
        {
            if (session.Edit.Undo())
            {
                Sfx.Play(Sound.Undo);
            }
            else
            {
                Sfx.Play(Sound.Blocked);
            }
        }

        void Redo()
        {
            if (session.Edit.Redo())
            {
                Sfx.Play(Sound.Undo);
            }
            else
            {
                Sfx.Play(Sound.Blocked);
            }
        }

        void ToggleSolve()
        {
            if (session.CancelSolve())
            {
                Refresh();
                return;
            }

            if (!session.CanSolve)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }

            session.StartSolve();
            Refresh();
        }

        void Export()
        {
            if (session.Edit.Proof == LevelProof.None)
            {
                Sfx.Play(Sound.Blocked);
                return;
            }

            CommitName();
            share.OpenExport(session.Edit.Level);
        }

        /// <summary>Esc on the open share dialog closes it. False if it was not open.</summary>
        public bool CloseShare() => share != null && share.Close();

        /// <summary>Esc while searching stops the search instead of leaving. False if nothing was running.</summary>
        public bool CancelSolve()
        {
            if (session == null || !session.CancelSolve())
            {
                return false;
            }

            Refresh();
            return true;
        }

        // ---------------------------------------------------------------- view

        void Refresh()
        {
            if (session == null)
            {
                return;
            }

            RefreshBoard();

            var other = session.Layer == EditLayer.Start ? EditLayer.Target : EditLayer.Start;
            string layerName = session.Layer == EditLayer.Start ? "START" : "TARGET";
            string otherWord = session.Layer == EditLayer.Start ? "TARGET" : "START";
            startTab.Selected = session.Layer == EditLayer.Start;
            targetTab.Selected = session.Layer == EditLayer.Target;
            otherName.text = otherWord;
            paintLabel.text = "PAINTING " + layerName;
            ghostLabel.text = "dashed = " + otherWord.ToLowerInvariant();
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    miniBoard[row * BoardSize + column].color = session.Edit.GetBit(other, row, column) ? Lcd.Ink
                        : Lcd.Background;
                }
            }

            for (int i = 0; i < ToolOrder.Length; i++)
            {
                toolButtons[i].Selected = session.Tool == ToolOrder[i];
            }

            opButton.Text = "OP " + BoardView.GateSymbol(session.Op);
            opButton.Disabled = session.Tool != PaintTool.Gate;
            RefreshRows();
            RefreshSettings();
            undoButton.Disabled = !session.Edit.CanUndo;
            redoButton.Disabled = !session.Edit.CanRedo;

            statusText.text = session.Status(out bool problem);
            statusText.color = statusDot.color = problem ? Lcd.LossText : Lcd.Text2;

            solveButton.Text = session.IsSolving ? "CANCEL" : "SOLVE";
            solveButton.Disabled = !session.IsSolving && !session.CanSolve;
            testPlayButton.Disabled = problem;
            exportButton.Disabled = session.Edit.Proof == LevelProof.None;
            RefreshProof();
        }

        void RefreshRows()
        {
            var edit = session.Edit;
            for (int row = 0; row < BoardSize; row++)
            {
                fillLeft[row].Selected = edit.HasFill(row, true);
                fillRight[row].Selected = edit.HasFill(row, false);
                wrap[row].Selected = edit.IsRotating(row);
                var rowGate = edit.GateOn(row);
                gate[row].Text = rowGate != null ? BoardView.GateSymbol(rowGate.op) : "·";
                gate[row].Disabled = rowGate == null;
                gate[row].Selected = rowGate != null;
                if (row < BoardSize - 1)
                {
                    link[row].Selected = edit.ChainTopOf(row) == row;
                }
            }
        }

        void RefreshSettings()
        {
            var level = session.Edit.Level;
            for (int i = 0; i < LossOrder.Length; i++)
            {
                lossTabs[i].Selected = level.lossRule == LossOrder[i];
            }

            bool counts = level.lossRule != LossRule.Any;
            lossCount.text = counts ? level.lossCount.ToString() : "-";
            lossDown.Disabled = !counts || level.lossCount <= 0;
            lossUp.Disabled = !counts || level.lossCount >= LevelEdit.MaxLossCount;
            columnsTick.enabled = level.gatesOnColumnMoves;
        }

        void RefreshProof()
        {
            proofTitle.text = session.ProofTitle;
            proofTitle.color = session.Outcome == SolveOutcome.NoSolution ? Lcd.LossText : Lcd.Ink;
            proofText.text = session.ProofText;
            progressBox.gameObject.SetActive(session.IsSolving);
            progressFill.sizeDelta = new Vector2(Mathf.Clamp01(session.SolveProgress) * progressWidth,
                progressFill.sizeDelta.y);
        }

        /// <summary>
        /// Painted bits only need a redraw; anything else changes the board's objects, which are rebuilt.
        /// </summary>
        void RefreshBoard()
        {
            var level = session.DisplayLevel();
            if (!SameBoardObjects(level, shownBoard))
            {
                var rules = LevelRules.Compile(level);
                game.BoardView.Build(rules);
                game.Controls.Build(rules, withArrows: false, tightBelts: true);
            }

            game.BoardView.Show(BoardBits.FromRows(level.start));
            shownBoard = level;
        }

        /// <summary>
        /// True if only the painted picture differs (which <see cref="BoardView.Show"/> can draw alone).
        /// </summary>
        static bool SameBoardObjects(LevelData first, LevelData second)
        {
            if (second == null)
            {
                return false;
            }

            var startless = LevelEdit.Clone(first);
            startless.start = second.start;
            return LevelEdit.SamePuzzle(startless, second);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            var canvas = (RectTransform)transform;
            bool phone = DeviceMode.IsMobile;
            float width = phone ? 915f : 1280f, height = phone ? 412f : 720f;
            frame = Centred(canvas, "Editor", width, height);
            left = Ui.Rect(frame, "Left", 0, 0, width, height);
            right = Ui.Rect(frame, "Right", 0, 0, width, height);
            if (phone)
            {
                BuildLeftPhone(left);
                BuildCentrePhone(frame);
                BuildRows(frame, new RowLayout
                {
                    Top = 20f,
                    Pitch = 47.15f,
                    CellHeight = 41f,
                    LeftX = 198f,
                    RightX = 611f,
                    FillWidth = 34f,
                    WrapX = 649f,
                    GateX = 689f,
                    LinkX = 729f,
                    Chip = 36f,
                    LinkSize = 30f,
                    HeaderY = 2f,
                    HeaderSize = 10f,
                    Font = 13f
                });
                BuildRightPhone(right);
            }
            else
            {
                BuildLeft(left);
                BuildCentre(frame);
                BuildRows(frame, new RowLayout
                {
                    Top = 124f,
                    Pitch = 60f,
                    CellHeight = 52f,
                    LeftX = 312f,
                    RightX = 840f,
                    FillWidth = 40f,
                    WrapX = 892f,
                    GateX = 938f,
                    LinkX = 984f,
                    Chip = 40f,
                    LinkSize = 32f,
                    HeaderY = 100f,
                    HeaderSize = 11f,
                    Font = 14f
                });
                BuildRight(right);
            }

            share = ShareDialog.Create(frame, width, height, display, mono); // last, so it covers the screen
            BuildRotateMessage(canvas);
        }

        static RectTransform Centred(RectTransform canvas, string name, float width, float height)
        {
            var rect = Ui.Rect(canvas, name, 0, 0, width, height);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>What an upright phone shows: the editor only fits sideways.</summary>
        void BuildRotateMessage(RectTransform canvas)
        {
            const float width = 412f, x = 24f, inner = width - 2f * x;
            rotateMessage = Centred(canvas, "Rotate Message", width, 915f);
            Ui.Frame(rotateMessage, "Phone Upright", 96, 300, 60, 100, 8f, 3f, Lcd.Text3);
            Ui.Text(rotateMessage, "Arrow", 156, 300, 60, 100, mono, 24, Lcd.Ink, TextAlignmentOptions.Center, "→");
            Ui.Frame(rotateMessage, "Phone Sideways", 216, 320, 100, 60, 8f, 3f, Lcd.Ink);
            Ui.Text(rotateMessage, "Heading", x, 424, inner, 34, display, 22, Lcd.Ink, TextAlignmentOptions.Center,
                "TURN IT SIDEWAYS");
            var text = Ui.Text(rotateMessage, "Text", x, 464, inner, 48, mono, 15, Lcd.Text2, TextAlignmentOptions.Top,
                "The editor needs the wide view. Your level is safe.");
            text.textWrappingMode = TextWrappingModes.Normal;
            HudButton.Create(rotateMessage, "BACK", x, 528, inner, 54, display, 16,
                () => flow.Show(GameScreen.MyLevels));
        }

        void BuildLeftPhone(Transform root)
        {
            const float x = 12f, width = 178f;
            nameField = NameField(root, x, 8, width, 32, 14, 16);

            float half = width / 2f;
            startTab = HudButton.Create(root, "START", x, 46, half, 38, display, 13, () => SetLayer(EditLayer.Start));
            targetTab = HudButton.Create(root, "TARGET", x + half, 46, half, 38, display, 13,
                () => SetLayer(EditLayer.Target));

            const float cardY = 90f, cardHeight = 84f, cell = 8f, gap = 1f;
            Ui.Box(root, "Other Picture", x, cardY, width, cardHeight, 8f, Lcd.Cell);
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    miniBoard[row * BoardSize + column] = Ui.Box(root, $"Mini {row + 1},{column + 1}",
                        x + 8 + column * (cell + gap), cardY + 6 + row * (cell + gap), cell, cell, 1f, Lcd.Background);
                }
            }

            otherName = Ui.Text(root, "Other Name", x + 88, cardY + 20, 88, 20, display, 12, Lcd.Ink);
            Ui.Text(root, "Other Hint", x + 88, cardY + 42, 88, 18, mono, 12, Lcd.Text2, text: "tap to swap");
            UiHotspot.Create(root, "Other Picture Hotspot", x, cardY, width, cardHeight).Click = SwapLayer;

            const float toolGap = 4f, toolHeight = 38f, toolY = 180f;
            float toolWidth = (width - toolGap) / 2f;
            string[] labels = { "BIT", "WALL", "PIN", "GATE", "ERASE" };
            for (int i = 0; i < ToolOrder.Length; i++)
            {
                var tool = ToolOrder[i];
                toolButtons[i] = HudButton.Create(root, labels[i], x + (i % 2) * (toolWidth + toolGap),
                    toolY + (i / 2) * (toolHeight + toolGap),
                    toolWidth, toolHeight, display, 12, () => PickTool(tool));
            }

            opButton = HudButton.Create(root, "OP &", x + toolWidth + toolGap, toolY + 2 * (toolHeight + toolGap),
                toolWidth, toolHeight, display, 12, CycleOp);

            undoButton = HudButton.Create(root, "UNDO", x, 312, toolWidth, 44, display, 12, Undo);
            redoButton = HudButton.Create(root, "REDO", x + toolWidth + toolGap, 312, toolWidth, 44, display, 12, Redo);
        }

        void BuildCentrePhone(Transform root)
        {
            const float x = 236f, width = 371f;
            paintLabel = Ui.Text(root, "Painting", x, 1, 190, 18, display, 11, Lcd.Ink);
            ghostLabel = Ui.Text(root, "Ghosts", x + 190, 1, width - 190, 18, mono, 11, Lcd.Text3,
                TextAlignmentOptions.MidlineRight);
            statusDot = Ui.Box(root, "Status Dot", x, 396, 8, 8, 2f, Lcd.Text2);
            statusText = Ui.Text(root, "Status", x + 12, 391, width + 20, 18, mono, 11, Lcd.Text2);
        }

        void BuildRightPhone(Transform root)
        {
            const float x = 768f, width = 135f;
            Ui.Text(root, "Loss Title", x, 4, width, 14, display, 10, Lcd.Ink, text: "BITS MAY FALL");
            for (int i = 0; i < LossOrder.Length; i++)
            {
                var rule = LossOrder[i];
                lossTabs[i] = HudButton.Create(root, LossLabels[i], x + i * 45f, 20, 45, 32, display, 9, () =>
                    session.Edit.SetLoss(rule, session.Edit.Level.lossCount));
            }

            Ui.Text(root, "Loss Count Label", x, 56, 40, 32, mono, 11, Lcd.Text2, text: "Bits");
            lossDown = HudButton.Create(root, "Fewer", x + width - 92, 56, 32, 32, mono, 15, () => StepLoss(-1));
            lossDown.Text = "-";
            lossCount = Ui.Text(root, "Loss Count", x + width - 60, 56, 28, 32, display, 14, Lcd.Ink,
                TextAlignmentOptions.Center);
            lossUp = HudButton.Create(root, "More", x + width - 32, 56, 32, 32, mono, 15, () => StepLoss(1));
            lossUp.Text = "+";

            Ui.Frame(root, "Columns Box", x, 98, 16, 16, 4f, 2f, Lcd.Ink);
            columnsTick = Ui.Box(root, "Columns Tick", x + 4, 102, 8, 8, 2f, Lcd.Ink);
            var columnsText = Ui.Text(root, "Columns Text", x + 22, 92, width - 22, 28, mono, 10, Lcd.Text2,
                text: "Gates also on column moves");
            columnsText.textWrappingMode = TextWrappingModes.Normal;
            UiHotspot.Create(root, "Columns Hotspot", x, 90, width, 32).Click = () =>
            {
                Sfx.Play(Sound.Click);
                session.Edit.SetGatesOnColumnMoves(!session.Edit.Level.gatesOnColumnMoves);
            };

            proofTitle = Ui.Text(root, "Proof Title", x, 124, width, 18, display, 13, Lcd.Ink);
            proofText = Ui.Text(root, "Proof Text", x, 143, width, 68, mono, 10, Lcd.Text2,
                TextAlignmentOptions.TopLeft);
            proofText.textWrappingMode = TextWrappingModes.Normal;

            progressWidth = width - 4f;
            progressBox = Ui.Rect(root, "Progress", x, 212, width, 10);
            Ui.Frame(progressBox, "Progress Frame", 0, 0, width, 10, 3f, 2f, Lcd.Ink);
            progressFill = Ui.Rect(progressBox, "Progress Fill", 2, 2, 0, 6);
            progressFill.gameObject.AddComponent<Image>().color = Lcd.Ink;
            progressFill.GetComponent<Image>().raycastTarget = false;

            solveButton = HudButton.CreateSolid(root, "SOLVE", x, 226, width, 44, display, 13, ToggleSolve);
            testPlayButton = HudButton.Create(root, "TEST PLAY", x, 274, width, 40, display, 12, TestPlay);
            exportButton = HudButton.Create(root, "EXPORT", x, 318, width, 40, display, 12, Export);
            HudButton.Create(root, "BACK", x, 362, width, 40, display, 12, Lcd.Text2, Lcd.Light,
                () => flow.Show(GameScreen.MyLevels));
        }

        void BuildLeft(Transform root)
        {
            const float x = 48f, width = 232f;
            var heading = Ui.Text(root, "Heading", x, 40, width, 18, mono, 13, Lcd.Text3, text: "MY LEVELS · EDITOR");
            heading.characterSpacing = 8;
            var nameLabel = Ui.Text(root, "Name Label", x, 68, width, 16, mono, 12, Lcd.Text3, text: "NAME");
            nameLabel.characterSpacing = 8;
            nameField = NameField(root, x, 88, width, 40, 18, 20);

            startTab = HudButton.Create(root, "START", x, 142, 116, 48, display, 15, () => SetLayer(EditLayer.Start));
            targetTab = HudButton.Create(root, "TARGET", x + 116, 142, 116, 48, display, 15,
                () => SetLayer(EditLayer.Target));

            const float cardY = 204f, cardHeight = 130f, cell = 12f, gap = 2f;
            Ui.Box(root, "Other Picture", x, cardY, width, cardHeight, 10f, Lcd.Cell);
            for (int row = 0; row < BoardSize; row++)
            {
                for (int column = 0; column < BoardSize; column++)
                {
                    miniBoard[row * BoardSize + column] = Ui.Box(root, $"Mini {row + 1},{column + 1}",
                        x + 14 + column * (cell + gap), cardY + 10 + row * (cell + gap), cell, cell, 2f,
                        Lcd.Background);
                }
            }

            otherName = Ui.Text(root, "Other Name", x + 138, cardY + 24, 90, 20, display, 13, Lcd.Ink);
            Ui.Text(root, "Other Hint", x + 138, cardY + 48, 90, 18, mono, 12, Lcd.Text2, text: "click or");
            Ui.Frame(root, "Tab Key", x + 138, cardY + 70, 40, 26, 5f, 2f, Lcd.Ink);
            Ui.Text(root, "Tab Key Label", x + 138, cardY + 70, 40, 26, mono, 12, Lcd.Ink, TextAlignmentOptions.Center,
                "Tab");
            UiHotspot.Create(root, "Other Picture Hotspot", x, cardY, width, cardHeight).Click = SwapLayer;

            const float toolWidth = 112f, toolHeight = 44f, toolGap = 8f, toolY = 348f;
            for (int i = 0; i < ToolOrder.Length; i++)
            {
                var tool = ToolOrder[i];
                toolButtons[i] = HudButton.Create(root, ToolLabels[i], x + (i % 2) * (toolWidth + toolGap),
                    toolY + (i / 2) * (toolHeight + toolGap),
                    toolWidth, toolHeight, display, 14, () => PickTool(tool));
            }

            opButton = HudButton.Create(root, "OP &", x + toolWidth + toolGap, toolY + 2 * (toolHeight + toolGap),
                toolWidth, toolHeight, display, 14, CycleOp);

            undoButton = HudButton.Create(root, "UNDO", x, 600, 110, 44, display, 14, Undo);
            redoButton = HudButton.Create(root, "REDO", x + 122, 600, 110, 44, display, 14, Redo);
        }

        void BuildCentre(Transform root)
        {
            const float x = 360f, width = 472f;
            paintLabel = Ui.Text(root, "Painting", x, 84, 300, 24, display, 16, Lcd.Ink);
            ghostLabel = Ui.Text(root, "Ghosts", x + 272, 84, 200, 24, mono, 12, Lcd.Text3,
                TextAlignmentOptions.MidlineRight);
            statusDot = Ui.Box(root, "Status Dot", x, 622, 10, 10, 2f, Lcd.Text2);
            statusText = Ui.Text(root, "Status", x + 18, 610, width - 18, 34, mono, 13, Lcd.Text2);
        }

        /// <summary>Where the options beside each row go, in line with the board's rows.</summary>
        struct RowLayout
        {
            public float Top, Pitch, CellHeight, LeftX, RightX, FillWidth, WrapX, GateX, LinkX, Chip, LinkSize, HeaderY,
                HeaderSize, Font;
        }

        void BuildRows(Transform root, RowLayout layout)
        {
            float headerWidth = layout.Chip + 12f;
            Ui.Text(root, "Wrap Header", layout.WrapX - 6, layout.HeaderY, headerWidth, 16, mono, layout.HeaderSize,
                Lcd.Text3, TextAlignmentOptions.Center, "WRAP");
            Ui.Text(root, "Gate Header", layout.GateX - 6, layout.HeaderY, headerWidth, 16, mono, layout.HeaderSize,
                Lcd.Text3, TextAlignmentOptions.Center, "GATE");
            Ui.Text(root, "Link Header", layout.LinkX - 6, layout.HeaderY, layout.LinkSize + 12, 16, mono,
                layout.HeaderSize, Lcd.Text3, TextAlignmentOptions.Center, "LINK");

            for (int i = 0; i < BoardSize; i++)
            {
                int row = i;
                float y = layout.Top + row * layout.Pitch;
                fillLeft[row] = HudButton.Create(root, $"1-IN left {row + 1}", layout.LeftX, y, layout.FillWidth,
                    layout.CellHeight, mono, layout.Font,
                    () => session.Edit.SetFill(row, true, !session.Edit.HasFill(row, true)));
                fillLeft[row].Text = "1›";
                fillRight[row] = HudButton.Create(root, $"1-IN right {row + 1}", layout.RightX, y, layout.FillWidth,
                    layout.CellHeight, mono, layout.Font,
                    () => session.Edit.SetFill(row, false, !session.Edit.HasFill(row, false)));
                fillRight[row].Text = "‹1";

                float chipY = y + (layout.CellHeight - layout.Chip) / 2f;
                wrap[row] = HudButton.Create(root, $"Wrap {row + 1}", layout.WrapX, chipY, layout.Chip, layout.Chip,
                    mono, layout.Font,
                    () => session.Edit.SetRotate(row, !session.Edit.IsRotating(row)));
                wrap[row].Text = "";
                // A dashed loop, like the belt around a wrapping row.
                wrap[row].AddIcon(Ui.Frame(wrap[row].transform, "Icon", (layout.Chip - 20f) / 2f,
                    (layout.Chip - 14f) / 2f, 20, 14, 5f, 2f, Lcd.Ink, 3f));

                gate[row] = HudButton.Create(root, $"Gate {row + 1}", layout.GateX, chipY, layout.Chip, layout.Chip,
                    mono, layout.Font + 1f, () => CycleRowGate(row));

                if (row == BoardSize - 1)
                {
                    continue;
                }

                float gap = layout.Pitch - layout.CellHeight;
                link[row] = HudButton.Create(root, $"Link {row + 1}", layout.LinkX,
                    y + layout.CellHeight + gap / 2f - layout.LinkSize / 2f, layout.LinkSize, layout.LinkSize, mono,
                    layout.Font,
                    () => session.Edit.SetChain(row, session.Edit.ChainTopOf(row) != row));
                link[row].Text = "";
                // Two links of a chain, one row over the next.
                float ringX = (layout.LinkSize - 12f) / 2f, ringY = (layout.LinkSize - 20f) / 2f;
                link[row].AddIcon(Ui.Frame(link[row].transform, "Icon Top", ringX, ringY, 12, 12, 4f, 2f, Lcd.Ink));
                link[row].AddIcon(Ui.Frame(link[row].transform, "Icon Bottom", ringX, ringY + 8f, 12, 12, 4f, 2f,
                    Lcd.Ink));
            }
        }

        void BuildRight(Transform root)
        {
            const float x = 1048f, width = 192f;
            Ui.Text(root, "Loss Title", x, 40, width, 18, display, 14, Lcd.Ink, text: "BITS MAY FALL");
            for (int i = 0; i < LossOrder.Length; i++)
            {
                var rule = LossOrder[i];
                lossTabs[i] = HudButton.Create(root, LossLabels[i], x + i * 64f, 62, 64, 36, display, 11, () =>
                    session.Edit.SetLoss(rule, session.Edit.Level.lossCount));
            }

            Ui.Text(root, "Loss Count Label", x, 106, 60, 32, mono, 13, Lcd.Text2, text: "Bits");
            lossDown = HudButton.Create(root, "Fewer", x + width - 96, 106, 32, 32, mono, 15, () => StepLoss(-1));
            lossDown.Text = "-";
            lossCount = Ui.Text(root, "Loss Count", x + width - 64, 106, 32, 32, display, 18, Lcd.Ink,
                TextAlignmentOptions.Center);
            lossUp = HudButton.Create(root, "More", x + width - 32, 106, 32, 32, mono, 15, () => StepLoss(1));
            lossUp.Text = "+";

            Ui.Frame(root, "Columns Box", x, 150, 18, 18, 4f, 2f, Lcd.Ink);
            columnsTick = Ui.Box(root, "Columns Tick", x + 4, 154, 10, 10, 2f, Lcd.Ink);
            var columnsText = Ui.Text(root, "Columns Text", x + 26, 144, width - 26, 34, mono, 12, Lcd.Text2,
                text: "Gates also act on column moves");
            columnsText.textWrappingMode = TextWrappingModes.Normal;
            UiHotspot.Create(root, "Columns Hotspot", x, 144, width, 34).Click = () =>
            {
                Sfx.Play(Sound.Click);
                session.Edit.SetGatesOnColumnMoves(!session.Edit.Level.gatesOnColumnMoves);
            };

            var proof = Ui.Text(root, "Proof Label", x, 196, width, 16, mono, 12, Lcd.Text3, text: "PROOF");
            proof.characterSpacing = 8;
            proofTitle = Ui.Text(root, "Proof Title", x, 218, width, 24, display, 18, Lcd.Ink);
            proofText = Ui.Text(root, "Proof Text", x, 246, width, 72, mono, 12, Lcd.Text2,
                TextAlignmentOptions.TopLeft);
            proofText.textWrappingMode = TextWrappingModes.Normal;

            progressBox = Ui.Rect(root, "Progress", x, 322, width, 12);
            Ui.Frame(progressBox, "Progress Frame", 0, 0, width, 12, 4f, 2f, Lcd.Ink);
            progressFill = Ui.Rect(progressBox, "Progress Fill", 2, 2, 0, 8);
            progressFill.gameObject.AddComponent<Image>().color = Lcd.Ink;
            progressFill.GetComponent<Image>().raycastTarget = false;

            var keys = Ui.Text(root, "Keys", x, 340, width, 90, mono, 12, Lcd.Text3, TextAlignmentOptions.BottomLeft,
                "Space paint\nZ undo · Y redo\nEnter solve · T test\nX export · Esc back");
            keys.textWrappingMode = TextWrappingModes.Normal;

            solveButton = HudButton.CreateSolid(root, "SOLVE", x, 440, width, 52, display, 15, ToggleSolve);
            testPlayButton = HudButton.Create(root, "TEST PLAY", x, 500, width, 44, display, 14, TestPlay);
            exportButton = HudButton.Create(root, "EXPORT", x, 552, width, 44, display, 14, Export);
            HudButton.Create(root, "BACK", x, 604, width, 40, display, 12, Lcd.Text2, Lcd.Light,
                () => flow.Show(GameScreen.MyLevels));
        }

        /// <summary>
        /// Draws the name field's caret. TextMeshPro's own runs from the font's ascent to its descent, and
        /// Silkscreen's ascent is far above its capitals, so that caret sits too high and cannot be moved.
        /// </summary>
        void RefreshCaret()
        {
            bool focused = nameField.isFocused;
            int position = nameField.stringPosition;
            if (position != caretShownAt)
            {
                caretShownAt = position;
                caretMovedTime = Time.unscaledTime;
            }

            nameCaret.enabled = focused
                && Mathf.Repeat(Time.unscaledTime - caretMovedTime, 2f * CaretBlink) < CaretBlink;
            if (!nameCaret.enabled)
            {
                return;
            }

            var info = nameText.textInfo;
            int count = info.characterCount;
            float x = count == 0 ? 0f : position <= 0 ? info.characterInfo[0].origin
                : info.characterInfo[Mathf.Min(position, count) - 1].xAdvance;
            nameCaret.rectTransform.anchoredPosition = new Vector2(x, nameCaret.rectTransform.anchoredPosition.y);
        }

        /// <summary>
        /// The name box. Only allowed characters get in, as capitals; the name is stored when typing ends.
        /// </summary>
        TMP_InputField NameField(Transform root, float x, float y, float width, float height, float fontSize,
            float caretHeight)
        {
            var rectTransform = Ui.Rect(root, "Name Field", x, y, width, height);
            rectTransform.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(rectTransform, "Ground", 0, 0, width, height, 6f, Lcd.Card);
            Ui.Frame(rectTransform, "Frame", 0, 0, width, height, 6f, 2f, Lcd.Ink);
            var area = Ui.Rect(rectTransform, "Text Area", 10, 0, width - 20, height);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Ui.Text(area, "Text", 0, 0, width - 20, height, display, fontSize, Lcd.Ink);
            text.richText = false;
            nameText = text;
            nameCaret = Ui.Rect(text.transform, "Caret", 0, (height - caretHeight) / 2f, 2,
                caretHeight).gameObject.AddComponent<Image>();
            nameCaret.color = Lcd.Ink;
            nameCaret.raycastTarget = false;
            nameCaret.enabled = false;

            var field = rectTransform.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.fontAsset = display;
            field.pointSize = fontSize;
            field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = LevelEdit.MaxNameLength;
            field.customCaretColor = true;
            field.caretColor = Color.clear; // see RefreshCaret
            field.selectionColor = new Color(Lcd.Text3.r / 255f, Lcd.Text3.g / 255f, Lcd.Text3.b / 255f, 0.4f);
            field.onValidateInput = (_, __, character) => LevelEdit.IsNameChar(character)
                ? char.ToUpperInvariant(character)
                : '\0';
            field.onEndEdit.AddListener(_ =>
            {
                Ui.NoteTypingEnded();
                CommitName();
                nameField.SetTextWithoutNotify(session.Edit.Level.name.ToUpperInvariant());
            });
            return field;
        }
    }
}
