using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Barracuda;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

public enum WarehouseTestConfigSource
{
    AutoFromTrainingRecord,
    ManualOverride
}

/// <summary>
/// Runs inference tests for a list of ONNX assets and writes one result folder per model.
/// In automatic mode, the first model's training provenance selects the environment config
/// before WarehouseGenerator.Awake creates the environment.
/// </summary>
[DefaultExecutionOrder(-2000)]
public class WarehouseModelTestRunner : MonoBehaviour
{
    [Header("Test models")]
    [Tooltip("ONNX assets to test, in the order they should run.")]
    public NNModel[] models;

    [Min(1)]
    public int completedEpisodesPerModel = 100;

    [Min(1f)]
    public float timeoutSecondsPerModel = 300f;

    [Tooltip("Relative to the project folder. Each model gets a subfolder named after the ONNX asset.")]
    public string resultsFolder = "results";

    [Header("Experiment config selection")]
    [Tooltip("Auto uses results/<model-name>/unity_experiment.json. Manual Override uses the config below instead.")]
    public WarehouseTestConfigSource configSource = WarehouseTestConfigSource.AutoFromTrainingRecord;

    [Tooltip("Used only when Config Source is Manual Override.")]
    public WarehouseExperimentConfig manualExperimentConfig;

    [Tooltip("Optional runtime fallback for builds where AssetDatabase cannot resolve a preset GUID. Not needed for normal Editor tests.")]
    public WarehouseExperimentConfig[] configFallbacks;

    [Tooltip("Optional ID for this whole test batch. It writes unity_experiment.json to results/<testBatchId>/.")]
    public string testBatchId = "";

    [Tooltip("In Auto mode, stop if the current preset contents differ from the hash recorded during training.")]
    public bool failOnConfigFingerprintMismatch = true;

    [Header("Test control")]
    public bool isTestMode = false;
    public bool runOnPlay = true;
    public bool stopPlayModeWhenFinished = true;
    public bool deterministicInference = true;

    private WarehouseTrainingManager manager;
    private WarehouseRobotAgent[] agents;
    private bool running;
    private bool configPrepared;
    private WarehouseExperimentConfig selectedConfig;
    private WarehouseExperimentProvenance batchTrainingProvenance;
    private string batchTrainingProvenancePath;

    [Serializable]
    private struct ModelResult
    {
        public string model;
        public int requestedEpisodes;
        public int completedEpisodes;
        public int wallCollisions;
        public int agentCollisions;
        public int totalCollisions;
        public float elapsedSeconds;
        public string stopReason;
    }

    [Serializable]
    private class ModelTestProvenance
    {
        public string modelName;
        public string configSource;
        public string testBatchId;
        public bool trainingRecordFound;
        public string trainingRunId;
        public string trainingRecordPath;
        public string trainingPresetId;
        public string trainingPresetAssetName;
        public string trainingPresetAssetGuid;
        public string trainingPresetContentHash;
        public string trainingConfigFingerprintVersion;
        public string trainingEnvironmentPresetId;
        public string trainingEnvironmentPresetAssetName;
        public string trainingEnvironmentPresetAssetGuid;
        public string trainingEnvironmentPresetContentHash;
        public string appliedPresetId;
        public string appliedPresetAssetName;
        public string appliedPresetAssetGuid;
        public string appliedPresetContentHash;
        public string appliedConfigFingerprintVersion;
        public string appliedEnvironmentPresetId;
        public string appliedEnvironmentPresetAssetName;
        public string appliedEnvironmentPresetAssetGuid;
        public string appliedEnvironmentPresetContentHash;
        public bool matchesTrainingConfig;
        public string comparison;
        public string savedAt;
    }

    void Awake()
    {
        if (isTestMode)
            configPrepared = PrepareConfigForTest();
    }

    void Start()
    {
        if (!isTestMode || !runOnPlay)
            return;

        if (!configPrepared)
        {
            Debug.LogError("[ModelTestRunner] Test configuration was not prepared before environment initialization. Enable Is Test Mode before pressing Play.");
            return;
        }

        StartCoroutine(RunAllModelsAfterInitialization());
    }

    public void RunTests()
    {
        isTestMode = true;
        if (!configPrepared)
        {
            Debug.LogError("[ModelTestRunner] RunTests was called after Awake. Automatic config selection must be enabled before Play so it can configure the environment first.");
            return;
        }

        if (!running)
            StartCoroutine(RunAllModelsAfterInitialization());
    }

