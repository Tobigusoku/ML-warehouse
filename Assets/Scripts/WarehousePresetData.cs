using UnityEngine;

// ==========================================
//  倉庫プリセット定義
// ==========================================

/// <summary>
/// 倉庫の基準値プリセット一覧
/// </summary>
public enum WarehousePreset
{
    Custom,         // カスタム (手動設定)
    Tiny,           // 極小 — テスト・デバッグ用
    Small,          // 小型 — 小規模倉庫
    Medium,         // 中型 — 標準倉庫
    Large,          // 大型 — 大規模物流倉庫
    Huge,           // 超大型 — 大型配送センター
    Narrow,         // 細長 — 通路型倉庫
    OpenYard,       // 広場 — 棚なし広いヤード
}

/// <summary>
/// プリセットの全パラメータを保持するデータクラス
/// </summary>
public class WarehousePresetData
{
    // 倉庫サイズ
    public int   warehouseWidth;
    public int   warehouseDepth;
    public float wallHeight;

    // 棚
    public int   shelfRows;
    public int   shelvesPerRow;
    public float shelfWidth;
    public float shelfHeight;
    public float shelfDepth;
    public float aisleWidth;
    public float shelfSpacing;
    public float shelfLateralGap;
    public bool  shelfPaired;

    // 荷物
    public float crateSpawnChance;
    public float palletSpawnChance;

    // 入口
    public float doorWidth;
    public float doorHeight;
    public bool  doorWest;
    public bool  doorEast;
    public bool  doorSouth;
    public bool  doorNorth;

    // 柱
    public bool  generatePillars;
    public float pillarSize;
    public float pillarSpacingX;
    public float pillarSpacingZ;
    public float pillarWallMargin;
    public bool  pillarAvoidShelves;
    public float pillarShelfClearance;

