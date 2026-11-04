// Clipboard for the web build. Reading the clipboard needs a permission prompt and Unity keeps Ctrl+V
// from reaching the page, so pasting goes through a small HTML box, where the browser's own paste works.
// Copying tries the clipboard API, then execCommand, and last shows the same box with the text selected.
var ShiftRegisterClipboard = {
  $SRDefineBox: function () {
    if (window["__srShareBox"]) return;
    window["__srShareBox"] = function (title, value, readOnly, onDone) {
      var shade = document.createElement("div");
      shade.style.cssText = "position:fixed;inset:0;z-index:1000;display:flex;align-items:center;justify-content:center;background:rgba(195,205,156,.85);font-family:monospace;color:#232C15";
      var panel = document.createElement("div");
      panel.style.cssText = "width:min(560px,90vw);box-sizing:border-box;padding:16px;background:#D3DBB2;border:3px solid #232C15;border-radius:10px";
      var head = document.createElement("div");
      head.textContent = title;
      head.style.cssText = "font-weight:bold;margin-bottom:10px";
      var input = document.createElement("textarea");
      input.value = value;
      input.readOnly = readOnly;
      input.rows = 4;
      input.style.cssText = "width:100%;box-sizing:border-box;padding:8px;background:#DCE4BC;color:#232C15;border:2px solid #232C15;border-radius:6px;font:14px monospace;word-break:break-all;resize:none";
      var ok = document.createElement("button");
      ok.textContent = "OK";
      ok.style.cssText = "margin-top:10px;margin-right:8px;padding:8px 20px;background:#232C15;color:#DCE4BC;border:0;border-radius:6px;font:14px monospace;cursor:pointer";
      var cancel = document.createElement("button");
      cancel.textContent = "CANCEL";
      cancel.style.cssText = "margin-top:10px;padding:8px 20px;background:transparent;color:#232C15;border:2px solid #232C15;border-radius:6px;font:14px monospace;cursor:pointer";

      function close(result) {
        document.body.removeChild(shade);
        var canvas = document.getElementById("unity-canvas");
        if (canvas) canvas.focus();
        if (onDone) onDone(result);
      }
      ok.onclick = function () { close(input.value); };
      cancel.onclick = function () { close(""); };

      // Keys typed here belong to the box, not to the game.
      var stop = function (e) { e.stopPropagation(); };
      shade.addEventListener("keypress", stop);
      shade.addEventListener("keyup", stop);
      shade.addEventListener("keydown", function (e) {
        e.stopPropagation();
        if (e.key === "Escape") close("");
        else if (e.key === "Enter") { e.preventDefault(); close(input.value); }
      });

      panel.appendChild(head);
      panel.appendChild(input);
      panel.appendChild(ok);
      if (!readOnly) panel.appendChild(cancel);
      shade.appendChild(panel);
      document.body.appendChild(shade);
      input.focus();
      input.select();
    };
  },

  SRClipboardCopy__deps: ["$SRDefineBox"],
  SRClipboardCopy: function (textPtr) {
    var text = UTF8ToString(textPtr);
    SRDefineBox();

    function legacy() {
      var area = document.createElement("textarea");
      area.value = text;
      area.style.position = "fixed";
      area.style.opacity = "0";
      document.body.appendChild(area);
      area.select();
      var ok = false;
      try { ok = document.execCommand("copy"); } catch (e) { ok = false; }
      document.body.removeChild(area);
      if (!ok) window["__srShareBox"]("Copy this share code", text, true, null);
    }

    var clip = navigator["clipboard"];
    if (clip && clip["writeText"]) clip["writeText"](text)["catch"](legacy);
    else legacy();
  },

  SRClipboardAskPaste__deps: ["$SRDefineBox"],
  SRClipboardAskPaste: function () {
    SRDefineBox();
    window["__srPasteDone"] = false;
    window["__srPasteText"] = "";
    window["__srShareBox"]("Paste the share code here (Ctrl+V)", "", false, function (result) {
      window["__srPasteText"] = result;
      window["__srPasteDone"] = true;
    });
  },

  // 0 = the box is open, 1 = it closed and the result can be taken.
  SRClipboardPasteState: function () {
    return window["__srPasteDone"] ? 1 : 0;
  },

  SRClipboardTakePaste: function () {
    window["__srPasteDone"] = false;
    var text = window["__srPasteText"] || "";
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  }
};

mergeInto(LibraryManager.library, ShiftRegisterClipboard);
