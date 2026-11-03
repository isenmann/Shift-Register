using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ShiftRegister.Core;
using ShiftRegister.Game;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ShiftRegister.Editor
{
    /// <summary>
    /// Validates and solves every level in the level catalogs and writes "par" and "solution"
    /// back into each JSON file, touching only those two fields.
    /// </summary>
    public static class LevelTools
    {
        public const string LevelsFolder = "Assets/Levels";
        public const int MaxStates = 5_000_000;

        [MenuItem("Shift Register/Clear Saved Progress")]
        public static void ClearSavedProgress()
        {
            ShiftRegister.Game.Progress.ProgressStore.Clear();
            Debug.Log("Saved progress cleared: the next Play starts at the first level.");
        }

        [MenuItem("Shift Register/Validate and Solve Levels")]
        public static void ValidateAndSolveAll()
        {
            var catalogs = AssetDatabase.FindAssets("t:" + nameof(LevelCatalog))
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelCatalog>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null)
                .ToList();
            if (catalogs.Count == 0)
            {
                Debug.LogError("No LevelCatalog asset found. Create one via Assets > Create > Shift Register > Level Catalog.");
                return;
            }

            var summary = new StringBuilder();
            int ok = 0, updated = 0, failed = 0;
            var seenIds = new Dictionary<string, string>();
            var referenced = new HashSet<string>();
            var entries = catalogs.SelectMany(c => c.AllLevels().Select(e => (catalog: c, e.chapter, e.level, e.file))).ToList();

            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var (catalog, chapter, level, file) = entries[i];
                    if (file == null)
                    {
                        Debug.LogError($"{catalog.name}: chapter {chapter + 1} level {level + 1} is empty.", catalog);
                        failed++;
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(file);
                    referenced.Add(path);
                    if (EditorUtility.DisplayCancelableProgressBar("Solving levels", path, (float)i / entries.Count))
                    {
                        Debug.LogWarning("Level check cancelled.");
                        break;
                    }

                    var r = Check(file);
                    if (r.Data != null && !string.IsNullOrEmpty(r.Data.id))
                    {
                        if (seenIds.TryGetValue(r.Data.id, out var other))
                        {
                            Debug.LogError($"Level id '{r.Data.id}' is used by both {other} and {path}.", file);
                            failed++;
                        }
                        else seenIds[r.Data.id] = path;
                    }

                    if (!r.Ok)
                    {
                        Debug.LogError($"{path}: {r.Message}", file);
                        failed++;
                        continue;
                    }

                    if (r.NewJson != null)
                    {
                        File.WriteAllText(path, r.NewJson, new UTF8Encoding(false));
                        AssetDatabase.ImportAsset(path);
                        updated++;
                    }
                    ok++;
                    summary.AppendLine($"{r.Data.id,-6} {r.Data.name,-14} {r.Message}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { LevelsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".json") && !referenced.Contains(path))
                    Debug.LogWarning($"{path} is not in any level catalog.", AssetDatabase.LoadAssetAtPath<TextAsset>(path));
            }

            string head = $"Levels: {ok} ok ({updated} updated), {failed} failed.";
            if (failed > 0) Debug.LogError(head + "\n" + summary);
            else Debug.Log(head + "\n" + summary);
        }

        public sealed class CheckResult
        {
            public bool Ok;
            public LevelData Data;
            public string Message;
            /// <summary>The file's new text when par or solution changed, else null.</summary>
            public string NewJson;
        }

        public static CheckResult Check(TextAsset file)
        {
            LevelData data;
            try
            {
                data = LevelJson.Parse(file);
            }
            catch (System.Exception e)
            {
                return new CheckResult { Message = "cannot parse JSON: " + e.Message };
            }

            var errors = LevelRules.Validate(data);
            if (errors.Count > 0)
                return new CheckResult { Data = data, Message = "invalid:\n- " + string.Join("\n- ", errors) };

            var sw = Stopwatch.StartNew();
            var result = Solver.Solve(data, MaxStates);
            string stats = $"({result.StatesVisited:N0} states, {sw.ElapsedMilliseconds} ms)";
            if (result.Status != SolveStatus.Solved)
                return new CheckResult { Data = data, Message = $"{result.Status} {stats}" };

            string solution = string.Join(" ", result.Solution);
            string newJson = null;
            if (data.par != result.Par || data.solution != solution)
            {
                newJson = SetField(file.text, "par", result.Par.ToString());
                newJson = SetField(newJson, "solution", JsonString(solution));
            }
            string change = data.par != 0 && data.par != result.Par ? $" (was {data.par})" : "";
            return new CheckResult
            {
                Ok = true,
                Data = data,
                NewJson = newJson,
                Message = $"par {result.Par}{change}  {solution}  {stats}"
            };
        }

        /// <summary>Sets a top-level number or string field in JSON text, keeping the rest of the text as written.</summary>
        public static string SetField(string json, string key, string valueLiteral)
        {
            var rx = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)(\"(?:[^\"\\\\]|\\\\.)*\"|-?\\d+)");
            if (rx.IsMatch(json))
                return rx.Replace(json, m => m.Groups[1].Value + valueLiteral, 1);

            int end = json.LastIndexOf('}');
            if (end < 0) return json;
            string before = json.Substring(0, end).TrimEnd();
            string comma = before.EndsWith("{") ? "" : ",";
            return before + comma + "\n  \"" + key + "\": " + valueLiteral + "\n" + json.Substring(end);
        }

        static string JsonString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