    // ==========================================
    //  プリセット定義 (静的メソッド)
    // ==========================================
    public static WarehousePresetData Get(WarehousePreset preset)
    {
        switch (preset)
        {
            case WarehousePreset.Tiny:
                return new WarehousePresetData
                {
                    warehouseWidth = 15, warehouseDepth = 15, wallHeight = 4f,
                    shelfRows = 2, shelvesPerRow = 3,
                    shelfWidth = 1.8f, shelfHeight = 2.5f, shelfDepth = 1.0f,
                    aisleWidth = 2.5f, shelfSpacing = 0.8f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.5f, palletSpawnChance = 0.05f,
                    doorWidth = 4f, doorHeight = 3f,
                    doorWest = true, doorEast = false, doorSouth = false, doorNorth = false,
                    generatePillars = false, pillarSize = 0.3f,
                    pillarSpacingX = 7f, pillarSpacingZ = 7f,
                    pillarWallMargin = 2f, pillarAvoidShelves = true, pillarShelfClearance = 0.5f,
                };

            case WarehousePreset.Small:
                return new WarehousePresetData
                {
                    warehouseWidth = 20, warehouseDepth = 25, wallHeight = 5f,
                    shelfRows = 2, shelvesPerRow = 5,
                    shelfWidth = 2f, shelfHeight = 3f, shelfDepth = 1.2f,
                    aisleWidth = 3f, shelfSpacing = 1f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.6f, palletSpawnChance = 0.1f,
                    doorWidth = 5f, doorHeight = 3.5f,
                    doorWest = true, doorEast = false, doorSouth = false, doorNorth = false,
                    generatePillars = true, pillarSize = 0.35f,
                    pillarSpacingX = 10f, pillarSpacingZ = 12f,
                    pillarWallMargin = 2f, pillarAvoidShelves = true, pillarShelfClearance = 0.5f,
                };

            case WarehousePreset.Medium:
                return new WarehousePresetData
                {
                    warehouseWidth = 30, warehouseDepth = 40, wallHeight = 5f,
                    shelfRows = 4, shelvesPerRow = 6,
                    shelfWidth = 2f, shelfHeight = 3f, shelfDepth = 1.2f,
                    aisleWidth = 3f, shelfSpacing = 1f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.7f, palletSpawnChance = 0.15f,
                    doorWidth = 5f, doorHeight = 3.5f,
                    doorWest = true, doorEast = true, doorSouth = false, doorNorth = false,
                    generatePillars = true, pillarSize = 0.4f,
                    pillarSpacingX = 8f, pillarSpacingZ = 10f,
                    pillarWallMargin = 2f, pillarAvoidShelves = true, pillarShelfClearance = 0.5f,
                };

            case WarehousePreset.Large:
                return new WarehousePresetData
                {
                    warehouseWidth = 50, warehouseDepth = 70, wallHeight = 7f,
                    shelfRows = 6, shelvesPerRow = 10,
                    shelfWidth = 2.5f, shelfHeight = 4f, shelfDepth = 1.4f,
                    aisleWidth = 3.5f, shelfSpacing = 1.2f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.75f, palletSpawnChance = 0.15f,
                    doorWidth = 6f, doorHeight = 4f,
                    doorWest = true, doorEast = true, doorSouth = true, doorNorth = false,
                    generatePillars = true, pillarSize = 0.5f,
                    pillarSpacingX = 10f, pillarSpacingZ = 10f,
                    pillarWallMargin = 3f, pillarAvoidShelves = true, pillarShelfClearance = 0.6f,
                };

            case WarehousePreset.Huge:
                return new WarehousePresetData
                {
                    warehouseWidth = 80, warehouseDepth = 100, wallHeight = 8f,
                    shelfRows = 10, shelvesPerRow = 14,
                    shelfWidth = 2.5f, shelfHeight = 5f, shelfDepth = 1.5f,
                    aisleWidth = 4f, shelfSpacing = 1.2f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.8f, palletSpawnChance = 0.2f,
                    doorWidth = 7f, doorHeight = 4.5f,
                    doorWest = true, doorEast = true, doorSouth = true, doorNorth = true,
                    generatePillars = true, pillarSize = 0.6f,
                    pillarSpacingX = 10f, pillarSpacingZ = 10f,
                    pillarWallMargin = 3f, pillarAvoidShelves = true, pillarShelfClearance = 0.7f,
                };

            case WarehousePreset.Narrow:
                return new WarehousePresetData
                {
                    warehouseWidth = 15, warehouseDepth = 60, wallHeight = 5f,
                    shelfRows = 2, shelvesPerRow = 12,
                    shelfWidth = 2f, shelfHeight = 3.5f, shelfDepth = 1.2f,
                    aisleWidth = 2.5f, shelfSpacing = 0.8f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0.7f, palletSpawnChance = 0.1f,
                    doorWidth = 4f, doorHeight = 3.5f,
                    doorWest = false, doorEast = false, doorSouth = true, doorNorth = true,
                    generatePillars = true, pillarSize = 0.35f,
                    pillarSpacingX = 7f, pillarSpacingZ = 10f,
                    pillarWallMargin = 2f, pillarAvoidShelves = true, pillarShelfClearance = 0.5f,
                };

            case WarehousePreset.OpenYard:
                return new WarehousePresetData
                {
                    warehouseWidth = 40, warehouseDepth = 40, wallHeight = 4f,
                    shelfRows = 0, shelvesPerRow = 0,
                    shelfWidth = 2f, shelfHeight = 3f, shelfDepth = 1.2f,
                    aisleWidth = 3f, shelfSpacing = 1f, shelfLateralGap = 0.3f, shelfPaired = true,
                    crateSpawnChance = 0f, palletSpawnChance = 0.3f,
                    doorWidth = 8f, doorHeight = 3.5f,
                    doorWest = true, doorEast = true, doorSouth = true, doorNorth = true,
                    generatePillars = false, pillarSize = 0.4f,
                    pillarSpacingX = 10f, pillarSpacingZ = 10f,
                    pillarWallMargin = 2f, pillarAvoidShelves = true, pillarShelfClearance = 0.5f,
                };

            default: // Custom
                return null;
        }
    }
}