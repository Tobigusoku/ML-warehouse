using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// フェロモンシステム デバッグHUD
///
/// ■ 概要:
///   WarehousePheromone の「入口×棚×出口」フェロモンマップを
///   リアルタイムで可視化・検査する。
///
/// ■ キー操作:
///   [P]         HUD 表示/非表示
///   [Tab]       フェーズ切り替え (Delivering ↔ Returning)
///   [↑][↓]      ランキング内のマップ選択を上下移動
///   [L]         現在の統計を Debug.Log に全出力
///   [C]         全フェロモンをリセット (確認あり)
///
/// ■ 表示内容:
///   ・Global Stats   : 総合フェロモン量・非ゼロセル数・マップ数
///   ・Top Maps       : フェロモン合計が多い順上位 N 本のマップ一覧
///                      (バーグラフ + 入口/棚/出口ラベル)
///   ・Agent Values   : 各エージェントの現在セルのフェロモン値
///                      (アクティブなマップ上の値)
///   ・Selected Map   : 選択中マップの詳細 (合計/最大/非ゼロセル数)
///
/// ■ セットアップ:
///   WarehouseTrainingManager と同じ GameObject にアタッチする。
///   WarehousePheromone・WarehouseTrainingManager は自動検出される。
/// </summary>
public class WarehousePheromoneDebugger : MonoBehaviour
{
    // ==========================================
    //  Inspector 設定
    // ==========================================
    [Header("===== 表示 =====")]
    public bool showHUD = true;

    [Tooltip("HUD の右端からのオフセット")]
    public float hudRight  = 10f;
    public float hudTop    = 10f;
    public float hudWidth  = 380f;

    [Tooltip("上位何本のマップを表示するか")]
    public int topK = 8;

    [Tooltip("統計を更新する間隔 (フレーム数)")]
    public int updateInterval = 30;

    [Tooltip("フォントサイズ")]
    public int fontSize = 13;

    [Tooltip("背景の透明度")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.82f;

    // ==========================================
    //  内部参照
    // ==========================================
    private WarehousePheromone        phero;
    private WarehouseTrainingManager  manager;

    // ==========================================
    //  統計キャッシュ
    // ==========================================
    private struct MapEntry
    {
        public bool  isDelivering;
        public int   eIdx;      // 入口インデックス (Delivering) または -1 (Returning)
        public int   sIdx;      // 棚インデックス
        public int   xIdx;      // 出口インデックス (Returning) または -1 (Delivering)
        public float total;
        public float max;
        public int   activeCells;
        public string label;    // 表示用 "E0→S3" / "S3→X1" など
    }

    private List<MapEntry>  topMaps      = new List<MapEntry>();
    private float           globalTotal  = 0f;
    private int             globalActive = 0;
    private int             totalMapCount = 0;
    private int             frameCounter = 0;

    // UI 状態
    private bool  viewDelivering = true;   // true=Delivering / false=Returning
    private int   selectedIndex  = 0;      // topMaps 内のカーソル位置
    private bool  resetConfirm   = false;  // C キー2回押し確認

    // ==========================================
    //  GUIスタイルキャッシュ
    // ==========================================
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle boxStyle;
    private GUIStyle sectionStyle;
    private GUIStyle footerStyle;
    private GUIStyle selectedStyle;  // 選択行のハイライト
    private GUIStyle barLabelStyle;
    private GUIStyle barValueStyle;
    private Texture2D bgTex;
    private Texture2D barBgTex;
    private Texture2D whiteTex;
    private Texture2D selectedBgTex;
    private bool stylesInitialized = false;

    private float lineH;
    private float barH;

    // ==========================================
    //  初期化
    // ==========================================
    void Start()
    {
        phero   = GetComponent<WarehousePheromone>();
        manager = GetComponent<WarehouseTrainingManager>();

        if (phero == null)   phero   = FindObjectOfType<WarehousePheromone>();
        if (manager == null) manager = FindObjectOfType<WarehouseTrainingManager>();

        if (phero == null)
        {
            Debug.LogWarning("[PheromoneDebugger] WarehousePheromone が見つかりません。");
            enabled = false;
        }
    }

    // ==========================================
    //  毎フレーム: 統計更新 & キー入力
    // ==========================================
    void Update()
    {
        HandleInput();

        frameCounter++;
        if (frameCounter % Mathf.Max(1, updateInterval) == 0)
            RefreshStats();
    }

    void HandleInput()
    {
        // [P] HUD 切り替え
        if (Input.GetKeyDown(KeyCode.P))
        {
            showHUD = !showHUD;
            resetConfirm = false;
        }

        if (!showHUD) return;

        // [Tab] フェーズ切り替え
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            viewDelivering = !viewDelivering;
            selectedIndex  = 0;
            ApplyVizTarget();
        }

        // [↑][↓] マップ選択移動
        if (topMaps.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                selectedIndex = (selectedIndex - 1 + topMaps.Count) % topMaps.Count;
                ApplyVizTarget();
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                selectedIndex = (selectedIndex + 1) % topMaps.Count;
                ApplyVizTarget();
            }
        }

