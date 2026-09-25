using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum WarehousePheromoneMode
{
    None,
    Shared,
    PhaseSeparated,
    TaskSeparated
}

[Serializable]
public class WarehouseExperimentProvenance
{
    public string runId;
    public string trainerSeed;
    public bool usePreset;
    public string presetId;
    public string presetAssetName;
    public string presetAssetGuid;
    public string presetContentHash;
    public string configFingerprintVersion;
    public string environmentPresetId;
    public string environmentPresetAssetName;
    public string environmentPresetAssetGuid;
    public string environmentPresetContentHash;
    public string sceneName;
    public string savedAt;
    public int environmentInstanceCount;
    public int totalAgentInstances;
    public WarehouseEnvironmentInstanceRecord[] environmentInstances;
}

[Serializable]
public class WarehouseEnvironmentInstanceRecord
{
    public string environmentPath;
    public int configuredAgentCount;
    public int effectiveAgentCount;
}

/// <summary>
/// Immutable experiment source data. Runtime components copy values from this asset;
/// the asset itself is never modified during play.
/// </summary>
[CreateAssetMenu(fileName = "WarehouseExperimentConfig", menuName = "Warehouse/Experiment Config")]
public class WarehouseExperimentConfig : ScriptableObject
{
    [Header("Identity")]
    public string configId = "experiment_v1";

    [Header("Pheromone")]
    public WarehousePheromoneMode pheromoneMode = WarehousePheromoneMode.TaskSeparated;
    public float pheromoneSecretionAmount = 1f;
    [Range(0f, 1f)] public float pheromoneEvaporationRate = 0.05f;
    public int pheromoneEvaporationInterval = 100;
    public float pheromoneMinValue = 0f;
    public float pheromoneMaxValue = 1000000f;
    public float pheromoneRewardScale = 0.0002f;
    public float pheromoneCellSize = 2f;

    [Header("Environment preset")]
    [Tooltip("Preferred versioned layout asset. When assigned, it takes priority over the legacy fields below.")]
    public WarehouseEnvironmentPreset environmentPreset;

    [Header("Legacy environment values")]
    [Tooltip("Kept for compatibility with existing config assets. New configs should use Environment Preset.")]
    public WarehousePreset warehousePreset = WarehousePreset.Custom;
    public int warehouseWidth = 20;
    public int warehouseDepth = 55;
    public float wallHeight = 5f;
    public int shelfRows = 1;
    public int shelvesPerRow = 3;
    public float shelfWidth = 8f;
    public float shelfHeight = 3f;
    public float shelfDepth = 1.2f;
    public float aisleWidth = 15f;
    public float shelfSpacing = 1f;
    public float shelfLateralGap = 10f;
    public bool shelfPaired = false;
    public float crateSpawnChance = 0f;
    public float palletSpawnChance = 0f;
    public float doorWidth = 20f;
    public float doorHeight = 3.5f;
    public bool doorWest = false;
    public bool doorEast = false;
    public bool doorSouth = true;
    public bool doorNorth = true;
    public float entrancePlatformDepth = 5f;
    public bool generatePillars = false;
    public float pillarSize = 0.4f;
    public float pillarSpacingX = 8f;
    public float pillarSpacingZ = 8f;
    public float pillarWallMargin = 2f;
    public bool pillarAvoidShelves = true;
    public float pillarShelfClearance = 0.5f;
    public int environmentSeed = 0;

    [Header("Agents and assignment")]
    public int agentCount = 16;
    public bool avoidDuplicateShelves = true;
    public float slotSpacing = 1.5f;

    [Header("Agent movement and rewards")]
    public float moveAccel = 30f;
    public float maxSpeed = 5f;
    public float turnSpeed = 150f;
    public float robotMass = 10f;
    public float robotDrag = 1f;
    public float robotAngularDrag = 5f;
    public float rayDistance = 10f;
    public float rayNoise = 0f;
    public float dropRange = 1.5f;
    public float exitRange = 1.5f;
    public bool allowBackAccess = true;
    public float rewardDropAtShelf = 50f;
    public float rewardExitComplete = 100f;
    public float penaltyWallHit = -1f;
    public float penaltyWallStay = -0.1f;
    public float penaltyAgentCollision = -1f;
    public float penaltyAgentStay = -0.1f;
    public float penaltyTimeStep = -0.001f;
    public float penaltyFall = -1f;
    public float shelfApproachReward = 1f;
    public float exitApproachReward = 1f;
    public int maxStepLimit = 50000;
    public float penaltyTimeout = -100f;
}

