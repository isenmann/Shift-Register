using System;
using ShiftRegister.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ShiftRegister.Game.Hud
{
    /// <summary>
    /// A panel over the screen that shows a level's share code to copy, or takes one to import. The code
    /// sits in a text field, so it can also be selected or typed by hand.
    /// </summary>
    public sealed class ShareDialog : MonoBehaviour
    {
        const float MaxWidth = 600f, H = 320f;
        /// <summary>Longer than any code: the largest level with the longest solution.</summary>
        const int MaxCodeLength = 600;

        TMP_InputField field;
        TextMeshProUGUI title, hint, message;
        HudButton primary, paste;
        Func<string, string> import;
        string exportCode;
        bool waitingForPaste;

        public bool IsOpen => gameObject.activeSelf;

        public static ShareDialog Create(Transform parent, float frameW, float frameH, TMP_FontAsset display, TMP_FontAsset mono)
        {
            var root = Ui.Rect(parent, "Share Dialog", 0, 0, frameW, frameH);
            var dialog = root.gameObject.AddComponent<ShareDialog>();
            dialog.Build(root, frameW, frameH, display, mono);
            root.gameObject.SetActive(false);
            return dialog;
        }

        void Build(RectTransform root, float frameW, float frameH, TMP_FontAsset display, TMP_FontAsset mono)
        {
            float dialogWidth = Mathf.Min(MaxWidth, frameW - 24f);
            // Wider than any window, so nothing behind it can be clicked or read through it.
            var shade = Ui.Rect(root, "Shade", -2000f, -2000f, frameW + 4000f, frameH + 4000f).gameObject.AddComponent<Image>();
            var ground = (Color)Lcd.Background;
            ground.a = 0.9f;
            shade.color = ground;

            float x = (frameW - dialogWidth) / 2f, y = (frameH - H) / 2f;
            Ui.Box(root, "Panel", x, y, dialogWidth, H, 10f, Lcd.Card);
            Ui.Frame(root, "Panel Frame", x, y, dialogWidth, H, 10f, 3f, Lcd.Ink);
            title = Ui.Text(root, "Title", x + 24, y + 20, dialogWidth - 48, 26, display, 20, Lcd.Ink);
            hint = Ui.Text(root, "Hint", x + 24, y + 50, dialogWidth - 48, 20, mono, 13, Lcd.Text2);

            field = CodeField(root, x + 24, y + 78, dialogWidth - 48, 118, mono);
            field.onSubmit.AddListener(_ =>
            {
                if (import != null)
                {
                    Primary();
                }
            });
            message = Ui.Text(root, "Message", x + 24, y + 202, dialogWidth - 48, 34, mono, 13, Lcd.LossText, TextAlignmentOptions.TopLeft);
            message.textWrappingMode = TextWrappingModes.Normal;

            float buttonY = y + H - 68f, buttonWidth = (dialogWidth - 48f - 16f) / 3f;
            primary = HudButton.CreateSolid(root, "COPY", x + 24, buttonY, buttonWidth, 44, display, 14, Primary);
            paste = HudButton.Create(root, "PASTE", x + 24 + buttonWidth + 8f, buttonY, buttonWidth, 44, display, 14, Paste);
            HudButton.Create(root, "CLOSE", x + 24 + 2f * (buttonWidth + 8f), buttonY, buttonWidth, 44, display, 14, () => Close());
        }

        static TMP_InputField CodeField(Transform root, float x, float y, float width, float height, TMP_FontAsset mono)
        {
            var rectTransform = Ui.Rect(root, "Code Field", x, y, width, height);
            rectTransform.gameObject.AddComponent<Image>().color = Color.clear;
            Ui.Box(rectTransform, "Ground", 0, 0, width, height, 6f, Lcd.Light);
            Ui.Frame(rectTransform, "Frame", 0, 0, width, height, 6f, 2f, Lcd.Ink);
            var area = Ui.Rect(rectTransform, "Text Area", 10, 8, width - 20, height - 16);
            area.gameObject.AddComponent<RectMask2D>();
            var text = Ui.Text(area, "Text", 0, 0, width - 20, height - 16, mono, 15, Lcd.Ink, TextAlignmentOptions.TopLeft);
            text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal;

            var field = rectTransform.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.fontAsset = mono;
            field.pointSize = 15;
            field.richText = false;
            field.lineType = TMP_InputField.LineType.MultiLineSubmit;
            field.characterLimit = MaxCodeLength;
            field.customCaretColor = true;
            field.caretColor = Lcd.Ink;
            field.selectionColor = new Color(Lcd.Text3.r / 255f, Lcd.Text3.g / 255f, Lcd.Text3.b / 255f, 0.4f);
            // Share codes hold only these, so a stray space or line break from a paste never gets in.
            field.onValidateInput = (_, __, character) => char.IsLetterOrDigit(character) && character < 128 || character == '-' || character == '_' ? character : '\0';
            field.onEndEdit.AddListener(_ => Ui.NoteTypingEnded());
            return field;
        }

        /// <summary>Shows a proven level's code; a level that cannot be shared says why instead.</summary>
        public void OpenExport(LevelData level)
        {
            gameObject.SetActive(true);
            import = null;
            waitingForPaste = false;
            title.text = "EXPORT LEVEL";
            field.readOnly = true;
            primary.Text = "COPY";
            paste.gameObject.SetActive(false);

            if (ShareCode.CanEncode(level, out string reason))
            {
                exportCode = ShareCode.Encode(level);
                hint.text = "Anyone can open this code with IMPORT.";
                SetMessage("");
                primary.Disabled = false;
            }
            else
            {
                exportCode = "";
                hint.text = "";
                SetMessage(level.par > ShareCode.MaxSolutionMoves
                    ? $"This solution is too long to share. Beat the level in at most {ShareCode.MaxSolutionMoves} moves."
                    : reason);
                primary.Disabled = true;
            }

            field.SetTextWithoutNotify(exportCode);
        }

        /// <param name="tryImport">Takes the code; returns what is wrong with it, or null if it was imported.</param>
        public void OpenImport(Func<string, string> tryImport)
        {
            gameObject.SetActive(true);
            import = tryImport;
            waitingForPaste = false;
            exportCode = "";
            title.text = "IMPORT LEVEL";
            hint.text = "Paste or type a code. It starts with SR1-";
            SetMessage("");
            field.readOnly = false;
            field.SetTextWithoutNotify("");
            primary.Text = "IMPORT";
            primary.Disabled = false;
            paste.gameObject.SetActive(true);
            // On a phone that would cover the dialog with the keyboard before PASTE can be tapped.
            if (!Input.DeviceMode.IsMobile)
            {
                field.ActivateInputField();
            }
        }

        /// <summary>False if the dialog was not open.</summary>
        public bool Close()
        {
            if (!IsOpen)
            {
                return false;
            }

            if (field.isFocused)
            {
                field.DeactivateInputField();
            }

            gameObject.SetActive(false);
            return true;
        }

        void Update()
        {
            if (waitingForPaste && Clipboard.TryFinishPaste(out string text))
            {
                waitingForPaste = false;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    field.text = Clean(text);
                }
            }
        }

        void Primary()
        {
            if (import == null)
            {
                Clipboard.Copy(exportCode);
                SetMessage("Copied.", false);
                return;
            }

            string problem = import(field.text);
            if (problem == null)
            {
                Close();
            }
            else
            {
                SetMessage(problem);
            }
        }

        void Paste()
        {
            waitingForPaste = true;
            Clipboard.BeginPaste();
        }

        /// <summary>A paste can bring line breaks and spaces with it; the field does not validate text set from code.</summary>
        static string Clean(string text)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char character in text)
            {
                if (!char.IsWhiteSpace(character))
                {
                    kept.Append(character);
                }
            }

            return kept.ToString();
        }

        void SetMessage(string text, bool problem = true)
        {
            message.text = text;
            message.color = problem ? Lcd.LossText : Lcd.Text2;
        }
    }
}
