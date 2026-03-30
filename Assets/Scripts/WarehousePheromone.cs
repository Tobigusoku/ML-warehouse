using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 倉庫ロボット用フェロモンシステム (床グリッド × 棚別レイヤー)
///
/// ■ 概要:
///   倉庫の床全体をグリッドに分割し、棚ごとに独立したフェロモンマップを持つ。
///   エージェントが移動するたびに「自分のターゲット棚のマップ」上の
///   現在セルにフェロモンを分泌する。
///
///   同じ棚を目指す後続エージェントがフェロモンの濃いセルを通ると
///   報酬が得られる → 成功ルートが自然に強化される (ACO方式)。
///
/// ■ 毎ステップの処理 (Agent.OnActionReceived から呼ぶ):
///   1. 現在セルの（ターゲット棚の）フェロモン値を取得
///   2. 報酬 = log(pheromoneValue + 1) * rewardScale
///   3. セルにフェロモンを pheroQ 分加算
///
/// ■ 蒸発:
///   evapInterval FixedUpdate ごとに全マップの全セルを (1 - evapRate) 倍
///
/// ■ セットアップ:
///   TrainingManager と同じ GameObject にアタッチ。
///   warehouseGenerator を紐付ける。
/// </summary>
public class WarehousePheromone : MonoBehaviour
{
    // ==========================================
    //  Inspector 設定
    // ==========================================
    [Header("===== 参照 =====")]
    public WarehouseGenerator warehouseGenerator;

    [Header("===== グリッド設定 =====")]
    [Tooltip("グリッドのセルサイズ (m)。小さいほど精密だがメモリ増")]
    public float cellSize = 2f;

    [Header("===== フェロモン設定 =====")]
    [Tooltip("1ステップで分泌するフェロモン量")]
    public float pheroQ = 1f;

    [Tooltip("フェロモンの蒸発率 (0〜1)")]
    [Range(0f, 1f)]
    public float evapRate = 0.05f;

    [Tooltip("蒸発の間隔 (FixedUpdate 回数)")]
    public int evapInterval = 100;

    [Header("===== 報酬設定 =====")]
    [Tooltip("フェロモン報酬のスケール係数")]
    public float rewardScale = 0.0001f;

    [Header("===== 可視化 =====")]
    [Tooltip("ランタイムでフェロモンを床に表示する")]
    public bool visualize = true;

    [Tooltip("表示する棚 (null=全棚合算)")]
    public ShelfUnit visualizeShelf;

    [Tooltip("可視化の色更新間隔 (フレーム数)")]
    public int visualUpdateFrames = 5;

    [Tooltip("低フェロモン色")]
    public Color vizColorLow  = new Color(0f, 0.1f, 0.4f, 0.3f);

    [Tooltip("高フェロモン色")]
    public Color vizColorHigh = new Color(1f, 0.3f, 0f, 0.7f);

    // ==========================================
    //  内部変数
    // ==========================================

    // グリッドサイズ
    private int gridW;
    private int gridD;
    private float warehouseW;
    private float warehouseD;
    private Transform genTransform;

    // 棚ごとのフェロモンマップ
    private Dictionary<ShelfUnit, float[,]> pheroMaps = new Dictionary<ShelfUnit, float[,]>();

    // 全棚リスト
    private List<ShelfUnit> allShelves = new List<ShelfUnit>();

    private int tickCount = 0;
    private bool initialized = false;

    // 可視化タイル
    private GameObject vizParent;
    private Renderer[,] vizTiles;
    private Material vizMaterial;
    private MaterialPropertyBlock vizPropBlock;
    private int vizFrameCount = 0;
    private static readonly int ColorID = Shader.PropertyToID("_Color");

