using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 倉庫フィールド自動生成スクリプト
/// 空のGameObjectにアタッチして使用してください。
/// Inspectorからパラメータを調整し、Play時に自動生成されます。
/// </summary>
[DefaultExecutionOrder(-1000)]
public class WarehouseGenerator : MonoBehaviour
{
    [Header("===== Experiment config =====")]
    [Tooltip("Use a WarehouseExperimentConfig instead of the Inspector values.")]
    public bool useExperimentConfig = false;
    public WarehouseExperimentConfig experimentConfig;
    [Tooltip("Fallback experiment/run ID when MLAGENTS_RUN_ID and --run-id are unavailable.")]
    public string experimentRunId = "";

    [Header("===== Environment preset =====")]
    [Tooltip("Versioned layout settings. Selecting this asset applies its values to the fields below.")]
    public WarehouseEnvironmentPreset environmentPreset;

    // ===== プリセット =====
    [Header("===== プリセット (基準値) =====")]
    [Tooltip("基準値を選択して適用できます。Custom なら手動設定を使用します。")]
    public WarehousePreset preset = WarehousePreset.Custom;

    // ===== 倉庫全体の設定 =====
    [Header("===== 倉庫サイズ =====")]
    [Tooltip("倉庫の横幅 (X軸)")]
    public int warehouseWidth = 30;

    [Tooltip("倉庫の奥行き (Z軸)")]
    public int warehouseDepth = 40;

    [Tooltip("壁の高さ")]
    public float wallHeight = 5f;

    // ===== 棚の設定 =====
    [Header("===== 棚 (ラック) 設定 =====")]
    [Tooltip("棚の列数")]
    public int shelfRows = 4;

    [Tooltip("1列あたりの棚の数")]
    public int shelvesPerRow = 6;

    [Tooltip("棚の幅")]
    public float shelfWidth = 2f;

    [Tooltip("棚の高さ")]
    public float shelfHeight = 3f;

    [Tooltip("棚の奥行き")]
    public float shelfDepth = 1.2f;

    [Tooltip("棚同士の間隔 (通路幅)")]
    public float aisleWidth = 3f;

    [Tooltip("背中合わせ棚の間の間隔")]
    public float shelfSpacing = 1.0f;

    [Tooltip("棚の横方向の隙間 (Z方向)")]
    public float shelfLateralGap = 0.3f;

    [Tooltip("背中合わせの2列セットにする (false=独立1列)")]
    public bool shelfPaired = true;

    // ===== 箱 (荷物) の設定 =====
    [Header("===== 荷物設定 =====")]
    [Tooltip("棚に荷物が置かれる確率 (0〜1)")]
    [Range(0f, 1f)]
    public float crateSpawnChance = 0.7f;

    [Tooltip("フォークリフト通路にパレットを配置する確率")]
    [Range(0f, 1f)]
    public float palletSpawnChance = 0.15f;

    // ===== 見た目の設定 =====
    [Header("===== マテリアル (未設定時は自動生成) =====")]
    public Material floorMaterial;
    public Material wallMaterial;
    public Material shelfMaterial;
    public Material crateMaterial;
    public Material palletMaterial;
    public Material pillarMaterial;

    // ===== 入口の設定 =====
    [Header("===== 入口設定 =====")]
    [Tooltip("入口の幅")]
    public float doorWidth = 5f;

    [Tooltip("入口の高さ")]
    public float doorHeight = 3.5f;

    [Tooltip("西壁に入口を作る")]
    public bool doorWest = true;

    [Tooltip("東壁に入口を作る")]
    public bool doorEast = true;

    [Tooltip("南壁に入口を作る")]
    public bool doorSouth = false;

    [Tooltip("北壁に入口を作る")]
    public bool doorNorth = false;

    [Tooltip("入口の外側に生成する搬入エリアの奥行き")]
    public float entrancePlatformDepth = 5f;

    // ===== 柱の設定 =====
    [Header("===== 柱 (ピラー) 設定 =====")]
    [Tooltip("柱を生成する")]
    public bool generatePillars = true;

    [Tooltip("柱の太さ (XZ)")]
    public float pillarSize = 0.4f;

    [Tooltip("柱の間隔 (X方向)")]
    public float pillarSpacingX = 8f;

    [Tooltip("柱の間隔 (Z方向)")]
    public float pillarSpacingZ = 8f;

    [Tooltip("壁からの最小マージン")]
    public float pillarWallMargin = 2f;

    [Tooltip("棚と重なる柱を除外する")]
    public bool pillarAvoidShelves = true;

    [Tooltip("棚との最小クリアランス")]
    public float pillarShelfClearance = 0.5f;

    // ===== 棚の当たり判定 =====
    [Header("===== 当たり判定 =====")]
    [Tooltip("棚ユニット全体にBoxColliderを付ける")]
    public bool addShelfCollider = true;

    [Tooltip("棚のコライダーのレイヤー名 (空欄ならDefault)")]
    public string shelfColliderLayer = "";

    [Tooltip("棚のコライダーのタグ (空欄ならUntagged)")]
    public string shelfColliderTag = "Untagged";

    // ===== シード設定 =====
    [Header("===== ランダムシード =====")]
    [Tooltip("0 = 毎回ランダム / 0以外 = 固定シード")]
    public int seed = 0;

    // ===== 内部変数 =====
    private Transform warehouseParent;
    private Transform floorParent;
    private Transform wallParent;
    private Transform shelfParent;
    private Transform crateParent;
    private Transform pillarParent;

    // 引っかかり防止用の共有PhysicMaterial
    private PhysicMaterial smoothPhysMat;

    // ==========================================
    //  初期化
    // ==========================================
    [System.NonSerialized]
    public bool isGenerated = false;

    void Awake()
    {
        WarehouseExperimentRuntime.ApplyToEnvironment(this);
        if (!useExperimentConfig && environmentPreset != null)
            ApplyEnvironmentPreset(environmentPreset);

        if (!isGenerated)
        {
            CleanupExistingWarehouse();
            Generate();
        }
    }