/// <summary>
/// Applies one selected config to the runtime environment and records its provenance.
/// </summary>
public static class WarehouseExperimentRuntime
{
    public const string ConfigFingerprintVersion = "public-fields-v1";
    private static readonly Dictionary<WarehouseGenerator, WarehouseTrainingManager> environments =
        new Dictionary<WarehouseGenerator, WarehouseTrainingManager>();
    private static readonly HashSet<WarehouseTrainingManager> readyManagers = new HashSet<WarehouseTrainingManager>();
    private static bool prepared;
    public static bool Failed { get; private set; }
    public static bool IsReady => recorded && !Failed;
    private static bool logged;
    private static bool recorded;
    private static WarehouseExperimentConfig activeConfig;
    private static bool activeUsePreset;
    public static int EnvironmentInstanceCount => environments.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        environments.Clear();
        readyManagers.Clear();
        prepared = logged = recorded = Failed = false;
        activeConfig = null;
        activeUsePreset = false;
    }

    public static string GetConfigAssetGuid(WarehouseExperimentConfig config)
    {
#if UNITY_EDITOR
        if (config == null) return string.Empty;
        string path = UnityEditor.AssetDatabase.GetAssetPath(config);
        return string.IsNullOrEmpty(path) ? string.Empty : UnityEditor.AssetDatabase.AssetPathToGUID(path);
#else
        return string.Empty;
#endif
    }

    public static string GetConfigContentHash(WarehouseExperimentConfig config)
    {
        return GetPublicFieldContentHash(config);
    }

    public static string GetEnvironmentPresetAssetGuid(WarehouseEnvironmentPreset preset)
    {
#if UNITY_EDITOR
        if (preset == null) return string.Empty;
        string path = UnityEditor.AssetDatabase.GetAssetPath(preset);
        return string.IsNullOrEmpty(path) ? string.Empty : UnityEditor.AssetDatabase.AssetPathToGUID(path);
#else
        return string.Empty;
#endif
    }

    public static string GetEnvironmentPresetContentHash(WarehouseEnvironmentPreset preset)
    {
        return GetPublicFieldContentHash(preset);
    }

    static string GetPublicFieldContentHash(object target)
    {
        if (target == null) return string.Empty;

        var fields = new List<FieldInfo>(target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public));
        fields.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        var content = new StringBuilder();
        foreach (FieldInfo field in fields)
        {
            // Unity object references have instance-specific serialization. Their identity and
            // content are recorded separately in the provenance JSON.
            if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
            object value = field.GetValue(target);
            string valueText = value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                : value?.ToString() ?? "<null>";
            content.Append(field.Name).Append(':').Append(valueText.Length).Append(':').Append(valueText).Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(content.ToString());
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    public static void ApplyToEnvironment(WarehouseGenerator generator)
    {
        if (Failed) throw new InvalidOperationException("Experiment startup has already failed.");
        if (prepared)
        {
            if (!environments.ContainsKey(generator))
                Fail("Environments added after startup are not supported. Place all environments before Play.");
            return;
        }

        try
        {
            // All test runners select their configs before any Generator.Awake executes.
            var generators = new List<WarehouseGenerator>();
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<WarehouseGenerator>())
                if (IsConfiguredActive(candidate)) generators.Add(candidate);
            generators.Sort((a, b) => string.CompareOrdinal(GetEnvironmentPath(a.transform), GetEnvironmentPath(b.transform)));
            PrepareEnvironments(generators);
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    static void PrepareEnvironments(List<WarehouseGenerator> generators)
    {
        if (generators.Count == 0) throw new InvalidOperationException("No active warehouse environments were found.");
        WarehouseGenerator first = generators[0];
        activeUsePreset = first.useExperimentConfig;
        activeConfig = activeUsePreset ? first.experimentConfig : null;
        var roots = new HashSet<Transform>();

        foreach (WarehouseGenerator g in generators)
        {
            if (g.useExperimentConfig != activeUsePreset ||
                (activeUsePreset && g.experimentConfig != activeConfig))
                throw new InvalidOperationException($"{GetEnvironmentPath(g.transform)}: all parallel environments must select the same config asset and Use Experiment Config setting.");
            if (g.useExperimentConfig && g.experimentConfig == null)
                throw new InvalidOperationException($"{g.name}: Use Experiment Config is enabled but Experiment Config is empty.");
            if (GetRunId(g) != GetRunId(first))
                throw new InvalidOperationException($"{g.name}: parallel environments have different experiment run IDs.");

            Transform root = GetEnvironmentRoot(g);
            var managers = root.GetComponentsInChildren<WarehouseTrainingManager>(true);
            if (!roots.Add(root) || managers.Length != 1 || !IsConfiguredActive(managers[0]) ||
                managers[0].warehouseGenerator != g)
                throw new InvalidOperationException($"{GetEnvironmentPath(root)}: each environment must contain one active manager referencing its own generator.");
            var pheromones = root.GetComponentsInChildren<WarehousePheromone>(true);
            if (pheromones.Length != 1 || !IsConfiguredActive(pheromones[0]) ||
                pheromones[0].gameObject != managers[0].gameObject ||
                (pheromones[0].warehouseGenerator != null && pheromones[0].warehouseGenerator != g))
                throw new InvalidOperationException($"{GetEnvironmentPath(root)}: put one active WarehousePheromone on the manager, referencing the local generator.");
            if (managers[0].envRoot != null && managers[0].envRoot != root)
                throw new InvalidOperationException($"{GetEnvironmentPath(root)}: manager Env Root must reference this environment's root.");
            ValidateAgentRoster(g, managers[0], true);
            environments.Add(g, managers[0]);
        }

        // Validate every environment before mutating any runtime settings.
        foreach (var entry in environments)
        {
            WarehouseGenerator g = entry.Key;
            WarehouseTrainingManager manager = entry.Value;
            manager.configuredAgentCount = g.useExperimentConfig ? g.experimentConfig.agentCount : 0;
            if (g.useExperimentConfig)
            {
                ApplyGenerator(g, g.experimentConfig);
                ApplyManager(manager, g.experimentConfig);
                ApplyPheromone(manager.GetComponent<WarehousePheromone>(), g.experimentConfig);
            }
            foreach (var agent in manager.robotAgents)
            {
                agent.trainingManager = manager;
                agent.pheromone = manager.GetComponent<WarehousePheromone>();
                agent.envRoot = GetEnvironmentRoot(g);
                if (g.useExperimentConfig) ApplyAgent(agent, g.experimentConfig);
            }
        }
        prepared = true;
    }

    static Transform GetEnvironmentRoot(WarehouseGenerator g)
    {
        return g.transform.parent != null ? g.transform.parent : g.transform;
    }

    static bool IsConfiguredActive(Behaviour component)
    {
        // Later execution-order components have not received OnEnable during Generator.Awake.
        return component.enabled && component.gameObject.activeInHierarchy;
    }

    static string GetEnvironmentPath(Transform transform)
    {
        string path = transform.name + "[" + transform.GetSiblingIndex() + "]";
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "[" + transform.GetSiblingIndex() + "]/" + path;
        }
        return transform.gameObject.scene.name + "/" + path;
    }

    static void ValidateAgentRoster(WarehouseGenerator g, WarehouseTrainingManager manager, bool allowPendingSpawn)
    {
        string path = GetEnvironmentPath(GetEnvironmentRoot(g));
        var unique = new HashSet<WarehouseRobotAgent>();
        foreach (var agent in manager.robotAgents)
        {
            if (agent == null || !IsConfiguredActive(agent) || !unique.Add(agent) ||
                !agent.transform.IsChildOf(GetEnvironmentRoot(g)) ||
                (agent.trainingManager != null && agent.trainingManager != manager))
                throw new InvalidOperationException($"{path}: Robot Agents contains a missing, disabled, duplicate, or foreign agent. Check the manager's list.");
        }
        foreach (var agent in GetEnvironmentRoot(g).GetComponentsInChildren<WarehouseRobotAgent>(true))
            if (IsConfiguredActive(agent) && !unique.Contains(agent))
                throw new InvalidOperationException($"{path}: active agent {agent.name} is absent from the manager's Robot Agents list.");

        int expected = g.useExperimentConfig ? g.experimentConfig.agentCount : 0;
        if (g.useExperimentConfig && expected <= 0)
            throw new InvalidOperationException($"{path}: Config Agent Count must be greater than zero.");
        if (allowPendingSpawn && unique.Count == 0 && (expected > 0 || manager.autoSpawnCount > 0)) return;
        if (unique.Count == 0 || (g.useExperimentConfig && unique.Count != expected))
            throw new InvalidOperationException($"{path}: Config Agent Count={expected}, effective agents={unique.Count}. Match the placed agents and Robot Agents list to the config, or select a new config with the intended count. Existing agents are not resized automatically.");
    }

    public static void NotifyEnvironmentReady(WarehouseTrainingManager manager)
    {
        if (Failed) return;
        try
        {
            WarehouseGenerator g = manager.warehouseGenerator;
            if (!prepared || g == null || !environments.TryGetValue(g, out var registered) || registered != manager)
                throw new InvalidOperationException($"{manager.name}: manager was not included in environment startup validation.");
            ValidateAgentRoster(g, manager, false);
            readyManagers.Add(manager);
            if (readyManagers.Count != environments.Count) return;
            LogEffective(activeConfig, activeUsePreset, g);
            Record(g, activeConfig, activeUsePreset);
        }
        catch (Exception error)
        {
            Fail(error.Message);
        }
    }

    public static void Fail(string reason)
    {
        Failed = true;
        Debug.LogError("[ExperimentConfig] Startup stopped: " + reason);
        foreach (var g in UnityEngine.Object.FindObjectsOfType<WarehouseGenerator>())
            GetEnvironmentRoot(g).gameObject.SetActive(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(1);
#endif
        throw new InvalidOperationException(reason);
    }

    static WarehouseEnvironmentInstanceRecord[] GetInstanceRecords()
    {
        var records = new List<WarehouseEnvironmentInstanceRecord>();
        foreach (var entry in environments)
            records.Add(new WarehouseEnvironmentInstanceRecord
            {
                environmentPath = GetEnvironmentPath(GetEnvironmentRoot(entry.Key)),
                configuredAgentCount = entry.Value.configuredAgentCount,
                effectiveAgentCount = entry.Value.robotAgents.Count
            });
        records.Sort((a, b) => string.CompareOrdinal(a.environmentPath, b.environmentPath));
        return records.ToArray();
    }

    public static void ApplyAgent(WarehouseRobotAgent agent, WarehouseExperimentConfig config)
    {
        if (agent == null || config == null) return;
        agent.moveAccel = config.moveAccel;
        agent.maxSpeed = config.maxSpeed;
        agent.turnSpeed = config.turnSpeed;
        agent.robotMass = config.robotMass;
        agent.robotDrag = config.robotDrag;
        agent.robotAngularDrag = config.robotAngularDrag;
        agent.rayDistance = config.rayDistance;
        agent.rayNoise = config.rayNoise;
        agent.dropRange = config.dropRange;
        agent.exitRange = config.exitRange;
        agent.allowBackAccess = config.allowBackAccess;
        agent.rewardDropAtShelf = config.rewardDropAtShelf;
        agent.rewardExitComplete = config.rewardExitComplete;
        agent.penaltyWallHit = config.penaltyWallHit;
        agent.penaltyWallStay = config.penaltyWallStay;
        agent.penaltyAgentCollision = config.penaltyAgentCollision;
        agent.penaltyAgentStay = config.penaltyAgentStay;
        agent.penaltyTimeStep = config.penaltyTimeStep;
        agent.penaltyFall = config.penaltyFall;
        agent.shelfApproachReward = config.shelfApproachReward;
        agent.exitApproachReward = config.exitApproachReward;
        agent.maxStepLimit = config.maxStepLimit;
        agent.penaltyTimeout = config.penaltyTimeout;
    }

    static void ApplyGenerator(WarehouseGenerator g, WarehouseExperimentConfig c)
    {
        if (c.environmentPreset != null)
        {
            g.ApplyEnvironmentPreset(c.environmentPreset);
            return;
        }

        g.preset = c.warehousePreset;
        g.warehouseWidth = c.warehouseWidth;
        g.warehouseDepth = c.warehouseDepth;
        g.wallHeight = c.wallHeight;
        g.shelfRows = c.shelfRows;
        g.shelvesPerRow = c.shelvesPerRow;
        g.shelfWidth = c.shelfWidth;
        g.shelfHeight = c.shelfHeight;
        g.shelfDepth = c.shelfDepth;
        g.aisleWidth = c.aisleWidth;
        g.shelfSpacing = c.shelfSpacing;
        g.shelfLateralGap = c.shelfLateralGap;
        g.shelfPaired = c.shelfPaired;
        g.crateSpawnChance = c.crateSpawnChance;
        g.palletSpawnChance = c.palletSpawnChance;
        g.doorWidth = c.doorWidth;
        g.doorHeight = c.doorHeight;
        g.doorWest = c.doorWest;
        g.doorEast = c.doorEast;
        g.doorSouth = c.doorSouth;
        g.doorNorth = c.doorNorth;
        g.entrancePlatformDepth = c.entrancePlatformDepth;
        g.generatePillars = c.generatePillars;
        g.pillarSize = c.pillarSize;
        g.pillarSpacingX = c.pillarSpacingX;
        g.pillarSpacingZ = c.pillarSpacingZ;
        g.pillarWallMargin = c.pillarWallMargin;
        g.pillarAvoidShelves = c.pillarAvoidShelves;
        g.pillarShelfClearance = c.pillarShelfClearance;
        g.seed = c.environmentSeed;
    }

    static void ApplyManager(WarehouseTrainingManager m, WarehouseExperimentConfig c)
    {
        m.avoidDuplicateShelves = c.avoidDuplicateShelves;
        m.slotSpacing = c.slotSpacing;
        m.configuredAgentCount = c.agentCount;
        if (m.robotAgents.Count == 0 && c.agentCount > 0)
            m.autoSpawnCount = c.agentCount;
    }

    static void ApplyPheromone(WarehousePheromone p, WarehouseExperimentConfig c)
    {
        p.cellSize = c.pheromoneCellSize;
        p.pheroQ = c.pheromoneSecretionAmount;
        p.evapRate = c.pheromoneEvaporationRate;
        p.evapInterval = c.pheromoneEvaporationInterval;
        p.rewardScale = c.pheromoneRewardScale;
        p.pheromoneMode = c.pheromoneMode;
        p.pheromoneMinValue = c.pheromoneMinValue;
        p.pheromoneMaxValue = c.pheromoneMaxValue;
        p.usePheromone = c.pheromoneMode != WarehousePheromoneMode.None;
        if (c.pheromoneMode == WarehousePheromoneMode.Shared || c.pheromoneMode == WarehousePheromoneMode.PhaseSeparated)
            Debug.LogWarning($"[ExperimentConfig] Pheromone mode {c.pheromoneMode} is recorded but not implemented by the current route-map code. Current maps remain task-separated.");
    }

    static void LogEffective(WarehouseExperimentConfig c, bool preset, WarehouseGenerator g)
    {
        if (logged) return;
        logged = true;
        Transform root = g != null && g.transform.parent != null ? g.transform.parent : (g != null ? g.transform : null);
        WarehousePheromone p = root != null ? root.GetComponentInChildren<WarehousePheromone>(true) : null;
        WarehouseTrainingManager m = root != null ? root.GetComponentInChildren<WarehouseTrainingManager>(true) : null;
        string id = c != null ? c.configId : "manual-inspector";
        string mode = c != null ? c.pheromoneMode.ToString() : (p != null ? p.pheromoneMode.ToString() : "Inspector");
        float evap = c != null ? c.pheromoneEvaporationRate : (p != null ? p.evapRate : 0f);
        float q = c != null ? c.pheromoneSecretionAmount : (p != null ? p.pheroQ : 0f);
        int count = c != null ? c.agentCount : (m != null ? m.robotAgents.Count : 0);
        WarehouseEnvironmentPreset environment = c != null ? c.environmentPreset : (g != null ? g.environmentPreset : null);
        Debug.Log("Experiment Config\n" +
                  $"ID: {id}\n" +
                  $"Source: {(preset ? "Preset" : "Inspector")}\n" +
                  $"Asset: {(c != null ? c.name : "(none)")}\n" +
                  $"Trainer Seed: {GetTrainerSeed()}\n" +
                  $"Pheromone Mode: {mode}\n" +
                  $"Evaporation Rate: {evap.ToString(CultureInfo.InvariantCulture)}\n" +
                  $"Secretion Amount: {q.ToString(CultureInfo.InvariantCulture)}\n" +
                  $"Configured Agent Count per Environment: {count}\n" +
                  $"Environment Instances (this Unity process): {environments.Count}\n" +
                  $"Environment Preset: {(environment != null ? environment.name + " (" + environment.presetId + ")" : "Inspector / legacy values")}");
        foreach (var instance in GetInstanceRecords())
            Debug.Log($"[ExperimentConfig] {instance.environmentPath}: effective agents={instance.effectiveAgentCount}");
    }

    static void Record(WarehouseGenerator g, WarehouseExperimentConfig c, bool preset)
    {
        if (recorded) return;
        string runId = GetRunId(g);
        string scene = SceneManager.GetActiveScene().name;
        string dir = GetRecordDirectory(runId);
        Directory.CreateDirectory(dir);

        string json = BuildJson(runId, c, preset, scene);
        string path = Path.Combine(dir, "unity_experiment.json");
        File.WriteAllText(path, json, Encoding.UTF8);
        recorded = true;
        Debug.Log($"[ExperimentConfig] provenance saved: {path}");
    }

    static string GetRecordDirectory(string runId)
    {
        string configuredResults = Environment.GetEnvironmentVariable("WAREHOUSE_RESULTS_DIR");
        if (!string.IsNullOrWhiteSpace(configuredResults))
        {
            if (!Path.IsPathRooted(configuredResults))
            {
                throw new InvalidOperationException(
                    $"WAREHOUSE_RESULTS_DIR must be an absolute path: {configuredResults}");
            }

            string resultsRoot = Path.GetFullPath(configuredResults);
            return string.IsNullOrEmpty(runId) ? resultsRoot : Path.Combine(resultsRoot, runId);
        }

#if UNITY_EDITOR
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return string.IsNullOrEmpty(runId)
            ? Path.Combine(projectRoot, "Logs")
            : Path.Combine(projectRoot, "results", runId);
#else
        throw new InvalidOperationException(
            "WAREHOUSE_RESULTS_DIR is required for a player build. Launch the build through tr.py so Unity and ML-Agents use the same absolute results directory.");
#endif
    }

    static string GetRunId(WarehouseGenerator g)
    {
        string env = Environment.GetEnvironmentVariable("MLAGENTS_RUN_ID");
        if (!string.IsNullOrEmpty(env)) return env;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--run-id") return args[i + 1];
        return g != null ? g.experimentRunId : string.Empty;
    }

    static string GetTrainerSeed()
    {
        string seed = Environment.GetEnvironmentVariable("MLAGENTS_TRAINER_SEED");
        return string.IsNullOrWhiteSpace(seed) ? "(not provided to Unity)" : seed;
    }

    static string BuildJson(string runId, WarehouseExperimentConfig c, bool preset, string scene)
    {
        var instances = GetInstanceRecords();
        int total = 0;
        foreach (var instance in instances) total += instance.effectiveAgentCount;
        var provenance = new WarehouseExperimentProvenance
        {
            runId = runId ?? string.Empty,
            trainerSeed = Environment.GetEnvironmentVariable("MLAGENTS_TRAINER_SEED") ?? string.Empty,
            usePreset = preset,
            presetId = c != null ? c.configId : string.Empty,
            presetAssetName = c != null ? c.name : string.Empty,
            presetAssetGuid = GetConfigAssetGuid(c),
            presetContentHash = GetConfigContentHash(c),
            configFingerprintVersion = ConfigFingerprintVersion,
            environmentPresetId = c != null && c.environmentPreset != null ? c.environmentPreset.presetId : string.Empty,
            environmentPresetAssetName = c != null && c.environmentPreset != null ? c.environmentPreset.name : string.Empty,
            environmentPresetAssetGuid = c != null ? GetEnvironmentPresetAssetGuid(c.environmentPreset) : string.Empty,
            environmentPresetContentHash = c != null ? GetEnvironmentPresetContentHash(c.environmentPreset) : string.Empty,
            sceneName = scene ?? string.Empty,
            savedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            environmentInstanceCount = instances.Length,
            totalAgentInstances = total,
            environmentInstances = instances
        };
        return JsonUtility.ToJson(provenance, true) + "\n";
    }
}
