using System;
using ShiftRegister.Core;
using ShiftRegister.Game.Audio;
using ShiftRegister.Game.CustomLevels;
using ShiftRegister.Game.Hud;
using ShiftRegister.Game.View;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShiftRegister.Game.LevelEditor
{
    /// <summary>
    /// The level editor screen (layout A of the editor canvas), desktop only, on its own canvas. The board
    /// stays where the game draws it and is painted directly: click or drag, or arrows and Space. Tab swaps
    /// start and target, 1–5 pick a tool, 6 changes the gate tool's operator, Z / Y undo and redo, Enter
    /// solves, T test plays, Esc cancels a search or leaves. Each row's options sit beside it: 1-IN toggles in the arrow
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

        const int N = BoardBits.Size;
        const double SolveBudgetMs = 8.0;
        const float AutosaveDelay = 2f;
        const float CaretBlink = 0.53f;
        static readonly PaintTool[] ToolOrder = { PaintTool.Bit, PaintTool.Wall, PaintTool.Pin, PaintTool.Gate, PaintTool.Erase };
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

        RectTransform left, right;
        TMP_InputField nameField;
        TextMeshProUGUI nameText;
        Image nameCaret;
        int caretShownAt = -1;
        float caretMovedTime;
        HudButton startTab, targetTab, solveButton, testPlayButton, undoButton, redoButton;
        readonly HudButton[] toolButtons = new HudButton[ToolOrder.Length];
        HudButton opButton, lossDown, lossUp;
        readonly HudButton[] fillLeft = new HudButton[N], fillRight = new HudButton[N], wrap = new HudButton[N], gate = new HudButton[N];
        readonly HudButton[] link = new HudButton[N - 1];
        readonly HudButton[] lossTabs = new HudButton[LossOrder.Length];
        TextMeshProUGUI lossCount;
        Image columnsTick;
        readonly Image[] mini = new Image[N * N];
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
            if (session != null) ResumeAfterTestPlay();
            else Open();
        }

        void OnDisable()
        {
            ScreenLayout.LayoutChanged -= ApplyFrame;
            game.BoardLent = false;
            if (session == null) return;
            session.EndPaint();
            session.CancelSolve();
            CommitName();
            Save();
            if (keepSession) return; // test play comes back here, with the undo history
            session.Edit.Changed -= OnEdited;
            session = null;
            shownBoard = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && session != null) Save();
        }

        void OnApplicationQuit()
        {
            if (session != null) Save();
        }

        void ApplyFrame()
        {
            var scaler = GetComponent<CanvasScaler>();
            if (scaler != null) scaler.referenceResolution = new Vector2(ScreenLayout.FrameWidthPx, ScreenLayout.FrameHeightPx);
            left.anchoredPosition = new Vector2(-ScreenLayout.OuterShiftPx, 0f);
            right.anchoredPosition = new Vector2(ScreenLayout.OuterShiftPx, 0f);
            game.PlaceBoard();
        }

        // ---------------------------------------------------------------- level

        void Open()
        {
            var entry = game.CustomLevels.Get(levelId);
            if (entry == null) throw new InvalidOperationException($"No custom level '{levelId}' to edit.");

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
                session.Edit.SetSolution(moves, entry.proof);
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
            if (!dirty) return;
            dirty = false;
            game.CustomLevels.Update(session.Edit.Level, session.Edit.Proof, DateTime.UtcNow.Ticks);
            CustomLevelStore.Save(game.CustomLevels);
        }

        void CommitName() => session.Edit.SetName(nameField.text);

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (session == null) return;

            if (session.IsSolving && session.Tick(SolveBudgetMs)) OnSolveFinished();
            else if (session.IsSolving) RefreshProof();

            HandlePointer();
            HandleKeyboard();
            game.Controls.HighlightCell(row, column);
            RefreshCaret();

            if (dirty && Time.unscaledTime >= saveAt && !painting) Save();
        }

        void OnSolveFinished()
        {
            Sfx.Play(session.Outcome == SolveOutcome.None ? Sound.Solved : session.Outcome == SolveOutcome.NoSolution ? Sound.Broken : Sound.Blocked);
            dirty = true;
            Refresh();
        }

        void HandlePointer()
        {
            var p = Pointer.current;
            if (p == null || cam == null) return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Feed(p.position.ReadValue(), p.press.isPressed, p.press.wasPressedThisFrame, overUi);
        }

        /// <summary>One frame of pointer input. Public so play-mode checks can drive it without input devices.</summary>
        public void Feed(Vector2 screen, bool down, bool justPressed, bool overUi)
        {
            if (session == null || cam == null) return;
            if (!down && painting)
            {
                painting = false;
                session.EndPaint();
                return;
            }
            if (Time.frameCount == enabledFrame) return; // the click that opened the screen must not paint

            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -cam.transform.position.z));
            Vector2 local = game.Controls.transform.InverseTransformPoint(world);
            bool onCell = BoardControls.HitCell(local, out int r, out int c);

            if (painting)
            {
                if (onCell) PaintTo(r, c);
                return;
            }
            if (overUi || !onCell) return;
            row = r;
            column = c;
            if (!justPressed) return;
            painting = true;
            session.BeginPaint(r, c);
            Sfx.Play(Sound.Tick);
        }

        void PaintTo(int r, int c)
        {
            if (r == row && c == column) return;
            row = r;
            column = c;
            session.PaintTo(r, c);
            Sfx.Play(Sound.Tick);
        }

        void HandleKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null || Time.frameCount == enabledFrame || Ui.IsTyping) return;

            if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) MoveCursor(0, -1);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) MoveCursor(0, 1);
            else if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) MoveCursor(-1, 0);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) MoveCursor(1, 0);

            if (kb.spaceKey.wasPressedThisFrame)
            {
                session.BeginPaint(row, column);
                session.EndPaint();
                Sfx.Play(Sound.Tick);
            }
            if (kb.tabKey.wasPressedThisFrame) SwapLayer();
            for (int i = 0; i < ToolOrder.Length; i++)
                if (kb[Key.Digit1 + i].wasPressedThisFrame) PickTool(ToolOrder[i]);
            if (kb.digit6Key.wasPressedThisFrame) CycleOp();

            if (kb.yKey.wasPressedThisFrame || (kb.zKey.wasPressedThisFrame && kb.shiftKey.isPressed)) Redo();
            else if (kb.zKey.wasPressedThisFrame) Undo();

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) ToggleSolve();
            if (kb.tKey.wasPressedThisFrame) TestPlay();
        }

        void MoveCursor(int dr, int dc)
        {
            int r = Mathf.Clamp(row + dr, 0, N - 1), c = Mathf.Clamp(column + dc, 0, N - 1);
            if (r == row && c == column) return;
            row = r;
            column = c;
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

        void CycleRowGate(int r)
        {
            var g = session.Edit.GateOn(r);
            if (g != null) session.Edit.SetGateOp(r, EditorSession.NextOp(g.op));
        }

        void StepLoss(int delta)
        {
            var level = session.Edit.Level;
            session.Edit.SetLoss(level.lossRule, level.lossCount + delta);
        }

        void Undo()
        {
            if (session.Edit.Undo()) Sfx.Play(Sound.Undo);
            else Sfx.Play(Sound.Blocked);
        }

        void Redo()
        {
            if (session.Edit.Redo()) Sfx.Play(Sound.Undo);
            else Sfx.Play(Sound.Blocked);
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

        /// <summary>Esc while searching stops the search instead of leaving. False if nothing was running.</summary>
        public bool CancelSolve()
        {
            if (session == null || !session.CancelSolve()) return false;
            Refresh();
            return true;
        }

        // ---------------------------------------------------------------- view

        void Refresh()
        {
            if (session == null) return;
            RefreshBoard();

            var other = session.Layer == EditLayer.Start ? EditLayer.Target : EditLayer.Start;
            string layerName = session.Layer == EditLayer.Start ? "START" : "TARGET";
            string otherWord = session.Layer == EditLayer.Start ? "TARGET" : "START";
            startTab.Selected = session.Layer == EditLayer.Start;
            targetTab.Selected = session.Layer == EditLayer.Target;
            otherName.text = otherWord;
            paintLabel.text = "PAINTING " + layerName;
            ghostLabel.text = "dashed = " + otherWord.ToLowerInvariant();
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                mini[r * N + c].color = session.Edit.GetBit(other, r, c) ? Lcd.Ink : Lcd.Background;
            for (int i = 0; i < ToolOrder.Length; i++)
                toolButtons[i].Selected = session.Tool == ToolOrder[i];
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
            RefreshProof();
        }

        void RefreshRows()
        {
            var edit = session.Edit;
            for (int r = 0; r < N; r++)
            {
                fillLeft[r].Selected = edit.HasFill(r, true);
                fillRight[r].Selected = edit.HasFill(r, false);
                wrap[r].Selected = edit.IsRotating(r);
                var g = edit.GateOn(r);
                gate[r].Text = g != null ? BoardView.GateSymbol(g.op) : "·";
                gate[r].Disabled = g == null;
                gate[r].Selected = g != null;
                if (r < N - 1) link[r].Selected = edit.ChainTopOf(r) == r;
            }
        }

        void RefreshSettings()
        {
            var level = session.Edit.Level;
            for (int i = 0; i < LossOrder.Length; i++) lossTabs[i].Selected = level.lossRule == LossOrder[i];
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
            progressFill.sizeDelta = new Vector2(Mathf.Clamp01(session.SolveProgress) * 188f, progressFill.sizeDelta.y);
        }

        /// <summary>Painted bits only need a redraw; anything else changes the board's objects, which are rebuilt.</summary>
        void RefreshBoard()
        {
            var d = session.DisplayLevel();
            if (!SameBoardObjects(d, shownBoard))
            {
                var rules = LevelRules.Compile(d);
                game.BoardView.Build(rules);
                game.Controls.Build(rules, withArrows: false, tightBelts: true);
            }
            game.BoardView.Show(BoardBits.FromRows(d.start));
            shownBoard = d;
        }

        /// <summary>True if only the painted picture differs (which <see cref="BoardView.Show"/> can draw alone).</summary>
        static bool SameBoardObjects(LevelData a, LevelData b)
        {
            if (b == null) return false;
            var startless = LevelEdit.Clone(a);
            startless.start = b.start;
            return LevelEdit.SamePuzzle(startless, b);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            var canvas = (RectTransform)transform;
            var frame = Ui.Rect(canvas, "Editor", 0, 0, 1280, 720);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.anchoredPosition = Vector2.zero;

            left = Ui.Rect(frame, "Left", 0, 0, 1280, 720);
            right = Ui.Rect(frame, "Right", 0, 0, 1280, 720);
            BuildLeft(left);
            BuildCentre(frame);
            BuildRows(frame);
            BuildRight(right);
        }

        void BuildLeft(Transform root)
        {
            const float x = 48f, w = 232f;
            var heading = Ui.Text(root, "Heading", x, 40, w, 18, mono, 13, Lcd.Text3, text: "MY LEVELS · EDITOR");
            heading.characterSpacing = 8;
            var nameLabel = Ui.Text(root, "Name Label", x, 68, w, 16, mono, 12, Lcd.Text3, text: "NAME");
            nameLabel.characterSpacing = 8;
            nameField = NameField(root, x, 88, w, 40);

            startTab = HudButton.Create(root, "START", x, 142, 116, 48, display, 15, () => SetLayer(EditLayer.Start));
            targetTab = HudButton.Create(root, "TARGET", x + 116, 142, 116, 48, display, 15, () => SetLayer(EditLayer.Target));

            const float cardY = 204f, cardH = 130f, cell = 12f, gap = 2f;
            Ui.Box(root, "Other Picture", x, cardY, w, cardH, 10f, Lcd.Cell);
            for (int r = 0; r < N; r++)
            for (int c = 0; c < N; c++)
                mini[r * N + c] = Ui.Box(root, $"Mini {r + 1},{c + 1}", x + 14 + c * (cell + gap), cardY + 10 + r * (cell + gap), cell, cell, 2f, Lcd.Background);
            otherName = Ui.Text(root, "Other Name", x + 138, cardY + 24, 90, 20, display, 13, Lcd.Ink);
            Ui.Text(root, "Other Hint", x + 138, cardY + 48, 90, 18, mono, 12, Lcd.Text2, text: "click or");
            Ui.Frame(root, "Tab Key", x + 138, cardY + 70, 40, 26, 5f, 2f, Lcd.Ink);
            Ui.Text(root, "Tab Key Label", x + 138, cardY + 70, 40, 26, mono, 12, Lcd.Ink, TextAlignmentOptions.Center, "Tab");
            UiHotspot.Create(root, "Other Picture Hotspot", x, cardY, w, cardH).Click = SwapLayer;

            const float toolW = 112f, toolH = 44f, toolGap = 8f, toolY = 348f;
            for (int i = 0; i < ToolOrder.Length; i++)
            {
                var tool = ToolOrder[i];
                toolButtons[i] = HudButton.Create(root, ToolLabels[i], x + (i % 2) * (toolW + toolGap), toolY + (i / 2) * (toolH + toolGap),
                    toolW, toolH, display, 14, () => PickTool(tool));
            }

            opButton = HudButton.Create(root, "OP &", x + toolW + toolGap, toolY + 2 * (toolH + toolGap), toolW, toolH, display, 14, CycleOp);

            undoButton = HudButton.Create(root, "UNDO", x, 600, 110, 44, display, 14, Undo);
            redoButton = HudButton.Create(root, "REDO", x + 122, 600, 110, 44, display, 14, Redo);
        }

        void BuildCentre(Transform root)
        {
            const float x = 360f, w = 472f;
            paintLabel = Ui.Text(root, "Painting", x, 84, 300, 24, display, 16, Lcd.Ink);
            ghostLabel = Ui.Text(root, "Ghosts", x + 272, 84, 200, 24, mono, 12, Lcd.Text3, TextAlignmentOptions.MidlineRight);
            statusDot = Ui.Box(root, "Status Dot", x, 622, 10, 10, 2f, Lcd.Text2);
            statusText = Ui.Text(root, "Status", x + 18, 610, w - 18, 34, mono, 13, Lcd.Text2);
        }

        /// <summary>The options beside each row, in line with the game's arrow buttons and row tags.</summary>
        void BuildRows(Transform root)
        {
            const float top = 124f, pitch = 60f, cellH = 52f;
            const float wrapX = 892f, gateX = 938f, linkX = 984f, chip = 40f, linkSize = 32f;
            Ui.Text(root, "Wrap Header", wrapX - 6, 100, chip + 12, 16, mono, 11, Lcd.Text3, TextAlignmentOptions.Center, "WRAP");
            Ui.Text(root, "Gate Header", gateX - 6, 100, chip + 12, 16, mono, 11, Lcd.Text3, TextAlignmentOptions.Center, "GATE");
            Ui.Text(root, "Link Header", linkX - 6, 100, linkSize + 12, 16, mono, 11, Lcd.Text3, TextAlignmentOptions.Center, "LINK");

            for (int i = 0; i < N; i++)
            {
                int r = i;
                float y = top + r * pitch;
                fillLeft[r] = HudButton.Create(root, $"1-IN left {r + 1}", 312, y, 40, cellH, mono, 14,
                    () => session.Edit.SetFill(r, true, !session.Edit.HasFill(r, true)));
                fillLeft[r].Text = "1›";
                fillRight[r] = HudButton.Create(root, $"1-IN right {r + 1}", 840, y, 40, cellH, mono, 14,
                    () => session.Edit.SetFill(r, false, !session.Edit.HasFill(r, false)));
                fillRight[r].Text = "‹1";

                float chipY = y + (cellH - chip) / 2f;
                wrap[r] = HudButton.Create(root, $"Wrap {r + 1}", wrapX, chipY, chip, chip, mono, 14,
                    () => session.Edit.SetRotate(r, !session.Edit.IsRotating(r)));
                wrap[r].Text = "";
                // A dashed loop, like the belt around a wrapping row.
                wrap[r].AddIcon(Ui.Frame(wrap[r].transform, "Icon", 10, 13, 20, 14, 5f, 2f, Lcd.Ink, 3f));

                gate[r] = HudButton.Create(root, $"Gate {r + 1}", gateX, chipY, chip, chip, mono, 15, () => CycleRowGate(r));

                if (r == N - 1) continue;
                link[r] = HudButton.Create(root, $"Link {r + 1}", linkX, y + cellH + 4f - linkSize / 2f, linkSize, linkSize, mono, 14,
                    () => session.Edit.SetChain(r, session.Edit.ChainTopOf(r) != r));
                link[r].Text = "";
                // Two links of a chain, one row over the next.
                link[r].AddIcon(Ui.Frame(link[r].transform, "Icon Top", 10, 6, 12, 12, 4f, 2f, Lcd.Ink));
                link[r].AddIcon(Ui.Frame(link[r].transform, "Icon Bottom", 10, 14, 12, 12, 4f, 2f, Lcd.Ink));
            }
        }

        void BuildRight(Transform root)
        {
            const float x = 1048f, w = 192f;
            Ui.Text(root, "Loss Title", x, 40, w, 18, display, 14, Lcd.Ink, text: "BITS MAY FALL");
            for (int i = 0; i < LossOrder.Length; i++)
            {
                var rule = LossOrder[i];
                lossTabs[i] = HudButton.Create(root, LossLabels[i], x + i * 64f, 62, 64, 36, display, 11, () =>
                    session.Edit.SetLoss(rule, session.Edit.Level.lossCount));
            }
            Ui.Text(root, "Loss Count Label", x, 106, 60, 32, mono, 13, Lcd.Text2, text: "Bits");
            lossDown = HudButton.Create(root, "Fewer", x + w - 96, 106, 32, 32, mono, 15, () => StepLoss(-1));
            lossDown.Text = "-";
            lossCount = Ui.Text(root, "Loss Count", x + w - 64, 106, 32, 32, display, 18, Lcd.Ink, TextAlignmentOptions.Center);
            lossUp = HudButton.Create(root, "More", x + w - 32, 106, 32, 32, mono, 15, () => StepLoss(1));
            lossUp.Text = "+";

            Ui.Frame(root, "Columns Box", x, 150, 18, 18, 4f, 2f, Lcd.Ink);
            columnsTick = Ui.Box(root, "Columns Tick", x + 4, 154, 10, 10, 2f, Lcd.Ink);
            var columnsText = Ui.Text(root, "Columns Text", x + 26, 144, w - 26, 34, mono, 12, Lcd.Text2, text: "Gates also act on column moves");
            columnsText.textWrappingMode = TextWrappingModes.Normal;
            UiHotspot.Create(root, "Columns Hotspot", x, 144, w, 34).Click = () =>
            {
                Sfx.Play(Sound.Click);
                session.Edit.SetGatesOnColumnMoves(!session.Edit.Level.gatesOnColumnMoves);
            };

            var proof = Ui.Text(root, "Proof Label", x, 196, w, 16, mono, 12, Lcd.Text3, text: "PROOF");
            proof.characterSpacing = 8;
            proofTitle = Ui.Text(root, "Proof Title", x, 218, w, 24, display, 18, Lcd.Ink);
            proofText = Ui.Text(root, "Proof Text", x, 246, w, 72, mono, 12, Lcd.Text2, TextAlignmentOptions.TopLeft);
            proofText.textWrappingMode = TextWrappingModes.Normal;

            progressBox = Ui.Rect(root, "Progress", x, 322, w, 12);
            Ui.Frame(progressBox, "Progress Frame", 0, 0, w, 12, 4f, 2f, Lcd.Ink);
            progressFill = Ui.Rect(progressBox, "Progress Fill", 2, 2, 0, 8);
            progressFill.gameObject.AddComponent<Image>().color = Lcd.Ink;
            progressFill.GetComponent<Image>().raycastTarget = false;

            var keys = Ui.Text(root, "Keys", x, 372, w, 108, mono, 12, Lcd.Text3, TextAlignmentOptions.BottomLeft,
                "Space paint\nZ undo · Y redo\nEnter solve · T test\nEsc back");
            keys.textWrappingMode = TextWrappingModes.Normal;

            solveButton = HudButton.CreateSolid(root, "SOLVE", x, 492, w, 52, display, 15, ToggleSolve);
            testPlayButton = HudButton.Create(root, "TEST PLAY", x, 552, w, 44, display, 14, TestPlay);
            HudButton.Create(root, "BACK", x, 604, w, 40, display, 12, Lcd.Text2, Lcd.Light, () => flow.Show(GameScreen.MyLevels));
        }

        /// <summary>
        /// Draws the name field's caret. TextMeshPro's own runs from the font's ascent to its descent, and
        /// Silkscreen's ascent is far above its capitals, so that caret sits too high and cannot be moved.
        /// </summary>
        void RefreshCaret()
        {
            bool focused = nameField.isFocused;
            int pos = nameField.stringPosition;
            if (pos != caretShownAt)
            {
                caretShownAt = pos;
                caretMovedTime = Time.unscaledTime;
            }
            nameCaret.enabled = focused && Mathf.Repeat(Time.unscaledTime - caretMovedTime, 2f * CaretBlink) < CaretBlink;
            if (!nameCaret.enabled) return;

            var info = nameText.textInfo;
            int count = info.characterCount;
            float x = count == 0 ? 0f : pos <= 0 ? info.characterInfo[0].origin : info.characterInfo[Mathf.Min(pos, count) - 1].xAdvance;
            nameCaret.rectTransform.anchoredPosition = new Vector2(x, nameCaret.rectTransform.anchoredPosition.y);
        }

        /// <summary>The name box. Only allowed characters get in, as capitals; the name is stored when typing ends.</summary>
        TMP_InputField NameField(Transform root, float x, float y, float w, float h)
        {
            var rt = Ui.Rect(root, "Name Field", x, y, w, h);
            rt.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(rt, "Ground", 0, 0, w, h, 6f, Lcd.Card);
            Ui.Frame(rt, "Frame", 0, 0, w, h, 6f, 2f, Lcd.Ink);
            var area = Ui.Rect(rt, "Text Area", 10, 0, w - 20, h);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Ui.Text(area, "Text", 0, 0, w - 20, h, display, 18, Lcd.Ink);
            text.richText = false;
            nameText = text;
            const float caretH = 20f;
            nameCaret = Ui.Rect(text.transform, "Caret", 0, (h - caretH) / 2f, 2, caretH).gameObject.AddComponent<Image>();
            nameCaret.color = Lcd.Ink;
            nameCaret.raycastTarget = false;
            nameCaret.enabled = false;

            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.fontAsset = display;
            field.pointSize = 18;
            field.richText = false;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = LevelEdit.MaxNameLength;
            field.customCaretColor = true;
            field.caretColor = Color.clear; // see RefreshCaret
            field.selectionColor = new Color(Lcd.Text3.r / 255f, Lcd.Text3.g / 255f, Lcd.Text3.b / 255f, 0.4f);
            field.onValidateInput = (_, __, ch) => LevelEdit.IsNameChar(ch) ? char.ToUpperInvariant(ch) : '\0';
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