        // [L] ログ出力
        if (Input.GetKeyDown(KeyCode.L))
            DumpFullLog();

        // [C] 全リセット (2回押しで確定)
        if (Input.GetKeyDown(KeyCode.C))
        {
            if (resetConfirm)
            {
                phero.ResetAll();
                RefreshStats();
                resetConfirm = false;
                Debug.Log("[PheromoneDebugger] 全フェロモンをリセットしました。");
            }
            else
            {
                resetConfirm = true;
                Debug.Log("[PheromoneDebugger] [C] をもう一度押すと全フェロモンをリセットします。");
            }
        }
        else if (Input.anyKeyDown)
        {
            resetConfirm = false;
        }
    }

    // ==========================================
    //  統計収集
    // ==========================================
    void RefreshStats()
    {
        if (phero == null || !phero.IsInitialized) return;

        int eCount = phero.EntranceCount;
        int sCount = phero.ShelfCount;
        int xCount = phero.ExitCount;

        // スキャン対象: 現在のフェーズに対応するマップのみ
        // （全マップをスキャンするとHugeプリセットで負荷大になるため）
        topMaps.Clear();
        globalTotal  = 0f;
        globalActive = 0;
        totalMapCount = 0;

        if (viewDelivering)
        {
            // Delivering: [eIdx × sCount + sIdx]
            totalMapCount = eCount * sCount;

            for (int e = 0; e < eCount; e++)
            {
                for (int s = 0; s < sCount; s++)
                {
                    var (total, max, active) = phero.GetDeliveringMapStats(e, s);
                    if (total <= 0f) continue;

                    globalTotal  += total;
                    globalActive += active;

                    ShelfUnit shelf = phero.GetShelfByIndex(s);
                    string shelfLabel = shelf != null ? shelf.shelfID : $"S{s}";

                    topMaps.Add(new MapEntry
                    {
                        isDelivering = true,
                        eIdx = e, sIdx = s, xIdx = -1,
                        total = total, max = max, activeCells = active,
                        label = $"E{e} → {shelfLabel}"
                    });
                }
            }
        }
        else
        {
            // Returning: [sIdx × xCount + xIdx]
            totalMapCount = sCount * xCount;

            for (int s = 0; s < sCount; s++)
            {
                for (int x = 0; x < xCount; x++)
                {
                    var (total, max, active) = phero.GetReturningMapStats(s, x);
                    if (total <= 0f) continue;

                    globalTotal  += total;
                    globalActive += active;

                    ShelfUnit shelf = phero.GetShelfByIndex(s);
                    string shelfLabel = shelf != null ? shelf.shelfID : $"S{s}";

                    topMaps.Add(new MapEntry
                    {
                        isDelivering = false,
                        eIdx = -1, sIdx = s, xIdx = x,
                        total = total, max = max, activeCells = active,
                        label = $"{shelfLabel} → X{x}"
                    });
                }
            }
        }

        // 合計フェロモン降順でソート (InsertionSort — リストは小さいので十分)
        for (int i = 1; i < topMaps.Count; i++)
        {
            MapEntry key = topMaps[i];
            int j = i - 1;
            while (j >= 0 && topMaps[j].total < key.total)
            {
                topMaps[j + 1] = topMaps[j];
                j--;
            }
            topMaps[j + 1] = key;
        }

        // 上位 topK に切り詰め
        if (topMaps.Count > topK)
            topMaps.RemoveRange(topK, topMaps.Count - topK);

        // カーソルが範囲外になった場合は補正
        if (topMaps.Count > 0)
            selectedIndex = Mathf.Clamp(selectedIndex, 0, topMaps.Count - 1);
        else
            selectedIndex = 0;
    }

    /// <summary>
    /// 選択中マップを WarehousePheromone の可視化ターゲットに反映する
    /// </summary>
    void ApplyVizTarget()
    {
        if (phero == null || topMaps.Count == 0) return;

        var entry = topMaps[selectedIndex];
        ShelfUnit shelf = entry.sIdx >= 0 ? phero.GetShelfByIndex(entry.sIdx) : null;

        int eOrX = entry.isDelivering ? entry.eIdx : entry.xIdx;
        phero.SetVizTarget(entry.isDelivering, eOrX, shelf);
    }

    // ==========================================
    //  ログ全出力
    // ==========================================
    void DumpFullLog()
    {
        if (phero == null || !phero.IsInitialized)
        {
            Debug.Log("[PheromoneDebugger] 未初期化");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("[PheromoneDebugger] FULL DUMP");
        sb.AppendLine($"  Entrance:{phero.EntranceCount} Shelf:{phero.ShelfCount} Exit:{phero.ExitCount}");
        sb.AppendLine($"  Global Total={globalTotal:F2}  Active Cells={globalActive}");
        sb.AppendLine($"  View Phase: {(viewDelivering ? "Delivering" : "Returning")}");
        sb.AppendLine("--- Top Maps ---");

        for (int i = 0; i < topMaps.Count; i++)
        {
            var m = topMaps[i];
            sb.AppendLine($"  [{i}] {m.label}  total={m.total:F2}  max={m.max:F2}  active={m.activeCells}");
        }

        // 全エージェントの現在値
        if (manager != null && manager.robotAgents.Count > 0)
        {
            sb.AppendLine("--- Agent Current Cell Values ---");
            foreach (var agent in manager.robotAgents)
            {
                if (agent == null) continue;
                bool isDel = agent.CurrentPhase == WarehouseRobotAgent.Phase.Delivering;
                var shelf  = agent.targetShelfTransform != null
                    ? agent.targetShelfTransform.GetComponent<ShelfUnit>() : null;
                int sIdx   = shelf != null ? phero.GetShelfIndex(shelf) : -1;
                float val  = phero.GetValue(
                    agent.transform.position, isDel,
                    agent.spawnEntranceIndex, sIdx, agent.targetExitIndex);
                sb.AppendLine($"  {agent.name}  phase={agent.CurrentPhase}  " +
                              $"E{agent.spawnEntranceIndex} S{sIdx} X{agent.targetExitIndex}  " +
                              $"cell={val:F4}");
            }
        }

        sb.AppendLine("========================================");
        Debug.Log(sb.ToString());
    }

    // ==========================================
    //  GUI 描画
    // ==========================================
    void OnGUI()
    {
        if (!showHUD) return;

        InitStyles();

        float sw = Screen.width;
        float x  = sw - hudRight - hudWidth;
        float y  = hudTop;
        float w  = hudWidth;

        // --- 高さ計算 ---
        int agentCount = (manager != null) ? CountValidAgents() : 0;
        int mapRows    = Mathf.Min(topMaps.Count, topK);

        float totalH = 4f + lineH           // ヘッダー
                     + lineH               // フェーズタブ
                     + lineH * 0.5f        // 区切り
                     + lineH              // Global Stats ヘッダー
                     + lineH * 3          // Global 3行
                     + lineH * 0.5f        // 区切り
                     + lineH              // Top Maps ヘッダー
                     + barH * mapRows     // マップバー
                     + lineH * 0.5f;       // 区切り

        if (topMaps.Count > 0)
            totalH += lineH * 3;           // Selected Map 詳細

        if (agentCount > 0)
        {
            totalH += lineH * 0.5f;        // 区切り
            totalH += lineH;               // Agents ヘッダー
            totalH += lineH * agentCount;  // エージェント行
        }

        totalH += lineH;                   // フッター

        // 背景ボックス
        GUI.Box(new Rect(x, y, w, totalH), "", boxStyle);

        float cx = x + 10f;
        float cw = w - 20f;
        float cy = y + 6f;

        // ==========================================
        //  ヘッダー
        // ==========================================
        GUI.Label(new Rect(cx, cy, cw, lineH),
            "PHEROMONE DEBUGGER", headerStyle);
        cy += lineH;

        // フェーズタブ (クリックでも切り替え)
        string delLabel = viewDelivering
            ? "<color=#FF8844>● DELIVERING  </color><color=#555566>RETURNING</color>"
            : "<color=#555566>  DELIVERING  </color><color=#44FFAA>● RETURNING</color>";
        if (GUI.Button(new Rect(cx, cy, cw, lineH),
                       viewDelivering ? "▶ Delivering (Tab)" : "▶ Returning  (Tab)",
                       sectionStyle))
        {
            viewDelivering = !viewDelivering;
            selectedIndex  = 0;
            ApplyVizTarget();
        }
        cy += lineH;

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  Global Stats
        // ==========================================
        DrawSectionHeader(ref cy, cx, cw, "GLOBAL STATS");
        DrawRow(ref cy, cx, cw, "Total Pheromone", $"{globalTotal:F1}");
        DrawRow(ref cy, cx, cw, "Active Cells",    $"{globalActive}");
        DrawRow(ref cy, cx, cw, "Active Maps",
            $"{topMaps.Count} / {totalMapCount}");

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  Top Maps ランキング
        // ==========================================
        DrawSectionHeader(ref cy, cx, cw,
            $"TOP MAPS  [↑↓ select]  (phase={( viewDelivering ? "DEL" : "RET" )})");

        float maxTotal = topMaps.Count > 0 ? topMaps[0].total : 1f;

        for (int i = 0; i < topMaps.Count && i < topK; i++)
        {
            var   m       = topMaps[i];
            bool  sel     = (i == selectedIndex);
            Color barCol  = sel
                ? new Color(1f, 0.8f, 0.2f)
                : new Color(0.3f, 0.7f, 1f);

            // 選択行は背景ハイライト
            if (sel)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.2f, 0.12f);
                GUI.DrawTexture(new Rect(cx - 4f, cy, cw + 8f, barH), whiteTex);
                GUI.color = prev;
            }

            // ラベル (40%) + バー (35%) + 値 (25%)
            string rankLabel = sel ? $"▶{i + 1}. {m.label}" : $"  {i + 1}. {m.label}";
            DrawBarRow(ref cy, cx, cw, rankLabel, m.total, 0f, maxTotal, barCol,
                       $"{m.total:F1}");
        }

        if (topMaps.Count == 0)
        {
            string noData = viewDelivering ? "フェロモンなし (DEL)" : "フェロモンなし (RET)";
            GUI.Label(new Rect(cx, cy, cw, lineH), noData, footerStyle);
            cy += lineH;
        }

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  選択中マップ詳細
        // ==========================================
        if (topMaps.Count > 0)
        {
            var sel = topMaps[selectedIndex];
            DrawSectionHeader(ref cy, cx, cw, "SELECTED MAP DETAIL");
            DrawRow(ref cy, cx, cw, "Map",
                sel.isDelivering ? $"DEL  {sel.label}" : $"RET  {sel.label}");
            DrawRow(ref cy, cx, cw, "Max Value",    $"{sel.max:F4}");
            DrawRow(ref cy, cx, cw, "Active Cells", $"{sel.activeCells}");
        }

        // ==========================================
        //  エージェント現在値
        // ==========================================
        if (agentCount > 0)
        {
            DrawSeparator(ref cy, cx, cw);
            DrawSectionHeader(ref cy, cx, cw, "AGENTS (current cell)");

            foreach (var agent in manager.robotAgents)
            {
                if (agent == null) continue;

                bool isDel = agent.CurrentPhase == WarehouseRobotAgent.Phase.Delivering;
                var shelf  = agent.targetShelfTransform != null
                    ? agent.targetShelfTransform.GetComponent<ShelfUnit>() : null;
                int sIdx   = shelf != null ? phero.GetShelfIndex(shelf) : -1;

                float val = phero.GetValue(
                    agent.transform.position, isDel,
                    agent.spawnEntranceIndex, sIdx, agent.targetExitIndex);

                string phaseTag = isDel
                    ? "<color=#FF8844>DEL</color>"
                    : "<color=#44FFAA>RET</color>";
                string mapTag   = isDel
                    ? $"E{agent.spawnEntranceIndex}→S{sIdx}"
                    : $"S{sIdx}→X{agent.targetExitIndex}";

                string label = $"{agent.name} [{phaseTag}]";
                string right = $"{mapTag}  {val:F3}";

                DrawRichRow(ref cy, cx, cw, label, right);
            }
        }

        // ==========================================
        //  フッター
        // ==========================================
        cy += 2f;
        string footer = "[P]HUD  [Tab]Phase  [↑↓]Select  [L]Log" +
                        (resetConfirm ? "  <color=#FF4444>[C]確定?</color>" : "  [C]Reset");
        footerStyle.richText = true;
        GUI.Label(new Rect(cx, cy, cw, lineH), footer, footerStyle);
    }

    // ==========================================
    //  描画ヘルパー
    // ==========================================

    void DrawRow(ref float y, float x, float w, string label, string value)
    {
        GUI.Label(new Rect(x, y, w * 0.5f, lineH), label, labelStyle);
        valueStyle.richText = false;
        GUI.Label(new Rect(x + w * 0.5f, y, w * 0.5f, lineH), value, valueStyle);
        y += lineH;
    }

    void DrawRichRow(ref float y, float x, float w, string label, string value)
    {
        labelStyle.richText = true;
        GUI.Label(new Rect(x, y, w * 0.55f, lineH), label, labelStyle);
        labelStyle.richText = false;
        valueStyle.richText = false;
        valueStyle.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(x + w * 0.55f, y, w * 0.45f, lineH), value, valueStyle);
        y += lineH;
    }

    void DrawSectionHeader(ref float y, float x, float w, string title)
    {
        GUI.Label(new Rect(x, y, w, lineH), title, sectionStyle);
        y += lineH;
    }

    void DrawSeparator(ref float y, float x, float w)
    {
        float sepY = y + lineH * 0.35f;
        Color prev = GUI.color;
        GUI.color = new Color(0.35f, 0.35f, 0.4f, 0.8f);
        GUI.DrawTexture(new Rect(x, sepY, w, 1f), whiteTex);
        GUI.color = prev;
        y += lineH * 0.5f;
    }

    void DrawBarRow(ref float y, float x, float w,
                    string label, float value, float min, float max,
                    Color barColor, string valueText)
    {
        float lw = w * 0.42f;
        float bw = w * 0.33f;
        float vw = w * 0.25f;
        float bx = x + lw + 2f;

        GUI.Label(new Rect(x, y, lw, barH), label, barLabelStyle);

        // バー背景
        float barVisH = barH * 0.38f;
        float barY    = y + (barH - barVisH) * 0.5f;
        Color prev    = GUI.color;
        GUI.color = new Color(0.2f, 0.2f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(bx, barY, bw, barVisH), whiteTex);

        // バー塗り
        float t = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
        GUI.color = barColor;
        GUI.DrawTexture(new Rect(bx, barY, bw * t, barVisH), whiteTex);
        GUI.color = prev;

        // 値
        barValueStyle.richText = false;
        GUI.Label(new Rect(bx + bw + 2f, y, vw, barH), valueText, barValueStyle);

        y += barH;
    }

    int CountValidAgents()
    {
        if (manager == null) return 0;
        int count = 0;
        foreach (var a in manager.robotAgents)
            if (a != null) count++;
        return count;
    }

    // ==========================================
    //  スタイル初期化 (OnGUI 初回呼び出し時)
    // ==========================================
    void InitStyles()
    {
        if (stylesInitialized) return;

        lineH = fontSize + 6f;
        barH  = fontSize + 10f;

        bgTex          = MakeTex(1, 1, new Color(0.08f, 0.08f, 0.10f, bgAlpha));
        barBgTex       = MakeTex(1, 1, new Color(0.18f, 0.18f, 0.22f, 1f));
        whiteTex       = MakeTex(1, 1, Color.white);
        selectedBgTex  = MakeTex(1, 1, new Color(1f, 0.85f, 0.2f, 0.12f));

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = bgTex;
        boxStyle.padding = new RectOffset(8, 8, 6, 6);

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize  = fontSize + 2;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(1f, 0.7f, 0.2f);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize  = fontSize;
        labelStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);
        labelStyle.alignment = TextAnchor.MiddleLeft;
        labelStyle.richText  = true;

        valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.fontSize  = fontSize;
        valueStyle.normal.textColor = Color.white;
        valueStyle.alignment = TextAnchor.MiddleRight;

        sectionStyle = new GUIStyle(GUI.skin.label);
        sectionStyle.fontSize  = fontSize;
        sectionStyle.fontStyle = FontStyle.Bold;
        sectionStyle.normal.textColor = new Color(0.5f, 0.85f, 1f);
        sectionStyle.alignment = TextAnchor.MiddleLeft;
        sectionStyle.richText  = true;

        footerStyle = new GUIStyle(GUI.skin.label);
        footerStyle.fontSize  = fontSize - 2;
        footerStyle.normal.textColor = new Color(0.45f, 0.45f, 0.5f);
        footerStyle.richText  = true;

        barLabelStyle = new GUIStyle(labelStyle);
        barLabelStyle.wordWrap = false;
        barLabelStyle.clipping = TextClipping.Clip;
        barLabelStyle.alignment = TextAnchor.MiddleLeft;

        barValueStyle = new GUIStyle(valueStyle);
        barValueStyle.alignment = TextAnchor.MiddleLeft;
        barValueStyle.fontSize  = fontSize - 1;

        var btnStyle = GUI.skin.button;

        stylesInitialized = true;
    }

    Texture2D MakeTex(int w, int h, Color col)
    {
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
        Texture2D tex = new Texture2D(w, h);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (bgTex)         Destroy(bgTex);
        if (barBgTex)      Destroy(barBgTex);
        if (whiteTex)      Destroy(whiteTex);
        if (selectedBgTex) Destroy(selectedBgTex);
    }
}