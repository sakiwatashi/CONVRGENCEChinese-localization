// CONVRGENCE 繁中化的執行期修正（BepInEx 6 IL2CPP 外掛）
//
// 紙本文件（機密報告等）有兩個翻譯檔解決不了的問題：
// 1. 塗黑用的黑條是文字物件底下的黑色 Cube，位置是照英文排版手擺的；
//    換成中文後會壓在不該遮的字上。→ 文字是中文時把這些 Cube 關掉，
//    塗黑改由譯文裡的 <mark=#000000> 標記做（跟著中文排版走）。
// 2. 打字機字型 gtw 的材質帶外框（_OutlineWidth 0.324），TextMeshPro 會把它
//    套到中文備援字型上，筆畫密的中文就黏成一團。→ 中文時把外框歸零。
//
// 設定 DumpRedactions=true 時，改成在英文模式下把每條黑條遮住的英文字
// 寫到 BepInEx\zhfix_redactions.txt，給譯者對照用。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;

namespace ConvrgenceZhFix
{
    [BepInPlugin(Guid, "CONVRGENCE zh-TW Text Fix", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "zh-tw.convrgence.textfix";
        public const string Version = "1.0.0";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> DumpRedactions;
        internal static ConfigEntry<float> ScanInterval;
        internal static ConfigEntry<float> CjkDilate;
        internal static ConfigEntry<bool> CheckGlyphs;
        internal static ConfigEntry<float> CjkPaperScale;
        internal static ConfigEntry<float> CjkCharSpacing;
        internal static ConfigEntry<float> CjkLineSpacing;
        internal static ConfigEntry<int> LoadScene;
        internal static ConfigEntry<string> ViewObject;
        internal static ConfigEntry<float> ViewDistance;

        public override void Load()
        {
            Logger = Log;
            DumpRedactions = Config.Bind("Debug", "DumpRedactions", false,
                "英文模式下把每條塗黑 Cube 遮住的文字寫到 BepInEx\\zhfix_redactions.txt（譯者用）");
            CheckGlyphs = Config.Bind("Debug", "CheckGlyphs", false,
                "啟動時把翻譯檔用到、但字型裡沒有的字寫到 BepInEx\\zhfix_glyphs.txt");
            LoadScene = Config.Bind("Debug", "LoadScene", -1,
                "啟動 20 秒後直接載入這個場景編號（藏身處是 4；-1 不載入）。只給平面模式測試用，正常遊玩請保持 -1");
            ViewObject = Config.Bind("Debug", "ViewObject", "",
                "把攝影機固定在這個物件名稱的文字前方（例如 Note_Raid_16），截圖檢查用；空白＝不動");
            ViewDistance = Config.Bind("Debug", "ViewDistance", 0.45f, "ViewObject 的攝影機距離（公尺）");
            CjkDilate = Config.Bind("General", "CjkDilate", 0.12f,
                "打字機字型的中文筆畫加粗量（0＝不加粗；原本英文外框是 0.324，太粗會黏在一起）");
            CjkPaperScale = Config.Bind("Paper", "MaxScale", 1.25f,
                "紙本文件（報告、字條）的中文最多放大幾倍；放得下才放大，放不下會自動縮回（1＝不放大）");
            CjkCharSpacing = Config.Bind("Paper", "CharSpacing", 6f,
                "紙本文件中文的額外字距（TextMeshPro 單位，字高的 1/100）");
            CjkLineSpacing = Config.Bind("Paper", "LineSpacing", 18f,
                "紙本文件中文的額外行距（TextMeshPro 單位，字高的 1/100）");
            ScanInterval = Config.Bind("General", "ScanInterval", 1.0f,
                "每隔幾秒檢查一次是否換了場景（換場景時才會整體掃一次文字物件）");
            ClassInjector.RegisterTypeInIl2Cpp<TextFixer>();
            AddComponent<TextFixer>();
            new HarmonyLib.Harmony(Guid).PatchAll(typeof(TextSetterPatch));
            Log.LogInfo("loaded" + (DumpRedactions.Value ? " (dump mode)" : ""));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), HarmonyLib.MethodType.Setter)]
    static class TextSetterPatch
    {
        static void Postfix(TMP_Text __instance)
        {
            try
            {
                var fixer = TextFixer.Instance;
                if (fixer != null && __instance != null) fixer.OnTextSet(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e);
            }
        }
    }