    /// <summary>
    /// 子オブジェクトの "Warehouse" を全て探して破棄する。
    /// warehouseParent 変数が null でも名前検索で確実に消す。
    /// </summary>
    void CleanupExistingWarehouse()
    {
        // warehouseParent 経由で消せる場合
        if (warehouseParent != null)
        {
            SafeDestroy(warehouseParent.gameObject);
            warehouseParent = null;
        }

        // 名前検索で残っている Warehouse を全て消す (二重保険)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == "Warehouse")
            {
                SafeDestroy(child.gameObject);
            }
        }
    }

    // ==========================================
    //  プリセット適用
    // ==========================================

    /// <summary>
    /// 現在選択中のプリセットを全パラメータに適用する。
    /// Custom の場合は何もしない (手動設定を維持)。
    /// </summary>
    public void ApplyPreset()
    {
        ApplyPreset(preset);
    }

    /// <summary>
    /// 指定したプリセットを全パラメータに適用する
    /// </summary>
    public void ApplyPreset(WarehousePreset targetPreset)
    {
        var data = WarehousePresetData.Get(targetPreset);
        if (data == null)
        {
            Debug.Log("[WarehouseGenerator] Custom プリセット — 手動設定を維持します");
            return;
        }

        preset = targetPreset;
        if (targetPreset != WarehousePreset.Custom)
            environmentPreset = null;

        // 倉庫サイズ
        warehouseWidth  = data.warehouseWidth;
        warehouseDepth  = data.warehouseDepth;
        wallHeight      = data.wallHeight;

        // 棚
        shelfRows       = data.shelfRows;
        shelvesPerRow   = data.shelvesPerRow;
        shelfWidth      = data.shelfWidth;
        shelfHeight     = data.shelfHeight;
        shelfDepth      = data.shelfDepth;
        aisleWidth      = data.aisleWidth;
        shelfSpacing    = data.shelfSpacing;
        shelfLateralGap = data.shelfLateralGap;
        shelfPaired     = data.shelfPaired;

        // 荷物
        crateSpawnChance  = data.crateSpawnChance;
        palletSpawnChance = data.palletSpawnChance;

        // 入口
        doorWidth   = data.doorWidth;
        doorHeight  = data.doorHeight;
        doorWest    = data.doorWest;
        doorEast    = data.doorEast;
        doorSouth   = data.doorSouth;
        doorNorth   = data.doorNorth;

        // 柱
        generatePillars     = data.generatePillars;
        pillarSize          = data.pillarSize;
        pillarSpacingX      = data.pillarSpacingX;
        pillarSpacingZ      = data.pillarSpacingZ;
        pillarWallMargin    = data.pillarWallMargin;
        pillarAvoidShelves  = data.pillarAvoidShelves;
        pillarShelfClearance = data.pillarShelfClearance;

        Debug.Log($"[WarehouseGenerator] プリセット '{targetPreset}' を適用: {warehouseWidth}x{warehouseDepth}m, 棚{shelfRows}列x{shelvesPerRow}個, 柱{(generatePillars ? "ON" : "OFF")}");
    }

    /// <summary>
    /// Copies a versioned environment asset into this component's existing serialized fields.
    /// The generated layout still uses the component fields, so they remain visible in the Inspector.
    /// </summary>
    public void ApplyEnvironmentPreset()
    {
        ApplyEnvironmentPreset(environmentPreset);
    }

    public void ApplyEnvironmentPreset(WarehouseEnvironmentPreset source)
    {
        if (source == null)
        {
            Debug.LogWarning("[WarehouseGenerator] No WarehouseEnvironmentPreset is assigned.");
            return;
        }

        environmentPreset = source;
        // A ScriptableObject layout can differ from the fixed code presets.
        preset = WarehousePreset.Custom;
        warehouseWidth = source.warehouseWidth;
        warehouseDepth = source.warehouseDepth;
        wallHeight = source.wallHeight;
        shelfRows = source.shelfRows;
        shelvesPerRow = source.shelvesPerRow;
        shelfWidth = source.shelfWidth;
        shelfHeight = source.shelfHeight;
        shelfDepth = source.shelfDepth;
        aisleWidth = source.aisleWidth;
        shelfSpacing = source.shelfSpacing;
        shelfLateralGap = source.shelfLateralGap;
        shelfPaired = source.shelfPaired;
        crateSpawnChance = source.crateSpawnChance;
        palletSpawnChance = source.palletSpawnChance;
        doorWidth = source.doorWidth;
        doorHeight = source.doorHeight;
        doorWest = source.doorWest;
        doorEast = source.doorEast;
        doorSouth = source.doorSouth;
        doorNorth = source.doorNorth;
        entrancePlatformDepth = source.entrancePlatformDepth;
        generatePillars = source.generatePillars;
        pillarSize = source.pillarSize;
        pillarSpacingX = source.pillarSpacingX;
        pillarSpacingZ = source.pillarSpacingZ;
        pillarWallMargin = source.pillarWallMargin;
        pillarAvoidShelves = source.pillarAvoidShelves;
        pillarShelfClearance = source.pillarShelfClearance;
        seed = source.environmentSeed;
    }

    /// <summary>
    /// 現在の設定値から最も近いプリセットを推定して返す (参考用)
    /// </summary>
    public WarehousePreset DetectClosestPreset()
    {
        var presets = new WarehousePreset[]
        {
            WarehousePreset.Tiny, WarehousePreset.Small, WarehousePreset.Medium,
            WarehousePreset.Large, WarehousePreset.Huge, WarehousePreset.Narrow,
            WarehousePreset.OpenYard
        };

        float bestScore = float.MaxValue;
        WarehousePreset best = WarehousePreset.Custom;

        foreach (var p in presets)
        {
            var d = WarehousePresetData.Get(p);
            if (d == null) continue;

            float score = Mathf.Abs(warehouseWidth - d.warehouseWidth)
                        + Mathf.Abs(warehouseDepth - d.warehouseDepth)
                        + Mathf.Abs(shelfRows - d.shelfRows) * 5f
                        + Mathf.Abs(shelvesPerRow - d.shelvesPerRow) * 2f;

            if (score < bestScore)
            {
                bestScore = score;
                best = p;
            }
        }

        return bestScore < 3f ? best : WarehousePreset.Custom;
    }

    /// <summary>
    /// 倉庫を生成するメイン関数 (外部からも呼べる)
    /// </summary>
    public void Generate()
    {
        if (!useExperimentConfig && environmentPreset != null)
            ApplyEnvironmentPreset(environmentPreset);

        // プリセットがCustom以外なら自動適用
        if (preset != WarehousePreset.Custom)
            ApplyPreset(preset);

        // 既存の倉庫を確実に破棄 (名前検索で漏れなく)
        CleanupExistingWarehouse();

        // シード設定
        if (seed != 0)
            Random.InitState(seed);
        else
            Random.InitState(System.DateTime.Now.Millisecond);

        // マテリアル自動生成
        EnsureMaterials();

        // 引っかかり防止用PhysicMaterial
        smoothPhysMat = new PhysicMaterial("SmoothWall");
        smoothPhysMat.dynamicFriction = 0.05f;
        smoothPhysMat.staticFriction  = 0.05f;
        smoothPhysMat.bounciness      = 0f;
        smoothPhysMat.frictionCombine  = PhysicMaterialCombine.Minimum;
        smoothPhysMat.bounceCombine    = PhysicMaterialCombine.Minimum;

        // 親オブジェクト階層を構築
        CreateHierarchy();

        // 各パーツを生成
        GenerateFloor();
        GenerateWalls();
        GenerateEntrancePlatforms();
        GeneratePillars();
        GenerateShelves();
        GenerateScatteredPallets();

        Debug.Log($"[WarehouseGenerator] 倉庫生成完了: {warehouseWidth}x{warehouseDepth}m");
        isGenerated = true;
    }

    // ==========================================
    //  マテリアル自動生成
    // ==========================================
    void EnsureMaterials()
    {
        if (floorMaterial == null)
            floorMaterial = MakeMat(new Color(0.65f, 0.65f, 0.63f)); // コンクリート床

        if (wallMaterial == null)
            wallMaterial = MakeMat(new Color(0.78f, 0.78f, 0.75f)); // 薄灰色壁

        if (shelfMaterial == null)
            shelfMaterial = MakeMat(new Color(0.30f, 0.45f, 0.65f)); // 青鉄骨

        if (crateMaterial == null)
            crateMaterial = MakeMat(new Color(0.72f, 0.55f, 0.30f)); // 段ボール色

        if (palletMaterial == null)
            palletMaterial = MakeMat(new Color(0.55f, 0.40f, 0.22f)); // 木製パレット

        if (pillarMaterial == null)
            pillarMaterial = MakeMat(new Color(0.60f, 0.60f, 0.58f)); // 柱
    }

    Material MakeMat(Color col)
    {
        Material m = new Material(Shader.Find("Standard"));
        m.color = col;
        return m;
    }

    // ==========================================
    //  階層構造
    // ==========================================
    void CreateHierarchy()
    {
        warehouseParent = new GameObject("Warehouse").transform;
        warehouseParent.SetParent(transform, false);

        floorParent  = CreateChild("Floor",   warehouseParent);
        wallParent   = CreateChild("Walls",   warehouseParent);
        shelfParent  = CreateChild("Shelves", warehouseParent);
        crateParent  = CreateChild("Crates",  warehouseParent);
        pillarParent = CreateChild("Pillars", warehouseParent);
    }

    Transform CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    // ==========================================
    //  床
    // ==========================================
    void GenerateFloor()
    {
        // 入口プラットフォーム分だけ床を拡張して継ぎ目をなくす
        float pd = entrancePlatformDepth > 0f ? entrancePlatformDepth : 0f;

        float minX = doorWest  ? -pd : 0f;
        float maxX = doorEast  ? warehouseWidth + pd : warehouseWidth;
        float minZ = doorSouth ? -pd : 0f;
        float maxZ = doorNorth ? warehouseDepth + pd : warehouseDepth;

        float totalW = maxX - minX;
        float totalD = maxZ - minZ;
        float centerX = (minX + maxX) / 2f;
        float centerZ = (minZ + maxZ) / 2f;
        float floorThick = 0.15f;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.SetParent(floorParent, false);
        floor.transform.localScale = new Vector3(totalW, floorThick, totalD);
        floor.transform.localPosition = new Vector3(centerX, -floorThick / 2f, centerZ);
        floor.GetComponent<Renderer>().material = floorMaterial;

        // 床にも低摩擦マテリアル
        var fc = floor.GetComponent<Collider>();
        if (fc != null && smoothPhysMat != null) fc.material = smoothPhysMat;

        GenerateFloorLines();
    }

    void GenerateFloorLines()
    {
        // 中央通路のライン
        float centerX = warehouseWidth / 2f;
        CreateFloorLine(new Vector3(centerX, 0.01f, warehouseDepth / 2f),
                        new Vector3(0.08f, 0.01f, warehouseDepth - 4f),
                        Color.yellow);

        // 横通路のライン
        CreateFloorLine(new Vector3(centerX, 0.01f, 2f),
                        new Vector3(warehouseWidth - 4f, 0.01f, 0.08f),
                        Color.yellow);
        CreateFloorLine(new Vector3(centerX, 0.01f, warehouseDepth - 2f),
                        new Vector3(warehouseWidth - 4f, 0.01f, 0.08f),
                        Color.yellow);
    }

    void CreateFloorLine(Vector3 pos, Vector3 scale, Color col)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = "FloorLine";
        line.transform.SetParent(floorParent, false);
        line.transform.localPosition = pos;
        line.transform.localScale = scale;
        line.GetComponent<Renderer>().material = MakeMat(col);
    }

    // ==========================================
    //  壁
    // ==========================================
    void GenerateWalls()
    {
        float hw = warehouseWidth;
        float hd = warehouseDepth;
        float wh = wallHeight;
        float thick = 0.2f;

        // --- 北壁 (Z = hd) ---
        if (doorNorth)
            CreateWallWithDoor("Wall_North", hw, hd, wh, thick, isXAxis: true, wallZ: hd);
        else
            CreateWall("Wall_North",
                new Vector3(hw / 2f, wh / 2f, hd),
                new Vector3(hw + thick, wh, thick));

        // --- 南壁 (Z = 0) ---
        if (doorSouth)
            CreateWallWithDoor("Wall_South", hw, hd, wh, thick, isXAxis: true, wallZ: 0f);
        else
            CreateWall("Wall_South",
                new Vector3(hw / 2f, wh / 2f, 0),
                new Vector3(hw + thick, wh, thick));

        // --- 東壁 (X = hw) ---
        if (doorEast)
            CreateWallWithDoor("Wall_East", hw, hd, wh, thick, isXAxis: false, wallZ: hw);
        else
            CreateWall("Wall_East",
                new Vector3(hw, wh / 2f, hd / 2f),
                new Vector3(thick, wh, hd + thick));

        // --- 西壁 (X = 0) ---
        if (doorWest)
            CreateWallWithDoor("Wall_West", hw, hd, wh, thick, isXAxis: false, wallZ: 0f);
        else
            CreateWall("Wall_West",
                new Vector3(0, wh / 2f, hd / 2f),
                new Vector3(thick, wh, hd + thick));
    }

    /// <summary>
    /// 入口付き壁を生成する汎用メソッド
    /// isXAxis=true : X軸方向に伸びる壁 (北壁/南壁)
    /// isXAxis=false: Z軸方向に伸びる壁 (東壁/西壁)
    /// </summary>
    void CreateWallWithDoor(string baseName, float hw, float hd, float wh, float thick,
                            bool isXAxis, float wallZ)
    {
        float wallLength = isXAxis ? hw : hd;
        float doorCenter = wallLength / 2f; // 入口は壁の中央

        float halfDoor = doorWidth / 2f;
        float leftLen = doorCenter - halfDoor;
        float rightLen = wallLength - (doorCenter + halfDoor);

        if (isXAxis)
        {
            // X軸方向の壁 (北/南) — 左セグメント
            if (leftLen > 0.1f)
                CreateWall(baseName + "_L",
                    new Vector3(leftLen / 2f, wh / 2f, wallZ),
                    new Vector3(leftLen, wh, thick));

            // 右セグメント
            if (rightLen > 0.1f)
                CreateWall(baseName + "_R",
                    new Vector3(hw - rightLen / 2f, wh / 2f, wallZ),
                    new Vector3(rightLen, wh, thick));

            // 入口上部
            float topH = wh - doorHeight;
            if (topH > 0.1f)
                CreateWall(baseName + "_Top",
                    new Vector3(doorCenter, doorHeight + topH / 2f, wallZ),
                    new Vector3(doorWidth, topH, thick));
        }
        else
        {
            // Z軸方向の壁 (東/西) — 左セグメント (Z小さい側)
            if (leftLen > 0.1f)
                CreateWall(baseName + "_L",
                    new Vector3(wallZ, wh / 2f, leftLen / 2f),
                    new Vector3(thick, wh, leftLen));

            // 右セグメント (Z大きい側)
            if (rightLen > 0.1f)
                CreateWall(baseName + "_R",
                    new Vector3(wallZ, wh / 2f, hd - rightLen / 2f),
                    new Vector3(thick, wh, rightLen));

            // 入口上部
            float topH = wh - doorHeight;
            if (topH > 0.1f)
                CreateWall(baseName + "_Top",
                    new Vector3(wallZ, doorHeight + topH / 2f, doorCenter),
                    new Vector3(thick, topH, doorWidth));
        }
    }

    // ==========================================
    //  入口の搬入エリア (外側プラットフォーム + ガードレール)
    // ==========================================
    void GenerateEntrancePlatforms()
    {
        float hw = warehouseWidth;
        float hd = warehouseDepth;
        float pd = entrancePlatformDepth;
        float floorThick = 0.15f;

        if (pd <= 0f) return;

        float platW = doorWidth; // ドアと同じ幅
        float guardH = 1.0f;          // ガードレールの高さ
        float guardThick = 0.15f;     // ガードレールの厚み

        // --- 西壁の入口 ---
        if (doorWest)
        {
            CreateEntrancePlatform("Platform_West",
                new Vector3(-pd / 2f, -floorThick / 2f, hd / 2f),
                new Vector3(pd, floorThick, platW));

            // ガードレール: 外側 (X=-pd)
            CreateGuardrail("Guard_West_Outer",
                new Vector3(-pd, guardH / 2f, hd / 2f),
                new Vector3(guardThick, guardH, platW));
            // ガードレール: 手前 (Z小さい側)
            CreateGuardrail("Guard_West_Near",
                new Vector3(-pd / 2f, guardH / 2f, hd / 2f - platW / 2f),
                new Vector3(pd, guardH, guardThick));
            // ガードレール: 奥 (Z大きい側)
            CreateGuardrail("Guard_West_Far",
                new Vector3(-pd / 2f, guardH / 2f, hd / 2f + platW / 2f),
                new Vector3(pd, guardH, guardThick));
        }

        // --- 東壁の入口 ---
        if (doorEast)
        {
            CreateEntrancePlatform("Platform_East",
                new Vector3(hw + pd / 2f, -floorThick / 2f, hd / 2f),
                new Vector3(pd, floorThick, platW));

            CreateGuardrail("Guard_East_Outer",
                new Vector3(hw + pd, guardH / 2f, hd / 2f),
                new Vector3(guardThick, guardH, platW));
            CreateGuardrail("Guard_East_Near",
                new Vector3(hw + pd / 2f, guardH / 2f, hd / 2f - platW / 2f),
                new Vector3(pd, guardH, guardThick));
            CreateGuardrail("Guard_East_Far",
                new Vector3(hw + pd / 2f, guardH / 2f, hd / 2f + platW / 2f),
                new Vector3(pd, guardH, guardThick));
        }

        // --- 南壁の入口 ---
        if (doorSouth)
        {
            CreateEntrancePlatform("Platform_South",
                new Vector3(hw / 2f, -floorThick / 2f, -pd / 2f),
                new Vector3(platW, floorThick, pd));

            CreateGuardrail("Guard_South_Outer",
                new Vector3(hw / 2f, guardH / 2f, -pd),
                new Vector3(platW, guardH, guardThick));
            CreateGuardrail("Guard_South_Left",
                new Vector3(hw / 2f - platW / 2f, guardH / 2f, -pd / 2f),
                new Vector3(guardThick, guardH, pd));
            CreateGuardrail("Guard_South_Right",
                new Vector3(hw / 2f + platW / 2f, guardH / 2f, -pd / 2f),
                new Vector3(guardThick, guardH, pd));
        }

        // --- 北壁の入口 ---
        if (doorNorth)
        {
            CreateEntrancePlatform("Platform_North",
                new Vector3(hw / 2f, -floorThick / 2f, hd + pd / 2f),
                new Vector3(platW, floorThick, pd));

            CreateGuardrail("Guard_North_Outer",
                new Vector3(hw / 2f, guardH / 2f, hd + pd),
                new Vector3(platW, guardH, guardThick));
            CreateGuardrail("Guard_North_Left",
                new Vector3(hw / 2f - platW / 2f, guardH / 2f, hd + pd / 2f),
                new Vector3(guardThick, guardH, pd));
            CreateGuardrail("Guard_North_Right",
                new Vector3(hw / 2f + platW / 2f, guardH / 2f, hd + pd / 2f),
                new Vector3(guardThick, guardH, pd));
        }
    }

    void CreateEntrancePlatform(string name, Vector3 pos, Vector3 scale)
    {
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = name;
        platform.transform.SetParent(floorParent, false);
        // 床の上に薄く乗せる (視覚表示用のみ)
        platform.transform.localPosition = new Vector3(pos.x, 0.005f, pos.z);
        platform.transform.localScale = new Vector3(scale.x, 0.01f, scale.z);

        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.55f, 0.55f, 0.53f);
        platform.GetComponent<Renderer>().material = mat;

        // Collider削除 (下の拡張床がカバーするので不要)
        SafeDestroy(platform.GetComponent<Collider>());
    }

    void CreateGuardrail(string name, Vector3 pos, Vector3 scale)
    {
        var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rail.name = name;
        rail.transform.SetParent(wallParent, false);
        rail.transform.localPosition = pos;
        rail.transform.localScale = scale;

        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.9f, 0.75f, 0.1f);
        rail.GetComponent<Renderer>().material = mat;

        var col = rail.GetComponent<Collider>();
        if (col != null && smoothPhysMat != null) col.material = smoothPhysMat;
    }

    void CreateWall(string name, Vector3 pos, Vector3 scale)
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(wallParent, false);
        wall.transform.localPosition = pos;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material = wallMaterial;

        // 引っかかり防止
        var col = wall.GetComponent<Collider>();
        if (col != null && smoothPhysMat != null) col.material = smoothPhysMat;
    }

    // ==========================================
    //  柱
    // ==========================================
    void GeneratePillars()
    {
        if (!generatePillars) return;
        if (pillarSpacingX <= 0f || pillarSpacingZ <= 0f) return;

        // 棚のBoundsを事前収集 (重なり回避用)
        List<Bounds> shelfBounds = new List<Bounds>();
        if (pillarAvoidShelves && shelfParent != null)
        {
            foreach (var col in shelfParent.GetComponentsInChildren<Collider>())
            {
                Bounds b = col.bounds;
                // クリアランス分だけ拡張
                b.Expand(pillarShelfClearance * 2f);
                shelfBounds.Add(b);
            }
        }

        for (float x = pillarWallMargin; x < warehouseWidth - pillarWallMargin; x += pillarSpacingX)
        {
            for (float z = pillarWallMargin; z < warehouseDepth - pillarWallMargin; z += pillarSpacingZ)
            {
                // 棚との重なりチェック
                if (pillarAvoidShelves && IsOverlappingShelf(x, z, shelfBounds))
                    continue;

                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = "Pillar";
                pillar.transform.SetParent(pillarParent, false);
                pillar.transform.localPosition = new Vector3(x, wallHeight / 2f, z);
                pillar.transform.localScale = new Vector3(pillarSize, wallHeight, pillarSize);
                pillar.GetComponent<Renderer>().material = pillarMaterial;

                var pc = pillar.GetComponent<Collider>();
                if (pc != null && smoothPhysMat != null) pc.material = smoothPhysMat;
            }
        }
    }

    bool IsOverlappingShelf(float x, float z, List<Bounds> shelfBounds)
    {
        Vector3 pillarPos = transform.TransformPoint(new Vector3(x, wallHeight / 2f, z));
        Vector3 pillarHalf = new Vector3(pillarSize / 2f, wallHeight / 2f, pillarSize / 2f);
        Bounds pillarBound = new Bounds(pillarPos, pillarHalf * 2f);

        for (int i = 0; i < shelfBounds.Count; i++)
        {
            if (pillarBound.Intersects(shelfBounds[i]))
                return true;
        }
        return false;
    }

    // ==========================================
    //  棚 (ラック)
    // ==========================================
    void GenerateShelves()
    {
        // 棚エリアの開始位置を計算
        float blockWidth;
        if (shelfPaired)
        {
            // 背中合わせ: [depth + spacing + depth] + aisleWidth
            blockWidth = shelfDepth * 2 + shelfSpacing;
        }
        else
        {
            // 独立: [depth] のみ
            blockWidth = shelfDepth;
        }

        float totalShelfBlockWidth = shelfRows * blockWidth + (shelfRows - 1) * aisleWidth;
        float startX = (warehouseWidth - totalShelfBlockWidth) / 2f;
        float startZ = 4f;

        float currentX = startX;

        for (int row = 0; row < shelfRows; row++)
        {
            int sidesPerRow = shelfPaired ? 2 : 1;

            for (int side = 0; side < sidesPerRow; side++)
            {
                float x;
                if (shelfPaired)
                    x = currentX + side * (shelfDepth + shelfSpacing);
                else
                    x = currentX;

                for (int i = 0; i < shelvesPerRow; i++)
                {
                    float z = startZ + i * (shelfWidth + shelfLateralGap);

                    if (z + shelfWidth > warehouseDepth - 4f) break;

                    CreateShelfUnit(x, z, side == 1, row, i, side);
                }
            }

            currentX += blockWidth + aisleWidth;
        }
    }

    void CreateShelfUnit(float x, float z, bool flipped, int row, int col, int side)
    {
        var shelfGo = new GameObject("ShelfUnit");
        shelfGo.transform.SetParent(shelfParent, false);
        shelfGo.transform.localPosition = new Vector3(x, 0, z);

        // ===== ShelfUnit スクリプトをアタッチ =====
        var shelfScript = shelfGo.AddComponent<ShelfUnit>();
        shelfScript.shelfID = $"R{row}-C{col}-S{side}";
        shelfScript.rowIndex = row;
        shelfScript.columnIndex = col;
        shelfScript.sideIndex = side;
        shelfScript.width = shelfWidth;
        shelfScript.height = shelfHeight;
        shelfScript.depth = shelfDepth;
        shelfScript.levelCount = 3;

        // ===== 棚全体を覆うBoxCollider =====
        if (addShelfCollider)
        {
            var bc = shelfGo.AddComponent<BoxCollider>();
            bc.center = new Vector3(shelfDepth / 2f, shelfHeight / 2f, shelfWidth / 2f);
            bc.size = new Vector3(shelfDepth, shelfHeight, shelfWidth);

            // 引っかかり防止
            if (smoothPhysMat != null) bc.material = smoothPhysMat;

            // レイヤー設定
            if (!string.IsNullOrEmpty(shelfColliderLayer))
            {
                int layerIdx = LayerMask.NameToLayer(shelfColliderLayer);
                if (layerIdx >= 0) shelfGo.layer = layerIdx;
                else Debug.LogWarning($"[WarehouseGenerator] レイヤー '{shelfColliderLayer}' が見つかりません");
            }

            // タグ設定
            if (!string.IsNullOrEmpty(shelfColliderTag) && shelfColliderTag != "Untagged")
            {
                try { shelfGo.tag = shelfColliderTag; }
                catch { Debug.LogWarning($"[WarehouseGenerator] タグ '{shelfColliderTag}' が未登録です"); }
            }
        }

        int numLevels = 3;
        float levelHeight = shelfHeight / numLevels;

        // 支柱 (4本)
        float postThickness = 0.06f;
        Vector3[] postOffsets = new Vector3[]
        {
            new Vector3(0, 0, 0),
            new Vector3(shelfDepth - postThickness, 0, 0),
            new Vector3(0, 0, shelfWidth - postThickness),
            new Vector3(shelfDepth - postThickness, 0, shelfWidth - postThickness),
        };

        foreach (var offset in postOffsets)
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            post.transform.SetParent(shelfGo.transform, false);
            post.transform.localPosition = offset + new Vector3(postThickness / 2f, shelfHeight / 2f, postThickness / 2f);
            post.transform.localScale = new Vector3(postThickness, shelfHeight, postThickness);
            post.GetComponent<Renderer>().material = shelfMaterial;
        }

        // 各段の棚板 + 荷物
        for (int level = 0; level < numLevels; level++)
        {
            float y = level * levelHeight;
            float boardThickness = 0.05f;

            // 棚板
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = $"Board_L{level}";
            board.transform.SetParent(shelfGo.transform, false);
            board.transform.localPosition = new Vector3(shelfDepth / 2f, y + boardThickness / 2f, shelfWidth / 2f);
            board.transform.localScale = new Vector3(shelfDepth, boardThickness, shelfWidth);
            board.GetComponent<Renderer>().material = shelfMaterial;

            // 荷物をランダム配置
            if (Random.value < crateSpawnChance)
            {
                SpawnCrateOnShelf(shelfGo.transform, y + boardThickness, level);
            }
        }

        // 最上段の棚板
        var topBoard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        topBoard.name = "Board_Top";
        topBoard.transform.SetParent(shelfGo.transform, false);
        topBoard.transform.localPosition = new Vector3(shelfDepth / 2f, shelfHeight, shelfWidth / 2f);
        topBoard.transform.localScale = new Vector3(shelfDepth, 0.05f, shelfWidth);
        topBoard.GetComponent<Renderer>().material = shelfMaterial;

        // ===== 子オブジェクトの個別コライダーを削除 (親の1つだけにする) =====
        if (addShelfCollider)
        {
            foreach (var childCol in shelfGo.GetComponentsInChildren<Collider>())
            {
                // 親に付けたBoxColliderは残す
                if (childCol.gameObject == shelfGo) continue;
                SafeDestroy(childCol);
            }
        }
    }

    // ==========================================
    //  荷物 (箱)
    // ==========================================
    void SpawnCrateOnShelf(Transform parent, float baseY, int level)
    {
        int numCrates = Random.Range(1, 4);

        for (int i = 0; i < numCrates; i++)
        {
            float crateW = Random.Range(0.3f, 0.7f);
            float crateH = Random.Range(0.25f, 0.7f);
            float crateD = Random.Range(0.3f, 0.6f);

            float cx = Random.Range(crateD / 2f + 0.05f, shelfDepth - crateD / 2f - 0.05f);
            float cz = Random.Range(crateW / 2f + 0.05f, shelfWidth - crateW / 2f - 0.05f);

            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "Crate";
            crate.transform.SetParent(parent, false);
            crate.transform.localPosition = new Vector3(cx, baseY + crateH / 2f, cz);
            crate.transform.localScale = new Vector3(crateD, crateH, crateW);

            // 色をランダムに少し変える
            Color baseCol = crateMaterial.color;
            Color variation = new Color(
                baseCol.r + Random.Range(-0.08f, 0.08f),
                baseCol.g + Random.Range(-0.08f, 0.08f),
                baseCol.b + Random.Range(-0.08f, 0.08f)
            );
            crate.GetComponent<Renderer>().material = MakeMat(variation);
        }
    }

    // ==========================================
    //  通路上のパレット
    // ==========================================
    void GenerateScatteredPallets()
    {
        float margin = 3f;

        for (float x = margin; x < warehouseWidth - margin; x += 2.5f)
        {
            for (float z = margin; z < warehouseDepth - margin; z += 2.5f)
            {
                if (Random.value > palletSpawnChance) continue;

                // 棚と重ならないかチェック (簡易)
                if (IsInsideShelfArea(x, z)) continue;

                CreatePallet(new Vector3(x, 0f, z));
            }
        }
    }

    bool IsInsideShelfArea(float x, float z)
    {
        float totalShelfBlockWidth = shelfRows * (shelfDepth * 2 + shelfSpacing) + (shelfRows - 1) * aisleWidth;
        float startX = (warehouseWidth - totalShelfBlockWidth) / 2f;
        float startZ = 4f;
        float endZ = startZ + shelvesPerRow * (shelfWidth + 0.3f);

        float currentX = startX;
        for (int row = 0; row < shelfRows; row++)
        {
            float rowStart = currentX - 0.3f;
            float rowEnd = currentX + shelfDepth * 2 + shelfSpacing + 0.3f;

            if (x > rowStart && x < rowEnd && z > startZ - 0.5f && z < endZ + 0.5f)
                return true;

            currentX += shelfDepth * 2 + shelfSpacing + aisleWidth;
        }
        return false;
    }

    void CreatePallet(Vector3 pos)
    {
        var palletGo = new GameObject("Pallet");
        palletGo.transform.SetParent(crateParent, false);
        palletGo.transform.localPosition = pos;

        // パレット土台
        float palletW = 1.2f, palletD = 1.0f, palletH = 0.12f;
        var pBase = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pBase.name = "PalletBase";
        pBase.transform.SetParent(palletGo.transform, false);
        pBase.transform.localPosition = new Vector3(0, palletH / 2f, 0);
        pBase.transform.localScale = new Vector3(palletD, palletH, palletW);
        pBase.GetComponent<Renderer>().material = palletMaterial;

        // パレット上の荷物
        int stackCount = Random.Range(1, 4);
        float currentY = palletH;

        for (int i = 0; i < stackCount; i++)
        {
            float bw = Random.Range(0.4f, palletW - 0.1f);
            float bd = Random.Range(0.4f, palletD - 0.1f);
            float bh = Random.Range(0.3f, 0.6f);

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "PalletBox";
            box.transform.SetParent(palletGo.transform, false);
            box.transform.localPosition = new Vector3(
                Random.Range(-0.1f, 0.1f),
                currentY + bh / 2f,
                Random.Range(-0.1f, 0.1f));
            box.transform.localScale = new Vector3(bd, bh, bw);

            Color baseCol = crateMaterial.color;
            box.GetComponent<Renderer>().material = MakeMat(new Color(
                baseCol.r + Random.Range(-0.1f, 0.1f),
                baseCol.g + Random.Range(-0.1f, 0.1f),
                baseCol.b + Random.Range(-0.1f, 0.1f)));

            currentY += bh;
        }
    }

    // ==========================================
    //  エディタ用: ギズモで範囲を表示
    // ==========================================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Vector3 localCenter = new Vector3(warehouseWidth / 2f, wallHeight / 2f, warehouseDepth / 2f);
        Vector3 worldCenter = transform.TransformPoint(localCenter);
        Vector3 size = new Vector3(warehouseWidth, wallHeight, warehouseDepth);
        Gizmos.DrawWireCube(worldCenter, size);
    }

    // ==========================================
    //  エディタ / ランタイム両対応の破棄
    // ==========================================
    void SafeDestroy(Object obj)
    {
        if (obj == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(obj);
            return;
        }
#endif
        Destroy(obj);
    }
}

