using UnityEngine;

/// <summary>
/// 倉庫ロボットのデバッグHUD
///
/// 観測値・行動・報酬・レイキャスト情報を画面上にリアルタイム表示する。
/// WarehouseRobotAgent と同じ GameObject にアタッチする。
///
/// ■ 使い方:
///   1. ロボットの GameObject にこのスクリプトをアタッチ
///   2. Play すると画面左上にHUDが表示される
///   3. [H] キーで表示/非表示を切り替え
///   4. [R] キーでレイキャスト詳細の表示/非表示を切り替え
/// </summary>
public class WarehouseDebugHUD : MonoBehaviour
{
    [Header("===== HUD 設定 =====")]
    [Tooltip("HUD の表示/非表示")]
    public bool showHUD = true;

    [Tooltip("レイキャスト詳細を表示")]
    public bool showRayDetails = false;

    [Tooltip("HUD の表示位置 (左上からのオフセット)")]
    public Vector2 hudPosition = new Vector2(10f, 10f);

    [Tooltip("HUD の幅")]
    public float hudWidth = 340f;

    [Tooltip("フォントサイズ")]
    public int fontSize = 13;

    [Tooltip("背景の透明度")]
    [Range(0f, 1f)]
    public float bgAlpha = 0.8f;

    // --- 内部参照 ---
    private WarehouseRobotAgent agent;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle boxStyle;
    private GUIStyle labelMiddleStyle;  // DrawRow/DrawBarRow用
    private GUIStyle valueMidStyle;     // DrawRow用
    private GUIStyle barLabelStyle;     // DrawBarRow用 (折り返し禁止)
    private GUIStyle barValueStyle;     // DrawBarRow用
    private GUIStyle sectionStyle;      // セクションヘッダー用
    private GUIStyle footerStyle;       // フッター用
    private Texture2D bgTex;
    private Texture2D barBgTex;
    private Texture2D whiteTex;
    private bool stylesInitialized = false;

    // レイラベル
    private readonly string[] rayLabels = new string[]
    {
        "Front", "FR15", "FL15", "FR35", "FL35",
        "Right", "Left",
        "BR135", "BL135",
        "BR160", "Back", "BL160"
    };

    // 複数環境時に最初のHUDだけ表示
    private static int hudInstanceCount = 0;
    private int hudIndex;

    void Start()
    {
        agent = GetComponent<WarehouseRobotAgent>();
        if (agent == null)
        {
            Debug.LogWarning("[DebugHUD] WarehouseRobotAgent not found on this GameObject!");
            enabled = false;
            return;
        }

        hudIndex = hudInstanceCount++;
        // 2つ目以降は自動的にOFF (Inspectorで手動ONも可)
        if (hudIndex > 0)
            showHUD = false;
    }

    void OnDestroy()
    {
        hudInstanceCount = Mathf.Max(0, hudInstanceCount - 1);
    }

    void Update()
    {
        // H キーでHUD切り替え
        if (Input.GetKeyDown(KeyCode.H))
            showHUD = !showHUD;

        // R キーでレイ詳細切り替え
        if (Input.GetKeyDown(KeyCode.R))
            showRayDetails = !showRayDetails;
    }

