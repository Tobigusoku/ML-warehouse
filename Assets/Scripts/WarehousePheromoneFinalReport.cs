using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Outputs final pheromone-map usage when play mode / test execution ends.
/// It is created automatically at runtime, so no scene setup is required.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class WarehousePheromoneFinalReport : MonoBehaviour
{
    private static bool created;
    private static bool emitted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateReporter()
    {
        if (created) return;
        created = true;
        emitted = false;

        var go = new GameObject("WarehousePheromoneFinalReport");
        DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<WarehousePheromoneFinalReport>();
    }

    void OnApplicationQuit()
    {
        Emit();
    }

    void OnDestroy()
    {
        Emit();
    }

    public static void EmitToDirectory(WarehousePheromone phero, string directory)
    {
        if (phero == null) return;

        Directory.CreateDirectory(directory);
        var stats = phero.GetUsageStats();
        string text = BuildText(stats);
        File.WriteAllText(Path.Combine(directory, "pheromone_final.txt"), text, Encoding.UTF8);
        File.WriteAllText(Path.Combine(directory, "pheromone_final.csv"), BuildCsv(stats), Encoding.UTF8);
        ExportRouteMaps(phero, Path.Combine(directory, "pheromone_routes"));
    }

    void Emit()
    {
        if (emitted) return;
        emitted = true;

        var runner = FindObjectOfType<WarehouseModelTestRunner>();
        if (runner == null || !runner.isTestMode)
            return;

        var phero = FindObjectOfType<WarehousePheromone>();
        if (phero == null)
        {
            Debug.LogWarning("[PheromoneFinalReport] WarehousePheromone not found.");
            return;
        }

        var stats = phero.GetUsageStats();
        string text = BuildText(stats);
        Debug.Log(text);

        string dir = Path.Combine(Application.dataPath, "..", "Logs");
        Directory.CreateDirectory(dir);

        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string txtPath = Path.GetFullPath(Path.Combine(dir, $"pheromone_final_{stamp}.txt"));
        string csvPath = Path.GetFullPath(Path.Combine(dir, $"pheromone_final_{stamp}.csv"));
        string routeDir = Path.GetFullPath(Path.Combine(dir, $"pheromone_routes_{stamp}"));

        File.WriteAllText(txtPath, text, Encoding.UTF8);
        File.WriteAllText(csvPath, BuildCsv(stats), Encoding.UTF8);
        ExportRouteMaps(phero, routeDir);

        Debug.Log($"[PheromoneFinalReport] saved: {txtPath}");
        Debug.Log($"[PheromoneFinalReport] saved: {csvPath}");
        Debug.Log($"[PheromoneFinalReport] route maps: {routeDir}");
    }

    static string BuildText(WarehousePheromone.PheromoneUsageStats s)
    {
        return
            "[PheromoneFinalReport]\n" +
            $"Grid: {s.gridW} x {s.gridD} = {s.cellCount} cells\n" +
            $"Route maps: {s.mapCount}\n" +
            $"Unique active cells: {s.uniqueActiveCells}/{s.cellCount} ({Pct(s.uniqueActiveCellRatio)})\n" +
            $"Active map-cells: {s.activeMapCells}/{s.totalMapCells} ({Pct(s.activeMapCellRatio)})\n" +
            $"Total pheromone: {s.total:F3}, max: {s.max:F3}";
    }

    static string BuildCsv(WarehousePheromone.PheromoneUsageStats s)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("grid_w,grid_d,cell_count,route_maps,total_map_cells,unique_active_cells,unique_active_cell_ratio,active_map_cells,active_map_cell_ratio,total_pheromone,max_pheromone");
        sb.Append(s.gridW).Append(',');
        sb.Append(s.gridD).Append(',');
        sb.Append(s.cellCount).Append(',');
        sb.Append(s.mapCount).Append(',');
        sb.Append(s.totalMapCells).Append(',');
        sb.Append(s.uniqueActiveCells).Append(',');
        sb.Append(s.uniqueActiveCellRatio.ToString(c)).Append(',');
        sb.Append(s.activeMapCells).Append(',');
        sb.Append(s.activeMapCellRatio.ToString(c)).Append(',');
        sb.Append(s.total.ToString(c)).Append(',');
        sb.Append(s.max.ToString(c));
        sb.AppendLine();
        return sb.ToString();
    }

    static string Pct(float value)
    {
        return (value * 100f).ToString("F2", CultureInfo.InvariantCulture) + "%";
    }

    static void ExportRouteMaps(WarehousePheromone phero, string dir)
    {
        var stats = phero.GetUsageStats();
        if (stats.cellCount <= 0 || stats.gridW <= 0 || stats.gridD <= 0)
            return;

        Directory.CreateDirectory(dir);

        float[] routeMap = new float[stats.cellCount];
        float globalRouteMax = 0f;
        var summary = new StringBuilder();
        summary.AppendLine("entrance,shelf,exit,total,max,active_cells,active_ratio,png,csv");

        for (int e = 0; e < phero.EntranceCount; e++)
        {
            for (int s = 0; s < phero.ShelfCount; s++)
            {
                for (int x = 0; x < phero.ExitCount; x++)
                {
                    if (!phero.TryGetRouteMap(e, s, x, routeMap))
                        continue;

                    var routeStats = phero.CalcValuesStats(routeMap);
                    if (routeStats.max > globalRouteMax)
                        globalRouteMax = routeStats.max;
                }
            }
        }

        for (int e = 0; e < phero.EntranceCount; e++)
        {
            for (int s = 0; s < phero.ShelfCount; s++)
            {
                for (int x = 0; x < phero.ExitCount; x++)
                {
                    if (!phero.TryGetRouteMap(e, s, x, routeMap))
                        continue;

                    var (total, max, active) = phero.CalcValuesStats(routeMap);
                    string baseName = $"route_e{e + 1}_s{s + 1}_x{x + 1}";
                    string pngName = baseName + ".png";
                    string csvName = baseName + ".csv";
                    string pngPath = Path.Combine(dir, pngName);
                    string csvPath = Path.Combine(dir, csvName);

                    WriteRoutePng(routeMap, stats.gridW, stats.gridD, globalRouteMax, pngPath);
                    WriteRouteCsv(routeMap, stats.gridW, stats.gridD, csvPath);

                    summary.Append(e + 1).Append(',');
                    summary.Append(s + 1).Append(',');
                    summary.Append(x + 1).Append(',');
                    summary.Append(total.ToString(CultureInfo.InvariantCulture)).Append(',');
                    summary.Append(max.ToString(CultureInfo.InvariantCulture)).Append(',');
                    summary.Append(active).Append(',');
                    summary.Append((stats.cellCount > 0 ? (float)active / stats.cellCount : 0f).ToString(CultureInfo.InvariantCulture)).Append(',');
                    summary.Append(pngName).Append(',');
                    summary.Append(csvName).AppendLine();
                }
            }
        }

        File.WriteAllText(Path.Combine(dir, "summary.csv"), summary.ToString(), Encoding.UTF8);
    }

    static void WriteRoutePng(float[] values, int gridW, int gridD, float max, string path)
    {
        const int scale = 24;
        int width = gridW * scale;
        int height = gridD * scale;
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

        for (int cj = 0; cj < gridD; cj++)
        {
            for (int ci = 0; ci < gridW; ci++)
            {
                float v = values[ci * gridD + cj];
                float t = max > 0f ? Mathf.Clamp01(v / max) : 0f;
                Color color = HeatColor(t);

                int px0 = ci * scale;
                int py0 = (gridD - 1 - cj) * scale;
                for (int py = 0; py < scale; py++)
                {
                    for (int px = 0; px < scale; px++)
                    {
                        bool gridLine = px == 0 || py == 0;
                        tex.SetPixel(px0 + px, py0 + py, gridLine ? new Color(0.15f, 0.15f, 0.15f) : color);
                    }
                }
            }
        }

        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.Destroy(tex);
    }

    static Color HeatColor(float t)
    {
        if (t <= 0f) return new Color(0.04f, 0.04f, 0.04f, 1f);
        if (t < 0.5f)
            return Color.Lerp(new Color(0.05f, 0.20f, 0.85f, 1f),
                              new Color(1.00f, 0.95f, 0.05f, 1f), t * 2f);
        return Color.Lerp(new Color(1.00f, 0.95f, 0.05f, 1f),
                          new Color(1.00f, 0.05f, 0.02f, 1f), (t - 0.5f) * 2f);
    }

    static void WriteRouteCsv(float[] values, int gridW, int gridD, string path)
    {
        var sb = new StringBuilder();
        var c = CultureInfo.InvariantCulture;

        for (int cj = gridD - 1; cj >= 0; cj--)
        {
            for (int ci = 0; ci < gridW; ci++)
            {
                if (ci > 0) sb.Append(',');
                sb.Append(values[ci * gridD + cj].ToString("F3", c));
            }
            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }
}