#if UNITY_EDITOR
// ==========================================
//  カスタムエディタ (Inspectorにプリセット選択・ボタン追加)
// ==========================================
[UnityEditor.CustomEditor(typeof(WarehouseGenerator))]
public class WarehouseGeneratorEditor : UnityEditor.Editor
{
    private bool showPresetDetails = false;

    public override void OnInspectorGUI()
    {
        var generator = (WarehouseGenerator)target;
        serializedObject.Update();

        UnityEditor.EditorGUILayout.Space(5);
        UnityEditor.EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Environment Preset", UnityEditor.EditorStyles.boldLabel);
        UnityEditor.EditorGUI.BeginChangeCheck();
        var newEnvironmentPreset = (WarehouseEnvironmentPreset)UnityEditor.EditorGUILayout.ObjectField(
            "Environment Preset", generator.environmentPreset, typeof(WarehouseEnvironmentPreset), false);
        if (UnityEditor.EditorGUI.EndChangeCheck())
        {
            UnityEditor.Undo.RecordObject(generator, "Change Warehouse Environment Preset");
            generator.environmentPreset = newEnvironmentPreset;
            if (newEnvironmentPreset != null)
                generator.ApplyEnvironmentPreset(newEnvironmentPreset);
            UnityEditor.EditorUtility.SetDirty(generator);
        }

        GUI.enabled = generator.environmentPreset != null;
        if (GUILayout.Button("Apply Environment Preset", GUILayout.Height(24)))
        {
            UnityEditor.Undo.RecordObject(generator, "Apply Warehouse Environment Preset");
            generator.ApplyEnvironmentPreset();
            UnityEditor.EditorUtility.SetDirty(generator);
        }
        GUI.enabled = true;
        UnityEditor.EditorGUILayout.HelpBox(
            "A linked asset is reapplied when the environment is generated. Clear it to use the raw Inspector values manually.",
            UnityEditor.MessageType.None);
        UnityEditor.EditorGUILayout.EndVertical();

        // ==========================================
        //  プリセット選択エリア
        // ==========================================
        UnityEditor.EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.85f, 0.95f, 1f);
        UnityEditor.EditorGUILayout.BeginVertical("box");
        GUI.backgroundColor = Color.white;