    // ==========================================
    //  初期化
    // ==========================================
    public void EnsureInitialized()
    {
        if (initialized) return;

        // warehouseGenerator 自動検出
        if (warehouseGenerator == null)
        {
            var tm = GetComponent<WarehouseTrainingManager>();
            if (tm != null) warehouseGenerator = tm.warehouseGenerator;
        }

        if (warehouseGenerator == null)
        {
            Debug.LogWarning("[Pheromone] WarehouseGenerator が未設定です");
            return;
        }

        if (!warehouseGenerator.isGenerated)
            warehouseGenerator.Generate();

        genTransform = warehouseGenerator.transform;
        warehouseW = warehouseGenerator.warehouseWidth;
        warehouseD = warehouseGenerator.warehouseDepth;

        // グリッドサイズ計算
        cellSize = Mathf.Max(0.5f, cellSize);
        gridW = Mathf.CeilToInt(warehouseW / cellSize);
        gridD = Mathf.CeilToInt(warehouseD / cellSize);

        // 棚収集 + マップ初期化
        CollectShelves();

        initialized = true;

        // 可視化タイルを生成 (trainingMode 時はスキップ)
        if (visualize && WarehousePerformance.IsEnabled(p => p.ShelfHighlight))
            CreateVisualizationTiles();

        if (WarehousePerformance.IsEnabled(p => p.DebugLog))
            Debug.Log($"[Pheromone] 初期化完了 — グリッド: {gridW}x{gridD} (cell={cellSize}m), 棚レイヤー: {allShelves.Count}");
    }

    void CollectShelves()
    {
        allShelves.Clear();
        pheroMaps.Clear();

        allShelves.AddRange(warehouseGenerator.GetComponentsInChildren<ShelfUnit>());

        foreach (var shelf in allShelves)
        {
            if (shelf != null)
                pheroMaps[shelf] = new float[gridW, gridD];
        }
    }

    // ==========================================
    //  座標変換
    // ==========================================

    /// <summary>
    /// ワールド座標 → グリッドインデックス (i, j)
    /// 範囲外の場合は (-1, -1)
    /// </summary>
    void WorldToGrid(Vector3 worldPos, out int i, out int j)
    {
        // Generator のローカル空間に変換
        Vector3 local = genTransform.InverseTransformPoint(worldPos);

        i = Mathf.FloorToInt(local.x / cellSize);
        j = Mathf.FloorToInt(local.z / cellSize);

        // 範囲外チェック
        if (i < 0 || i >= gridW || j < 0 || j >= gridD)
        {
            i = -1;
            j = -1;
        }
    }

    // ==========================================
    //  蒸発
    // ==========================================
    void FixedUpdate()
    {
        if (!initialized) return;

        tickCount++;
        if (evapInterval > 0 && tickCount % evapInterval == 0)
        {
            Evaporate();
        }
    }

    void Evaporate()
    {
        float factor = 1f - evapRate;
        foreach (var kvp in pheroMaps)
        {
            float[,] map = kvp.Value;
            for (int i = 0; i < gridW; i++)
            {
                for (int j = 0; j < gridD; j++)
                {
                    map[i, j] *= factor;
                    if (map[i, j] < 0.001f) map[i, j] = 0f;
                }
            }
        }
    }

    // ==========================================
    //  可視化: タイル生成
    // ==========================================
    void CreateVisualizationTiles()
    {
        DestroyVisualizationTiles();

        vizParent = new GameObject("PheromoneViz");
        vizParent.transform.SetParent(genTransform, false);

        // 半透明マテリアル (全タイル共有)
        vizMaterial = new Material(Shader.Find("Standard"));
        vizMaterial.SetFloat("_Mode", 3); // Transparent
        vizMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        vizMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        vizMaterial.SetInt("_ZWrite", 0);
        vizMaterial.DisableKeyword("_ALPHATEST_ON");
        vizMaterial.EnableKeyword("_ALPHABLEND_ON");
        vizMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        vizMaterial.renderQueue = 3000;
        vizMaterial.color = new Color(0, 0, 0, 0); // 初期は透明

        vizTiles = new Renderer[gridW, gridD];
        vizPropBlock = new MaterialPropertyBlock();

        float tileY = 0.02f; // 床のすぐ上

        for (int i = 0; i < gridW; i++)
        {
            for (int j = 0; j < gridD; j++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"PheroTile_{i}_{j}";
                quad.transform.SetParent(vizParent.transform, false);

                // Quad は Y+ 方向を向いているので X軸で90度回転して床に寝かせる
                float lx = i * cellSize + cellSize / 2f;
                float lz = j * cellSize + cellSize / 2f;
                quad.transform.localPosition = new Vector3(lx, tileY, lz);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(cellSize * 0.95f, cellSize * 0.95f, 1f);

                // Collider 削除 (レイキャストに干渉させない)
                Destroy(quad.GetComponent<Collider>());

                // 影なし
                var rend = quad.GetComponent<Renderer>();
                rend.material = vizMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;

                vizTiles[i, j] = rend;
            }
        }
    }

