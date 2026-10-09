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
///   [Tab]       表示単位切り替え (完全タスク / 入口→棚 / 棚→出口)
///   [↑][↓]      ランキング内のマップ選択を上下移動
///   [L]         現在の統計を Debug.Log に全出力
///   [C]         全フェロモンをリセット (確認あり)
///
/// ■ 表示内容:
///   ・Global Stats   : 総合フェロモン量・非ゼロセル数・マップ数
///   ・Top Layers     : フェロモン合計が多い順上位 N 本のレイヤー一覧
///                      完全タスクと2種類のサブタスク合算を切り替え可能
///   ・Agent Values   : 各エージェントの現在セルのフェロモン値
///                      (アクティブなマップ上の値)
///   ・Selected Map   : 選択中マップの詳細 (合計/最大/非ゼロセル数)
///
/// ■ セットアップ:
///   ManagerまたはAgentにアタッチできる。複数Agentに付いていても、
///   ランタイムでは代表インスタンス1つだけがHUDを描画する。
///   WarehousePheromone・WarehouseTrainingManager は自動検出される。
/// </summary>
public class WarehousePheromoneDebugger : MonoBehaviour
{
    private static WarehousePheromoneDebugger activeHudOwner;

    private enum MapViewMode
    {
        CompleteRoutes,
        EntranceToShelf,
        ShelfToExit,
        Shared
    }

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
        public bool  isCompleteRoute;
        public int   eIdx;
        public int   sIdx;      // 棚インデックス
        public int   xIdx;
        public float total;
        public float max;
        public int   activeCells;
        public string label;    // 表示用 "E0→S3" / "S3→X1" など
    }

    private List<MapEntry>  topMaps      = new List<MapEntry>();
    private float           globalTotal  = 0f;
    private int             globalActive = 0;
    private int             activeMapCount = 0;
    private int             totalMapCount = 0;
    private int             frameCounter = 0;

    // UI 状態
    private MapViewMode viewMode = MapViewMode.CompleteRoutes;
    private int   selectedIndex  = 0;      // topMaps 内のカーソル位置
    private bool  resetConfirm   = false;  // C キー2回押し確認
    private Vector2 scrollPosition;

    // ==========================================
    //  GUIスタイルキャッシュ
    // ==========================================
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle boxStyle;
    private GUIStyle sectionStyle;
    private GUIStyle footerStyle;
    private GUIStyle barLabelStyle;
    private GUIStyle barValueStyle;
    private GUIStyle tabStyle;
    private Texture2D bgTex;
    private Texture2D whiteTex;
    private bool stylesInitialized = false;

    private float lineH;
    private float barH;

    // ==========================================
    //  初期化
    // ==========================================
    void Start()
    {
        ResolveReferences();

        if (phero == null)
        {
            Debug.LogWarning("[PheromoneDebugger] WarehousePheromone が見つかりません。");
            enabled = false;
            return;
        }

        if (phero.Mode == WarehousePheromoneMode.Shared || phero.Mode == WarehousePheromoneMode.None)
            viewMode = MapViewMode.Shared;
        else if (!phero.SupportsCompleteRouteMaps)
            viewMode = MapViewMode.EntranceToShelf;
    }

    void ResolveReferences()
    {
        var hostAgent = GetComponent<WarehouseRobotAgent>();
        if (hostAgent != null)
        {
            manager = hostAgent.trainingManager;
            phero = hostAgent.pheromone;
        }

        if (manager == null) manager = GetComponent<WarehouseTrainingManager>();
        if (phero == null) phero = GetComponent<WarehousePheromone>();
        if (phero == null && manager != null) phero = manager.GetComponent<WarehousePheromone>();

        if (phero == null)   phero   = FindObjectOfType<WarehousePheromone>();
        if (manager == null) manager = FindObjectOfType<WarehouseTrainingManager>();
    }

    bool IsPrimaryHud()
    {
        if (activeHudOwner == null || !activeHudOwner.isActiveAndEnabled)
            activeHudOwner = this;
        return activeHudOwner == this;
    }

    bool CanRunHud()
    {
        return WarehousePerformance.IsEnabled(p => p.HUD) && IsPrimaryHud();
    }

    // ==========================================
    //  毎フレーム: 統計更新 & キー入力
    // ==========================================
    void Update()
    {
        if (!CanRunHud()) return;

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

        // [Tab] 表示単位切り替え
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            SetViewMode(NextViewMode());
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
        if (phero.Mode == WarehousePheromoneMode.Shared || phero.Mode == WarehousePheromoneMode.None)
            viewMode = MapViewMode.Shared;
        else if (viewMode == MapViewMode.CompleteRoutes && !phero.SupportsCompleteRouteMaps)
            viewMode = MapViewMode.EntranceToShelf;

        int eCount = phero.EntranceCount;
        int sCount = phero.ShelfCount;
        int xCount = phero.ExitCount;
        bool hadSelection = selectedIndex >= 0 && selectedIndex < topMaps.Count;
        MapEntry previousSelection = hadSelection ? topMaps[selectedIndex] : default;

        // 現在選択している表示単位だけを集計する。
        topMaps.Clear();
        globalTotal  = 0f;
        globalActive = 0;
        activeMapCount = 0;
        totalMapCount = 0;

        if (viewMode == MapViewMode.Shared)
        {
            totalMapCount = 1;
            var (total, max, active) = phero.GetDeliveringMapStats(0, 0);
            globalTotal = total;
            globalActive = active;
            activeMapCount = total > 0f ? 1 : 0;
            if (total > 0f)
            {
                topMaps.Add(new MapEntry
                {
                    isCompleteRoute = false,
                    eIdx = -1, sIdx = -1, xIdx = -1,
                    total = total, max = max, activeCells = active,
                    label = phero.Mode == WarehousePheromoneMode.None ? "None" : "Shared"
                });
            }
        }
        else if (viewMode == MapViewMode.CompleteRoutes)
        {
            totalMapCount = eCount * sCount * xCount;

            for (int e = 0; e < eCount; e++)
            {
                for (int s = 0; s < sCount; s++)
                {
                    ShelfUnit shelf = phero.GetShelfByIndex(s);
                    string shelfLabel = shelf != null ? shelf.shelfID : $"S{s}";

                    for (int x = 0; x < xCount; x++)
                    {
                        var (total, max, active) = phero.GetRouteMapStats(e, s, x);
                        if (total <= 0f) continue;

                        globalTotal += total;
                        globalActive += active;
                        activeMapCount++;
                        topMaps.Add(new MapEntry
                        {
                            isCompleteRoute = true,
                            eIdx = e, sIdx = s, xIdx = x,
                            total = total, max = max, activeCells = active,
                            label = $"E{e} → {shelfLabel} → X{x}"
                        });
                    }
                }
            }
        }
        else if (viewMode == MapViewMode.EntranceToShelf)
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
                    activeMapCount++;

                    ShelfUnit shelf = phero.GetShelfByIndex(s);
                    string shelfLabel = shelf != null ? shelf.shelfID : $"S{s}";

                    topMaps.Add(new MapEntry
                    {
                        isCompleteRoute = false,
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
                    activeMapCount++;

                    ShelfUnit shelf = phero.GetShelfByIndex(s);
                    string shelfLabel = shelf != null ? shelf.shelfID : $"S{s}";

                    topMaps.Add(new MapEntry
                    {
                        isCompleteRoute = false,
                        eIdx = -1, sIdx = s, xIdx = x,
                        total = total, max = max, activeCells = active,
                        label = $"{shelfLabel} → X{x}"
                    });
                }
            }
        }

        topMaps.Sort(CompareMapTotals);

        // 上位 topK に切り詰め
        int limit = Mathf.Max(1, topK);
        if (topMaps.Count > limit)
            topMaps.RemoveRange(limit, topMaps.Count - limit);

        if (topMaps.Count > 0)
        {
            selectedIndex = 0;
            if (hadSelection)
            {
                for (int i = 0; i < topMaps.Count; i++)
                {
                    if (!SameLayer(topMaps[i], previousSelection)) continue;
                    selectedIndex = i;
                    break;
                }
            }
            ApplyVizTarget();
        }
        else
            selectedIndex = 0;
    }

    static bool SameLayer(MapEntry a, MapEntry b)
    {
        return a.isCompleteRoute == b.isCompleteRoute &&
               a.eIdx == b.eIdx && a.sIdx == b.sIdx && a.xIdx == b.xIdx;
    }

    static int CompareMapTotals(MapEntry a, MapEntry b)
    {
        return b.total.CompareTo(a.total);
    }

    /// <summary>
    /// 選択中マップを WarehousePheromone の可視化ターゲットに反映する
    /// </summary>
    void ApplyVizTarget()
    {
        if (phero == null || topMaps.Count == 0) return;

        var entry = topMaps[selectedIndex];
        if (viewMode == MapViewMode.Shared)
        {
            phero.SetVizTarget(true, -1, null);
            return;
        }
        if (entry.isCompleteRoute)
        {
            phero.SetVizRoute(entry.eIdx, entry.sIdx, entry.xIdx);
            return;
        }

        ShelfUnit shelf = entry.sIdx >= 0 ? phero.GetShelfByIndex(entry.sIdx) : null;
        bool entranceToShelf = viewMode == MapViewMode.EntranceToShelf;
        int eOrX = entranceToShelf ? entry.eIdx : entry.xIdx;
        phero.SetVizTarget(entranceToShelf, eOrX, shelf);
    }

    void SetViewMode(MapViewMode mode)
    {
        if (phero.Mode == WarehousePheromoneMode.Shared || phero.Mode == WarehousePheromoneMode.None)
            mode = MapViewMode.Shared;
        else if (mode == MapViewMode.CompleteRoutes && !phero.SupportsCompleteRouteMaps)
            mode = MapViewMode.EntranceToShelf;
        if (viewMode == mode) return;
        viewMode = mode;
        selectedIndex = 0;
        RefreshStats();
    }

    MapViewMode NextViewMode()
    {
        if (phero.Mode == WarehousePheromoneMode.Shared || phero.Mode == WarehousePheromoneMode.None)
            return MapViewMode.Shared;
        if (!phero.SupportsCompleteRouteMaps)
            return viewMode == MapViewMode.EntranceToShelf
                ? MapViewMode.ShelfToExit
                : MapViewMode.EntranceToShelf;
        return (MapViewMode)(((int)viewMode + 1) % 3);
    }

    string GetViewModeLabel()
    {
        switch (viewMode)
        {
            case MapViewMode.Shared: return phero.Mode == WarehousePheromoneMode.None ? "None" : "Shared";
            case MapViewMode.EntranceToShelf: return "E→S aggregate";
            case MapViewMode.ShelfToExit: return "S→X aggregate";
            default: return "E×S×X routes";
        }
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
        sb.AppendLine($"  View: {GetViewModeLabel()}");
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
        if (!showHUD || !CanRunHud()) return;

        InitStyles();

        float sw = Screen.width;
        float sh = Screen.height;
        float margin = 6f;
        float availableW = Mathf.Max(120f, sw - margin * 2f);
        float w = Mathf.Min(Mathf.Max(280f, hudWidth), availableW);
        float x = Mathf.Max(margin, sw - Mathf.Max(margin, hudRight) - w);
        float y = Mathf.Clamp(hudTop, margin, Mathf.Max(margin, sh - 120f));

        // --- 高さ計算 ---
        int agentCount = (manager != null) ? CountValidAgents() : 0;
        int mapRows = Mathf.Min(topMaps.Count, Mathf.Max(0, topK));

        float contentH = lineH             // ヘッダー
                       + lineH             // 表示モードタブ
                       + lineH * 0.5f      // 区切り
                       + lineH * 4f        // Global Stats 見出し + 3行
                       + lineH * 0.5f      // 区切り
                       + lineH             // Top Layers 見出し
                       + (mapRows > 0 ? barH * mapRows : lineH)
                       + lineH * 0.5f;     // 区切り

        if (topMaps.Count > 0)
            contentH += lineH * 4f;        // Selected Layer 見出し + 3行

        if (agentCount > 0)
        {
            contentH += lineH * 0.5f;      // 区切り
            contentH += lineH;             // Agents ヘッダー
            contentH += lineH * agentCount;
        }

        contentH += lineH * 2f + 4f;       // フッター2行

        float maxPanelH = Mathf.Max(80f, sh - y - margin);
        float panelH = Mathf.Min(contentH + 12f, maxPanelH);
        bool needsScroll = contentH + 12f > panelH;

        // 背景ボックス
        GUI.Box(new Rect(x, y, w, panelH), "", boxStyle);

        Rect viewport = new Rect(x + 6f, y + 6f, w - 12f, panelH - 12f);
        float contentW = viewport.width - (needsScroll ? 18f : 0f);
        scrollPosition = GUI.BeginScrollView(
            viewport, scrollPosition, new Rect(0f, 0f, contentW, contentH),
            false, needsScroll);

        float cx = 4f;
        float cw = contentW - 8f;
        float cy = 0f;

        // ==========================================
        //  ヘッダー
        // ==========================================
        GUI.Label(new Rect(cx, cy, cw, lineH),
            "PHEROMONE DEBUGGER", headerStyle);
        cy += lineH;

        // 完全タスク層と2種類の合算ビュー
        const float tabGap = 3f;
        if (viewMode == MapViewMode.Shared)
        {
            GUI.Toggle(new Rect(cx, cy, cw, lineH), true, GetViewModeLabel(), tabStyle);
        }
        else
        {
            float tabW = (cw - tabGap * 2f) / 3f;
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && phero.SupportsCompleteRouteMaps;
            if (GUI.Toggle(new Rect(cx, cy, tabW, lineH),
                           viewMode == MapViewMode.CompleteRoutes, "E×S×X", tabStyle) &&
                viewMode != MapViewMode.CompleteRoutes)
                SetViewMode(MapViewMode.CompleteRoutes);
            GUI.enabled = previousEnabled;
            if (GUI.Toggle(new Rect(cx + tabW + tabGap, cy, tabW, lineH),
                           viewMode == MapViewMode.EntranceToShelf, "E→S", tabStyle) &&
                viewMode != MapViewMode.EntranceToShelf)
                SetViewMode(MapViewMode.EntranceToShelf);
            if (GUI.Toggle(new Rect(cx + (tabW + tabGap) * 2f, cy, tabW, lineH),
                           viewMode == MapViewMode.ShelfToExit, "S→X", tabStyle) &&
                viewMode != MapViewMode.ShelfToExit)
                SetViewMode(MapViewMode.ShelfToExit);
        }
        cy += lineH;

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  Global Stats
        // ==========================================
        DrawSectionHeader(ref cy, cx, cw, "GLOBAL STATS");
        DrawRow(ref cy, cx, cw, "Total Pheromone", $"{globalTotal:F1}");
        DrawRow(ref cy, cx, cw, "Active Cells",    $"{globalActive}");
        DrawRow(ref cy, cx, cw, "Active Layers", $"{activeMapCount} / {totalMapCount}");

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  Top Maps ランキング
        // ==========================================
        DrawSectionHeader(ref cy, cx, cw, $"TOP LAYERS  [{GetViewModeLabel()}]");

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
            GUI.Label(new Rect(cx, cy, cw, lineH), "この表示単位にフェロモンなし", footerStyle);
            cy += lineH;
        }

        DrawSeparator(ref cy, cx, cw);

        // ==========================================
        //  選択中マップ詳細
        // ==========================================
        if (topMaps.Count > 0)
        {
            var sel = topMaps[selectedIndex];
            DrawSectionHeader(ref cy, cx, cw, "SELECTED LAYER");
            DrawFullRow(ref cy, cx, cw, sel.label);
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
        footerStyle.richText = true;
        GUI.Label(new Rect(cx, cy, cw, lineH), "[P] Hide  [Tab] View  [↑↓] Select", footerStyle);
        cy += lineH;
        string resetLabel = resetConfirm ? "<color=#FF4444>[C] Confirm reset</color>" : "[C] Reset";
        GUI.Label(new Rect(cx, cy, cw, lineH), $"[L] Log  {resetLabel}", footerStyle);

        GUI.EndScrollView();
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

    void DrawFullRow(ref float y, float x, float w, string value)
    {
        GUI.Label(new Rect(x, y, w, lineH), value, labelStyle);
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
        float lw = w * 0.55f;
        float bw = w * 0.25f;
        float vw = w * 0.20f;
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
        whiteTex       = MakeTex(1, 1, Color.white);

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

        tabStyle = new GUIStyle(GUI.skin.button);
        tabStyle.fontSize = Mathf.Max(11, fontSize - 2);
        tabStyle.alignment = TextAnchor.MiddleCenter;

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
        if (activeHudOwner == this) activeHudOwner = null;
        if (bgTex)         Destroy(bgTex);
        if (whiteTex)      Destroy(whiteTex);
    }

    void OnDisable()
    {
        if (activeHudOwner == this) activeHudOwner = null;
    }
}
