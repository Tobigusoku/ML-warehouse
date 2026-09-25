using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Edit-mode regression checks; no trainer, model, saved scene, or result files are required.
public static class WarehouseStartupChecks
{
    private static readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();

    [MenuItem("Warehouse/Checks/Experiment Startup")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run startup checks outside Play Mode.");

        int passed = 0;
        try
        {
            Check(() =>
            {
                var config = Config(1);
                var first = Environment("First", config, 1);
                var second = Environment("Second", config, 1);
                string hash = WarehouseExperimentRuntime.GetConfigContentHash(config);
                Prepare(first, second);
                foreach (var g in new[] { first, second })
                {
                    Require(g.warehouseWidth == config.warehouseWidth, "Both generators receive config");
                    var m = Manager(g);
                    Require(m.robotAgents[0].maxSpeed == config.maxSpeed, "Both agents receive config");
                    Require(m.GetComponent<WarehousePheromone>().pheroQ == config.pheromoneSecretionAmount,
                        "Both maps receive config");
                }
                Require(hash == WarehouseExperimentRuntime.GetConfigContentHash(config), "Source asset is unchanged");
                var json = (string)Invoke("BuildJson", "check", config, true, "CheckScene");
                var record = JsonUtility.FromJson<WarehouseExperimentProvenance>(json);
                Require(record.environmentInstanceCount == 2 && record.totalAgentInstances == 2,
                    "Record contains process-local environment and agent totals");
                Require(record.environmentInstances.Length == 2 &&
                    record.environmentInstances[0].effectiveAgentCount == 1, "Per-environment actual counts");
                WarehouseExperimentRuntime.ApplyToEnvironment(first);
                Require(WarehouseExperimentRuntime.EnvironmentInstanceCount == 2, "Repeated apply is idempotent");
            }, ref passed);

            Check(() =>
            {
                var config = Config(2);
                var g = Environment("WrongCount", config, 1);
                ExpectRejected(() => Prepare(g), "effective agents=1");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("MissingConfig", null, 1);
                ExpectRejected(() => Prepare(g), "Experiment Config is empty");
            }, ref passed);

            Check(() =>
            {
                var a = Environment("A", Config(1), 1);
                var b = Environment("B", Config(1), 1);
                a.warehouseWidth = 123;
                ExpectRejected(() => Prepare(a, b), "same config asset");
                Require(a.warehouseWidth == 123, "Validation fails before applying any config");
            }, ref passed);

            Check(() =>
            {
                var config = Config(1);
                var a = Environment("A", config, 1);
                var b = Environment("B", config, 1);
                b.useExperimentConfig = false;
                ExpectRejected(() => Prepare(a, b), "same config asset");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("NullAgent", Config(1), 1);
                Manager(g).robotAgents[0] = null;
                ExpectRejected(() => Prepare(g), "missing, disabled, duplicate, or foreign");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("Duplicate", Config(2), 1);
                Manager(g).robotAgents.Add(Manager(g).robotAgents[0]);
                ExpectRejected(() => Prepare(g), "missing, disabled, duplicate, or foreign");
            }, ref passed);

            Check(() =>
            {
                var config = Config(1);
                var a = Environment("A", config, 1);
                var b = Environment("B", config, 1);
                Manager(a).robotAgents[0] = Manager(b).robotAgents[0];
                ExpectRejected(() => Prepare(a, b), "missing, disabled, duplicate, or foreign");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("Unlisted", Config(1), 2);
                Manager(g).robotAgents.RemoveAt(1);
                ExpectRejected(() => Prepare(g), "absent from the manager");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("Disabled", Config(1), 1);
                Manager(g).robotAgents[0].enabled = false;
                ExpectRejected(() => Prepare(g), "missing, disabled, duplicate, or foreign");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("AutoSpawn", Config(2), 0);
                Prepare(g);
                Require(Manager(g).autoSpawnCount == 2, "Config drives empty-list automatic spawning");
                ExpectRejected(() => Invoke("ValidateAgentRoster", g, Manager(g), false), "effective agents=0");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("Manual", Config(16), 1);
                g.useExperimentConfig = false;
                g.warehouseWidth = 37;
                Manager(g).robotAgents[0].maxSpeed = 7;
                Prepare(g);
                Require(g.warehouseWidth == 37 && Manager(g).robotAgents[0].maxSpeed == 7,
                    "Manual settings are preserved even if an unused config is assigned");
                Require(Manager(g).configuredAgentCount == 0, "No stale expected count in manual mode");
            }, ref passed);

            Check(() =>
            {
                var g = Environment("Reset", Config(1), 1);
                Prepare(g);
                Invoke("ResetRuntimeState");
                Require(WarehouseExperimentRuntime.EnvironmentInstanceCount == 0 &&
                    !WarehouseExperimentRuntime.IsReady && !WarehouseExperimentRuntime.Failed,
                    "Domain-reload-disabled startup clears all static state");
                Prepare(g);
                Require(WarehouseExperimentRuntime.EnvironmentInstanceCount == 1, "Next play can apply again");
            }, ref passed);

            Check(() =>
            {
                var shelfObject = new GameObject("Shelf");
                objects.Add(shelfObject);
                var initialCrate = Child(shelfObject, "Crate");
                var shelf = shelfObject.AddComponent<ShelfUnit>();
                shelf.CaptureInitialState();
                shelf.AddCrate(0, 10f);
                Require(shelf.GetCrateCount() == 2, "Task completion adds a transient crate");
                shelf.Highlight();
                shelf.ResetForEvaluationTrial();
                Require(shelf.GetCrateCount() == 1 && initialCrate.activeSelf,
                    "Trial reset preserves generated crates and removes task crates");
                Require(shelf.highlightCount == 0 && Mathf.Approximately(shelf.currentWeightKg, 10f),
                    "Trial reset restores shelf visual and load state");
            }, ref passed);

            Check(() =>
            {
                var agentObject = new GameObject("MetricAgent");
                objects.Add(agentObject);
                var agent = agentObject.AddComponent<WarehouseRobotAgent>();
                agent.completedCount = 4;
                agent.crashToWall = 3;
                agent.crashToAgent = 2;
                agent.totalMoveDistance = 99f;
                agent.ResetEvaluationMetrics();
                Require(agent.completedCount == 0 && agent.crashToWall == 0 &&
                    agent.crashToAgent == 0 && Mathf.Approximately(agent.totalMoveDistance, 0f),
                    "Trial metrics reset together");
            }, ref passed);

            Debug.Log($"[WarehouseStartupChecks] PASS: {passed} checks.");
        }
        finally
        {
            Cleanup();
        }
    }

