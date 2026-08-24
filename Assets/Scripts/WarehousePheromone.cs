using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 倉庫ロボット用フェロモンシステム (入口 × 棚 × 出口 レイヤー)
///
/// ■ 概要:
///   タスクフローの2フェーズを独立したフェロモンマップで管理する。
///
///   ・DELIVERING (入口→棚) :
///       pheroDelivering[eIdx * shelfCount + sIdx][cellIdx]
///       「入口 eIdx から出発して棚 sIdx を目指すルート」を強化
///
///   ・RETURNING (棚→出口) :
///       pheroReturning[sIdx * exitCount + eIdx][cellIdx]
///       「棚 sIdx から出口 eIdx へ戻るルート」を強化
///
///   ※入口と出口は同じドアを使うため exitCount == entranceCount
///
/// ■ データ構造:
///   Dictionary を廃止し、フラット float[][] + インデックス計算に変更。
///   グリッドも 2D 配列ではなく 1D 配列 (ci * gridD + cj) に統一し
///   キャッシュ効率を高める。
///
/// ■ メモリ試算 (Huge プリセット: 棚140, 入口4, グリッド40×50):
///   Delivering: 4 × 140 × 2000 × 4B ≒ 4.5 MB
///   Returning : 同上                  ≒ 4.5 MB  合計 ≒ 9 MB
///
/// ■ セットアップ:
///   TrainingManager と同じ GameObject にアタッチ。
///   warehouseGenerator を紐付ける。
/// </summary>
public class WarehousePheromone : MonoBehaviour
{
    public struct PheromoneUsageStats
    {
        public int gridW;
        public int gridD;
        public int cellCount;
        public int deliveringMapCount;
        public int returningMapCount;
        public int mapCount;
        public int totalMapCells;
        public int activeMapCells;
        public int uniqueActiveCells;
        public float activeMapCellRatio;
        public float uniqueActiveCellRatio;
        public float total;
        public float max;
        public float deliveringTotal;
        public float deliveringMax;
        public int deliveringActiveMapCells;
        public float returningTotal;
        public float returningMax;
        public int returningActiveMapCells;
    }

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
    [Tooltip("Current route-map interpretation. Shared and PhaseSeparated are recorded but not implemented by the current route-map storage.")]
    public WarehousePheromoneMode pheromoneMode = WarehousePheromoneMode.TaskSeparated;
    public bool usePheromone = true;
    public float pheromoneMinValue = 0f;
    public float pheromoneMaxValue = 1000000f;

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

    [Tooltip("表示フェーズ (true=Delivering 入口→棚, false=Returning 棚→出口)")]
    public bool visualizeDelivering = true;

    [Tooltip("表示する入口/出口 インデックス (-1=全合算)")]
    public int visualizeEntranceIndex = -1;

    [Tooltip("表示する棚 (null=全棚合算)")]
    public ShelfUnit visualizeShelf;

    [Tooltip("可視化の色更新間隔 (フレーム数)")]
    public int visualUpdateFrames = 5;

    [Tooltip("低フェロモン色")]
    public Color vizColorLow = new Color(0f, 0.1f, 0.4f, 0.3f);

    [Tooltip("高フェロモン色")]
    public Color vizColorHigh = new Color(1f, 0.3f, 0f, 0.7f);

    // ==========================================
    //  内部変数 — グリッド
    // ==========================================
    private int gridW;
    private int gridD;
    private int cellCount;   // gridW * gridD
    private float warehouseW;
    private float warehouseD;
    private Transform genTransform;

    // ==========================================
    //  内部変数 — フェロモンマップ
    //
    //  pheroDelivering[eIdx * shelfCount + sIdx][ci * gridD + cj]
    //  pheroReturning [sIdx * exitCount  + eIdx][ci * gridD + cj]
    // ==========================================
    private float[][] pheroRoutes;

    private int entranceCount;  // 入口の総数
    private int exitCount;      // 出口の総数 (== entranceCount)
    private int shelfCount;     // 棚の総数

    private List<ShelfUnit> allShelves = new List<ShelfUnit>();
    private Dictionary<ShelfUnit, int> shelfIndexMap = new Dictionary<ShelfUnit, int>();

