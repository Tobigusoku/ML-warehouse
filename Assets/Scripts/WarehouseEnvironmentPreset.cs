using UnityEngine;

/// <summary>
/// Versioned warehouse layout and generation settings shared by training and inference.
/// This asset contains environment conditions only; pheromone and reward settings live
/// in WarehouseExperimentConfig.
/// </summary>
[CreateAssetMenu(fileName = "WarehouseEnvironmentPreset", menuName = "Warehouse/Environment Preset")]
public class WarehouseEnvironmentPreset : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("倉庫レイアウトを識別するID。形状や生成条件を変える場合は新しいIDとアセットを作成します。")]
    public string presetId = "environment_v1";

    [Header("Warehouse size")]
    [Tooltip("倉庫の横幅（X方向、m）。")]
    public int warehouseWidth = 20;
    [Tooltip("倉庫の奥行き（Z方向、m）。")]
    public int warehouseDepth = 55;
    [Tooltip("倉庫を囲う壁の高さ（m）。")]
    public float wallHeight = 5f;

    [Header("Shelves")]
    [Tooltip("棚の列数。Pairedが有効なら各列に背中合わせの棚を2つずつ配置します。")]
    public int shelfRows = 1;
    [Tooltip("1列あたりに並べる棚の数。倉庫の奥行きを超える棚は生成されません。")]
    public int shelvesPerRow = 3;
    [Tooltip("棚1台の幅（Z方向、m）。")]
    public float shelfWidth = 8f;
    [Tooltip("棚1台の高さ（m）。")]
    public float shelfHeight = 3f;
    [Tooltip("棚1台の奥行き（X方向、m）。")]
    public float shelfDepth = 1.2f;
    [Tooltip("棚の列間の通路幅（m）。")]
    public float aisleWidth = 15f;
    [Tooltip("背中合わせにした棚2台の間隔（m）。Pairedが無効なら配置には使われません。")]
    public float shelfSpacing = 1f;
    [Tooltip("同じ列に並ぶ棚同士のZ方向の隙間（m）。")]
    public float shelfLateralGap = 10f;
    [Tooltip("棚を背中合わせの2列セットにするか。オフなら各列に1台ずつ配置します。")]
    public bool shelfPaired = false;

    [Header("Obstacles")]
    [Tooltip("棚の各段に荷物箱を生成する確率（0～1）。")]
    [Range(0f, 1f)] public float crateSpawnChance = 0f;
    [Tooltip("床の候補位置ごとにパレットを生成する確率（0～1）。")]
    [Range(0f, 1f)] public float palletSpawnChance = 0f;

    [Header("Gates")]
    [Tooltip("各ゲートの幅（m）。帰還ゴールとスポーン位置の幅にも使われます。")]
    public float doorWidth = 20f;
    [Tooltip("各ゲートの開口部の高さ（m）。")]
    public float doorHeight = 3.5f;
    [Tooltip("倉庫の西側にゲートを作るか。")]
    public bool doorWest = false;
    [Tooltip("倉庫の東側にゲートを作るか。")]
    public bool doorEast = false;
    [Tooltip("倉庫の南側にゲートを作るか。")]
    public bool doorSouth = true;
    [Tooltip("倉庫の北側にゲートを作るか。")]
    public bool doorNorth = true;
    [Tooltip("ゲート外側のプラットフォームの奥行き（m）。")]
    public float entrancePlatformDepth = 5f;

    [Header("Pillars")]
    [Tooltip("倉庫内に柱を生成するか。")]
    public bool generatePillars = false;
    [Tooltip("柱の太さ（X/Z方向、m）。")]
    public float pillarSize = 0.4f;
    [Tooltip("柱を並べるX方向の間隔（m）。")]
    public float pillarSpacingX = 8f;
    [Tooltip("柱を並べるZ方向の間隔（m）。")]
    public float pillarSpacingZ = 8f;
    [Tooltip("柱の配置を壁から離す距離（m）。")]
    public float pillarWallMargin = 2f;
    [Tooltip("棚と重なる位置に柱を生成しないか。")]
    public bool pillarAvoidShelves = true;
    [Tooltip("棚の周囲で柱を避ける余裕（m）。")]
    public float pillarShelfClearance = 0.5f;

    [Header("Randomness")]
    [Tooltip("倉庫生成に使うUnityの乱数シード。0=毎回変動、0以外=固定。Trainerのseedとは別です。")]
    public int environmentSeed = 0;
}
