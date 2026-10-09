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
                var g = Environment("GrowCount", config, 1);
                Prepare(g);
                var manager = Manager(g);
                manager.robotAgents[0].transform.localScale = new Vector3(1.1f, 0.5f, 0.9f);
                Require(manager.configuredAgentCount == 2 && manager.robotAgents.Count == 1,
                    "Config records the desired count until runtime spawning");
                InvokeInstance(manager, "ReconcileConfiguredAgentCount");
                Require(manager.robotAgents.Count == 2 && manager.robotAgents.TrueForAll(a => a.gameObject.activeInHierarchy),
                    "Config spawns missing agents at runtime");
                Require(manager.robotAgents[1].transform.localScale == manager.robotPrefab.transform.localScale,
                    "Spawned agents preserve the configured prefab's physical size");
                Require(manager.robotAgents[1].GetComponent<MeshFilter>().sharedMesh ==
                        manager.robotPrefab.GetComponent<MeshFilter>().sharedMesh &&
                        manager.robotAgents[1].GetComponent<WarehousePheromoneDebugger>() != null,
                    "Spawned agents preserve the prefab mesh and components");
                Invoke("ValidateAgentRoster", g, manager, false);
            }, ref passed);

            Check(() =>
            {
                var g = Environment("ShrinkCount", Config(1), 2);
                WarehouseRobotAgent extra = Manager(g).robotAgents[1];
                Prepare(g);
                Require(Manager(g).robotAgents.Count == 1 && !extra.gameObject.activeSelf,
                    "Config disables excess agents for the play session");
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
                Require(Manager(g).configuredAgentCount == 2, "Config drives empty-list automatic spawning");
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
                Child(shelfObject, "Crate");
                InvokeInstance(shelf, "ScanExistingCrates");
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
                agent.crashToShelf = 5;
                agent.crashToAgent = 2;
                agent.totalMoveDistance = 99f;
                agent.ResetEvaluationMetrics();
                Require(agent.completedCount == 0 && agent.crashToWall == 0 &&
                    agent.crashToShelf == 0 && agent.crashToAgent == 0 &&
                    Mathf.Approximately(agent.totalMoveDistance, 0f),
                    "Trial metrics reset together");
            }, ref passed);

            Check(() =>
            {
                var agentObject = new GameObject("ShelfCollisionAgent");
                var targetObject = new GameObject("TargetShelf");
                var otherObject = new GameObject("OtherShelf");
                objects.Add(agentObject);
                objects.Add(targetObject);
                objects.Add(otherObject);
                var agent = agentObject.AddComponent<WarehouseRobotAgent>();
                var target = targetObject.AddComponent<ShelfUnit>();
                var other = otherObject.AddComponent<ShelfUnit>();
                agent.targetShelfTransform = target.transform;

                Require(!(bool)InvokeInstance(agent, "ShouldCountShelfCollision", target),
                    "Delivering contact with the target shelf is excluded");
                Require((bool)InvokeInstance(agent, "ShouldCountShelfCollision", other),
                    "Delivering contact with another shelf is counted");

                typeof(WarehouseRobotAgent)
                    .GetField("currentPhase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(agent, WarehouseRobotAgent.Phase.Returning);
                Require((bool)InvokeInstance(agent, "ShouldCountShelfCollision", target),
                    "Returning contact with the former target shelf is counted");

                Require((bool)InvokeInstance(agent, "RegisterShelfContact", other),
                    "First contact with a shelf is countable");
                Require(!(bool)InvokeInstance(agent, "RegisterShelfContact", other),
                    "Continuous contact with the same shelf is deduplicated");
                InvokeInstance(agent, "UnregisterShelfContact", other);
                InvokeInstance(agent, "UnregisterShelfContact", other);
                Require((bool)InvokeInstance(agent, "RegisterShelfContact", other),
                    "A new contact after exit is countable");
            }, ref passed);

            Check(() =>
            {
                Vector3 center = Vector3.zero;
                Vector3 direction = Vector3.right;
                const float halfWidth = 5f;

                Vector3 pointInside = (Vector3)InvokeManagerStatic(
                    "ClosestPointOnSegment", center, direction, halfWidth, new Vector3(2f, 0f, 3f));
                Vector3 pointPastEnd = (Vector3)InvokeManagerStatic(
                    "ClosestPointOnSegment", center, direction, halfWidth, new Vector3(8f, 0f, 3f));
                Vector3 pointPastStart = (Vector3)InvokeManagerStatic(
                    "ClosestPointOnSegment", center, direction, halfWidth, new Vector3(-8f, 0f, 3f));

                Require(Vector3.Distance(pointInside, new Vector3(2f, 0f, 0f)) < 0.001f,
                    "Gate goal uses the perpendicular closest point inside the segment");
                Require(Vector3.Distance(pointPastEnd, new Vector3(5f, 0f, 0f)) < 0.001f &&
                        Vector3.Distance(pointPastStart, new Vector3(-5f, 0f, 0f)) < 0.001f,
                    "Gate goal clamps the closest point to both segment ends");
            }, ref passed);

            Check(() =>
            {
                Require((int)WarehousePheromoneMode.TaskSeparated == 3,
                    "TaskSeparated keeps its serialized numeric value");
                Require(Array.IndexOf(Enum.GetNames(typeof(WarehousePheromoneMode)), "PhaseSeparated") < 0,
                    "Unused PhaseSeparated mode is removed");
                Require((int)WarehousePheromoneMode.SubtaskSeparated == 4,
                    "SubtaskSeparated has a new non-conflicting serialized value");

                var pheromoneObject = new GameObject("SubtaskPheromone");
                objects.Add(pheromoneObject);
                var phero = pheromoneObject.AddComponent<WarehousePheromone>();
                phero.pheromoneMode = WarehousePheromoneMode.SubtaskSeparated;
                phero.pheromoneContent = WarehousePheromoneContent.Scalar;
                phero.cellSize = 2f;
                SetField(phero, "entranceCount", 2);
                SetField(phero, "exitCount", 2);
                SetField(phero, "shelfCount", 2);
                SetField(phero, "gridW", 1);
                SetField(phero, "gridD", 1);
                SetField(phero, "cellCount", 1);
                SetField(phero, "genTransform", pheromoneObject.transform);
                SetField(phero, "initialized", true);
                InvokeInstance(phero, "AllocateMaps");

                phero.StepPheromone(Vector3.zero, true, 0, 0, 0);
                Require(phero.GetValue(Vector3.zero, true, 0, 0, 1) > 0f,
                    "Delivering subtask map is shared across destination exits");
                Require(Mathf.Approximately(phero.GetValue(Vector3.zero, true, 1, 0, 0), 0f),
                    "Different entrance-to-shelf subtasks remain isolated");

                phero.StepPheromone(Vector3.zero, false, 1, 0, 1);
                Require(phero.GetValue(Vector3.zero, false, 0, 0, 1) > 0f,
                    "Returning subtask map is shared across spawn entrances");

                phero.ResetAll();
                phero.pheromoneContent = WarehousePheromoneContent.Directional;
                phero.observationFormat = WarehousePheromoneObservationFormat.VectorField27;
                float reward = phero.StepPheromone(
                    Vector3.zero, Vector3.right, true, 0, 0, 0);
                float[] observation = phero.GetPheromoneObservationList(
                    Vector3.zero, true, 0, 0, 1, pheromoneObject.transform);
                Require(Mathf.Approximately(reward, 0f),
                    "Directional pheromone does not add a pheromone reward");
                Require(observation.Length == 27 && observation[0] > 0f &&
                        observation[1] > 0.99f && Mathf.Abs(observation[2]) < 0.001f,
                    "Directional observation contains normalized strength and local movement direction");
            }, ref passed);

            Check(() =>
            {
                var firstObject = new GameObject("FirstHud");
                var secondObject = new GameObject("SecondHud");
                var pheromoneObject = new GameObject("PheromoneVizSelection");
                objects.Add(firstObject);
                objects.Add(secondObject);
                objects.Add(pheromoneObject);

                var ownerField = typeof(WarehousePheromoneDebugger)
                    .GetField("activeHudOwner", BindingFlags.Static | BindingFlags.NonPublic);
                ownerField.SetValue(null, null);

                var firstHud = firstObject.AddComponent<WarehousePheromoneDebugger>();
                var secondHud = secondObject.AddComponent<WarehousePheromoneDebugger>();
                Require((bool)InvokeInstance(firstHud, "IsPrimaryHud"),
                    "The first active pheromone HUD becomes the single screen owner");
                Require(!(bool)InvokeInstance(secondHud, "IsPrimaryHud"),
                    "Additional agent HUDs do not draw over the owner");

                var phero = pheromoneObject.AddComponent<WarehousePheromone>();
                phero.SetVizRoute(1, 2, 3);
                Require((bool)typeof(WarehousePheromone)
                        .GetField("visualizeExactRoute", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(phero),
                    "Complete-route selection enables exact-layer visualization");
                phero.SetVizTarget(true, 1, null);
                Require(!(bool)typeof(WarehousePheromone)
                        .GetField("visualizeExactRoute", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(phero),
                    "Aggregate selection exits exact-layer visualization");

                ownerField.SetValue(null, null);
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
        manager.robotPrefab = AssetDatabase.LoadAssetAtPath<WarehouseRobotAgent>("Assets/Prefab/Agent.prefab");
        Require(manager.robotPrefab != null, "Agent prefab is available for automatic spawning");
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

    static object InvokeInstance(object target, string name, params object[] args)
    {
        try
        {
            return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, args);
        }
        catch (TargetInvocationException error)
        {
            throw error.InnerException ?? error;
        }
    }

    static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    static object InvokeManagerStatic(string name, params object[] args)
    {
        try
        {
            return typeof(WarehouseTrainingManager)
                .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
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
