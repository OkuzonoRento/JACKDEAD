#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

/// <summary>
/// デバッグ表示。敵の視覚・聴覚と、ゲームの状態を、画面（Game ビュー）と Scene ビューに重ねて表示する。
/// ・ファイル全体がエディタ専用（#if UNITY_EDITOR）なので、ビルドしたゲームには含まれない
/// ・F3：表示の ON / OFF　F4：視覚　F5：音　F6：状態の文字　F7：立体の視界コーン（ON/OFF は次の再生でも覚えている）
/// ・敵の判断は、ソロとホストの画面でだけ分かる（参加者の敵は見た目だけ）
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    private const string PrefOn = "JackDead.Debug.On";
    private const string PrefVision = "JackDead.Debug.Vision";
    private const string PrefSound = "JackDead.Debug.Sound";
    private const string PrefInfo = "JackDead.Debug.Info";

    private const float SoundShowSeconds = 2f;
    private const int ArcSegments = 24;
    private const int SoundLogCount = 6;

    private struct Line { public Vector3 a, b; public Color color; }
    private struct Fill { public Vector3 a, b, c; public Color color; }
    private const string PrefCone = "JackDead.Debug.Cone";
    private bool showCone;
    private readonly List<Fill> fills = new List<Fill>();
    private readonly Vector3[] clipped = new Vector3[4];
    private struct Label { public Vector3 pos; public string text; public Color color; }
    private struct Sound { public Vector3 pos; public float loudness; public float time; public int listeners; }

    private bool on, showVision, showSound, showInfo;

    private readonly List<Line> lines = new List<Line>();
    private readonly List<Label> labels = new List<Label>();
    private readonly List<Sound> sounds = new List<Sound>();
    private readonly List<string> soundLog = new List<string>();
    private readonly StringBuilder text = new StringBuilder();

    private Material lineMaterial;
    private PlayerController player;
    private float fps;
    private float nextCountTime;
    private int itemCount;
    private GUIStyle labelStyle, panelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<DebugOverlay>() != null) return;
        GameObject go = new GameObject("DebugOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<DebugOverlay>();
    }

    private void Awake()
    {
        on = EditorPrefs.GetBool(PrefOn, false);
        showVision = EditorPrefs.GetBool(PrefVision, true);
        showSound = EditorPrefs.GetBool(PrefSound, true);
        showInfo = EditorPrefs.GetBool(PrefInfo, true);
        showCone = EditorPrefs.GetBool(PrefCone, false);
    }

    private void OnEnable()
    {
        EnemyNoise.Heard += OnSound;
    }

    private void OnDisable()
    {
        EnemyNoise.Heard -= OnSound;
    }

    private void OnDestroy()
    {
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    private void OnSound(Vector3 position, float loudness)
    {
        int listeners = 0;
        foreach (EnemyBrain enemy in EnemyBrain.DebugAll)
        {
            if (enemy != null && !enemy.DebugIsPuppet && Vector3.Distance(enemy.transform.position, position) <= loudness) listeners++;
        }

        // スロットのように同じ場所で鳴り続ける音は、円と履歴を増やさずに、最新のものへ置き換える
        bool repeated = sounds.RemoveAll(s => Mathf.Approximately(s.loudness, loudness) && (s.pos - position).sqrMagnitude < 0.04f) > 0;

        sounds.Add(new Sound { pos = position, loudness = loudness, time = Time.time, listeners = listeners });
        string name = SoundName(loudness);
        string line = $"{Time.time:0.0}s  {name} {loudness:0.#}m  届いた敵 {listeners}";
        if (repeated && soundLog.Count > 0 && soundLog[0].Contains($"  {name} ")) soundLog[0] = line;
        else soundLog.Insert(0, line);
        if (soundLog.Count > SoundLogCount) soundLog.RemoveAt(soundLog.Count - 1);
    }

    private static string SoundName(float loudness)
    {
        if (Mathf.Approximately(loudness, EnemyNoise.Sprint)) return "走る足音";
        if (Mathf.Approximately(loudness, EnemyNoise.Jump)) return "ジャンプ/Boxがぶつかる"; // 大きさが同じ（9m）
        if (Mathf.Approximately(loudness, EnemyNoise.ItemImpact)) return "物がぶつかる";
        if (Mathf.Approximately(loudness, EnemyNoise.Explosion)) return "爆発/スロット";
        if (loudness >= EnemyNoise.VoiceMin && loudness <= EnemyNoise.LandMax) return "声/着地";
        return "音";
    }

    private void Update()
    {
        HandleKeys();
        fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
        sounds.RemoveAll(s => Time.time - s.time > SoundShowSeconds);
    }

    private void HandleKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.f3Key.wasPressedThisFrame) Toggle(ref on, PrefOn);
        if (!on) return;
        if (kb.f4Key.wasPressedThisFrame) Toggle(ref showVision, PrefVision);
        if (kb.f5Key.wasPressedThisFrame) Toggle(ref showSound, PrefSound);
        if (kb.f6Key.wasPressedThisFrame) Toggle(ref showInfo, PrefInfo);
        if (kb.f7Key.wasPressedThisFrame) Toggle(ref showCone, PrefCone);
    }

    private static void Toggle(ref bool value, string pref)
    {
        value = !value;
        EditorPrefs.SetBool(pref, value);
    }

    // ------------------------------------------------------------------
    // 表示する線と文字を集める（敵の判断のあとで）
    // ------------------------------------------------------------------

    private void LateUpdate()
    {
        lines.Clear();
        fills.Clear();
        labels.Clear();
        if (!on) return;

        if (player == null) player = FindAnyObjectByType<PlayerController>();

        if (showVision) CollectVision();
        if (showSound) CollectSounds();

        // Scene ビューにも同じ線を出す
        foreach (Line l in lines) Debug.DrawLine(l.a, l.b, l.color);
    }

    private void CollectVision()
    {
        foreach (EnemyBrain enemy in EnemyBrain.DebugAll)
        {
            if (enemy == null) continue;
            Vector3 feet = enemy.transform.position + Vector3.up * 0.1f;
            Vector3 forward = enemy.transform.forward;

            if (!enemy.DebugIsPuppet)
            {
                float half = enemy.DebugSightHalfAngle;
                Color standColor = new Color(1f, 0.9f, 0.2f);
                Color crouchColor = new Color(0.2f, 0.9f, 1f);

                // 地面の扇形（薄い塗り）：黄＝立っている相手が見える距離 / 水色＝しゃがみ / 橙・赤＝すぐ気づく（立ち・しゃがみ）
                float stand = enemy.DebugSightRange;
                float crouch = enemy.DebugCrouchSightRange;
                AddSector(feet, forward, 0f, crouch, half, WithAlpha(crouchColor, 0.14f));
                AddSector(feet, forward, crouch, stand, half, WithAlpha(standColor, 0.08f));
                AddSector(feet, forward, 0f, enemy.DebugInstantRange, half, new Color(1f, 0.5f, 0f, 0.22f));
                AddSector(feet, forward, 0f, enemy.DebugInstantRangeCrouching, half, new Color(1f, 0.1f, 0.1f, 0.3f));

                // 扇形の縁と距離
                AddArc(feet, forward, stand, half, standColor);
                AddArc(feet, forward, crouch, half, crouchColor);
                AddLine(feet, feet + Quaternion.Euler(0f, -half, 0f) * forward * stand, standColor);
                AddLine(feet, feet + Quaternion.Euler(0f, half, 0f) * forward * stand, standColor);
                labels.Add(new Label { pos = feet + forward * stand, text = $"立ち {stand:0}m", color = standColor });
                labels.Add(new Label { pos = feet + forward * crouch, text = $"しゃがみ {crouch:0}m", color = crouchColor });

                // 立体のコーン（上下の広さ）は、必要なときだけ F7 で表示
                if (showCone)
                {
                    AddViewCone(enemy.DebugEye, enemy.DebugViewRotation, crouch, half, enemy.DebugSightVerticalHalfAngle, new Color(1f, 1f, 1f, 0.55f));
                }

                // 視線（緑：見えている / 赤：遮られている）。視界の外の相手への線は出さない
                foreach (EnemyBrain.DebugRay ray in enemy.DebugRays)
                {
                    if (!ray.inView) continue;
                    AddLine(ray.from, ray.to, ray.clear ? Color.green : new Color(1f, 0.2f, 0.2f, 0.45f));
                }

                // 向かっている経路と、調べに行く場所
                NavMeshPath path = enemy.DebugPath;
                if (path != null)
                {
                    Vector3[] corners = path.corners;
                    for (int i = 1; i < corners.Length; i++) AddLine(corners[i - 1] + Vector3.up * 0.15f, corners[i] + Vector3.up * 0.15f, new Color(0.35f, 0.5f, 1f));
                }
                if (enemy.DebugInvestigating)
                {
                    Vector3 p = enemy.DebugLastKnownPos + Vector3.up * 0.1f;
                    AddLine(p + new Vector3(-0.5f, 0f, -0.5f), p + new Vector3(0.5f, 0f, 0.5f), Color.magenta);
                    AddLine(p + new Vector3(-0.5f, 0f, 0.5f), p + new Vector3(0.5f, 0f, -0.5f), Color.magenta);
                    AddLine(p, p + Vector3.up * 1.5f, Color.magenta);
                    labels.Add(new Label { pos = p + Vector3.up * 1.7f, text = "調べる場所", color = Color.magenta });
                }
            }

            // 頭の上：状態と、プレイヤーごとの気づきかけ
            text.Length = 0;
            text.Append("敵#").Append(enemy.NetId).Append("  ").Append(enemy.DebugState);
            foreach (KeyValuePair<ulong, float> pair in enemy.DebugAwareness)
            {
                if (pair.Value <= 0.001f) continue;
                string who = pair.Key == EnemyBrain.DebugLocalKey ? "自分" : "P" + pair.Key;
                text.Append('\n').Append(who).Append(' ').Append(Bar(pair.Value)).Append(' ').Append(Mathf.RoundToInt(pair.Value * 100f)).Append('%');
                if (enemy.DebugHidden.Contains(pair.Key)) text.Append("（潜り中）");
            }
            foreach (ulong id in enemy.DebugHidden)
            {
                if (enemy.DebugAwareness.TryGetValue(id, out float v) && v > 0.001f) continue;
                text.Append('\n').Append(id == EnemyBrain.DebugLocalKey ? "自分" : "P" + id).Append("（潜り中）");
            }
            labels.Add(new Label { pos = enemy.transform.position + Vector3.up * 2.4f, text = text.ToString(), color = Color.white });
        }
    }

    private static string Bar(float value)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(value * 10f), 0, 10);
        return new string('■', filled) + new string('□', 10 - filled);
    }

    private void CollectSounds()
    {
        foreach (Sound s in sounds)
        {
            float age = (Time.time - s.time) / SoundShowSeconds;
            Color c = s.listeners > 0 ? new Color(1f, 0.4f, 0.9f, 1f - age) : new Color(1f, 1f, 1f, 0.7f * (1f - age));
            Vector3 center = s.pos + Vector3.up * 0.2f;

            AddCircle(center, s.loudness, c);
            AddLine(center, center + Vector3.up * 1.2f, c);
            labels.Add(new Label { pos = center + Vector3.up * 1.4f, text = $"{SoundName(s.loudness)} {s.loudness:0.#}m", color = c });

            foreach (EnemyBrain enemy in EnemyBrain.DebugAll)
            {
                if (enemy == null || enemy.DebugIsPuppet) continue;
                if (Vector3.Distance(enemy.transform.position, s.pos) <= s.loudness) AddLine(center, enemy.DebugEye, c);
            }
        }
    }

    /// <summary>立体の視界：先の楕円の縁と、目から上下左右へ向かう4本の線だけ（線を増やすと見づらいので最小限）</summary>
    private void AddViewCone(Vector3 eye, Quaternion view, float distance, float halfH, float halfV, Color color)
    {
        const int Segments = 32;
        Vector3 previous = ConePoint(eye, view, distance, halfH, 0f);
        for (int i = 1; i <= Segments; i++)
        {
            float phi = 2f * Mathf.PI * i / Segments;
            Vector3 next = ConePoint(eye, view, distance, Mathf.Cos(phi) * halfH, Mathf.Sin(phi) * halfV);
            AddLine(previous, next, color);
            if (i % (Segments / 4) == 0) AddLine(eye, next, color);
            previous = next;
        }
    }

    /// <summary>地面の扇形（内側の半径〜外側の半径）を、塗りつぶしとして足す</summary>
    private void AddSector(Vector3 center, Vector3 forward, float inner, float outer, float halfAngle, Color color)
    {
        if (outer <= inner) return;
        Vector3 previous = Quaternion.Euler(0f, -halfAngle, 0f) * forward;
        for (int i = 1; i <= ArcSegments; i++)
        {
            Vector3 next = Quaternion.Euler(0f, Mathf.Lerp(-halfAngle, halfAngle, (float)i / ArcSegments), 0f) * forward;
            fills.Add(new Fill { a = center + previous * inner, b = center + previous * outer, c = center + next * outer, color = color });
            if (inner > 0f) fills.Add(new Fill { a = center + previous * inner, b = center + next * outer, c = center + next * inner, color = color });
            previous = next;
        }
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    // 視線の向きから、左右 yaw 度・上下 pitch 度（上が正）の方向へ distance 進んだ点
    private static Vector3 ConePoint(Vector3 eye, Quaternion view, float distance, float yaw, float pitch)
    {
        float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
        Vector3 local = new Vector3(Mathf.Cos(p) * Mathf.Sin(y), Mathf.Sin(p), Mathf.Cos(p) * Mathf.Cos(y));
        return eye + view * local * distance;
    }

    private void AddLine(Vector3 a, Vector3 b, Color color)
    {
        lines.Add(new Line { a = a, b = b, color = color });
    }

    private void AddArc(Vector3 center, Vector3 forward, float radius, float halfAngle, Color color)
    {
        Vector3 previous = center + Quaternion.Euler(0f, -halfAngle, 0f) * forward * radius;
        for (int i = 1; i <= ArcSegments; i++)
        {
            float angle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / ArcSegments);
            Vector3 next = center + Quaternion.Euler(0f, angle, 0f) * forward * radius;
            AddLine(previous, next, color);
            previous = next;
        }
    }

    private void AddCircle(Vector3 center, float radius, Color color)
    {
        AddArc(center, Vector3.forward, radius, 180f, color);
    }

    // ------------------------------------------------------------------
    // 画面への描画
    // ------------------------------------------------------------------

    private void OnGUI()
    {
        if (!on) return;
        EnsureStyles();

        Camera cam = Camera.main;
        if (cam != null)
        {
            if (Event.current.type == EventType.Repaint) DrawLines(cam);
            DrawLabels(cam);
        }
        if (showInfo) DrawPanel();
        else GUI.Label(new Rect(10f, 10f, 400f, 22f), "DEBUG  F3:閉じる F6:状態", labelStyle);
    }

    private void EnsureStyles()
    {
        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // 壁越しでも見える
        }
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = false };
            panelStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
            panelStyle.normal.textColor = Color.white;
        }
    }

    private void DrawLines(Camera cam)
    {
        lineMaterial.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f); // 左上が原点
        DrawFills(cam);
        GL.Begin(GL.LINES);

        float near = cam.nearClipPlane + 0.01f;
        Transform t = cam.transform;
        foreach (Line l in lines)
        {
            // カメラの後ろに回る部分は切り取る
            float da = Vector3.Dot(l.a - t.position, t.forward);
            float db = Vector3.Dot(l.b - t.position, t.forward);
            if (da < near && db < near) continue;

            Vector3 a = l.a, b = l.b;
            if (da < near) a = Vector3.Lerp(l.a, l.b, (near - da) / (db - da));
            if (db < near) b = Vector3.Lerp(l.b, l.a, (near - db) / (da - db));

            Vector3 sa = cam.WorldToScreenPoint(a);
            Vector3 sb = cam.WorldToScreenPoint(b);
            GL.Color(l.color);
            GL.Vertex3(sa.x, Screen.height - sa.y, 0f);
            GL.Vertex3(sb.x, Screen.height - sb.y, 0f);
        }

        GL.End();
        GL.PopMatrix();
    }

    // 塗りつぶし（カメラの後ろに回る部分は切り取る）
    private void DrawFills(Camera cam)
    {
        GL.Begin(GL.TRIANGLES);
        float near = cam.nearClipPlane + 0.01f;
        Transform t = cam.transform;
        foreach (Fill fill in fills)
        {
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                Vector3 from = i == 0 ? fill.a : (i == 1 ? fill.b : fill.c);
                Vector3 to = i == 0 ? fill.b : (i == 1 ? fill.c : fill.a);
                float depthFrom = Vector3.Dot(from - t.position, t.forward);
                float depthTo = Vector3.Dot(to - t.position, t.forward);
                if (depthFrom >= near && count < clipped.Length) clipped[count++] = from;
                if ((depthFrom >= near) != (depthTo >= near) && count < clipped.Length)
                    clipped[count++] = Vector3.Lerp(from, to, (near - depthFrom) / (depthTo - depthFrom));
            }
            for (int i = 2; i < count; i++)
            {
                GL.Color(fill.color);
                AddVertex(cam, clipped[0]);
                AddVertex(cam, clipped[i - 1]);
                AddVertex(cam, clipped[i]);
            }
        }
        GL.End();
    }

    private static void AddVertex(Camera cam, Vector3 world)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        GL.Vertex3(s.x, Screen.height - s.y, 0f);
    }

    private void DrawLabels(Camera cam)
    {
        foreach (Label label in labels)
        {
            Vector3 s = cam.WorldToScreenPoint(label.pos);
            if (s.z <= cam.nearClipPlane) continue;

            GUIContent content = new GUIContent(label.text);
            Vector2 size = labelStyle.CalcSize(content);
            Rect rect = new Rect(s.x - size.x * 0.5f, Screen.height - s.y - size.y, size.x, size.y);

            labelStyle.normal.textColor = Color.black; // 影
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), content, labelStyle);
            labelStyle.normal.textColor = label.color;
            GUI.Label(rect, content, labelStyle);
        }
        labelStyle.normal.textColor = Color.white;
    }

    private void DrawPanel()
    {
        if (Time.unscaledTime >= nextCountTime)
        {
            nextCountTime = Time.unscaledTime + 0.5f;
            itemCount = FindObjectsByType<Item>(FindObjectsSortMode.None).Length;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("DEBUG  F3:閉じる  F4:視覚[").Append(OnOff(showVision)).Append("]  F5:音[").Append(OnOff(showSound))
          .Append("]  F6:状態  F7:立体[").Append(OnOff(showCone)).Append("]\n");
        sb.Append($"FPS {fps:0}\n");

        NetworkSession net = NetworkSession.Instance;
        if (net == null || !net.IsOnline) sb.Append("通信: ソロ\n");
        else sb.Append($"通信: {(net.IsHost ? "ホスト" : "参加者")}  ping {net.LocalPingMs}ms  部屋 {net.Members.Count}人\n");
        sb.Append($"ボイス: {(ProximityVoice.IsActive ? "参加中" : "なし")}{(ProximityVoice.MicMuted ? "（ミュート）" : "")}\n");

        if (player != null)
        {
            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            CharacterController controller = player.GetComponent<CharacterController>();
            Vector3 p = player.transform.position;
            Vector3 v = controller != null ? controller.velocity : Vector3.zero;
            v.y = 0f;
            sb.Append($"位置 ({p.x:0.0}, {p.y:0.0}, {p.z:0.0})  速さ {v.magnitude:0.0}\n");
            sb.Append($"体力 {player.Health:0}  正気度 {player.Sanity:0}  ドーパミン {player.Dopamine:0}");
            if (movement != null)
            {
                sb.Append($"  スタミナ {movement.CurrentStamina:0}\n");
                sb.Append($"しゃがみ {OnOff(movement.IsCrouching)}  スタン {OnOff(movement.IsStunned)}");
            }
            sb.Append('\n');
        }

        int enemies = 0;
        bool puppets = false;
        foreach (EnemyBrain enemy in EnemyBrain.DebugAll)
        {
            if (enemy == null) continue;
            enemies++;
            puppets |= enemy.DebugIsPuppet;
        }
        sb.Append($"敵 {enemies}体  アイテム {itemCount}個");
        if (puppets) sb.Append("\n※参加者の画面では、敵の判断は分かりません（ホストで確認）");

        if (showSound && soundLog.Count > 0)
        {
            sb.Append("\n\n― 最近の音 ―");
            foreach (string entry in soundLog) sb.Append('\n').Append(entry);
        }

        GUIContent content = new GUIContent(sb.ToString());
        const float width = 460f;
        float height = panelStyle.CalcHeight(content, width);
        GUI.Box(new Rect(10f, 10f, width, height), content, panelStyle);
    }

    private static string OnOff(bool value)
    {
        return value ? "ON" : "OFF";
    }
}
#endif
