using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class DebugCommandConsole : MonoBehaviour
{
    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Slash;
    [SerializeField] private bool requireShift = true;
    [SerializeField] private bool startHidden = true;

    [Header("Window")]
    [SerializeField] private Vector2 windowSize = new Vector2(520, 260);
    [SerializeField] private Vector2 windowMargin = new Vector2(16, 16);
    [SerializeField] private int fontSize = 16;
    [SerializeField] private int maxLogLines = 200;
    [SerializeField] private int maxSuggestions = 8;

    [Header("Commands (첫 토큰 자동완성 대상)")]
    [SerializeField]
    private List<string> commandNames = new()
    { "Test","Help","Give","Teleport","TimeScale","God","Play","summon" };

    [Header("Play 서브명령 자동완성")]
    [SerializeField] private List<string> playSubcommands = new() { "Sound", "Animation" }; // Animation은 확장용

    [Serializable] public class SummonEntry { public string name; public GameObject prefab; }
    [Header("Summon 라이브러리(자동완성/소환)")]
    [SerializeField] private List<SummonEntry> summonList = new(); // 인스펙터 등록

    // ---- 상태 ----
    private bool _visible;
    private string _input = string.Empty;
    private Vector2 _scroll;
    private Vector2 _suggestScroll;
    private readonly List<string> _log = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private string _controlName = "CmdInputField";
    private bool _focusInputNextFrame = false;
    private bool _autoscrollLog = false;
    private bool _moveCaretToEnd = false; // 자동완성 후 커서 이동

    // 자동완성
    private List<string> _suggestions = new();
    private int _suggestIndex = -1;

    // 스타일
    private GUIStyle _box, _label, _textField, _button, _smallLabel;
    private bool _stylesReady = false;

    // 라인 높이(하단 잘림 보정)
    private int _lineH => fontSize + 6;

    void Awake() { _visible = !startHidden; }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey) && (!requireShift || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            _visible = !_visible;
            if (_visible) _focusInputNextFrame = true;
        }
    }

    void OnGUI()
    {
        if (!_visible) return;

        EnsureStyles();

        var w = windowSize.x; var h = windowSize.y;
        var x = Screen.width - w - windowMargin.x; var y = windowMargin.y;

        var windowRect = new Rect(x, y, w, h);
        GUI.Box(windowRect, "", _box);

        const float pad = 8f;
        var inner = new Rect(windowRect.x + pad, windowRect.y + pad, windowRect.width - pad * 2f, windowRect.height - pad * 2f);

        float inputHeight = 28f, gap = 4f, suggestMax = Mathf.Min(140f, inner.height * 0.55f);
        var inputRect = new Rect(inner.x, inner.yMax - inputHeight - 1f, inner.width, inputHeight);
        var suggestRect = new Rect(inner.x, Mathf.Max(inner.y, inputRect.yMin - gap - suggestMax), inner.width, Mathf.Max(0f, inputRect.yMin - inner.y - gap));
        var logRect = new Rect(inner.x, inner.y, inner.width, Mathf.Max(0f, suggestRect.yMin - inner.y - gap));

        DrawLogArea(logRect);

        if (Event.current.type == EventType.MouseDown && inputRect.Contains(Event.current.mousePosition))
            GUI.FocusControl(_controlName);

        GUI.SetNextControlName(_controlName);
        var newInput = GUI.TextField(inputRect, _input, _textField);
        if (newInput != _input) { _input = newInput; RefreshSuggestions(); }

        if (_focusInputNextFrame) { _focusInputNextFrame = false; GUI.FocusControl(_controlName); _suggestIndex = -1; }

        // 자동완성 적용 후 커서를 맨뒤로 이동
        if (_moveCaretToEnd)
        {
            _moveCaretToEnd = false;
            GUI.FocusControl(_controlName);
            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            te.cursorIndex = te.selectIndex = _input.Length;
        }

        HandleKeyboard(inputRect);
        DrawSuggestions(suggestRect);
    }

    private void DrawLogArea(Rect r)
    {
        var viewH = Mathf.Max(r.height, _log.Count * _lineH + 2);
        _scroll = GUI.BeginScrollView(r, _scroll, new Rect(0, 0, r.width - 16, viewH));
        float y = 0f;
        for (int i = 0; i < _log.Count; i++)
        {
            var lr = new Rect(0, y, r.width - 16, _lineH);
            GUI.Label(lr, _log[i], _label);
            y += _lineH;
        }
        GUI.EndScrollView();

        if (_autoscrollLog) { _autoscrollLog = false; _scroll.y = float.MaxValue; }
    }

    private void DrawSuggestions(Rect r)
    {
        if (!ShouldShowSuggest()) return;

        GUI.Box(r, "", _box);
        var itemH = _lineH;
        var viewH = Mathf.Max(r.height, _suggestions.Count * itemH + 2);
        _suggestScroll = GUI.BeginScrollView(r, _suggestScroll, new Rect(0, 0, r.width - 16, viewH));

        for (int i = 0; i < _suggestions.Count; i++)
        {
            var ir = new Rect(0, i * itemH, r.width - 16, itemH);
            bool selected = (i == _suggestIndex);

            if (selected)
            {
                var col = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.Box(ir, GUIContent.none);
                GUI.color = col;
            }
            if (GUI.Button(ir, _suggestions[i], _button)) AcceptSuggestion(i);
        }

        GUI.EndScrollView();

        var hint = "Tab: 자동완성, ↑/↓: 선택, Enter: 실행, Esc: 비우기/닫기";
        var hintRect = new Rect(r.x, r.yMax - 18f, r.width, 16f);
        GUI.Label(hintRect, hint, _smallLabel);
    }

    private void HandleKeyboard(Rect inputRect)
    {
        var e = Event.current; if (e.type != EventType.KeyDown) return;
        if (GUI.GetNameOfFocusedControl() != _controlName) return;

        bool isEnter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n' || e.character == '\r';
        if (isEnter)
        {
            if (ShouldShowSuggest() && _suggestIndex >= 0 && _suggestIndex < _suggestions.Count) { AcceptSuggestion(_suggestIndex); e.Use(); return; }
            SubmitCommand(_input); e.Use(); return;
        }

        if (e.keyCode == KeyCode.Escape)
        {
            if (!string.IsNullOrEmpty(_input)) { _input = string.Empty; RefreshSuggestions(); }
            else _visible = false;
            e.Use(); return;
        }

        if (!ShouldShowSuggest())
        {
            if (e.keyCode == KeyCode.UpArrow)
            {
                if (_history.Count > 0)
                {
                    _historyIndex = (_historyIndex < 0) ? _history.Count - 1 : Mathf.Max(0, _historyIndex - 1);
                    _input = _history[_historyIndex]; RefreshSuggestions(); e.Use();
                }
                return;
            }
            if (e.keyCode == KeyCode.DownArrow)
            {
                if (_history.Count > 0)
                {
                    if (_historyIndex >= 0 && _historyIndex < _history.Count - 1) { _historyIndex++; _input = _history[_historyIndex]; }
                    else { _historyIndex = -1; _input = string.Empty; }
                    RefreshSuggestions(); e.Use();
                }
                return;
            }
        }

        if (ShouldShowSuggest())
        {
            if (e.keyCode == KeyCode.UpArrow) { _suggestIndex = (_suggestIndex <= 0) ? _suggestions.Count - 1 : _suggestIndex - 1; e.Use(); return; }
            if (e.keyCode == KeyCode.DownArrow) { _suggestIndex = (_suggestIndex + 1) % _suggestions.Count; e.Use(); return; }
            if (e.keyCode == KeyCode.Tab)
            {
                if (_suggestions.Count > 0) { if (_suggestIndex < 0) _suggestIndex = 0; AcceptSuggestion(_suggestIndex); e.Use(); return; }
            }
        }
    }

    // === 자동완성 핵심: 현재 입력의 '마지막 토큰'을 치환 ===
    private void AcceptSuggestion(int index)
    {
        var tokens = TokenizeRespectQuotes(_input.StartsWith("/") ? _input.Substring(1) : _input);
        if (tokens.Count == 0) _input = "/" + _suggestions[index];
        else { tokens[tokens.Count - 1] = _suggestions[index]; _input = "/" + string.Join(" ", tokens); }
        RefreshSuggestions();
        _moveCaretToEnd = true;
    }

    private void SubmitCommand(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        _history.Add(trimmed); if (_history.Count > 200) _history.RemoveAt(0); _historyIndex = -1;
        Log($"> {trimmed}");

        if (!trimmed.StartsWith("/")) { _input = string.Empty; RefreshSuggestions(); return; }

        string feature; List<string> args; ParseSlashCommand(trimmed, out feature, out args);
        if (!string.IsNullOrEmpty(feature)) Execute(feature, args);

        _input = string.Empty; RefreshSuggestions();
    }

    private void Execute(string feature, List<string> args)
    {
        if (string.Equals(feature, "Test", StringComparison.OrdinalIgnoreCase)) { Log("안녕하세요"); return; }

        if (string.Equals(feature, "Help", StringComparison.OrdinalIgnoreCase))
        {
            Log("도움말: /Test, /Help, /TimeScale <값>, /God [on|off|toggle|<초>s], /Teleport <x> <y>, /Give <item> [수량], /Play Sound <이름>, /summon <이름>");
            return;
        }

        if (string.Equals(feature, "TimeScale", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count >= 1 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var ts))
            { Time.timeScale = Mathf.Max(0f, ts); Log($"Time.timeScale = {Time.timeScale}"); }
            else Log("사용법: /TimeScale <값>");
            return;
        }

        // ---- /God [on|off|toggle|<초>s] ----
        if (string.Equals(feature, "God", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(feature, "GodMode", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count == 0)
            {
                GodModeService.ToggleGod();
                Log($"God {(GodModeService.Instance && GodModeService.Instance.IsActive ? "ON" : "OFF")}");
                return;
            }

            string a0 = args[0].ToLowerInvariant();
            if (a0 is "on" or "1" or "true")
            {
                float dur = ParseDurationSeconds(args.Count >= 2 ? args[1] : null);
                GodModeService.SetGod(true, dur);
                Log($"God ON{(dur > 0 ? $" ({dur:0.#}s)" : "")}");
                return;
            }
            if (a0 is "off" or "0" or "false")
            {
                GodModeService.SetGod(false);
                Log("God OFF");
                return;
            }
            if (a0 is "toggle")
            {
                GodModeService.ToggleGod();
                Log($"God {(GodModeService.Instance && GodModeService.Instance.IsActive ? "ON" : "OFF")}");
                return;
            }

            float secs = ParseDurationSeconds(a0);
            if (secs > 0f)
            {
                GodModeService.SetGod(true, secs);
                Log($"God ON ({secs:0.#}s)");
                return;
            }

            Log("사용법: /God [on|off|toggle|<초>s]  예) /God on 30s, /God 5s");
            return;
        }

        // ---- /Play Sound <name> ----
        if (string.Equals(feature, "Play", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count >= 1 && string.Equals(args[0], "Sound", StringComparison.OrdinalIgnoreCase))
            {
                string soundNameRaw = (args.Count >= 2) ? string.Join(" ", args.Skip(1)) : string.Empty;
                string soundName = UnwrapQuotes(soundNameRaw);

                if (string.IsNullOrWhiteSpace(soundName))
                { Log("사용법: /Play Sound <사운드이름>  (예: /Play Sound Hit, /Play Sound \"Door Heavy\")"); return; }

                // 라이브러리 검증
                var names = SoundManager.GetSoundNames();
                bool exists = names.Any(n => string.Equals(n, soundName, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                { Log($"사운드 '{soundName}'를 라이브러리에서 찾지 못했습니다."); return; }

                // 카메라 위치에서 1회 재생
                var cam = Camera.main;
                if (cam) SoundManager.PlayAt(soundName, cam.transform.position);
                else SoundManager.Play(soundName);

                Log($"재생: {soundName}");
                return;
            }

            Log("사용법: /Play Sound <사운드이름>");
            return;
        }

        // ---- /summon <name> ----
        if (string.Equals(feature, "summon", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count < 1) { Log("사용법: /summon <이름>"); return; }

            string target = UnwrapQuotes(string.Join(" ", args));
            var entry = summonList.FirstOrDefault(e => e != null && !string.IsNullOrEmpty(e.name) && string.Equals(e.name, target, StringComparison.OrdinalIgnoreCase));
            if (entry == null || !entry.prefab) { Log($"소환 실패: '{target}' 을(를) 찾을 수 없습니다."); return; }

            Vector3 pos = Get2DCenterOfScreen(); // 화면 중앙(2D z=0)에 소환
            var go = Instantiate(entry.prefab, pos, Quaternion.identity);
            Log($"소환 완료: {entry.name} @ {pos}");
            return;
        }

        Log($"'{feature}' 명령을 실행하려 했지만, 핸들러가 비어 있습니다.");
    }

    // ====== 파서 & 토크나이저 ======
    private void ParseSlashCommand(string raw, out string feature, out List<string> args)
    {
        feature = string.Empty; args = new List<string>();
        if (!raw.StartsWith("/")) return;
        var tokens = TokenizeRespectQuotes(raw.Substring(1));
        if (tokens.Count == 0) return;
        feature = tokens[0];
        for (int i = 1; i < tokens.Count; i++) args.Add(tokens[i]);
    }
    private static List<string> TokenizeRespectQuotes(string s)
    {
        var list = new List<string>(); var sb = new StringBuilder(); bool inQuote = false; char q = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inQuote) { if (c == q) inQuote = false; else sb.Append(c); }
            else
            {
                if (c == '"' || c == '\'') { inQuote = true; q = c; }
                else if (char.IsWhiteSpace(c)) { if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); } }
                else sb.Append(c);
            }
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
    private static string UnwrapQuotes(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\''))) ? s[1..^1] : s;
    }
    private static string WrapIfNeeded(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return s.IndexOf(' ') >= 0 || s.IndexOf('\t') >= 0 ? $"\"{s}\"" : s;
    }

    // ====== 자동완성 생성 ======
    private void RefreshSuggestions()
    {
        _suggestions.Clear(); _suggestIndex = -1;
        if (!_input.StartsWith("/")) return;

        var tokens = TokenizeRespectQuotes(_input.Substring(1));
        string current = (tokens.Count == 0) ? "" : tokens[tokens.Count - 1];

        if (tokens.Count == 0)
        {
            _suggestions = commandNames.Take(maxSuggestions).ToList();
            return;
        }

        // 1) 첫 토큰(기능명)
        if (tokens.Count == 1)
        {
            _suggestions = commandNames
                .Where(c => c.StartsWith(current, true, CultureInfo.InvariantCulture))
                .Take(maxSuggestions).ToList();
            return;
        }

        // 2) 서브/인수
        var feature = tokens[0];

        // /Play ...
        if (string.Equals(feature, "Play", StringComparison.OrdinalIgnoreCase))
        {
            // 두 번째 토큰: 서브커맨드
            if (tokens.Count == 2)
            {
                _suggestions = playSubcommands
                    .Where(s => s.StartsWith(current, true, CultureInfo.InvariantCulture))
                    .Take(maxSuggestions).ToList();
                return;
            }
            // 세 번째 토큰: 사운드 이름
            if (tokens.Count >= 3 && string.Equals(tokens[1], "Sound", StringComparison.OrdinalIgnoreCase))
            {
                var names = SoundManager.GetSoundNames();
                string key = UnwrapQuotes(current);
                IEnumerable<string> q = names;
                if (!string.IsNullOrEmpty(key))
                    q = q.Where(n => n.StartsWith(key, true, CultureInfo.InvariantCulture));
                _suggestions = q.Select(WrapIfNeeded).Take(maxSuggestions).ToList();
                return;
            }
        }

        // /summon <이름>
        if (string.Equals(feature, "summon", StringComparison.OrdinalIgnoreCase))
        {
            var names = summonList.Where(e => e != null && !string.IsNullOrEmpty(e.name)).Select(e => e.name);
            string key = UnwrapQuotes(current);
            IEnumerable<string> q = names;
            if (!string.IsNullOrEmpty(key))
                q = q.Where(n => n.StartsWith(key, true, CultureInfo.InvariantCulture));
            _suggestions = q.Select(WrapIfNeeded).Take(maxSuggestions).ToList();
            return;
        }

        // /God 인수
        if (string.Equals(feature, "God", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(feature, "GodMode", StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Count == 2)
            {
                string cur = current;
                var opts = new[] { "on", "off", "toggle", "5s", "10s", "30s", "60s" };
                _suggestions = opts.Where(o => o.StartsWith(cur, true, CultureInfo.InvariantCulture))
                                   .Take(maxSuggestions).ToList();
                return;
            }
            if (tokens.Count == 3 && tokens[1].Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                string cur = current;
                var opts = new[] { "5s", "10s", "30s", "60s" };
                _suggestions = opts.Where(o => o.StartsWith(cur, true, CultureInfo.InvariantCulture))
                                   .Take(maxSuggestions).ToList();
                return;
            }
        }
    }

    private bool ShouldShowSuggest() => _input.StartsWith("/") && _suggestions != null && _suggestions.Count > 0;

    private void Log(string msg)
    {
        if (_log.Count >= maxLogLines) _log.RemoveAt(0);
        _log.Add(msg);
        _autoscrollLog = true;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        _box = new GUIStyle(GUI.skin.box) { normal = { textColor = Color.white }, fontSize = fontSize };
        _label = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = false };
        _textField = new GUIStyle(GUI.skin.textField) { fontSize = fontSize };
        _button = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = fontSize };
        _smallLabel = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, fontSize - 4), alignment = TextAnchor.LowerRight };
        _stylesReady = true;
    }

    // ====== 유틸 ======
    private static Vector3 Get2DCenterOfScreen()
    {
        var cam = Camera.main;
        if (!cam) return Vector3.zero;
        var vp = new Vector3(0.5f, 0.5f, Mathf.Abs(cam.transform.position.z));
        var w = cam.ViewportToWorldPoint(vp);
        w.z = 0f;
        return w;
    }

    private static float ParseDurationSeconds(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return -1f;
        token = token.Trim().ToLowerInvariant();
        if (token.EndsWith("s") && float.TryParse(token[..^1], out var sec)) return Mathf.Max(0f, sec);
        if (float.TryParse(token, out var sec2)) return Mathf.Max(0f, sec2);
        return -1f;
    }
}
