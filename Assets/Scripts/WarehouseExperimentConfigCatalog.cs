using System;
using System.Collections.Generic;
using UnityEngine;

// A Resources asset keeps all referenced configs and their environment presets in one Player build.
[CreateAssetMenu(fileName = "WarehouseExperimentConfigCatalog", menuName = "Warehouse/Experiment Config Catalog")]
public class WarehouseExperimentConfigCatalog : ScriptableObject
{
    public WarehouseExperimentConfig[] configs;

    public WarehouseExperimentConfig Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Experiment config ID is empty.");
        if (configs == null || configs.Length == 0)
            throw new InvalidOperationException("Experiment config catalog has no configs.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        WarehouseExperimentConfig selected = null;
        foreach (WarehouseExperimentConfig config in configs)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.configId))
                throw new InvalidOperationException("Experiment config catalog has an empty entry or ID.");
            if (!ids.Add(config.configId))
                throw new InvalidOperationException($"Duplicate experiment config ID in catalog: {config.configId}");
            if (string.Equals(config.configId, id, StringComparison.Ordinal)) selected = config;
        }
        if (selected == null)
            throw new InvalidOperationException($"Experiment config ID is not in this Player build: {id}");
        return selected;
    }
}