    static void Check(Action test, ref int passed)
    {
        Cleanup();
        test();
        passed++;
    }

    static void Cleanup()
    {
        for (int i = objects.Count - 1; i >= 0; i--)
            if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
        objects.Clear();
        Invoke("ResetRuntimeState");
    }

    static WarehouseExperimentConfig Config(int count)
    {
        var c = ScriptableObject.CreateInstance<WarehouseExperimentConfig>();
        objects.Add(c);
        c.agentCount = count;
        c.warehouseWidth = 31;
        c.maxSpeed = 9;
        c.pheromoneSecretionAmount = 3;
        return c;
    }

    static WarehouseGenerator Environment(string name, WarehouseExperimentConfig config, int agents)
    {
        var root = new GameObject("StartupCheck_" + name);
        objects.Add(root);
        root.hideFlags = HideFlags.DontSave;
        var generator = Child(root, "Generator").AddComponent<WarehouseGenerator>();
        generator.useExperimentConfig = true;
        generator.experimentConfig = config;
        var manager = Child(root, "Manager").AddComponent<WarehouseTrainingManager>();
        manager.warehouseGenerator = generator;
        manager.envRoot = root.transform;
        manager.gameObject.AddComponent<WarehousePheromone>().warehouseGenerator = generator;
        for (int i = 0; i < agents; i++)
        {
            var agent = Child(root, "Agent" + i).AddComponent<WarehouseRobotAgent>();
            agent.trainingManager = manager;
            manager.robotAgents.Add(agent);
        }
        return generator;
    }

    static GameObject Child(GameObject root, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(root.transform, false);
        return child;
    }

    static WarehouseTrainingManager Manager(WarehouseGenerator g) =>
        g.transform.parent.GetComponentInChildren<WarehouseTrainingManager>();

    static void Prepare(params WarehouseGenerator[] generators) =>
        Invoke("PrepareEnvironments", new List<WarehouseGenerator>(generators));

    static object Invoke(string name, params object[] args)
    {
        try
        {
            return typeof(WarehouseExperimentRuntime).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, args);
        }
        catch (TargetInvocationException error)
        {
            throw error.InnerException ?? error;
        }
    }

    static void ExpectRejected(Action action, string expected)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            Require(error.Message.Contains(expected), "Expected rejection: " + expected + "; got " + error.Message);
            return;
        }
        throw new Exception("Expected startup rejection: " + expected);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
