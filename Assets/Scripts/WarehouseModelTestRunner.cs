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

/// <summary>
/// Runs inference tests for a list of ONNX assets and writes one CSV per model.
/// Attach this component to the environment prefab.
/// </summary>
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

    [Header("Test control")]
    public bool isTestMode = false;
    public bool runOnPlay = true;
    public bool stopPlayModeWhenFinished = true;
    public bool deterministicInference = true;

    private WarehouseTrainingManager manager;
    private WarehouseRobotAgent[] agents;
    private bool running;

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

    void Start()
    {
        if (isTestMode && runOnPlay)
            StartCoroutine(RunAllModelsAfterInitialization());
    }

    public void RunTests()
    {
        isTestMode = true;
        if (!running)
            StartCoroutine(RunAllModelsAfterInitialization());
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

        foreach (var model in models)
        {
            if (model == null) continue;
            yield return RunOneModel(model);
        }

        running = false;
        Debug.Log("[ModelTestRunner] All model tests finished. CSV and pheromone reports have been saved.");

        if (stopPlayModeWhenFinished)
        {
            // Give Unity one frame to finish file writes and display the completion log.
            yield return null;
            StopPlayMode();
        }
    }

    IEnumerator RunOneModel(NNModel model)
    {
        ApplyModel(model);
        ResetCounters();

        foreach (var agent in agents)
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

        string resultDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", resultsFolder, model.name));
        Directory.CreateDirectory(resultDir);
        File.WriteAllText(Path.Combine(resultDir, "logTest.csv"), BuildCsv(result), Encoding.UTF8);

        if (phero != null)
            WarehousePheromoneFinalReport.EmitToDirectory(phero, resultDir);

        Debug.Log($"[ModelTestRunner] {model.name}: completed={result.completedEpisodes}, " +
                  $"collisions={result.totalCollisions}, saved={resultDir}");
        yield return new WaitForSecondsRealtime(0.1f);
    }

    void ApplyModel(NNModel model)
    {
        foreach (var agent in agents)
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
        foreach (var agent in agents)
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
        foreach (var agent in agents)
            if (agent != null) total += agent.completedCount;
        return total;
    }

    int SumWallCollisions()
    {
        int total = 0;
        foreach (var agent in agents)
            if (agent != null) total += agent.crashToWall;
        return total;
    }

    int SumAgentCollisions()
    {
        int total = 0;
        foreach (var agent in agents)
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