    void DestroyVisualizationTiles()
    {
        if (vizParent != null)
        {
            Destroy(vizParent);
            vizParent = null;
        }
        vizTiles = null;
    }

    // ==========================================
    //  可視化: 色の更新 (LateUpdate で数フレームごと)
    // ==========================================
    void LateUpdate()
    {
        if (!initialized || vizTiles == null || !visualize) return;

        vizFrameCount++;
        if (vizFrameCount % Mathf.Max(1, visualUpdateFrames) != 0) return;

        UpdateVisualization();
    }

    void UpdateVisualization()
    {
        // 表示対象のマップを合算
        float[,] displayMap = new float[gridW, gridD];
        float maxVal = 0f;

        if (visualizeShelf != null && pheroMaps.ContainsKey(visualizeShelf))
        {
            // 特定の棚のマップだけ表示
            float[,] map = pheroMaps[visualizeShelf];
            for (int i = 0; i < gridW; i++)
                for (int j = 0; j < gridD; j++)
                {
                    displayMap[i, j] = map[i, j];
                    if (map[i, j] > maxVal) maxVal = map[i, j];
                }
        }
        else
        {
            // 全棚のマップを合算
            foreach (var kvp in pheroMaps)
            {
                float[,] map = kvp.Value;
                for (int i = 0; i < gridW; i++)
                    for (int j = 0; j < gridD; j++)
                        displayMap[i, j] += map[i, j];
            }

            for (int i = 0; i < gridW; i++)
                for (int j = 0; j < gridD; j++)
                    if (displayMap[i, j] > maxVal) maxVal = displayMap[i, j];
        }

        // 色の更新
        for (int i = 0; i < gridW; i++)
        {
            for (int j = 0; j < gridD; j++)
            {
                if (vizTiles[i, j] == null) continue;

                float val = displayMap[i, j];

                if (val <= 0.001f)
                {
                    // フェロモンなし → 完全透明
                    vizPropBlock.SetColor(ColorID, Color.clear);
                }
                else
                {
                    float norm = maxVal > 0f ? Mathf.Clamp01(val / maxVal) : 0f;
                    Color col = Color.Lerp(vizColorLow, vizColorHigh, norm);
                    vizPropBlock.SetColor(ColorID, col);
                }

                vizTiles[i, j].SetPropertyBlock(vizPropBlock);
            }
        }
    }

    void OnDestroy()
    {
        DestroyVisualizationTiles();
    }

    // ==========================================
    //  毎ステップ処理 (Agent.OnActionReceived から呼ぶ)
    // ==========================================

    /// <summary>
    /// エージェントの現在位置にフェロモンを分泌し、報酬を返す。
    ///
    /// 1. ターゲット棚のマップ上で現在セルの値を取得
    /// 2. 報酬を計算: log(value + 1) * rewardScale
    /// 3. セルに pheroQ を加算
    /// 4. 報酬値を返す
    /// </summary>
    /// <param name="worldPos">エージェントのワールド座標</param>
    /// <param name="targetShelf">現在のターゲット棚</param>
    /// <returns>フェロモン報酬 (加算用)</returns>
    public float StepPheromone(Vector3 worldPos, ShelfUnit targetShelf)
    {
        EnsureInitialized();
        if (!initialized || targetShelf == null) return 0f;

        // この棚のマップを取得 (なければ作成)
        if (!pheroMaps.ContainsKey(targetShelf))
            pheroMaps[targetShelf] = new float[gridW, gridD];

        float[,] map = pheroMaps[targetShelf];

        // 座標変換
        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return 0f;  // 範囲外

        // 加算前の値で報酬計算
        float prevValue = map[ci, cj];
        float reward = Mathf.Log(prevValue + 1f) * rewardScale;

        // フェロモン分泌
        map[ci, cj] += pheroQ;

        return reward;
    }

