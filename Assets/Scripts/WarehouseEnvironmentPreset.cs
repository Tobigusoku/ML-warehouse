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
    public string presetId = "environment_v1";

    [Header("Warehouse size")]
    public int warehouseWidth = 20;
    public int warehouseDepth = 55;
    public float wallHeight = 5f;

    [Header("Shelves")]
    public int shelfRows = 1;
    public int shelvesPerRow = 3;
    public float shelfWidth = 8f;
    public float shelfHeight = 3f;
    public float shelfDepth = 1.2f;
    public float aisleWidth = 15f;
    public float shelfSpacing = 1f;
    public float shelfLateralGap = 10f;
    public bool shelfPaired = false;

    [Header("Obstacles")]
    [Range(0f, 1f)] public float crateSpawnChance = 0f;
    [Range(0f, 1f)] public float palletSpawnChance = 0f;

    [Header("Gates")]
    public float doorWidth = 20f;
    public float doorHeight = 3.5f;
    public bool doorWest = false;
    public bool doorEast = false;
    public bool doorSouth = true;
    public bool doorNorth = true;
    public float entrancePlatformDepth = 5f;

    [Header("Pillars")]
    public bool generatePillars = false;
    public float pillarSize = 0.4f;
    public float pillarSpacingX = 8f;
    public float pillarSpacingZ = 8f;
    public float pillarWallMargin = 2f;
    public bool pillarAvoidShelves = true;
    public float pillarShelfClearance = 0.5f;

    [Header("Randomness")]
    [Tooltip("0 uses Unity's time-based seed. Use a non-zero value for a repeatable layout.")]
    public int environmentSeed = 0;
}
