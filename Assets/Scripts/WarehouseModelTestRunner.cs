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
using UnityEngine.Serialization;

public enum WarehouseTestConfigSource
{
    AutoFromTrainingRecord,
    ManualOverride
}

public enum WarehouseTestStopCondition
{
    FixedCompletedTasks,
    FixedDuration
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

    [Header("Trial settings")]
    [Min(1)]
    public int trialsPerModel = 1;

    public WarehouseTestStopCondition stopCondition = WarehouseTestStopCondition.FixedCompletedTasks;

    [FormerlySerializedAs("completedEpisodesPerModel")]
    [Min(1)]
    public int completedTasksPerTrial = 100;

    [Min(0.01f)]
    public float durationSecondsPerTrial = 300f;

    [Tooltip("Real-world seconds allowed per trial. Set to 0 to disable this safety limit.")]
    [FormerlySerializedAs("timeoutSecondsPerModel")]
    [Min(0f)]
    public float safetyTimeoutSecondsPerTrial = 300f;

    [Tooltip("Reset Unity's random sequence before every trial. The same trial index then starts from the same seed for every model.")]
    public bool reseedEachTrial = true;

    public int baseTrialSeed = 1;

    [Tooltip("Relative to the project folder. Each model gets a subfolder named after the ONNX asset.")]
    public string resultsFolder = "results";

    [Header("Experiment config selection")]
    [Tooltip("Auto uses results/<model-name>/unity_experiment.json. Manual Override uses the config below instead.")]
    public WarehouseTestConfigSource configSource = WarehouseTestConfigSource.AutoFromTrainingRecord;

    [Tooltip("Used only when Config Source is Manual Override.")]
    public WarehouseExperimentConfig manualExperimentConfig;

    [Tooltip("Optional fallback for player builds, where project assets cannot be searched with AssetDatabase. Normal Editor tests resolve the config automatically by GUID, ID, name, and content hash.")]
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
    private struct TrialResult
    {
        public string model;
        public int trial;
        public int trialSeed;
        public string stopCondition;
        public int targetCompletedTasks;
        public float targetDurationSeconds;
        public int completedTasks;
        public int wallCollisions;
        public int agentCollisions;
        public int totalCollisions;
        public int simulationSteps;
        public float simulationSeconds;
        public float wallSeconds;
        public float tasksPerSimulationMinute;
        public float collisionsPerTask;
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
        public string stopCondition;
        public int trialsPerModel;
        public int completedTasksPerTrial;
        public float durationSecondsPerTrial;
        public float safetyTimeoutSecondsPerTrial;
        public bool reseedEachTrial;
        public int baseTrialSeed;
        public bool deterministicInference;
        public int environmentInstanceCount;
        public int effectiveAgentCount;
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
            WarehouseExperimentRuntime.Fail("WarehouseGenerator was not found under the test runner.");
            return false;
        }

        if (!TrySelectInitialConfig(out string reason))
        {
            WarehouseExperimentRuntime.Fail($"Test config selection failed: {reason}");
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

        if (!TryResolveConfig(batchTrainingProvenance, out selectedConfig, out reason))
            return false;

        return VerifyAutoConfig(selectedConfig, batchTrainingProvenance, out reason);
    }