    private int tickCount = 0;
    private bool initialized = false;

    // ==========================================
    //  可視化タイル
    // ==========================================
    private GameObject vizParent;
    private Renderer[] vizTiles;      // [ci * gridD + cj] — 1Dフラット
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

        // WarehouseGenerator 自動検出
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

        cellSize = Mathf.Max(0.5f, cellSize);
        gridW = Mathf.CeilToInt(warehouseW / cellSize);
        gridD = Mathf.CeilToInt(warehouseD / cellSize);
        cellCount = gridW * gridD;

        // 入口数 (TrainingManager の CalculateEntrances と同じ順序)
        entranceCount = CalcEntranceCount();
        exitCount = entranceCount;

        // 棚収集 + マップ確保
        CollectShelves();

        initialized = true;

        // 可視化タイル生成
        if (visualize && WarehousePerformance.IsEnabled(p => p.ShelfHighlight))
            CreateVisualizationTiles();

        if (WarehousePerformance.IsEnabled(p => p.DebugLog))
            Debug.Log($"[Pheromone] 初期化完了 — グリッド:{gridW}×{gridD} (cell={cellSize}m) " +
                      $"入口:{entranceCount} 棚:{shelfCount} " +
                      $"Routes:{pheroRoutes.Length}マップ");
    }

    // ==========================================
    //  入口数計算 (TrainingManager.CalculateEntrances と同順)
    // ==========================================
    int CalcEntranceCount()
    {
        if (warehouseGenerator == null) return 1;
        int count = 0;
        if (warehouseGenerator.doorWest) count++;
        if (warehouseGenerator.doorEast) count++;
        if (warehouseGenerator.doorSouth) count++;
        if (warehouseGenerator.doorNorth) count++;
        return Mathf.Max(1, count);
    }

    // ==========================================
    //  棚収集 + マップ確保
    // ==========================================
    void CollectShelves()
    {
        allShelves.Clear();
        shelfIndexMap.Clear();

        allShelves.AddRange(warehouseGenerator.GetComponentsInChildren<ShelfUnit>());

        for (int i = 0; i < allShelves.Count; i++)
        {
            if (allShelves[i] != null)
                shelfIndexMap[allShelves[i]] = i;
        }
        shelfCount = allShelves.Count;

        // Route: [entranceCount × shelfCount × exitCount] マップ
        int routeCount = entranceCount * Mathf.Max(1, shelfCount) * exitCount;
        pheroRoutes = new float[routeCount][];
        for (int i = 0; i < routeCount; i++)
            pheroRoutes[i] = new float[cellCount];
    }

    // ==========================================
    //  インデックス計算ヘルパー
    // ==========================================

    int RouteKey(int eIdx, int sIdx, int xIdx)
        => (Mathf.Clamp(eIdx, 0, entranceCount - 1) * shelfCount
          + Mathf.Clamp(sIdx, 0, shelfCount - 1)) * exitCount
          + Mathf.Clamp(xIdx, 0, exitCount - 1);

    // ==========================================
    //  公開: 棚インデックス取得
    // ==========================================

    /// <summary>
    /// ShelfUnit → pheroDelivering / pheroReturning のインデックス
    /// 見つからない場合は -1
    /// </summary>
    public int GetShelfIndex(ShelfUnit shelf)
    {
        if (shelf == null) return -1;
        return shelfIndexMap.TryGetValue(shelf, out int idx) ? idx : -1;
    }

    // ==========================================
    //  座標変換
    // ==========================================

    /// <summary>
    /// ワールド座標 → グリッドインデックス (ci, cj)
    /// 範囲外なら (-1, -1)
    /// </summary>
    void WorldToGrid(Vector3 worldPos, out int ci, out int cj)
    {
        Vector3 local = genTransform.InverseTransformPoint(worldPos);
        ci = Mathf.FloorToInt(local.x / cellSize);
        cj = Mathf.FloorToInt(local.z / cellSize);
        if (ci < 0 || ci >= gridW || cj < 0 || cj >= gridD)
        {
            ci = -1;
            cj = -1;
        }
    }

    // ==========================================
    //  蒸発 (FixedUpdate)
    // ==========================================
    void FixedUpdate()
    {
        if (!initialized) return;

        tickCount++;
        if (evapInterval > 0 && tickCount % evapInterval == 0)
            Evaporate();
    }

    void Evaporate()
    {
        float factor = 1f - evapRate;
        EvaporateMaps(pheroRoutes, factor);
    }

    static void EvaporateMaps(float[][] maps, float factor)
    {
        if (maps == null) return;
        for (int m = 0; m < maps.Length; m++)
        {
            float[] map = maps[m];
            if (map == null) continue;
            for (int i = 0; i < map.Length; i++)
            {
                map[i] *= factor;
                if (map[i] < 0.001f) map[i] = 0f;
            }
        }
    }

    // ==========================================
    //  毎ステップ処理 (Agent.OnActionReceived から呼ぶ)
    // ==========================================

    /// <summary>
    /// エージェントの現在位置にフェロモンを分泌し、報酬を返す。
    ///
    /// <para>DELIVERING フェーズ: pheroDelivering[eIdx * shelfCount + sIdx] に記録</para>
    /// <para>RETURNING  フェーズ: pheroReturning [sIdx * exitCount  + xIdx] に記録</para>
    ///
    /// 呼び出し条件:
    ///   isDelivering=true  → entranceIdx と shelfIdx が有効 (>= 0)
    ///   isDelivering=false → shelfIdx と exitIdx が有効 (>= 0)
    /// </summary>
    /// <param name="worldPos">エージェントのワールド座標</param>
    /// <param name="isDelivering">true=DELIVERINGフェーズ / false=RETURNINGフェーズ</param>
    /// <param name="entranceIdx">スポーン入口インデックス (TrainingManager.entrances の順)</param>
    /// <param name="shelfIdx">ターゲット棚インデックス (GetShelfIndex で取得)</param>
    /// <param name="exitIdx">帰還出口インデックス (TrainingManager.entrances の順)</param>
    /// <returns>フェロモン報酬 (log(prevValue+1) * rewardScale)</returns>
    public float StepPheromone(Vector3 worldPos, bool isDelivering,
                                int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!usePheromone || pheromoneMode == WarehousePheromoneMode.None) return 0f;
        EnsureInitialized();
        if (!initialized || shelfCount == 0) return 0f;

        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return 0f;

        int cellIdx = ci * gridD + cj;
        if (entranceIdx < 0 || shelfIdx < 0 || exitIdx < 0) return 0f;
        float[] map = pheroRoutes[RouteKey(entranceIdx, shelfIdx, exitIdx)];

        float prev = map[cellIdx];
        float reward = Mathf.Log(prev + 1f) * rewardScale;
        map[cellIdx] = Mathf.Clamp(prev + pheroQ, pheromoneMinValue, pheromoneMaxValue);

        return reward;
    }

    public float[] GetPheromoneObservationList(Vector3 worldPos, bool isDelivering,
                                int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!usePheromone || pheromoneMode == WarehousePheromoneMode.None)
            return new float[9];
        EnsureInitialized();
        float[] observations = new float[9];
        float[] map = GetMap(isDelivering, entranceIdx, shelfIdx, exitIdx);
        if (map == null || map.Length < cellCount) return observations;

        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return new float[9];
        int[,] vec = new int[,]
        {
            {  0,  0 },
            { -1, -1 },
            { -1,  1 },
            {  1,  1 },
            {  1, -1 },
            {  0,  1 },
            {  1,  0 },
            {  0, -1 },
            { -1,  0 }
        };
        int ti, tj;
        for (int i = 0; i < 9; i++)
        {
            ti = ci + vec[i, 0];
            tj = cj + vec[i, 1];

            if (ti < 0 || ti >= gridW || tj < 0 || tj >= gridD)
            {
                observations[i] = 0f;  // 壁・範囲外はゼロ
                continue;
            }

            int cellIdx = ti * gridD + tj;
            observations[i] = map[cellIdx];
        }

        return observations;
    }

    // ==========================================
    //  参照用メソッド
    // ==========================================

    /// <summary>
    /// 指定したマップ・位置のフェロモン値を取得
    /// </summary>
    public float GetValue(Vector3 worldPos, bool isDelivering,
                          int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!usePheromone || pheromoneMode == WarehousePheromoneMode.None) return 0f;
        if (!initialized || shelfCount == 0) return 0f;

        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return 0f;

        int cellIdx = ci * gridD + cj;
        if (entranceIdx < 0 || shelfIdx < 0 || exitIdx < 0) return 0f;
        float[] map = pheroRoutes[RouteKey(entranceIdx, shelfIdx, exitIdx)];

        return map[cellIdx];
    }

    public float[] GetMap(bool isDelivering,
                          int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!initialized || shelfCount == 0) return null;
        if (entranceIdx < 0 || shelfIdx < 0 || exitIdx < 0) return null;
        return pheroRoutes[RouteKey(entranceIdx, shelfIdx, exitIdx)];
    }

    /// <summary>
    /// 全マップのフェロモンを 0 にリセット
    /// </summary>
    public void ResetAll()
    {
        ClearMaps(pheroRoutes);
        tickCount = 0;
    }

    static void ClearMaps(float[][] maps)
    {
        if (maps == null) return;
        for (int m = 0; m < maps.Length; m++)
            if (maps[m] != null)
                System.Array.Clear(maps[m], 0, maps[m].Length);
    }

    /// <summary>
    /// 指定した Delivering マップの最大フェロモン値
    /// </summary>
    public float GetMaxValueDelivering(int entranceIdx, int shelfIdx)
    {
        if (!initialized || shelfCount == 0) return 0f;
        float max = 0f;
        for (int x = 0; x < exitCount; x++)
            max = Mathf.Max(max, MaxOfMap(pheroRoutes[RouteKey(entranceIdx, shelfIdx, x)]));
        return max;
    }

    /// <summary>
    /// 指定した Returning マップの最大フェロモン値
    /// </summary>
    public float GetMaxValueReturning(int shelfIdx, int exitIdx)
    {
        if (!initialized || shelfCount == 0) return 0f;
        float max = 0f;
        for (int e = 0; e < entranceCount; e++)
            max = Mathf.Max(max, MaxOfMap(pheroRoutes[RouteKey(e, shelfIdx, exitIdx)]));
        return max;
    }

    // ==========================================
    //  デバッガー向け公開API
    // ==========================================

    /// <summary> 入口/出口の総数 </summary>
    public int EntranceCount => initialized ? entranceCount : 0;

    /// <summary> 棚の総数 </summary>
    public int ShelfCount => initialized ? shelfCount : 0;

    /// <summary> 出口の総数 (== EntranceCount) </summary>
    public int ExitCount => initialized ? exitCount : 0;

    /// <summary> 初期化済みかどうか </summary>
    public bool IsInitialized => initialized;

    /// <summary>
    /// インデックスから ShelfUnit を取得
    /// </summary>
    public ShelfUnit GetShelfByIndex(int sIdx)
    {
        if (!initialized || sIdx < 0 || sIdx >= allShelves.Count) return null;
        return allShelves[sIdx];
    }

    /// <summary>
    /// Delivering マップ [eIdx × shelfCount + sIdx] の統計を返す。
    /// </summary>
    /// <returns>(合計フェロモン, 最大値, 非ゼロセル数)</returns>
    public (float total, float max, int activeCells) GetDeliveringMapStats(int eIdx, int sIdx)
    {
        if (!initialized || shelfCount == 0)
            return (0f, 0f, 0);
        return CalcAggregateRouteStats(eIdx, sIdx, -1);
    }

    /// <summary>
    /// Returning マップ [sIdx × exitCount + xIdx] の統計を返す。
    /// </summary>
    /// <returns>(合計フェロモン, 最大値, 非ゼロセル数)</returns>
    public (float total, float max, int activeCells) GetReturningMapStats(int sIdx, int xIdx)
    {
        if (!initialized || shelfCount == 0)
            return (0f, 0f, 0);
        return CalcAggregateRouteStats(-1, sIdx, xIdx);
    }

    (float total, float max, int activeCells) CalcAggregateRouteStats(int eFilter, int sFilter, int xFilter)
    {
        float[] aggregate = new float[cellCount];

        for (int e = 0; e < entranceCount; e++)
        {
            if (eFilter >= 0 && e != eFilter) continue;
            for (int s = 0; s < shelfCount; s++)
            {
                if (sFilter >= 0 && s != sFilter) continue;
                for (int x = 0; x < exitCount; x++)
                {
                    if (xFilter >= 0 && x != xFilter) continue;
                    float[] src = pheroRoutes[RouteKey(e, s, x)];
                    for (int i = 0; i < cellCount; i++)
                        aggregate[i] += src[i];
                }
            }
        }

        return CalcMapStats(aggregate);
    }

    public PheromoneUsageStats GetUsageStats(float activeThreshold = 0.001f)
    {
        EnsureInitialized();

        var stats = new PheromoneUsageStats
        {
            gridW = gridW,
            gridD = gridD,
            cellCount = cellCount,
            deliveringMapCount = pheroRoutes != null ? pheroRoutes.Length : 0,
            returningMapCount = 0,
        };

        stats.mapCount = stats.deliveringMapCount + stats.returningMapCount;
        stats.totalMapCells = stats.mapCount * cellCount;

        bool[] uniqueActive = cellCount > 0 ? new bool[cellCount] : null;

        AddUsageStats(pheroRoutes, activeThreshold, uniqueActive,
                      ref stats.deliveringTotal,
                      ref stats.deliveringMax,
                      ref stats.deliveringActiveMapCells);

        stats.total = stats.deliveringTotal + stats.returningTotal;
        stats.max = Mathf.Max(stats.deliveringMax, stats.returningMax);
        stats.activeMapCells = stats.deliveringActiveMapCells + stats.returningActiveMapCells;

        if (uniqueActive != null)
        {
            for (int i = 0; i < uniqueActive.Length; i++)
                if (uniqueActive[i]) stats.uniqueActiveCells++;
        }

        stats.activeMapCellRatio = stats.totalMapCells > 0
            ? (float)stats.activeMapCells / stats.totalMapCells : 0f;
        stats.uniqueActiveCellRatio = stats.cellCount > 0
            ? (float)stats.uniqueActiveCells / stats.cellCount : 0f;

        return stats;
    }

    public bool TryGetRouteMap(int entranceIdx, int shelfIdx, int exitIdx, float[] output)
    {
        EnsureInitialized();

        if (!initialized || shelfCount == 0 || output == null || output.Length < cellCount)
            return false;
        if (entranceIdx < 0 || entranceIdx >= entranceCount ||
            shelfIdx < 0 || shelfIdx >= shelfCount ||
            exitIdx < 0 || exitIdx >= exitCount)
            return false;

        float[] route = pheroRoutes[RouteKey(entranceIdx, shelfIdx, exitIdx)];
        for (int i = 0; i < cellCount; i++)
            output[i] = route[i];

        return true;
    }

    public (float total, float max, int activeCells) CalcValuesStats(float[] values, float activeThreshold = 0.001f)
    {
        if (values == null) return (0f, 0f, 0);

        float total = 0f;
        float max = 0f;
        int active = 0;
        for (int i = 0; i < values.Length; i++)
        {
            float v = values[i];
            if (v <= activeThreshold) continue;

            total += v;
            active++;
            if (v > max) max = v;
        }
        return (total, max, active);
    }

    static void AddUsageStats(float[][] maps, float threshold, bool[] uniqueActive,
                              ref float total, ref float max, ref int activeMapCells)
    {
        if (maps == null) return;

        for (int m = 0; m < maps.Length; m++)
        {
            float[] map = maps[m];
            if (map == null) continue;

            for (int i = 0; i < map.Length; i++)
            {
                float v = map[i];
                if (v <= threshold) continue;

                total += v;
                activeMapCells++;
                if (v > max) max = v;
                if (uniqueActive != null && i < uniqueActive.Length)
                    uniqueActive[i] = true;
            }
        }
    }

    static (float total, float max, int activeCells) CalcMapStats(float[] map)
    {
        if (map == null) return (0f, 0f, 0);
        float total = 0f, max = 0f;
        int active = 0;
        for (int i = 0; i < map.Length; i++)
        {
            float v = map[i];
            if (v > 0.001f)
            {
                total += v;
                active++;
                if (v > max) max = v;
            }
        }
        return (total, max, active);
    }

    /// <summary>
    /// WarehousePheromone の可視化ターゲットをデバッガーから上書きする。
    /// Delivering / Returning いずれかのマップを Inspector と同じ方法で選択する。
    /// </summary>
    public void SetVizTarget(bool isDelivering, int eOrXIdx, ShelfUnit shelf)
    {
        visualizeDelivering = isDelivering;
        visualizeEntranceIndex = eOrXIdx;
        visualizeShelf = shelf;
    }

    static float MaxOfMap(float[] map)
    {
        if (map == null) return 0f;
        float max = 0f;
        for (int i = 0; i < map.Length; i++)
            if (map[i] > max) max = map[i];
        return max;
    }

    // ==========================================
    //  可視化: タイル生成
    // ==========================================
    void CreateVisualizationTiles()
    {
        DestroyVisualizationTiles();

        vizParent = new GameObject("PheromoneViz");
        vizParent.transform.SetParent(genTransform, false);

        vizMaterial = new Material(Shader.Find("Standard"));
        vizMaterial.SetFloat("_Mode", 3);
        vizMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        vizMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        vizMaterial.SetInt("_ZWrite", 0);
        vizMaterial.DisableKeyword("_ALPHATEST_ON");
        vizMaterial.EnableKeyword("_ALPHABLEND_ON");
        vizMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        vizMaterial.renderQueue = 3000;
        vizMaterial.color = Color.clear;

        // タイルを1Dフラット配列で管理 (ci * gridD + cj)
        vizTiles = new Renderer[cellCount];
        vizPropBlock = new MaterialPropertyBlock();

        float tileY = 0.02f;

        for (int ci = 0; ci < gridW; ci++)
        {
            for (int cj = 0; cj < gridD; cj++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"PheroTile_{ci}_{cj}";
                quad.transform.SetParent(vizParent.transform, false);

                float lx = ci * cellSize + cellSize * 0.5f;
                float lz = cj * cellSize + cellSize * 0.5f;
                quad.transform.localPosition = new Vector3(lx, tileY, lz);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(cellSize * 0.95f, cellSize * 0.95f, 1f);

                Destroy(quad.GetComponent<Collider>());

                var rend = quad.GetComponent<Renderer>();
                rend.material = vizMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;

                vizTiles[ci * gridD + cj] = rend;
            }
        }
    }

    void DestroyVisualizationTiles()
    {
        if (vizParent != null) { Destroy(vizParent); vizParent = null; }
        vizTiles = null;
    }

    // ==========================================
    //  可視化: 色の更新
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
        // 表示用の合算バッファ (スタック上の仮配列を避け、毎回 new せず使い回す)
        float[] displayMap = new float[cellCount];
        float maxVal = 0f;

        if (visualizeDelivering)
            AggregateForViz(true, displayMap, ref maxVal);
        else
            AggregateForViz(false, displayMap, ref maxVal);

        // タイルの色を更新
        for (int i = 0; i < cellCount; i++)
        {
            Renderer rend = vizTiles[i];
            if (rend == null) continue;

            float val = displayMap[i];
            if (val <= 0.001f)
            {
                vizPropBlock.SetColor(ColorID, Color.clear);
            }
            else
            {
                float norm = maxVal > 0f ? Mathf.Clamp01(val / maxVal) : 0f;
                vizPropBlock.SetColor(ColorID, Color.Lerp(vizColorLow, vizColorHigh, norm));
            }
            rend.SetPropertyBlock(vizPropBlock);
        }
    }

    /// <summary>
    /// Inspector 設定 (visualizeEntranceIndex / visualizeShelf) に従い
    /// maps を displayMap に合算する。
    /// </summary>
    void AggregateForViz(bool isDelivering, float[] displayMap, ref float maxVal)
    {
        if (pheroRoutes == null || shelfCount == 0) return;

        int visualSIdx = (visualizeShelf != null)
            ? GetShelfIndex(visualizeShelf) : -1;

        if (isDelivering)
        {
            // Route maps aggregated by entrance -> shelf, across all exits.
            for (int e = 0; e < entranceCount; e++)
            {
                if (visualizeEntranceIndex >= 0 && e != visualizeEntranceIndex) continue;

                int sStart = visualSIdx >= 0 ? visualSIdx : 0;
                int sEnd = visualSIdx >= 0 ? visualSIdx : shelfCount - 1;

                for (int s = sStart; s <= sEnd; s++)
                {
                    for (int x = 0; x < exitCount; x++)
                    {
                        float[] src = pheroRoutes[RouteKey(e, s, x)];
                        if (src == null) continue;
                        for (int i = 0; i < cellCount; i++)
                        {
                            displayMap[i] += src[i];
                            if (displayMap[i] > maxVal) maxVal = displayMap[i];
                        }
                    }
                }
            }
        }
        else
        {
            // Route maps aggregated by shelf -> exit, across all entrances.
            int sStart = visualSIdx >= 0 ? visualSIdx : 0;
            int sEnd = visualSIdx >= 0 ? visualSIdx : shelfCount - 1;

            for (int s = sStart; s <= sEnd; s++)
            {
                for (int x = 0; x < exitCount; x++)
                {
                    if (visualizeEntranceIndex >= 0 && x != visualizeEntranceIndex) continue;

                    for (int e = 0; e < entranceCount; e++)
                    {
                        float[] src = pheroRoutes[RouteKey(e, s, x)];
                        if (src == null) continue;
                        for (int i = 0; i < cellCount; i++)
                        {
                            displayMap[i] += src[i];
                            if (displayMap[i] > maxVal) maxVal = displayMap[i];
                        }
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        DestroyVisualizationTiles();
    }

    // ==========================================
    //  ギズモ (Scene ビューでフェロモンヒートマップ表示)
    //  Inspector の visualizeDelivering / visualizeEntranceIndex / visualizeShelf
    //  に従い、最も合計フェロモンの高いマップを表示する。
    // ==========================================
    void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!initialized || genTransform == null) return;

        // 合算してヒートマップ表示
        float[] displayMap = new float[cellCount];
        float   maxVal     = 0f;

        if (visualizeDelivering)
            AggregateForViz(true, displayMap, ref maxVal);
        else
            AggregateForViz(false, displayMap, ref maxVal);

        if (maxVal <= 0f) return;

        for (int ci = 0; ci < gridW; ci++)
        {
            for (int cj = 0; cj < gridD; cj++)
            {
                float val = displayMap[ci * gridD + cj];
                if (val <= 0f) continue;

                float norm   = Mathf.Clamp01(val / maxVal);
                float lx     = ci * cellSize + cellSize * 0.5f;
                float lz     = cj * cellSize + cellSize * 0.5f;
                Vector3 center = genTransform.TransformPoint(new Vector3(lx, 0.05f, lz));

                Gizmos.color = Color.Lerp(
                    new Color(0f, 0.2f, 0.5f, 0.2f),
                    new Color(1f, 0.4f, 0f,   0.7f),
                    norm);
                Gizmos.DrawCube(center, new Vector3(cellSize * 0.9f, 0.05f, cellSize * 0.9f));
            }
        }

        // ラベル
        string phase  = visualizeDelivering ? "Delivering" : "Returning";
        string eLabel = visualizeEntranceIndex < 0 ? "All" : visualizeEntranceIndex.ToString();
        string sLabel = visualizeShelf != null ? visualizeShelf.shelfID : "All";
        Vector3 labelPos = genTransform.TransformPoint(
            new Vector3(warehouseW * 0.5f, 3f, warehouseD * 0.5f));
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.Label(labelPos,
            $"Pheromone [{phase}] Entrance:{eLabel} Shelf:{sLabel} (max={maxVal:F1})");
#endif
    }
}