        GUILayout.Label("基準値プリセット", UnityEditor.EditorStyles.boldLabel);

        UnityEditor.EditorGUI.BeginChangeCheck();
        var newPreset = (WarehousePreset)UnityEditor.EditorGUILayout.EnumPopup("プリセット選択", generator.preset);
        if (UnityEditor.EditorGUI.EndChangeCheck())
        {
            UnityEditor.Undo.RecordObject(generator, "Change Warehouse Preset");
            generator.preset = newPreset;
        }

        // プリセット説明
        string desc = GetPresetDescription(generator.preset);
        if (!string.IsNullOrEmpty(desc))
        {
            UnityEditor.EditorGUILayout.HelpBox(desc, UnityEditor.MessageType.Info);
        }

        // プリセット適用ボタン
        UnityEditor.EditorGUILayout.BeginHorizontal();

        GUI.enabled = generator.preset != WarehousePreset.Custom;
        if (GUILayout.Button("プリセットを適用", GUILayout.Height(28)))
        {
            UnityEditor.Undo.RecordObject(generator, "Apply Warehouse Preset");
            generator.ApplyPreset();
            UnityEditor.EditorUtility.SetDirty(generator);
        }
        GUI.enabled = true;

        if (GUILayout.Button("現在値をCustomに", GUILayout.Height(28), GUILayout.Width(130)))
        {
            UnityEditor.Undo.RecordObject(generator, "Set Custom Preset");
            generator.preset = WarehousePreset.Custom;
            UnityEditor.EditorUtility.SetDirty(generator);
        }