    // ==========================================
    //  参照用メソッド
    // ==========================================

    /// <summary>
    /// 指定した棚のマップ上の、指定位置のフェロモン値を取得
    /// </summary>
    public float GetValue(Vector3 worldPos, ShelfUnit shelf)
    {
        if (!initialized || shelf == null) return 0f;
        if (!pheroMaps.ContainsKey(shelf)) return 0f;

        WorldToGrid(worldPos, out int i, out int j);
        if (i < 0) return 0f;

        return pheroMaps[shelf][i, j];
    }

    /// <summary>
    /// 全棚・全セルのフェロモンを0にリセット
    /// </summary>
    public void ResetAll()
    {
        foreach (var kvp in pheroMaps)
        {
            float[,] map = kvp.Value;
            System.Array.Clear(map, 0, map.Length);
        }
        tickCount = 0;
    }

    /// <summary>
    /// 指定した棚のマップの最大フェロモン値
    /// </summary>
    public float GetMaxValue(ShelfUnit shelf)
    {
        if (shelf == null || !pheroMaps.ContainsKey(shelf)) return 0f;
        float[,] map = pheroMaps[shelf];
        float max = 0f;
        for (int i = 0; i < gridW; i++)
            for (int j = 0; j < gridD; j++)
                if (map[i, j] > max) max = map[i, j];
        return max;
    }

    // ==========================================
    //  ギズモ (Scene ビューでフェロモンヒートマップ表示)
    //  選択中の棚 or 最もフェロモンが高い棚のマップを表示
    // ==========================================
    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!initialized || pheroMaps.Count == 0 || genTransform == null) return;

        // 最もフェロモンが高い棚のマップを表示
        ShelfUnit displayShelf = null;
        float maxTotal = 0f;
        foreach (var kvp in pheroMaps)
        {
            float total = 0f;
            float[,] m = kvp.Value;
            for (int i = 0; i < gridW; i++)
                for (int j = 0; j < gridD; j++)
                    total += m[i, j];
            if (total > maxTotal)
            {
                maxTotal = total;
                displayShelf = kvp.Key;
            }
        }

        if (displayShelf == null || maxTotal <= 0f) return;

        float[,] map = pheroMaps[displayShelf];
        float maxVal = GetMaxValue(displayShelf);
        if (maxVal <= 0f) return;

        for (int i = 0; i < gridW; i++)
        {
            for (int j = 0; j < gridD; j++)
            {
                float val = map[i, j];
                if (val <= 0f) continue;

                float norm = Mathf.Clamp01(val / maxVal);

                // ローカル座標の中心
                float lx = i * cellSize + cellSize / 2f;
                float lz = j * cellSize + cellSize / 2f;
                Vector3 center = genTransform.TransformPoint(new Vector3(lx, 0.05f, lz));

                Color color = Color.Lerp(
                    new Color(0f, 0.2f, 0.5f, 0.2f),
                    new Color(1f, 0.4f, 0f, 0.7f),
                    norm);

                Gizmos.color = color;
                Gizmos.DrawCube(center, new Vector3(cellSize * 0.9f, 0.05f, cellSize * 0.9f));
            }
        }

        // ラベル: どの棚のマップを表示中か
        if (displayShelf != null)
        {
            Vector3 labelPos = genTransform.TransformPoint(new Vector3(warehouseW / 2f, 3f, warehouseD / 2f));
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.Label(labelPos, $"Pheromone: {displayShelf.shelfID} (max={maxVal:F1})");
        }
#endif
    }
}