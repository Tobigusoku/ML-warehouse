using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 倉庫ロボット用フェロモンシステム
///
/// ■ 概要:
///   Shared、完全タスク単位、入口→棚／棚→出口のサブタスク単位を選べる。
///   セルにはスカラー強度、または強度と通過方向の加重和を保持する。
///
/// ■ データ構造:
///   Dictionary を廃止し、フラット float[][] + インデックス計算に変更。
///   グリッドも 2D 配列ではなく 1D 配列 (ci * gridD + cj) に統一し
///   キャッシュ効率を高める。
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
    [Tooltip("How pheromone maps are shared between tasks.")]
    public WarehousePheromoneMode pheromoneMode = WarehousePheromoneMode.TaskSeparated;
    [Tooltip("Scalar stores only intensity. Directional also stores the weighted mean movement direction.")]
    public WarehousePheromoneContent pheromoneContent = WarehousePheromoneContent.Scalar;
    [Tooltip("LegacyScalar9 keeps existing ONNX input size. VectorField27 emits strength and local X/Z direction for each cell.")]
    public WarehousePheromoneObservationFormat observationFormat =
        WarehousePheromoneObservationFormat.LegacyScalar9;
    public bool usePheromone = true;
    public float pheromoneMinValue = 0f;
    public float pheromoneMaxValue = 1000000f;
    [Min(0f)] public float directionalMovementThreshold = 0.01f;
    [Min(0.001f)] public float observationNormalizationMax = 1000f;

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
    private float[][] pheroDelivering;
    private float[][] pheroReturning;
    private float[][] directionXDelivering;
    private float[][] directionZDelivering;
    private float[][] directionXReturning;
    private float[][] directionZReturning;

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
    private float[] vizDisplayMap;
    private int vizFrameCount = 0;
    private static readonly int ColorID = Shader.PropertyToID("_Color");
    private bool visualizeExactRoute = false;
    private int visualizeRouteEntrance = -1;
    private int visualizeRouteShelf = -1;
    private int visualizeRouteExit = -1;
    private float[] statsScratch;

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
                      $"Maps:{GetDistinctMapCount()} Mode:{pheromoneMode} Content:{pheromoneContent}");
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
        AllocateMaps();
    }

    void AllocateMaps()
    {
        int safeShelfCount = Mathf.Max(1, shelfCount);
        int deliveringCount;
        int returningCount;

        switch (pheromoneMode)
        {
            case WarehousePheromoneMode.SubtaskSeparated:
                deliveringCount = entranceCount * safeShelfCount;
                returningCount = safeShelfCount * exitCount;
                break;
            case WarehousePheromoneMode.TaskSeparated:
                deliveringCount = entranceCount * safeShelfCount * exitCount;
                returningCount = deliveringCount;
                break;
            default:
                deliveringCount = 1;
                returningCount = 1;
                break;
        }

        pheroDelivering = CreateMaps(deliveringCount);
        pheroReturning = pheromoneMode == WarehousePheromoneMode.SubtaskSeparated
            ? CreateMaps(returningCount)
            : pheroDelivering;

        directionXDelivering = CreateMaps(deliveringCount);
        directionZDelivering = CreateMaps(deliveringCount);
        if (pheromoneMode == WarehousePheromoneMode.SubtaskSeparated)
        {
            directionXReturning = CreateMaps(returningCount);
            directionZReturning = CreateMaps(returningCount);
        }
        else
        {
            directionXReturning = directionXDelivering;
            directionZReturning = directionZDelivering;
        }
    }

    float[][] CreateMaps(int count)
    {
        var maps = new float[Mathf.Max(1, count)][];
        for (int i = 0; i < maps.Length; i++)
            maps[i] = new float[cellCount];
        return maps;
    }

    // ==========================================
    //  インデックス計算ヘルパー
    // ==========================================

    int RouteKey(int eIdx, int sIdx, int xIdx)
        => (Mathf.Clamp(eIdx, 0, entranceCount - 1) * shelfCount
          + Mathf.Clamp(sIdx, 0, shelfCount - 1)) * exitCount
          + Mathf.Clamp(xIdx, 0, exitCount - 1);

    int SubtaskKey(bool isDelivering, int eIdx, int sIdx, int xIdx)
    {
        return isDelivering
            ? Mathf.Clamp(eIdx, 0, entranceCount - 1) * shelfCount + Mathf.Clamp(sIdx, 0, shelfCount - 1)
            : Mathf.Clamp(sIdx, 0, shelfCount - 1) * exitCount + Mathf.Clamp(xIdx, 0, exitCount - 1);
    }

    int MapKey(bool isDelivering, int entranceIdx, int shelfIdx, int exitIdx)
    {
        switch (pheromoneMode)
        {
            case WarehousePheromoneMode.TaskSeparated:
                return RouteKey(entranceIdx, shelfIdx, exitIdx);
            case WarehousePheromoneMode.SubtaskSeparated:
                return SubtaskKey(isDelivering, entranceIdx, shelfIdx, exitIdx);
            default:
                return 0;
        }
    }

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
        EvaporateMapPair(pheroDelivering, pheroReturning, factor);
        EvaporateSignedMapPair(directionXDelivering, directionXReturning, factor);
        EvaporateSignedMapPair(directionZDelivering, directionZReturning, factor);
    }

    static void EvaporateMapPair(float[][] delivering, float[][] returning, float factor)
    {
        EvaporateMaps(delivering, factor);
        if (!ReferenceEquals(delivering, returning))
            EvaporateMaps(returning, factor);
    }

    static void EvaporateSignedMapPair(float[][] delivering, float[][] returning, float factor)
    {
        EvaporateSignedMaps(delivering, factor);
        if (!ReferenceEquals(delivering, returning))
            EvaporateSignedMaps(returning, factor);
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

    public float StepPheromone(Vector3 worldPos, bool isDelivering,
                                int entranceIdx, int shelfIdx, int exitIdx)
    {
        return StepPheromone(worldPos, Vector3.zero, isDelivering,
            entranceIdx, shelfIdx, exitIdx);
    }

    static void EvaporateSignedMaps(float[][] maps, float factor)
    {
        if (maps == null) return;
        for (int m = 0; m < maps.Length; m++)
        {
            float[] map = maps[m];
            if (map == null) continue;
            for (int i = 0; i < map.Length; i++)
            {
                map[i] *= factor;
                if (Mathf.Abs(map[i]) < 0.001f) map[i] = 0f;
            }
        }
    }

    /// <summary>
    /// Deposits pheromone at the current position. Directional content records
    /// the actual horizontal movement and never produces a pheromone reward.
    /// </summary>
    public float StepPheromone(Vector3 worldPos, Vector3 worldMovement, bool isDelivering,
                                int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!usePheromone || pheromoneMode == WarehousePheromoneMode.None) return 0f;
        EnsureInitialized();
        if (!initialized || shelfCount == 0) return 0f;

        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return 0f;

        int cellIdx = ci * gridD + cj;
        if (entranceIdx < 0 || shelfIdx < 0 || exitIdx < 0) return 0f;
        int mapKey = MapKey(isDelivering, entranceIdx, shelfIdx, exitIdx);
        float[] map = (isDelivering ? pheroDelivering : pheroReturning)[mapKey];

        float prev = map[cellIdx];
        if (pheromoneContent == WarehousePheromoneContent.Directional)
        {
            Vector3 movement = worldMovement;
            movement.y = 0f;
            if (movement.magnitude < directionalMovementThreshold)
                return 0f;

            Vector3 localDirection = genTransform.InverseTransformDirection(movement.normalized);
            float min = Mathf.Min(pheromoneMinValue, pheromoneMaxValue);
            float max = Mathf.Max(pheromoneMinValue, pheromoneMaxValue);
            float next = Mathf.Clamp(prev + Mathf.Max(0f, pheroQ), min, max);
            float added = Mathf.Max(0f, next - prev);
            map[cellIdx] = next;

            float[][] xMaps = isDelivering ? directionXDelivering : directionXReturning;
            float[][] zMaps = isDelivering ? directionZDelivering : directionZReturning;
            xMaps[mapKey][cellIdx] += localDirection.x * added;
            zMaps[mapKey][cellIdx] += localDirection.z * added;
            return 0f;
        }

        float reward = Mathf.Log(Mathf.Max(0f, prev) + 1f) * rewardScale;
        map[cellIdx] = Mathf.Clamp(prev + pheroQ, pheromoneMinValue, pheromoneMaxValue);
        return reward;
    }

    public float[] GetPheromoneObservationList(Vector3 worldPos, bool isDelivering,
                                int entranceIdx, int shelfIdx, int exitIdx,
                                Transform observer = null)
    {
        int observationSize = PheromoneObservationSize;
        if (!usePheromone || pheromoneMode == WarehousePheromoneMode.None)
            return new float[observationSize];
        EnsureInitialized();
        float[] observations = new float[observationSize];
        float[] map = GetMap(isDelivering, entranceIdx, shelfIdx, exitIdx);
        if (map == null || map.Length < cellCount) return observations;

        WorldToGrid(worldPos, out int ci, out int cj);
        if (ci < 0) return observations;
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
                if (observationFormat == WarehousePheromoneObservationFormat.LegacyScalar9)
                    observations[i] = 0f;
                continue;
            }

            int cellIdx = ti * gridD + tj;
            if (observationFormat == WarehousePheromoneObservationFormat.LegacyScalar9)
            {
                observations[i] = map[cellIdx];
                continue;
            }

            int output = i * 3;
            float mass = Mathf.Max(0f, map[cellIdx]);
            observations[output] = NormalizeStrength(mass);
            if (pheromoneContent != WarehousePheromoneContent.Directional || mass <= 0.001f)
                continue;

            int mapKey = MapKey(isDelivering, entranceIdx, shelfIdx, exitIdx);
            float[][] xMaps = isDelivering ? directionXDelivering : directionXReturning;
            float[][] zMaps = isDelivering ? directionZDelivering : directionZReturning;
            Vector3 warehouseLocalDirection = new Vector3(
                xMaps[mapKey][cellIdx] / mass,
                0f,
                zMaps[mapKey][cellIdx] / mass);
            Vector3 worldDirection = genTransform.TransformDirection(warehouseLocalDirection);
            Vector3 localDirection = observer != null
                ? observer.InverseTransformDirection(worldDirection)
                : worldDirection;
            observations[output + 1] = Mathf.Clamp(localDirection.x, -1f, 1f);
            observations[output + 2] = Mathf.Clamp(localDirection.z, -1f, 1f);
        }

        return observations;
    }

    float NormalizeStrength(float value)
    {
        float max = Mathf.Max(0.001f, observationNormalizationMax);
        return Mathf.Clamp01(Mathf.Log(value + 1f) / Mathf.Log(max + 1f));
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
        float[] map = GetMap(isDelivering, entranceIdx, shelfIdx, exitIdx);

        return map[cellIdx];
    }

    public float[] GetMap(bool isDelivering,
                          int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!initialized || shelfCount == 0) return null;
        if (entranceIdx < 0 || shelfIdx < 0 || exitIdx < 0) return null;
        int key = MapKey(isDelivering, entranceIdx, shelfIdx, exitIdx);
        float[][] maps = isDelivering ? pheroDelivering : pheroReturning;
        return key >= 0 && key < maps.Length ? maps[key] : null;
    }

    /// <summary>
    /// 全マップのフェロモンを 0 にリセット
    /// </summary>
    public void ResetAll()
    {
        ClearMapPair(pheroDelivering, pheroReturning);
        ClearMapPair(directionXDelivering, directionXReturning);
        ClearMapPair(directionZDelivering, directionZReturning);
        tickCount = 0;
    }

    static void ClearMapPair(float[][] delivering, float[][] returning)
    {
        ClearMaps(delivering);
        if (!ReferenceEquals(delivering, returning))
            ClearMaps(returning);
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
        return GetDeliveringMapStats(entranceIdx, shelfIdx).max;
    }

    /// <summary>
    /// 指定した Returning マップの最大フェロモン値
    /// </summary>
    public float GetMaxValueReturning(int shelfIdx, int exitIdx)
    {
        return GetReturningMapStats(shelfIdx, exitIdx).max;
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

    public WarehousePheromoneMode Mode => pheromoneMode;

    public WarehousePheromoneContent Content => pheromoneContent;

    public bool SupportsCompleteRouteMaps => pheromoneMode == WarehousePheromoneMode.TaskSeparated;

    public int PheromoneObservationSize =>
        observationFormat == WarehousePheromoneObservationFormat.VectorField27 ? 27 : 9;

    int GetDistinctMapCount()
    {
        int count = pheroDelivering != null ? pheroDelivering.Length : 0;
        if (!ReferenceEquals(pheroDelivering, pheroReturning) && pheroReturning != null)
            count += pheroReturning.Length;
        return count;
    }

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
        EnsureStatsScratch();
        return TryGetDeliveringMap(eIdx, sIdx, statsScratch)
            ? CalcMapStats(statsScratch)
            : (0f, 0f, 0);
    }

    /// <summary>
    /// Returning マップ [sIdx × exitCount + xIdx] の統計を返す。
    /// </summary>
    /// <returns>(合計フェロモン, 最大値, 非ゼロセル数)</returns>
    public (float total, float max, int activeCells) GetReturningMapStats(int sIdx, int xIdx)
    {
        if (!initialized || shelfCount == 0)
            return (0f, 0f, 0);
        EnsureStatsScratch();
        return TryGetReturningMap(sIdx, xIdx, statsScratch)
            ? CalcMapStats(statsScratch)
            : (0f, 0f, 0);
    }

    /// <summary>
    /// 完全タスクマップ [entrance × shelf × exit] 1枚の統計を返す。
    /// </summary>
    public (float total, float max, int activeCells) GetRouteMapStats(
        int entranceIdx, int shelfIdx, int exitIdx)
    {
        if (!initialized || shelfCount == 0 ||
            entranceIdx < 0 || entranceIdx >= entranceCount ||
            shelfIdx < 0 || shelfIdx >= shelfCount ||
            exitIdx < 0 || exitIdx >= exitCount)
        {
            return (0f, 0f, 0);
        }

        if (!SupportsCompleteRouteMaps)
            return (0f, 0f, 0);
        return CalcMapStats(pheroDelivering[RouteKey(entranceIdx, shelfIdx, exitIdx)]);
    }

    void EnsureStatsScratch()
    {
        if (statsScratch == null || statsScratch.Length != cellCount)
            statsScratch = new float[cellCount];
        else
            System.Array.Clear(statsScratch, 0, statsScratch.Length);
    }

    public bool TryGetDeliveringMap(int entranceIdx, int shelfIdx, float[] output)
    {
        EnsureInitialized();
        if (!ValidateMapOutput(output) || entranceIdx < 0 || entranceIdx >= entranceCount ||
            shelfIdx < 0 || shelfIdx >= shelfCount)
            return false;

        System.Array.Clear(output, 0, cellCount);
        if (pheromoneMode == WarehousePheromoneMode.TaskSeparated)
        {
            for (int x = 0; x < exitCount; x++)
                AddMap(pheroDelivering[RouteKey(entranceIdx, shelfIdx, x)], output);
            return true;
        }

        float[] source = pheromoneMode == WarehousePheromoneMode.SubtaskSeparated
            ? pheroDelivering[SubtaskKey(true, entranceIdx, shelfIdx, 0)]
            : pheroDelivering[0];
        System.Array.Copy(source, output, cellCount);
        return true;
    }

    public bool TryGetReturningMap(int shelfIdx, int exitIdx, float[] output)
    {
        EnsureInitialized();
        if (!ValidateMapOutput(output) || shelfIdx < 0 || shelfIdx >= shelfCount ||
            exitIdx < 0 || exitIdx >= exitCount)
            return false;

        System.Array.Clear(output, 0, cellCount);
        if (pheromoneMode == WarehousePheromoneMode.TaskSeparated)
        {
            for (int e = 0; e < entranceCount; e++)
                AddMap(pheroReturning[RouteKey(e, shelfIdx, exitIdx)], output);
            return true;
        }

        float[] source = pheromoneMode == WarehousePheromoneMode.SubtaskSeparated
            ? pheroReturning[SubtaskKey(false, 0, shelfIdx, exitIdx)]
            : pheroReturning[0];
        System.Array.Copy(source, output, cellCount);
        return true;
    }

    bool ValidateMapOutput(float[] output)
    {
        return initialized && shelfCount > 0 && output != null && output.Length >= cellCount;
    }

    static void AddMap(float[] source, float[] destination)
    {
        for (int i = 0; i < source.Length; i++)
            destination[i] += source[i];
    }

    public PheromoneUsageStats GetUsageStats(float activeThreshold = 0.001f)
    {
        EnsureInitialized();

        var stats = new PheromoneUsageStats
        {
            gridW = gridW,
            gridD = gridD,
            cellCount = cellCount,
            deliveringMapCount = pheroDelivering != null ? pheroDelivering.Length : 0,
            returningMapCount = !ReferenceEquals(pheroDelivering, pheroReturning) && pheroReturning != null
                ? pheroReturning.Length : 0,
        };

        stats.mapCount = stats.deliveringMapCount + stats.returningMapCount;
        stats.totalMapCells = stats.mapCount * cellCount;

        bool[] uniqueActive = cellCount > 0 ? new bool[cellCount] : null;

        AddUsageStats(pheroDelivering, activeThreshold, uniqueActive,
                      ref stats.deliveringTotal,
                      ref stats.deliveringMax,
                      ref stats.deliveringActiveMapCells);

        if (!ReferenceEquals(pheroDelivering, pheroReturning))
            AddUsageStats(pheroReturning, activeThreshold, uniqueActive,
                          ref stats.returningTotal,
                          ref stats.returningMax,
                          ref stats.returningActiveMapCells);

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

        if (!SupportsCompleteRouteMaps || !ValidateMapOutput(output))
            return false;
        if (entranceIdx < 0 || entranceIdx >= entranceCount ||
            shelfIdx < 0 || shelfIdx >= shelfCount ||
            exitIdx < 0 || exitIdx >= exitCount)
            return false;

        float[] route = pheroDelivering[RouteKey(entranceIdx, shelfIdx, exitIdx)];
        for (int i = 0; i < cellCount; i++)
            output[i] = route[i];

        return true;
    }

    /// <summary>
    /// Copies the weighted mean movement direction for one active map.
    /// Components are expressed in warehouse-local X/Z coordinates.
    /// </summary>
    public bool TryGetDirectionMap(bool isDelivering, int entranceIdx, int shelfIdx, int exitIdx,
                                   float[] outputX, float[] outputZ)
    {
        EnsureInitialized();
        if (pheromoneContent != WarehousePheromoneContent.Directional ||
            !ValidateMapOutput(outputX) || !ValidateMapOutput(outputZ) ||
            entranceIdx < 0 || entranceIdx >= entranceCount ||
            shelfIdx < 0 || shelfIdx >= shelfCount ||
            exitIdx < 0 || exitIdx >= exitCount)
            return false;

        int key = MapKey(isDelivering, entranceIdx, shelfIdx, exitIdx);
        float[][] massMaps = isDelivering ? pheroDelivering : pheroReturning;
        float[][] xMaps = isDelivering ? directionXDelivering : directionXReturning;
        float[][] zMaps = isDelivering ? directionZDelivering : directionZReturning;
        float[] mass = massMaps[key];
        float[] x = xMaps[key];
        float[] z = zMaps[key];
        for (int i = 0; i < cellCount; i++)
        {
            if (mass[i] <= 0.001f)
            {
                outputX[i] = 0f;
                outputZ[i] = 0f;
                continue;
            }
            outputX[i] = Mathf.Clamp(x[i] / mass[i], -1f, 1f);
            outputZ[i] = Mathf.Clamp(z[i] / mass[i], -1f, 1f);
        }
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
        visualizeExactRoute = false;
        visualizeDelivering = isDelivering;
        visualizeEntranceIndex = eOrXIdx;
        visualizeShelf = shelf;
    }

    /// <summary>完全タスクマップ1枚をランタイム可視化の対象にする。</summary>
    public void SetVizRoute(int entranceIdx, int shelfIdx, int exitIdx)
    {
        visualizeExactRoute = SupportsCompleteRouteMaps;
        visualizeRouteEntrance = entranceIdx;
        visualizeRouteShelf = shelfIdx;
        visualizeRouteExit = exitIdx;
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
                rend.sharedMaterial = vizMaterial;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rend.receiveShadows = false;

                vizTiles[ci * gridD + cj] = rend;
            }
        }
    }

    void DestroyVisualizationTiles()
    {
        if (vizParent != null) { Destroy(vizParent); vizParent = null; }
        if (vizMaterial != null) { Destroy(vizMaterial); vizMaterial = null; }
        vizTiles = null;
        vizDisplayMap = null;
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
        if (vizDisplayMap == null || vizDisplayMap.Length != cellCount)
            vizDisplayMap = new float[cellCount];
        else
            System.Array.Clear(vizDisplayMap, 0, vizDisplayMap.Length);

        float maxVal = 0f;

        if (visualizeDelivering)
            AggregateForViz(true, vizDisplayMap, ref maxVal);
        else
            AggregateForViz(false, vizDisplayMap, ref maxVal);

        // タイルの色を更新
        for (int i = 0; i < cellCount; i++)
        {
            Renderer rend = vizTiles[i];
            if (rend == null) continue;

            float val = vizDisplayMap[i];
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
        if (pheroDelivering == null || shelfCount == 0) return;

        if (visualizeExactRoute && SupportsCompleteRouteMaps)
        {
            if (visualizeRouteEntrance < 0 || visualizeRouteEntrance >= entranceCount ||
                visualizeRouteShelf < 0 || visualizeRouteShelf >= shelfCount ||
                visualizeRouteExit < 0 || visualizeRouteExit >= exitCount)
                return;

            float[] route = pheroDelivering[RouteKey(
                visualizeRouteEntrance, visualizeRouteShelf, visualizeRouteExit)];
            for (int i = 0; i < cellCount; i++)
            {
                displayMap[i] = route[i];
                if (route[i] > maxVal) maxVal = route[i];
            }
            return;
        }

        int visualSIdx = (visualizeShelf != null)
            ? GetShelfIndex(visualizeShelf) : -1;

        if (pheromoneMode == WarehousePheromoneMode.Shared)
        {
            float[] shared = isDelivering ? pheroDelivering[0] : pheroReturning[0];
            System.Array.Copy(shared, displayMap, cellCount);
            maxVal = MaxOfMap(shared);
            return;
        }

        float[] phaseMap = new float[cellCount];

        if (isDelivering)
        {
            for (int e = 0; e < entranceCount; e++)
            {
                if (visualizeEntranceIndex >= 0 && e != visualizeEntranceIndex) continue;

                int sStart = visualSIdx >= 0 ? visualSIdx : 0;
                int sEnd = visualSIdx >= 0 ? visualSIdx : shelfCount - 1;

                for (int s = sStart; s <= sEnd; s++)
                {
                    if (TryGetDeliveringMap(e, s, phaseMap))
                        AddMap(phaseMap, displayMap);
                }
            }
        }
        else
        {
            int sStart = visualSIdx >= 0 ? visualSIdx : 0;
            int sEnd = visualSIdx >= 0 ? visualSIdx : shelfCount - 1;

            for (int s = sStart; s <= sEnd; s++)
            {
                for (int x = 0; x < exitCount; x++)
                {
                    if (visualizeEntranceIndex >= 0 && x != visualizeEntranceIndex) continue;
                    if (TryGetReturningMap(s, x, phaseMap))
                        AddMap(phaseMap, displayMap);
                }
            }
        }

        maxVal = MaxOfMap(displayMap);
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
        string phase = visualizeExactRoute
            ? $"Route E{visualizeRouteEntrance}-S{visualizeRouteShelf}-X{visualizeRouteExit}"
            : (visualizeDelivering ? "Delivering aggregate" : "Returning aggregate");
        string eLabel = visualizeEntranceIndex < 0 ? "All" : visualizeEntranceIndex.ToString();
        string sLabel = visualizeShelf != null ? visualizeShelf.shelfID : "All";
        Vector3 labelPos = genTransform.TransformPoint(
            new Vector3(warehouseW * 0.5f, 3f, warehouseD * 0.5f));
        UnityEditor.Handles.color = Color.yellow;
        string label = visualizeExactRoute
            ? $"Pheromone [{phase}] (max={maxVal:F1})"
            : $"Pheromone [{phase}] Gate:{eLabel} Shelf:{sLabel} (max={maxVal:F1})";
        UnityEditor.Handles.Label(labelPos, label);
#endif
    }
}