        UnityEditor.EditorGUILayout.EndHorizontal();

        // プリセット詳細 (折り畳み)
        showPresetDetails = UnityEditor.EditorGUILayout.Foldout(showPresetDetails, "全プリセット一覧");
        if (showPresetDetails)
        {
            DrawPresetTable();
        }

        UnityEditor.EditorGUILayout.EndVertical();

        // ==========================================
        //  デフォルトInspector
        // ==========================================
        UnityEditor.EditorGUILayout.Space(8);
        DrawPropertiesExcluding(serializedObject, "m_Script", "preset", "environmentPreset");
        serializedObject.ApplyModifiedProperties();

        // ==========================================
        //  生成ボタンエリア
        // ==========================================
        UnityEditor.EditorGUILayout.Space(10);

        GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
        if (GUILayout.Button("倉庫を生成", GUILayout.Height(35)))
        {
            generator.Generate();
        }
        GUI.backgroundColor = Color.white;

        GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
        if (GUILayout.Button("倉庫を削除", GUILayout.Height(25)))
        {
            var existing = generator.transform.Find("Warehouse");
            if (existing != null)
                DestroyImmediate(existing.gameObject);
        }
        GUI.backgroundColor = Color.white;

        // プリセット自動検出
        UnityEditor.EditorGUILayout.Space(5);
        if (GUILayout.Button("現在値に近いプリセットを検出"))
        {
            var detected = generator.DetectClosestPreset();
            string msg = detected == WarehousePreset.Custom
                ? "現在の設定はどのプリセットとも一致しません (Custom)"
                : $"最も近いプリセット: {detected}";
            UnityEditor.EditorUtility.DisplayDialog("プリセット検出", msg, "OK");
        }
    }

    // ==========================================
    //  プリセット説明文
    // ==========================================
    string GetPresetDescription(WarehousePreset p)
    {
        switch (p)
        {
            case WarehousePreset.Custom:   return "手動でパラメータを設定します。プリセットは適用されません。";
            case WarehousePreset.Tiny:     return "極小 (15x15m) — テスト・デバッグ用。棚2列、入口1つ。";
            case WarehousePreset.Small:    return "小型 (20x25m) — 小規模倉庫。棚2列x5個、入口1つ。";
            case WarehousePreset.Medium:   return "中型 (30x40m) — 標準的な倉庫。棚4列x6個、入口2つ。";
            case WarehousePreset.Large:    return "大型 (50x70m) — 大規模物流倉庫。棚6列x10個、入口3つ。";
            case WarehousePreset.Huge:     return "超大型 (80x100m) — 大型配送センター。棚10列x14個、入口4つ。";
            case WarehousePreset.Narrow:   return "細長 (15x60m) — 通路型倉庫。棚2列x12個、南北に入口。";
            case WarehousePreset.OpenYard: return "広場 (40x40m) — 棚なしの広いヤード。パレット多め、入口4つ。";
            default: return "";
        }
    }

    // ==========================================
    //  プリセット比較テーブル
    // ==========================================
    void DrawPresetTable()
    {
        UnityEditor.EditorGUI.indentLevel++;

        var presets = new WarehousePreset[]
        {
            WarehousePreset.Tiny, WarehousePreset.Small, WarehousePreset.Medium,
            WarehousePreset.Large, WarehousePreset.Huge, WarehousePreset.Narrow,
            WarehousePreset.OpenYard
        };

        foreach (var p in presets)
        {
            var d = WarehousePresetData.Get(p);
            if (d == null) continue;

            string line = $"{p}: {d.warehouseWidth}x{d.warehouseDepth}m, 壁{d.wallHeight}m, " +
                          $"棚{d.shelfRows}列x{d.shelvesPerRow}個, 通路{d.aisleWidth}m";
            UnityEditor.EditorGUILayout.LabelField(line, UnityEditor.EditorStyles.miniLabel);
        }

        UnityEditor.EditorGUI.indentLevel--;
    }
}
#endif