    // ==========================================
    //  スタイル初期化
    // ==========================================
    void InitStyles()
    {
        if (stylesInitialized) return;

        // テクスチャ (初回1回だけ生成)
        bgTex = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.12f, bgAlpha));
        barBgTex = MakeTex(1, 1, new Color(0.2f, 0.2f, 0.25f, 1f));
        whiteTex = MakeTex(1, 1, Color.white); // GUI.color で色を変えて使い回す

        // ボックススタイル
        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = bgTex;
        boxStyle.padding = new RectOffset(10, 10, 8, 8);

        // ヘッダー
        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = fontSize + 2;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);

        // ラベル (左側)
        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = fontSize;
        labelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

        // 値 (右側)
        valueStyle = new GUIStyle(GUI.skin.label);
        valueStyle.fontSize = fontSize;
        valueStyle.normal.textColor = Color.white;
        valueStyle.alignment = TextAnchor.MiddleRight;

        // DrawRow 用キャッシュスタイル
        labelMiddleStyle = new GUIStyle(labelStyle);
        labelMiddleStyle.alignment = TextAnchor.MiddleLeft;

        valueMidStyle = new GUIStyle(valueStyle);
        valueMidStyle.alignment = TextAnchor.MiddleRight;

        // DrawBarRow 用キャッシュスタイル
        barLabelStyle = new GUIStyle(labelStyle);
        barLabelStyle.alignment = TextAnchor.MiddleLeft;
        barLabelStyle.wordWrap = false;
        barLabelStyle.clipping = TextClipping.Overflow;

        barValueStyle = new GUIStyle(valueStyle);
        barValueStyle.alignment = TextAnchor.MiddleRight;
        barValueStyle.richText = true;

        // セクションヘッダー用
        sectionStyle = new GUIStyle(labelStyle);
        sectionStyle.fontStyle = FontStyle.Bold;
        sectionStyle.normal.textColor = new Color(0.6f, 0.8f, 1f);
        sectionStyle.richText = true;
        sectionStyle.alignment = TextAnchor.MiddleLeft;

        // フッター用
        footerStyle = new GUIStyle(labelStyle);
        footerStyle.fontSize = fontSize - 2;
        footerStyle.normal.textColor = new Color(0.4f, 0.4f, 0.45f);

        stylesInitialized = true;
    }

    // ==========================================
    //  GUI 描画
    // ==========================================
    void OnGUI()
    {
        if (!showHUD || agent == null) return;
        if (!WarehousePerformance.IsEnabled(p => p.HUD)) return;

        InitStyles();

        float x = hudPosition.x;
        float y = hudPosition.y;
        float w = hudWidth;
        float lineH = fontSize + 6;     // テキスト行 (コンパクト)
        float barH  = fontSize + 10;    // バー行

        // --- 各セクションの高さを計算 ---
        float totalH = 30f;
        totalH += lineH * 2;   // ヘッダー + フェーズ
        totalH += lineH * 5;   // 状態テキスト (タイマー, ステップ, 完了, 速度, 壁)
        totalH += barH * 1;    // 角速度バー
        totalH += lineH * 0.5f; // 区切り
        totalH += lineH * 1;   // ACTIONSヘッダー
        totalH += barH * 2;    // 行動バー2本
        totalH += lineH * 0.5f; // 区切り
        totalH += lineH * 1;   // POLARヘッダー
        totalH += barH * 4;    // 極座標バー4本 (棚:距離+角度, 出口:距離+角度)
        totalH += lineH * 0.5f; // 区切り
        totalH += lineH * 2;   // 報酬
        if (showRayDetails && agent.debugRayData != null)
        {
            totalH += lineH * 1; // RAYSヘッダー
            for (int i = 0; i < 12 && i * 4 + 3 < agent.debugRayData.Length; i++)
            {
                bool hasTag = agent.debugRayData[i * 4 + 1] > 0.5f
                           || agent.debugRayData[i * 4 + 2] > 0.5f
                           || agent.debugRayData[i * 4 + 3] > 0.5f;
                totalH += hasTag ? barH + lineH : barH;
            }
        }
        totalH += lineH * 1;   // フッター

        // 背景
        GUI.Box(new Rect(x, y, w, totalH), "", boxStyle);

        float cx = x + 10f; // コンテンツ左端
        float cw = w - 20f; // コンテンツ幅
        float cy = y + 8f;  // 現在のY位置

        // ==========================================
        //  ヘッダー
        // ==========================================
        GUI.Label(new Rect(cx, cy, cw, lineH), "WAREHOUSE ROBOT DEBUG", headerStyle);
        cy += lineH;

        // フェーズ
        string phaseStr = agent.debugPhase == WarehouseRobotAgent.Phase.Delivering
            ? "<color=#FF8844>DELIVERING</color>"
            : "<color=#44FF88>RETURNING</color>";
        DrawRow(ref cy, cx, cw, lineH, "Phase", phaseStr, true);

        // ==========================================
        //  状態
        // ==========================================
        DrawSeparator(ref cy, cx, cw, lineH);
        DrawSectionHeader(ref cy, cx, cw, lineH, "STATE");
        DrawRow(ref cy, cx, cw, lineH, "Time", $"{agent.debugEpisodeTimer:F1}s");
        DrawRow(ref cy, cx, cw, lineH, "Step", $"{agent.debugStepCount}");
        DrawRow(ref cy, cx, cw, lineH, "Completed", $"{agent.debugCompletedCount}");
        DrawRow(ref cy, cx, cw, lineH, "Speed", $"{agent.debugSpeed:F2} m/s");
        string wallStr = agent.debugIsPushingWall
            ? "<color=#FF4444>CONTACT</color>" : "<color=#666666>---</color>";
        DrawRow(ref cy, cx, cw, lineH, "Wall", wallStr, true);
        DrawBarRow(ref cy, cx, cw, barH, "AngVel",
            agent.debugAngularVelY, -3f, 3f, GetInputColor(agent.debugAngularVelY / 3f),
            $"{agent.debugAngularVelY:F2} r/s");

        // ==========================================
        //  行動
        // ==========================================
        DrawSeparator(ref cy, cx, cw, lineH);
        DrawSectionHeader(ref cy, cx, cw, lineH, "ACTIONS");
        DrawBarRow(ref cy, cx, cw, barH, "Move",
            agent.debugMoveInput, -1f, 1f, GetInputColor(agent.debugMoveInput));
        DrawBarRow(ref cy, cx, cw, barH, "Turn",
            agent.debugTurnInput, -1f, 1f, GetInputColor(agent.debugTurnInput));

        // ==========================================
        //  極座標 (距離 + 角度)
        // ==========================================
        DrawSeparator(ref cy, cx, cw, lineH);
        DrawSectionHeader(ref cy, cx, cw, lineH, "POLAR (dist / angle)");

        float maxDist = 50f;
        bool isDelivering = agent.debugPhase == WarehouseRobotAgent.Phase.Delivering;

        // 棚
        Color shelfColor = isDelivering ? new Color(1f, 0.5f, 0.2f) : new Color(0.5f, 0.5f, 0.5f);
        if (agent.debugDistToShelf >= 0f)
        {
            DrawBarRow(ref cy, cx, cw, barH, "Shelf Dist",
                agent.debugDistToShelf, 0f, maxDist, shelfColor, $"{agent.debugDistToShelf:F1}m");
            DrawBarRow(ref cy, cx, cw, barH, "Shelf Ang",
                agent.debugAngleToShelf, -180f, 180f, shelfColor, $"{agent.debugAngleToShelf:F0}\u00B0");
        }
        else
        {
            DrawRow(ref cy, cx, cw, barH, "Shelf Dist", "N/A");
            DrawRow(ref cy, cx, cw, barH, "Shelf Ang", "N/A");
        }

        // 出口
        Color exitColor = !isDelivering ? new Color(0.2f, 1f, 0.5f) : new Color(0.5f, 0.5f, 0.5f);
        DrawBarRow(ref cy, cx, cw, barH, "Exit Dist",
            agent.debugDistToExit, 0f, maxDist, exitColor, $"{agent.debugDistToExit:F1}m");
        DrawBarRow(ref cy, cx, cw, barH, "Exit Ang",
            agent.debugAngleToExit, -180f, 180f, exitColor, $"{agent.debugAngleToExit:F0}\u00B0");

        // ==========================================
        //  報酬
        // ==========================================
        DrawSeparator(ref cy, cx, cw, lineH);
        DrawSectionHeader(ref cy, cx, cw, lineH, "REWARD");

        Color rewardColor = agent.debugCumulativeReward >= 0f
            ? new Color(0.3f, 1f, 0.5f) : new Color(1f, 0.4f, 0.3f);
        string rewardStr = $"<color=#{ColorUtility.ToHtmlStringRGB(rewardColor)}>{agent.debugCumulativeReward:F3}</color>";
        DrawRow(ref cy, cx, cw, lineH, "Cumulative", rewardStr, true);

        // ==========================================
        //  レイキャスト詳細 (Rキーで切り替え)
        // ==========================================
        if (showRayDetails && agent.debugRayData != null)
        {
            DrawSeparator(ref cy, cx, cw, lineH);
            DrawSectionHeader(ref cy, cx, cw, lineH, "RAYS  [R:toggle]");

            for (int i = 0; i < 12 && i * 4 + 3 < agent.debugRayData.Length; i++)
            {
                int idx = i * 4;
                float dist    = agent.debugRayData[idx];
                float shelf   = agent.debugRayData[idx + 1];
                float wall    = agent.debugRayData[idx + 2];
                float agentHt = agent.debugRayData[idx + 3];

                string hitTag = agentHt > 0.5f ? "<color=#44FF44>AGENT</color>"
                              : shelf > 0.5f   ? "<color=#4488FF>SHELF</color>"
                              : wall > 0.5f    ? "<color=#FF4444>WALL</color>"
                              : "";

                string label = i < rayLabels.Length ? rayLabels[i] : $"R{i}";
                Color barCol = agentHt > 0.5f ? new Color(0.3f, 1f, 0.3f)
                             : shelf > 0.5f   ? new Color(0.3f, 0.5f, 1f)
                             : wall > 0.5f    ? new Color(1f, 0.3f, 0.3f)
                             : new Color(0.5f, 0.5f, 0.5f);

                // ヒットタグがある行は2行分の高さにする
                bool hasTag = hitTag.Length > 0;
                float rowH = hasTag ? barH + lineH : barH;

                DrawBarRow(ref cy, cx, cw, rowH, label,
                    dist, 0f, 1f, barCol, $"{dist:F2}\n{hitTag}", true);
            }
        }

        // フッター
        cy += 4f;
        GUI.Label(new Rect(cx, cy, cw, lineH), "[H] toggle HUD  [R] toggle rays", footerStyle);
    }

    // ==========================================
    //  描画ヘルパー
    // ==========================================

    void DrawRow(ref float y, float x, float w, float h, string label, string value, bool richText = false)
    {
        GUI.Label(new Rect(x, y, w * 0.45f, h), label, labelMiddleStyle);
        valueMidStyle.richText = richText;
        GUI.Label(new Rect(x + w * 0.45f, y, w * 0.55f, h), value, valueMidStyle);
        y += h;
    }

    void DrawSectionHeader(ref float y, float x, float w, float h, string title)
    {
        GUI.Label(new Rect(x, y, w, h), title, sectionStyle);
        y += h;
    }

    void DrawSeparator(ref float y, float x, float w, float h)
    {
        float sepY = y + h * 0.4f;
        Color prev = GUI.color;
        GUI.color = new Color(0.3f, 0.3f, 0.35f, 0.8f);
        GUI.DrawTexture(new Rect(x, sepY, w, 1f), whiteTex);
        GUI.color = prev;
        y += h * 0.5f;
    }

    void DrawBarRow(ref float y, float x, float w, float h,
                    string label, float value, float min, float max,
                    Color barColor, string valueText = null, bool richText = false)
    {
        Color prevColor = GUI.color;

        // ラベル (30%)
        GUI.Label(new Rect(x, y, w * 0.30f, h), label, barLabelStyle);

        // バー領域 (29%)
        float barX = x + w * 0.32f;
        float barW = w * 0.29f;
        float barVisH = Mathf.Min(h * 0.45f, fontSize * 0.75f);
        float barY = y + (h - barVisH) * 0.5f;

        // バー背景
        GUI.DrawTexture(new Rect(barX, barY, barW, barVisH), barBgTex);

        // バー塗りつぶし
        float t = Mathf.Clamp01((value - min) / (max - min));

        GUI.color = barColor;
        if (min < 0f)
        {
            float center = barX + barW * 0.5f;
            float fillW = Mathf.Abs(t - 0.5f) * barW;
            float fillX = t >= 0.5f ? center : center - fillW;
            GUI.DrawTexture(new Rect(fillX, barY, fillW, barVisH), whiteTex);

            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            GUI.DrawTexture(new Rect(center - 0.5f, barY, 1f, barVisH), whiteTex);
        }
        else
        {
            GUI.DrawTexture(new Rect(barX, barY, barW * t, barVisH), whiteTex);
        }
        GUI.color = prevColor;

        // 値テキスト (38%)
        string text = valueText ?? $"{value:F2}";
        barValueStyle.wordWrap = text.Contains("\n");
        GUI.Label(new Rect(x + w * 0.62f, y, w * 0.38f, h), text, barValueStyle);

        y += h;
    }

    Color GetInputColor(float v)
    {
        float abs = Mathf.Abs(v);
        return Color.Lerp(new Color(0.4f, 0.4f, 0.45f), new Color(0.3f, 0.8f, 1f), abs);
    }

    // ==========================================
    //  テクスチャ生成
    // ==========================================
    Texture2D MakeTex(int w, int h, Color col)
    {
        Color[] pixels = new Color[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = col;
        Texture2D tex = new Texture2D(w, h);
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }
}