    public class TextFixer : MonoBehaviour
    {
        public TextFixer(IntPtr ptr) : base(ptr) { }

        float _next;
        // TMP instance id -> hash of the text we last handled
        readonly Dictionary<int, int> _seen = new Dictionary<int, int>();
        // TMP instance id -> material before we removed the outline
        readonly Dictionary<int, Material> _origMat = new Dictionary<int, Material>();
        readonly HashSet<string> _dumped = new HashSet<string>();
        bool _sceneRequested;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Mathf.Max(0.2f, Plugin.ScanInterval.Value);
            try
            {
                if (!_fontsPatched) PatchFonts();
                if (Plugin.DumpRedactions.Value) Dump();
                else SweepOnSceneChange();
                MaybeLoadScene();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError(e);
            }
        }

        // ---------- 標點缺字 ----------
        // The game's Chinese atlas (NotoSansSC) only holds ideographs: 。、「」 etc. render as blanks.
        // The Japanese atlas that ships with the game has them, so chain it behind the Chinese one.

        bool _fontsPatched;
        int _fontTries;
        const string Probe = "。、「」『』《》〈〉…—～・";

        void PatchFonts()
        {
            if (_fontTries++ > 60) { _fontsPatched = true; return; }
            TMP_FontAsset sc = null, jp = null, kr = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                string n = f.name;
                if (n.StartsWith("NotoSansSC")) sc = f;
                else if (n.StartsWith("NotoSansJP")) jp = f;
                else if (n.StartsWith("NotoSansKR")) kr = f;
            }
            if (sc == null) return;
            _fontsPatched = true;
            // character tables are built lazily; HasCharacter is wrong until they are
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>()) f.ReadFontAssetDefinition();

            var sb = new StringBuilder("glyph coverage SC/JP/KR: ");
            foreach (char c in Probe)
                sb.Append(c).Append(Has(sc, c) ? '1' : '0').Append(Has(jp, c) ? '1' : '0').Append(Has(kr, c) ? '1' : '0').Append(' ');
            Plugin.Logger.LogInfo(sb.ToString());

            if (sc.fallbackFontAssetTable == null)
                sc.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
            foreach (var extra in new[] { jp, kr })
                if (extra != null && !sc.fallbackFontAssetTable.Contains(extra))
                {
                    sc.fallbackFontAssetTable.Add(extra);
                    Plugin.Logger.LogInfo("fallback added: " + sc.name + " -> " + extra.name);
                }
            PatchLineBreaking();

            if (Plugin.CheckGlyphs.Value)
            {
                CheckGlyphs(sc, jp, kr);
                // which font asset could stand in for the missing punctuation
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                var rep = new StringBuilder("fallback chains:\n");
                var global = TMP_Settings.fallbackFontAssets;
                rep.Append("  TMP_Settings: ");
                if (global != null) foreach (var g in global) rep.Append(g == null ? "null" : g.name).Append(" | ");
                rep.Append('\n');
                foreach (var f in all)
                {
                    rep.Append("  ").Append(f.name).Append(" [").Append(f.atlasPopulationMode.ToString()).Append("] -> ");
                    if (f.fallbackFontAssetTable != null) foreach (var g in f.fallbackFontAssetTable) rep.Append(g == null ? "null" : g.name).Append(" | ");
                    rep.Append('\n');
                }
                rep.Append("candidates:\n");
                foreach (char c in "。、「」—…｡､｢｣．－─―‐–⋯‥・·•〜，")
                {
                    rep.Append(c).Append(string.Format(" U+{0:X4}: ", (int)c));
                    foreach (var f in all) if (f.HasCharacter(c)) rep.Append(f.name).Append(" | ");
                    rep.Append('\n');
                }
                File.AppendAllText(Path.Combine(Paths.BepInExRootPath, "zhfix_glyphs.txt"), rep.ToString(), new UTF8Encoding(false));
            }
        }

        static bool Has(TMP_FontAsset f, char c) => f != null && f.HasCharacter(c);

        // TMP's CJK line breaking keeps 。，」 off the start of a line, but it does not know the
        // halfwidth stand-ins we display instead (｡､｣), so a full stop could open a line.
        const string NoLineStart = "｡､｣，．！？：；）〕］｝〉》」』－～";
        const string NoLineEnd = "｢（〔［｛〈《「『";

        static void PatchLineBreaking()
        {
            var rules = TMP_Settings.linebreakingRules;
            if (rules == null) { Plugin.Logger.LogWarning("no TMP line breaking rules"); return; }
            int added = 0;
            foreach (char c in NoLineStart)
                if (rules.followingCharacters != null && !rules.followingCharacters.ContainsKey(c)) { rules.followingCharacters.Add(c, c); added++; }
            foreach (char c in NoLineEnd)
                if (rules.leadingCharacters != null && !rules.leadingCharacters.ContainsKey(c)) { rules.leadingCharacters.Add(c, c); added++; }
            Plugin.Logger.LogInfo("line breaking rules: added " + added + " characters");
        }

        static void CheckGlyphs(TMP_FontAsset sc, TMP_FontAsset jp, TMP_FontAsset kr)
        {
            var used = new SortedSet<char>();
            string dir = Path.Combine(Paths.BepInExRootPath, "Translation", "zh-TW", "Text");
            foreach (var file in Directory.GetFiles(dir, "*.txt"))
                foreach (var line in File.ReadAllLines(file))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0 || line.StartsWith("//")) continue;
                    foreach (char c in line) if (c >= 0x2000) used.Add(c);
                }
            var noSc = new StringBuilder();
            var none = new StringBuilder();
            foreach (char c in used)
            {
                if (Has(sc, c)) continue;
                noSc.Append(c);
                if (!Has(jp, c) && !Has(kr, c)) none.Append(c);
            }
            string report = "used chars >= U+2000: " + used.Count + "\nnot in SC: " + noSc + "\nnot in SC/JP/KR: " + none + "\n";
            File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "zhfix_glyphs.txt"), report, new UTF8Encoding(false));
            Plugin.Logger.LogInfo("glyph check: " + noSc.Length + " chars missing in SC, " + none.Length + " missing everywhere");
        }

        void MaybeLoadScene()
        {
            int scene = Plugin.LoadScene.Value;
            if (_sceneRequested || scene < 0 || Time.realtimeSinceStartup < 20f || _dumped.Count > 0) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == scene) return;
            _sceneRequested = true;
            Plugin.Logger.LogInfo("debug: loading scene " + scene);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
        }

        Transform _viewTarget;

        void LateUpdate()
        {
            string want = Plugin.ViewObject.Value;
            if (string.IsNullOrEmpty(want)) return;
            if (_viewTarget == null)
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<TextMeshPro>())
                {
                    if (t.gameObject.scene.name == null || !t.enabled) continue;
                    string p = PathOf(t.transform);
                    if (!p.Contains("/" + want + "/") || p.EndsWith("/Rus")) continue;
                    _viewTarget = t.transform;
                    // notes the save has not unlocked yet are inactive: switch them on for the screenshot
                    for (Transform q = t.transform; q != null; q = q.parent)
                        if (!q.gameObject.activeSelf) q.gameObject.SetActive(true);
                    break;
                }
                if (_viewTarget == null) return;
                _next = 0; // let Scan() pick the newly active text up right away
                Plugin.Logger.LogInfo("debug: viewing " + PathOf(_viewTarget));
            }
            // the game drives its own camera, so render the screenshot from a camera of ours on top
            if (_viewCam == null)
            {
                var go = new GameObject("zhfix debug camera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _viewCam = go.AddComponent<Camera>();
                _viewCam.depth = 100;
                _viewCam.nearClipPlane = 0.01f;
                _viewCam.fieldOfView = 60;
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 3;
                light.spotAngle = 80;
                light.intensity = 0.6f;
            }
            var main = Camera.main;
            if (main != null && !_camPlaced)
            {
                // stand where the player's camera is (it is lit there), but level and without roll
                Vector3 fwd = main.transform.forward; fwd.y = 0;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                _viewCam.transform.position = main.transform.position;
                _viewCam.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                _camPlaced = true;
            }
            // bring the note in front of our camera, facing it
            Transform note = _viewTarget;
            while (note.parent != null && !note.name.Contains(want)) note = note.parent;
            if (note.parent != null) note.SetParent(null, true); // parents are scaled non-uniformly
            Quaternion rel = Quaternion.Inverse(note.rotation) * _viewTarget.rotation;
            note.rotation = _viewCam.transform.rotation * Quaternion.Inverse(rel);
            var r = _viewTarget.GetComponent<MeshRenderer>();
            Vector3 center = r != null ? r.bounds.center : _viewTarget.position;
            note.position += _viewCam.transform.position + _viewCam.transform.forward * Plugin.ViewDistance.Value - center;
        }

        bool _camPlaced;

        Camera _viewCam;

        static bool HasCjk(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c >= '\u3400' && c <= '\u9FFF') return true;
            return false;
        }

        static List<MeshRenderer> Bars(TextMeshPro t)
        {
            var list = new List<MeshRenderer>();
            Transform tr = t.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var mr = tr.GetChild(i).GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var m = mr.sharedMaterial;
                if (m != null && m.name.StartsWith("Black")) list.Add(mr);
            }
            return list;
        }

        // ---------- 正常模式 ----------

        // Texts are handled the moment their text is set (TextSetterPatch). Walking every
        // TextMeshPro in the scene costs 5–12 ms, which is a dropped frame in VR, so that
        // only happens once after each scene change as a safety net (during loading).

        internal static TextFixer Instance;
        int _sceneKey = int.MinValue;

        void Start() { Instance = this; }

        void SweepOnSceneChange()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            int key = active.handle * 31 + UnityEngine.SceneManagement.SceneManager.sceneCount;
            if (key == _sceneKey) return;
            _sceneKey = key;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = UnityEngine.Object.FindObjectsOfType<TextMeshPro>();
            foreach (var t in all) Process(t);
            Plugin.Logger.LogInfo(string.Format("scene '{0}': swept {1} texts in {2:F1} ms",
                active.name, all.Length, sw.Elapsed.TotalMilliseconds));
        }

        internal void OnTextSet(TMP_Text text)
        {
            if (Plugin.DumpRedactions.Value) return;
            var t = text.TryCast<TextMeshPro>();
            if (t != null) Process(t);
        }

        void Process(TextMeshPro t)
        {
            string text = t.text;
            int id = t.GetInstanceID();
            int h = text == null ? 0 : text.GetHashCode();
            if (_seen.TryGetValue(id, out int old) && old == h) return;
            _seen[id] = h;

            bool cjk = HasCjk(text);
            foreach (var bar in Bars(t)) bar.enabled = !cjk;
            if (cjk) RemoveOutline(t, id);
            else RestoreOutline(t, id);
            if (cjk) ApplyPaperLayout(t, id);
            else RestorePaperLayout(t, id);
        }

        // ---------- 紙本文件放大 ----------
        // Chinese is much shorter than the English on the same page, and dense ideographs at the
        // original size blur in a headset. On notes and documents, let TMP grow the text up to
        // CjkPaperScale while it still fits the text box, and open up letter and line spacing.

        struct Layout { public float size, min, max, cs, ls; public bool auto; }
        readonly Dictionary<int, Layout> _origLayout = new Dictionary<int, Layout>();

        static bool IsPaper(TextMeshPro t)
        {
            // notes are <Note>/Text/{Eng,Rus} with the paper mesh as a sibling of Text
            var text = t.transform.parent;
            var note = text != null ? text.parent : null;
            if (note == null) return false;
            for (int i = 0; i < note.childCount; i++)
            {
                var mr = note.GetChild(i).GetComponent<MeshRenderer>();
                var m = mr != null ? mr.sharedMaterial : null;
                if (m == null) continue;
                string n = m.name;
                if (n.Contains("Note") || n.Contains("Doc") || n.Contains("Paper") || n.Contains("Card") || n.Contains("Letter"))
                    return true;
            }
            return false;
        }

        void ApplyPaperLayout(TextMeshPro t, int id)
        {
            float scale = Plugin.CjkPaperScale.Value;
            if (scale <= 1.001f && Plugin.CjkCharSpacing.Value == 0 && Plugin.CjkLineSpacing.Value == 0) return;
            if (_origLayout.ContainsKey(id) || !IsPaper(t)) return;
            var o = new Layout
            {
                size = t.fontSize, min = t.fontSizeMin, max = t.fontSizeMax,
                cs = t.characterSpacing, ls = t.lineSpacing, auto = t.enableAutoSizing,
            };
            _origLayout[id] = o;
            float baseSize = o.auto ? o.max : o.size;
            t.characterSpacing = o.cs + Plugin.CjkCharSpacing.Value;
            t.lineSpacing = o.ls + Plugin.CjkLineSpacing.Value;
            t.fontSizeMin = o.auto ? o.min : baseSize * 0.8f;
            t.fontSizeMax = baseSize * scale;
            t.enableAutoSizing = true;
            t.ForceMeshUpdate();
        }

        void RestorePaperLayout(TextMeshPro t, int id)
        {
            if (!_origLayout.TryGetValue(id, out var o)) return;
            _origLayout.Remove(id);
            t.enableAutoSizing = o.auto;
            t.fontSizeMin = o.min;
            t.fontSizeMax = o.max;
            t.fontSize = o.size;
            t.characterSpacing = o.cs;
            t.lineSpacing = o.ls;
            t.ForceMeshUpdate();
        }

        void RemoveOutline(TextMeshPro t, int id)
        {
            var mat = t.fontSharedMaterial;
            if (mat == null || !mat.HasProperty("_OutlineWidth") || mat.GetFloat("_OutlineWidth") <= 0.001f) return;
            if (!_origMat.ContainsKey(id)) _origMat[id] = mat;
            var copy = new Material(mat);
            copy.name = mat.name + " (zh-TW no outline)";
            // the typewriter look is a white face inside a thick black outline; on dense Chinese
            // strokes the outline fills the gaps, so draw the face in the outline colour instead
            if (mat.HasProperty("_OutlineColor") && mat.HasProperty("_FaceColor"))
                copy.SetColor("_FaceColor", mat.GetColor("_OutlineColor"));
            copy.SetFloat("_OutlineWidth", 0f);
            if (copy.HasProperty("_FaceDilate")) copy.SetFloat("_FaceDilate", Plugin.CjkDilate.Value);
            t.fontSharedMaterial = copy;
            // Chinese glyphs come from TMP-generated fallback materials that pick up the outline
            // width but not _FaceColor, so tint through the vertex colour, which every sub-mesh uses
            if (!_origColor.ContainsKey(id)) _origColor[id] = t.color;
            Color ink = mat.HasProperty("_OutlineColor") ? mat.GetColor("_OutlineColor") : Color.black;
            t.color = new Color(Mathf.Max(ink.r, 0.08f), Mathf.Max(ink.g, 0.08f), Mathf.Max(ink.b, 0.08f), t.color.a);
            t.ForceMeshUpdate();
        }

        readonly Dictionary<int, Color> _origColor = new Dictionary<int, Color>();

        void RestoreOutline(TextMeshPro t, int id)
        {
            if (!_origMat.TryGetValue(id, out var mat)) return;
            _origMat.Remove(id);
            t.fontSharedMaterial = mat;
            if (_origColor.TryGetValue(id, out var c)) { t.color = c; _origColor.Remove(id); }
            t.ForceMeshUpdate();
        }

        // ---------- 量測模式 ----------

        void Dump()
        {
            var all = Resources.FindObjectsOfTypeAll<TextMeshPro>();
            var sb = new StringBuilder();
            foreach (var t in all)
            {
                var bars = Bars(t);
                if (bars.Count == 0) continue;
                string text = t.text;
                if (string.IsNullOrEmpty(text) || HasCjk(text) || _dumped.Contains(text)) continue;
                if (t.gameObject.scene.name == null) continue; // prefab asset, not in a scene

                // inactive documents: switch the chain on just long enough to lay out the text
                var turnedOn = new List<GameObject>();
                for (Transform p = t.transform; p != null; p = p.parent)
                    if (!p.gameObject.activeSelf) { p.gameObject.SetActive(true); turnedOn.Add(p.gameObject); }
                try
                {
                    t.ForceMeshUpdate();
                    sb.Append(Describe(t, text, bars));
                    _dumped.Add(text);
                }
                finally
                {
                    for (int i = turnedOn.Count - 1; i >= 0; i--) turnedOn[i].SetActive(false);
                }
            }
            if (sb.Length == 0) return;
            string path = Path.Combine(Paths.BepInExRootPath, "zhfix_redactions.txt");
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
            Plugin.Logger.LogInfo("redactions written: " + path);
        }

        static string Describe(TextMeshPro t, string text, List<MeshRenderer> bars)
        {
            var info = t.textInfo;
            var chars = info.characterInfo;
            int n = info.characterCount;
            var sb = new StringBuilder();
            sb.Append("### ").Append(t.gameObject.scene.name).Append(" / ").Append(PathOf(t.transform)).Append('\n');
            sb.Append("TEXT: ").Append(text.Replace("\n", "\\n")).Append('\n');
            foreach (var bar in bars)
            {
                // bar rectangle in the text's local space
                Transform bt = bar.transform;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
                    Vector3 local = t.transform.InverseTransformPoint(bt.TransformPoint(corner));
                    x0 = Math.Min(x0, local.x); x1 = Math.Max(x1, local.x);
                    y0 = Math.Min(y0, local.y); y1 = Math.Max(y1, local.y);
                }
                var covered = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    var ci = chars[i];
                    float cx = (ci.bottomLeft.x + ci.topRight.x) * 0.5f;
                    float cy = (ci.bottomLeft.y + ci.topRight.y) * 0.5f;
                    if (cx >= x0 && cx <= x1 && cy >= y0 && cy <= y1) covered.Add(ci.index);
                }
                sb.Append(string.Format("BAR {0} rect=({1:F0},{2:F0})-({3:F0},{4:F0}) : ", bt.name, x0, y0, x1, y1));
                if (covered.Count == 0) { sb.Append("<none>\n"); continue; }
                covered.Sort();
                int a = covered[0], prev = a;
                var spans = new List<string>();
                for (int k = 1; k <= covered.Count; k++)
                {
                    if (k < covered.Count && covered[k] <= prev + 1) { prev = covered[k]; continue; }
                    spans.Add("[" + text.Substring(a, prev - a + 1).Replace("\n", "\\n") + "]");
                    if (k < covered.Count) { a = covered[k]; prev = a; }
                }
                sb.Append(string.Join(" ", spans)).Append('\n');
            }
            sb.Append('\n');
            return sb.ToString();
        }

        static string PathOf(Transform tr)
        {
            var parts = new List<string>();
            for (var p = tr; p != null; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }
    }
}