    bool PrepareConfigForTest()
    {
        WarehouseGenerator generator = GetComponentInChildren<WarehouseGenerator>(true);
        if (generator == null)
        {
            Debug.LogError("[ModelTestRunner] WarehouseGenerator was not found under this test runner.");
            return false;
        }

        if (!TrySelectInitialConfig(out string reason))
        {
            Debug.LogError($"[ModelTestRunner] Test config selection failed: {reason}");
            return false;
        }

        generator.useExperimentConfig = true;
        generator.experimentConfig = selectedConfig;
        if (!string.IsNullOrWhiteSpace(testBatchId))
            generator.experimentRunId = testBatchId;

        Debug.Log($"[ModelTestRunner] Config source: {configSource}; applied preset: {selectedConfig.name} ({selectedConfig.configId}).");
        return true;
    }

    bool TrySelectInitialConfig(out string reason)
    {
        reason = string.Empty;
        batchTrainingProvenance = null;
        batchTrainingProvenancePath = string.Empty;

        if (configSource == WarehouseTestConfigSource.ManualOverride)
        {
            selectedConfig = manualExperimentConfig;
            if (selectedConfig == null)
            {
                reason = "Manual Override is selected, but Manual Experiment Config is empty.";
                return false;
            }
            return true;
        }

        NNModel firstModel = FindFirstModel();
        if (firstModel == null)
        {
            reason = "Auto mode requires at least one ONNX model.";
            return false;
        }

        if (!TryReadTrainingProvenance(firstModel, out batchTrainingProvenance, out batchTrainingProvenancePath, out reason))
            return false;

        if (!batchTrainingProvenance.usePreset)
        {
            reason = $"{firstModel.name} was trained with Inspector settings, so no preset can be selected automatically.";
            return false;
        }

        selectedConfig = ResolveConfig(batchTrainingProvenance);
        if (selectedConfig == null)
        {
            reason = $"Could not resolve the training preset '{batchTrainingProvenance.presetAssetName}' ({batchTrainingProvenance.presetId}).";
            return false;
        }

        return VerifyAutoConfig(selectedConfig, batchTrainingProvenance, out reason);
    }

    IEnumerator RunAllModelsAfterInitialization()
    {
        running = true;
        manager = GetComponentInChildren<WarehouseTrainingManager>(true);
        agents = manager != null ? manager.robotAgents.ToArray() : FindObjectsOfType<WarehouseRobotAgent>();

        if (manager == null || agents.Length == 0 || models == null || models.Length == 0)
        {
            Debug.LogError("[ModelTestRunner] Manager, agents, or models are missing.");
            running = false;
            yield break;
        }

        // Let the manager finish spawning and registering agents before model switching.
        yield return new WaitForSecondsRealtime(0.5f);
        agents = manager.robotAgents.ToArray();

        foreach (NNModel model in models)
        {
            if (model == null) continue;

            if (!TryValidateModelConfig(model, out WarehouseExperimentProvenance provenance,
                                        out string provenancePath, out string reason))
            {
                Debug.LogError($"[ModelTestRunner] Test batch stopped before {model.name}: {reason}");
                break;
            }

            yield return RunOneModel(model, provenance, provenancePath);
        }

        running = false;
        Debug.Log("[ModelTestRunner] All eligible model tests finished. CSV and pheromone reports have been saved.");

        if (stopPlayModeWhenFinished)
        {
            // Give Unity one frame to finish file writes and display the completion log.
            yield return null;
            StopPlayMode();
        }
    }

    bool TryValidateModelConfig(NNModel model, out WarehouseExperimentProvenance provenance,
                                out string provenancePath, out string reason)
    {
        provenance = null;
        provenancePath = string.Empty;
        reason = string.Empty;

        bool found = TryReadTrainingProvenance(model, out provenance, out provenancePath, out string readReason);
        if (configSource == WarehouseTestConfigSource.ManualOverride)
        {
            if (!found)
                Debug.LogWarning($"[ModelTestRunner] Manual Override: training provenance for {model.name} was not found ({readReason}).");
            return true;
        }

        if (!found)
        {
            reason = readReason;
            return false;
        }

        if (!provenance.usePreset)
        {
            reason = $"{model.name} was trained with Inspector settings and has no preset to reproduce automatically.";
            return false;
        }

        return VerifyAutoConfig(selectedConfig, provenance, out reason);
    }