    IEnumerator RunAllModelsAfterInitialization()
    {
        running = true;
        manager = GetComponentInChildren<WarehouseTrainingManager>(true);

        if (manager == null || models == null || models.Length == 0)
        {
            Debug.LogError("[ModelTestRunner] Manager, agents, or models are missing.");
            running = false;
            yield break;
        }

        if (!ValidateTrialSettings(out string settingsError))
        {
            Debug.LogError($"[ModelTestRunner] Invalid trial settings: {settingsError}");
            running = false;
            yield break;
        }

        // Wait for actual initialization, including automatic spawning and startup validation.
        float startupDeadline = Time.realtimeSinceStartup + 30f;
        while (!manager.RuntimeReady || !WarehouseExperimentRuntime.IsReady)
        {
            if (WarehouseExperimentRuntime.Failed || Time.realtimeSinceStartup >= startupDeadline)
            {
                running = false;
                WarehouseExperimentRuntime.Fail("Test environment initialization did not complete within 30 real-time seconds.");
                yield break;
            }
            yield return null;
        }
        agents = manager.robotAgents.ToArray();

        foreach (NNModel model in models)
        {
            if (model == null) continue;

            if (!TryValidateModelConfig(model, out WarehouseExperimentProvenance provenance,
                                        out string provenancePath, out string reason))
            {
                running = false;
                WarehouseExperimentRuntime.Fail($"Test batch stopped before {model.name}: {reason}");
                yield break;
            }

            yield return RunModelTrials(model, provenance, provenancePath);
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

        // Parallel environment count may differ; the population inside each warehouse must match.
        if (provenance.environmentInstances != null && provenance.environmentInstances.Length > 0)
        {
            foreach (var instance in provenance.environmentInstances)
            {
                if (instance == null || instance.effectiveAgentCount != agents.Length)
                {
                    reason = $"{model.name}: training agents per environment do not match the test's {agents.Length} agents. Use Manual Override for an intentional population change.";
                    return false;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[ModelTestRunner] {model.name}: legacy training record has no effective agent counts. Only the selected config's expected count can be verified.");
        }

        return VerifyAutoConfig(selectedConfig, provenance, out reason);
    }

    IEnumerator RunModelTrials(NNModel model, WarehouseExperimentProvenance trainingProvenance,
                               string trainingProvenancePath)
    {
        ApplyModel(model);
        WarehousePheromone phero = GetComponentInChildren<WarehousePheromone>(true);
        string resultDir = GetResultDirectory(model.name);
        Directory.CreateDirectory(resultDir);
        WriteTestProvenance(resultDir, model, trainingProvenance, trainingProvenancePath);
        var trialResults = new List<TrialResult>();
        File.WriteAllText(Path.Combine(resultDir, "logTest.csv"), BuildTrialCsv(trialResults), Encoding.UTF8);
        File.WriteAllText(Path.Combine(resultDir, "logTest_summary.csv"), BuildSummaryCsv(trialResults), Encoding.UTF8);

        string pheromoneTrialsDirectory = Path.Combine(resultDir, "pheromone_trials");
        if (Directory.Exists(pheromoneTrialsDirectory))
            Directory.Delete(pheromoneTrialsDirectory, true);

        Debug.Log($"[ModelTestRunner] Starting {model.name}: trials={trialsPerModel}, " +
                  $"stopCondition={stopCondition}, reseedEachTrial={reseedEachTrial}, baseTrialSeed={baseTrialSeed}");

        for (int trialIndex = 0; trialIndex < trialsPerModel; trialIndex++)
        {
            int trialNumber = trialIndex + 1;
            int trialSeed = reseedEachTrial ? unchecked(baseTrialSeed + trialIndex) : -1;
            if (reseedEachTrial)
                UnityEngine.Random.InitState(trialSeed);

            manager.ResetForEvaluationTrial();

            float wallStart = Time.realtimeSinceStartup;
            int simulationSteps = 0;
            string stopReason;

            while (true)
            {
                int completedTasks = CompletedCount();
                float simulationSeconds = simulationSteps * Time.fixedDeltaTime;
                if (stopCondition == WarehouseTestStopCondition.FixedCompletedTasks &&
                    completedTasks >= completedTasksPerTrial)
                {
                    stopReason = "completed_task_target";
                    break;
                }
                if (stopCondition == WarehouseTestStopCondition.FixedDuration &&
                    simulationSeconds >= durationSecondsPerTrial)
                {
                    stopReason = "simulation_duration_target";
                    break;
                }
                if (safetyTimeoutSecondsPerTrial > 0f &&
                    Time.realtimeSinceStartup - wallStart >= safetyTimeoutSecondsPerTrial)
                {
                    stopReason = "wall_time_safety_timeout";
                    break;
                }

                yield return new WaitForFixedUpdate();
                simulationSteps++;
            }

            int completed = CompletedCount();
            int wallCollisions = SumWallCollisions();
            int agentCollisions = SumAgentCollisions();
            float simulated = simulationSteps * Time.fixedDeltaTime;
            var result = new TrialResult
            {
                model = model.name,
                trial = trialNumber,
                trialSeed = trialSeed,
                stopCondition = stopCondition.ToString(),
                targetCompletedTasks = stopCondition == WarehouseTestStopCondition.FixedCompletedTasks
                    ? completedTasksPerTrial : 0,
                targetDurationSeconds = stopCondition == WarehouseTestStopCondition.FixedDuration
                    ? durationSecondsPerTrial : 0f,
                completedTasks = completed,
                wallCollisions = wallCollisions,
                agentCollisions = agentCollisions,
                totalCollisions = wallCollisions + agentCollisions,
                simulationSteps = simulationSteps,
                simulationSeconds = simulated,
                wallSeconds = Time.realtimeSinceStartup - wallStart,
                tasksPerSimulationMinute = simulated > 0f ? completed * 60f / simulated : 0f,
                collisionsPerTask = completed > 0 ? (wallCollisions + agentCollisions) / (float)completed : 0f,
                stopReason = stopReason
            };
            trialResults.Add(result);

            // Rewrite after each trial so an interrupted batch still retains completed trials.
            File.WriteAllText(Path.Combine(resultDir, "logTest.csv"), BuildTrialCsv(trialResults), Encoding.UTF8);
            File.WriteAllText(Path.Combine(resultDir, "logTest_summary.csv"), BuildSummaryCsv(trialResults), Encoding.UTF8);

            if (phero != null)
            {
                string trialDirectory = Path.Combine(pheromoneTrialsDirectory, $"trial_{trialNumber:D2}");
                WarehousePheromoneFinalReport.EmitToDirectory(phero, trialDirectory);
            }

            Debug.Log($"[ModelTestRunner] {model.name} trial {trialNumber}/{trialsPerModel}: " +
                      $"completed={result.completedTasks}, collisions={result.totalCollisions}, " +
                      $"simulated={result.simulationSeconds:F2}s, stop={result.stopReason}");
            yield return new WaitForSecondsRealtime(0.1f);
        }

        Debug.Log($"[ModelTestRunner] {model.name}: {trialResults.Count} trials saved to {resultDir}");
    }

    bool ValidateTrialSettings(out string reason)
    {
        reason = string.Empty;
        if (trialsPerModel < 1)
        {
            reason = "Trials Per Model must be at least 1.";
            return false;
        }
        if (stopCondition == WarehouseTestStopCondition.FixedCompletedTasks && completedTasksPerTrial < 1)
        {
            reason = "Completed Tasks Per Trial must be at least 1 for Fixed Completed Tasks.";
            return false;
        }
        if (stopCondition == WarehouseTestStopCondition.FixedDuration && durationSecondsPerTrial <= 0f)
        {
            reason = "Duration Seconds Per Trial must be greater than 0 for Fixed Duration.";
            return false;
        }
        if (safetyTimeoutSecondsPerTrial < 0f)
        {
            reason = "Safety Timeout Seconds Per Trial cannot be negative.";
            return false;
        }
        return true;
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

    bool TryResolveConfig(WarehouseExperimentProvenance provenance,
                          out WarehouseExperimentConfig config, out string reason)
    {
        config = null;
        reason = string.Empty;
        if (provenance == null)
        {
            reason = "Training provenance is missing.";
            return false;
        }

#if UNITY_EDITOR
        if (!string.IsNullOrEmpty(provenance.presetAssetGuid))
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(provenance.presetAssetGuid);
            if (!string.IsNullOrEmpty(assetPath))
            {
                WarehouseExperimentConfig asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WarehouseExperimentConfig>(assetPath);
                if (asset != null)
                {
                    config = asset;
                    return true;
                }
            }
        }

        string[] assetGuids = UnityEditor.AssetDatabase.FindAssets("t:WarehouseExperimentConfig");
        var projectConfigs = new List<WarehouseExperimentConfig>();
        foreach (string assetGuid in assetGuids)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(assetGuid);
            WarehouseExperimentConfig asset =
                UnityEditor.AssetDatabase.LoadAssetAtPath<WarehouseExperimentConfig>(assetPath);
            if (asset != null)
                projectConfigs.Add(asset);
        }

        if (TrySelectConfigCandidate(projectConfigs, provenance, "project assets", out config, out reason))
            return true;
        if (!string.IsNullOrEmpty(reason))
            return false;
#endif

        var fallbackConfigs = new List<WarehouseExperimentConfig>();
        if (configFallbacks != null)
        {
            foreach (WarehouseExperimentConfig candidate in configFallbacks)
                if (candidate != null) fallbackConfigs.Add(candidate);
        }

        if (TrySelectConfigCandidate(fallbackConfigs, provenance, "Config Fallbacks", out config, out reason))
            return true;
        if (!string.IsNullOrEmpty(reason))
            return false;

        reason = $"Could not resolve training preset '{provenance.presetAssetName}' " +
                 $"(ID: {provenance.presetId}). No matching project asset or Config Fallback was found.";
        return false;
    }

    static bool TrySelectConfigCandidate(IReadOnlyList<WarehouseExperimentConfig> availableConfigs,
                                         WarehouseExperimentProvenance provenance, string source,
                                         out WarehouseExperimentConfig config, out string reason)
    {
        config = null;
        reason = string.Empty;
        var candidates = new List<WarehouseExperimentConfig>();

        if (!string.IsNullOrEmpty(provenance.presetId))
        {
            foreach (WarehouseExperimentConfig candidate in availableConfigs)
                if (candidate != null && candidate.configId == provenance.presetId)
                    candidates.Add(candidate);
        }

        if (candidates.Count == 0 && !string.IsNullOrEmpty(provenance.presetAssetName))
        {
            foreach (WarehouseExperimentConfig candidate in availableConfigs)
                if (candidate != null && candidate.name == provenance.presetAssetName)
                    candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return false;
        if (candidates.Count == 1)
        {
            config = candidates[0];
            return true;
        }

        if (!string.IsNullOrEmpty(provenance.presetContentHash))
        {
            WarehouseExperimentConfig hashMatch = null;
            int hashMatchCount = 0;
            foreach (WarehouseExperimentConfig candidate in candidates)
            {
                if (WarehouseExperimentRuntime.GetConfigContentHash(candidate) != provenance.presetContentHash)
                    continue;
                hashMatch = candidate;
                hashMatchCount++;
            }

            if (hashMatchCount == 1)
            {
                config = hashMatch;
                return true;
            }
            if (hashMatchCount == 0)
            {
                reason = $"Multiple configs in {source} match preset ID/name, but none match the training content hash.";
                return false;
            }
        }

        reason = $"Multiple configs in {source} match training preset '{provenance.presetAssetName}' " +
                 $"(ID: {provenance.presetId}). Give every config a unique Config ID.";
        return false;
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
            stopCondition = stopCondition.ToString(),
            trialsPerModel = trialsPerModel,
            completedTasksPerTrial = completedTasksPerTrial,
            durationSecondsPerTrial = durationSecondsPerTrial,
            safetyTimeoutSecondsPerTrial = safetyTimeoutSecondsPerTrial,
            reseedEachTrial = reseedEachTrial,
            baseTrialSeed = baseTrialSeed,
            deterministicInference = deterministicInference,
            environmentInstanceCount = WarehouseExperimentRuntime.EnvironmentInstanceCount,
            effectiveAgentCount = agents.Length,
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

    static string BuildTrialCsv(IReadOnlyList<TrialResult> results)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("model,trial,trial_seed,stop_condition,target_completed_tasks,target_duration_seconds,completed_tasks,wall_collisions,agent_collisions,total_collisions,simulation_steps,simulation_seconds,wall_seconds,tasks_per_simulation_minute,collisions_per_task,stop_reason");
        foreach (TrialResult result in results)
        {
            sb.Append(EscapeCsv(result.model)).Append(',')
              .Append(result.trial).Append(',')
              .Append(result.trialSeed).Append(',')
              .Append(result.stopCondition).Append(',')
              .Append(result.targetCompletedTasks).Append(',')
              .Append(result.targetDurationSeconds.ToString("F3", c)).Append(',')
              .Append(result.completedTasks).Append(',')
              .Append(result.wallCollisions).Append(',')
              .Append(result.agentCollisions).Append(',')
              .Append(result.totalCollisions).Append(',')
              .Append(result.simulationSteps).Append(',')
              .Append(result.simulationSeconds.ToString("F3", c)).Append(',')
              .Append(result.wallSeconds.ToString("F3", c)).Append(',')
              .Append(result.tasksPerSimulationMinute.ToString("F6", c)).Append(',')
              .Append(result.collisionsPerTask.ToString("F6", c)).Append(',')
              .Append(result.stopReason).AppendLine();
        }
        return sb.ToString();
    }

    static string BuildSummaryCsv(IReadOnlyList<TrialResult> results)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("model,trials,stop_condition,mean_completed_tasks,std_completed_tasks,mean_simulation_seconds,std_simulation_seconds,mean_total_collisions,std_total_collisions,mean_tasks_per_simulation_minute,std_tasks_per_simulation_minute,mean_collisions_per_task,std_collisions_per_task");
        if (results.Count == 0)
            return sb.ToString();

        float meanCompleted = Mean(results, result => result.completedTasks);
        float meanSimulationSeconds = Mean(results, result => result.simulationSeconds);
        float meanCollisions = Mean(results, result => result.totalCollisions);
        float meanThroughput = Mean(results, result => result.tasksPerSimulationMinute);
        float meanCollisionsPerTask = Mean(results, result => result.collisionsPerTask);
        TrialResult first = results[0];

        sb.Append(EscapeCsv(first.model)).Append(',')
          .Append(results.Count).Append(',')
          .Append(first.stopCondition).Append(',')
          .Append(meanCompleted.ToString("F6", c)).Append(',')
          .Append(PopulationStandardDeviation(results, result => result.completedTasks, meanCompleted).ToString("F6", c)).Append(',')
          .Append(meanSimulationSeconds.ToString("F6", c)).Append(',')
          .Append(PopulationStandardDeviation(results, result => result.simulationSeconds, meanSimulationSeconds).ToString("F6", c)).Append(',')
          .Append(meanCollisions.ToString("F6", c)).Append(',')
          .Append(PopulationStandardDeviation(results, result => result.totalCollisions, meanCollisions).ToString("F6", c)).Append(',')
          .Append(meanThroughput.ToString("F6", c)).Append(',')
          .Append(PopulationStandardDeviation(results, result => result.tasksPerSimulationMinute, meanThroughput).ToString("F6", c)).Append(',')
          .Append(meanCollisionsPerTask.ToString("F6", c)).Append(',')
          .Append(PopulationStandardDeviation(results, result => result.collisionsPerTask, meanCollisionsPerTask).ToString("F6", c)).AppendLine();
        return sb.ToString();
    }

    static float Mean(IReadOnlyList<TrialResult> results, Func<TrialResult, float> selector)
    {
        float total = 0f;
        foreach (TrialResult result in results)
            total += selector(result);
        return total / results.Count;
    }

    static float PopulationStandardDeviation(IReadOnlyList<TrialResult> results,
                                             Func<TrialResult, float> selector, float mean)
    {
        float squaredDifferenceTotal = 0f;
        foreach (TrialResult result in results)
        {
            float difference = selector(result) - mean;
            squaredDifferenceTotal += difference * difference;
        }
        return Mathf.Sqrt(squaredDifferenceTotal / results.Count);
    }

    static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (!value.Contains(",") && !value.Contains("\"") && !value.Contains("\n") && !value.Contains("\r"))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
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