    IEnumerator RunOneModel(NNModel model, WarehouseExperimentProvenance trainingProvenance,
                            string trainingProvenancePath)
    {
        ApplyModel(model);
        ResetCounters();

        foreach (WarehouseRobotAgent agent in agents)
            if (agent != null) agent.EndEpisode();

        WarehousePheromone phero = GetComponentInChildren<WarehousePheromone>(true);
        if (phero != null)
            phero.ResetAll();

        float start = Time.realtimeSinceStartup;
        int startCompleted = CompletedCount();
        string stopReason = "episode_target";

        while (CompletedCount() - startCompleted < completedEpisodesPerModel)
        {
            if (Time.realtimeSinceStartup - start >= timeoutSecondsPerModel)
            {
                stopReason = "timeout";
                break;
            }
            yield return null;
        }

        ModelResult result = new ModelResult {
            model = model.name,
            requestedEpisodes = completedEpisodesPerModel,
            completedEpisodes = CompletedCount() - startCompleted,
            wallCollisions = SumWallCollisions(),
            agentCollisions = SumAgentCollisions(),
            totalCollisions = SumWallCollisions() + SumAgentCollisions(),
            elapsedSeconds = Time.realtimeSinceStartup - start,
            stopReason = stopReason
        };

        string resultDir = GetResultDirectory(model.name);
        Directory.CreateDirectory(resultDir);
        File.WriteAllText(Path.Combine(resultDir, "logTest.csv"), BuildCsv(result), Encoding.UTF8);
        WriteTestProvenance(resultDir, model, trainingProvenance, trainingProvenancePath);

        if (phero != null)
            WarehousePheromoneFinalReport.EmitToDirectory(phero, resultDir);

        Debug.Log($"[ModelTestRunner] {model.name}: completed={result.completedEpisodes}, " +
                  $"collisions={result.totalCollisions}, saved={resultDir}");
        yield return new WaitForSecondsRealtime(0.1f);
    }

    NNModel FindFirstModel()
    {
        if (models == null) return null;
        foreach (NNModel model in models)
            if (model != null) return model;
        return null;
    }

    string GetResultDirectory(string modelName)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", resultsFolder, modelName));
    }

    bool TryReadTrainingProvenance(NNModel model, out WarehouseExperimentProvenance provenance,
                                   out string path, out string reason)
    {
        provenance = null;
        path = string.Empty;
        reason = string.Empty;

        if (model == null)
        {
            reason = "Model is null.";
            return false;
        }

        path = Path.Combine(GetResultDirectory(model.name), "unity_experiment.json");
        if (!File.Exists(path))
        {
            reason = $"Training provenance was not found: {path}";
            return false;
        }

        try
        {
            provenance = JsonUtility.FromJson<WarehouseExperimentProvenance>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception exception)
        {
            reason = $"Training provenance could not be read: {exception.Message}";
            return false;
        }

        if (provenance == null)
        {
            reason = $"Training provenance is empty or invalid: {path}";
            return false;
        }

        return true;
    }

    WarehouseExperimentConfig ResolveConfig(WarehouseExperimentProvenance provenance)
    {
#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(provenance.presetAssetGuid))
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(provenance.presetAssetGuid);
            if (!string.IsNullOrEmpty(assetPath))
            {
                WarehouseExperimentConfig asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WarehouseExperimentConfig>(assetPath);
                if (asset != null) return asset;
            }
        }
#endif

        if (configFallbacks == null) return null;
        foreach (WarehouseExperimentConfig candidate in configFallbacks)
        {
            if (candidate == null) continue;
            if (!string.IsNullOrEmpty(provenance.presetId) && candidate.configId == provenance.presetId)
                return candidate;
            if (!string.IsNullOrEmpty(provenance.presetAssetName) && candidate.name == provenance.presetAssetName)
                return candidate;
        }
        return null;
    }

    bool VerifyAutoConfig(WarehouseExperimentConfig config, WarehouseExperimentProvenance provenance, out string reason)
    {
        if (HasSameConfig(config, provenance, out reason))
        {
            if (provenance.configFingerprintVersion != WarehouseExperimentRuntime.ConfigFingerprintVersion)
            {
                Debug.LogWarning("[ModelTestRunner] Training provenance uses an older config fingerprint format. " +
                                 "Preset identity was checked, but the training-time values cannot be verified completely.");
            }
            return true;
        }

        bool isFingerprintMismatch = reason.StartsWith("Preset content hash differs", StringComparison.Ordinal) ||
                                   reason.StartsWith("Environment preset content hash differs", StringComparison.Ordinal);
        if (isFingerprintMismatch && !failOnConfigFingerprintMismatch)
        {
            Debug.LogWarning($"[ModelTestRunner] {reason} Continuing because Fail On Config Fingerprint Mismatch is disabled.");
            return true;
        }
        return false;
    }

    bool HasSameConfig(WarehouseExperimentConfig config, WarehouseExperimentProvenance provenance, out string reason)
    {
        reason = string.Empty;
        if (config == null)
        {
            reason = "No test config is selected.";
            return false;
        }
        if (provenance == null)
        {
            reason = "No training provenance is available.";
            return false;
        }
        if (!provenance.usePreset)
        {
            reason = "Training used Inspector settings instead of a preset.";
            return false;
        }

        if (!string.IsNullOrEmpty(provenance.presetId) && config.configId != provenance.presetId)
        {
            reason = $"Preset ID differs (training={provenance.presetId}, test={config.configId}).";
            return false;
        }

        string currentGuid = WarehouseExperimentRuntime.GetConfigAssetGuid(config);
        bool canCompareGuid = !string.IsNullOrEmpty(provenance.presetAssetGuid) && !string.IsNullOrEmpty(currentGuid);
        if (canCompareGuid && currentGuid != provenance.presetAssetGuid)
        {
            reason = $"Preset asset GUID differs (training={provenance.presetAssetGuid}, test={currentGuid}).";
            return false;
        }

        if (!canCompareGuid && !string.IsNullOrEmpty(provenance.presetAssetName) && config.name != provenance.presetAssetName)
        {
            reason = $"Preset asset name differs (training={provenance.presetAssetName}, test={config.name}).";
            return false;
        }

        if (provenance.configFingerprintVersion == WarehouseExperimentRuntime.ConfigFingerprintVersion &&
            !string.IsNullOrEmpty(provenance.presetContentHash))
        {
            string currentHash = WarehouseExperimentRuntime.GetConfigContentHash(config);
            if (currentHash != provenance.presetContentHash)
            {
                reason = $"Preset content hash differs (training={provenance.presetContentHash}, test={currentHash}).";
                return false;
            }
        }

        if (!string.IsNullOrEmpty(provenance.environmentPresetId) ||
            !string.IsNullOrEmpty(provenance.environmentPresetAssetName))
        {
            WarehouseEnvironmentPreset environment = config.environmentPreset;
            if (environment == null)
            {
                reason = "Training used an Environment Preset, but the test config has none.";
                return false;
            }
            if (!string.IsNullOrEmpty(provenance.environmentPresetId) && environment.presetId != provenance.environmentPresetId)
            {
                reason = $"Environment preset ID differs (training={provenance.environmentPresetId}, test={environment.presetId}).";
                return false;
            }

            string currentEnvironmentGuid = WarehouseExperimentRuntime.GetEnvironmentPresetAssetGuid(environment);
            bool canCompareEnvironmentGuid = !string.IsNullOrEmpty(provenance.environmentPresetAssetGuid) &&
                                             !string.IsNullOrEmpty(currentEnvironmentGuid);
            if (canCompareEnvironmentGuid && currentEnvironmentGuid != provenance.environmentPresetAssetGuid)
            {
                reason = $"Environment preset asset GUID differs (training={provenance.environmentPresetAssetGuid}, test={currentEnvironmentGuid}).";
                return false;
            }
            if (!canCompareEnvironmentGuid && !string.IsNullOrEmpty(provenance.environmentPresetAssetName) &&
                environment.name != provenance.environmentPresetAssetName)
            {
                reason = $"Environment preset asset name differs (training={provenance.environmentPresetAssetName}, test={environment.name}).";
                return false;
            }
            if (!string.IsNullOrEmpty(provenance.environmentPresetContentHash))
            {
                string currentEnvironmentHash = WarehouseExperimentRuntime.GetEnvironmentPresetContentHash(environment);
                if (currentEnvironmentHash != provenance.environmentPresetContentHash)
                {
                    reason = $"Environment preset content hash differs (training={provenance.environmentPresetContentHash}, test={currentEnvironmentHash}).";
                    return false;
                }
            }
        }

        return true;
    }

    void WriteTestProvenance(string resultDir, NNModel model, WarehouseExperimentProvenance training,
                             string trainingPath)
    {
        bool matchesTraining = HasSameConfig(selectedConfig, training, out string comparison);
        var record = new ModelTestProvenance
        {
            modelName = model != null ? model.name : string.Empty,
            configSource = configSource.ToString(),
            testBatchId = testBatchId ?? string.Empty,
            trainingRecordFound = training != null,
            trainingRunId = training != null ? training.runId : string.Empty,
            trainingRecordPath = trainingPath ?? string.Empty,
            trainingPresetId = training != null ? training.presetId : string.Empty,
            trainingPresetAssetName = training != null ? training.presetAssetName : string.Empty,
            trainingPresetAssetGuid = training != null ? training.presetAssetGuid : string.Empty,
            trainingPresetContentHash = training != null ? training.presetContentHash : string.Empty,
            trainingConfigFingerprintVersion = training != null ? training.configFingerprintVersion : string.Empty,
            trainingEnvironmentPresetId = training != null ? training.environmentPresetId : string.Empty,
            trainingEnvironmentPresetAssetName = training != null ? training.environmentPresetAssetName : string.Empty,
            trainingEnvironmentPresetAssetGuid = training != null ? training.environmentPresetAssetGuid : string.Empty,
            trainingEnvironmentPresetContentHash = training != null ? training.environmentPresetContentHash : string.Empty,
            appliedPresetId = selectedConfig != null ? selectedConfig.configId : string.Empty,
            appliedPresetAssetName = selectedConfig != null ? selectedConfig.name : string.Empty,
            appliedPresetAssetGuid = WarehouseExperimentRuntime.GetConfigAssetGuid(selectedConfig),
            appliedPresetContentHash = WarehouseExperimentRuntime.GetConfigContentHash(selectedConfig),
            appliedConfigFingerprintVersion = WarehouseExperimentRuntime.ConfigFingerprintVersion,
            appliedEnvironmentPresetId = selectedConfig != null && selectedConfig.environmentPreset != null ? selectedConfig.environmentPreset.presetId : string.Empty,
            appliedEnvironmentPresetAssetName = selectedConfig != null && selectedConfig.environmentPreset != null ? selectedConfig.environmentPreset.name : string.Empty,
            appliedEnvironmentPresetAssetGuid = selectedConfig != null ? WarehouseExperimentRuntime.GetEnvironmentPresetAssetGuid(selectedConfig.environmentPreset) : string.Empty,
            appliedEnvironmentPresetContentHash = selectedConfig != null ? WarehouseExperimentRuntime.GetEnvironmentPresetContentHash(selectedConfig.environmentPreset) : string.Empty,
            matchesTrainingConfig = matchesTraining,
            comparison = matchesTraining ? "Experiment and environment preset identities and recorded hashes match." : comparison,
            savedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture)
        };

        string path = Path.Combine(resultDir, "test_experiment.json");
        File.WriteAllText(path, JsonUtility.ToJson(record, true) + "\n", Encoding.UTF8);
    }

    void ApplyModel(NNModel model)
    {
        foreach (WarehouseRobotAgent agent in agents)
        {
            if (agent == null) continue;
            var parameters = agent.GetComponent<BehaviorParameters>();
            if (parameters == null) continue;
            parameters.DeterministicInference = deterministicInference;
            agent.SetModel(parameters.BehaviorName, model, InferenceDevice.CPU);
        }
    }

    void ResetCounters()
    {
        foreach (WarehouseRobotAgent agent in agents)
        {
            if (agent == null) continue;
            agent.completedCount = 0;
            agent.crashToWall = 0;
            agent.crashToAgent = 0;
        }
    }

    int CompletedCount()
    {
        int total = 0;
        foreach (WarehouseRobotAgent agent in agents)
            if (agent != null) total += agent.completedCount;
        return total;
    }

    int SumWallCollisions()
    {
        int total = 0;
        foreach (WarehouseRobotAgent agent in agents)
            if (agent != null) total += agent.crashToWall;
        return total;
    }

    int SumAgentCollisions()
    {
        int total = 0;
        foreach (WarehouseRobotAgent agent in agents)
            if (agent != null) total += agent.crashToAgent;
        return total;
    }

    static string BuildCsv(ModelResult result)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("model,requested_episodes,completed_episodes,wall_collisions,agent_collisions,total_collisions,elapsed_seconds,stop_reason");
        sb.Append(result.model).Append(',')
          .Append(result.requestedEpisodes).Append(',')
          .Append(result.completedEpisodes).Append(',')
          .Append(result.wallCollisions).Append(',')
          .Append(result.agentCollisions).Append(',')
          .Append(result.totalCollisions).Append(',')
          .Append(result.elapsedSeconds.ToString("F3", c)).Append(',')
          .Append(result.stopReason).AppendLine();
        return sb.ToString();
    }

    static void StopPlayMode()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
