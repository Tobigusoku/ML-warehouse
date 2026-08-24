using UnityEngine;
using System.Collections.Generic;
using System.Threading;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 倉庫ロボット訓練マネージャー (マルチエージェント対応)
///
/// ■ セットアップ:
///   【単一エージェント】 robotAgents に1つ登録
///   【マルチエージェント】 robotAgents に複数登録 or autoSpawnCount > 0
///
/// ■ フェロモン連携:
///   ResetEpisode が agent.spawnEntranceIndex をセットする。
///   GetRandomEntranceWithIndex で出口インデックスも返す。
///   これにより WarehousePheromone が
///   「入口 × 棚 × 出口」の3軸でルートを学習できる。
/// </summary>
public class WarehouseTrainingManager : MonoBehaviour
{
    // ==========================================
    //  Inspector 設定
    // ==========================================
    [Header("===== 参照 =====")]
    public WarehouseGenerator warehouseGenerator;

    [Tooltip("手動配置のロボット (複数可)")]
    public List<WarehouseRobotAgent> robotAgents = new List<WarehouseRobotAgent>();

    [Tooltip("環境のルートTransform (未指定なら自動検出)")]
    public Transform envRoot;

    [Header("===== ロボット自動生成 =====")]
    [Tooltip("自動生成するロボット数 (0=手動配置のみ)")]
    public int autoSpawnCount = 0;
    public Vector3 robotSize  = new Vector3(0.8f, 0.5f, 1.0f);
    public Color   robotColor = new Color(0.2f, 0.6f, 0.9f);

    [Header("===== マーカー =====")]
    public Color shelfMarkerColor = new Color(0.9f, 0.2f, 0.1f, 0.8f);
    public Color exitMarkerColor  = new Color(0.1f, 0.9f, 0.2f, 0.8f);
    public float markerSize = 1.2f;

    [Header("===== マルチエージェント =====")]
    [Tooltip("同じ棚を複数のエージェントに割り当てない")]
    public bool avoidDuplicateShelves = true;

    [Header("===== スポーン設定 =====")]
    [Tooltip("スロット間隔 (エージェント1台分の幅)")]
    public float slotSpacing = 1.5f;

    [HideInInspector]
    public int configuredAgentCount = 0;

    // ==========================================
    //  入口情報
    // ==========================================
    private struct EntranceInfo
    {
        public Vector3 center;
        public Vector3 spreadDir;
        public float   halfWidth;
    }

    private struct SpawnSlot
    {
        public int    entranceIndex;
        public Vector3 position;
    }

    // ==========================================
    //  Per-Agent 状態
    // ==========================================
    private class AgentState
    {
        public ShelfUnit targetShelf;
        public GameObject shelfMarker;
        public GameObject exitMarker;
        public int spawnSlotIndex = -1;
    }

    // ==========================================
    //  内部変数
    // ==========================================
    private List<EntranceInfo>                       entrances     = new List<EntranceInfo>();
    private List<SpawnSlot>                          allSlots      = new List<SpawnSlot>();
    private HashSet<int>                             occupiedSlots = new HashSet<int>();
    private List<ShelfUnit>                          allShelves    = new List<ShelfUnit>();
    private Dictionary<WarehouseRobotAgent, AgentState> agentStates
        = new Dictionary<WarehouseRobotAgent, AgentState>();
    private HashSet<ShelfUnit> assignedShelves = new HashSet<ShelfUnit>();
    private bool initialized = false;

    // ==========================================
    //  初期化
    // ==========================================
    void Start()
    {
        Invoke(nameof(LateInit), 0.15f);
    }

    void Update()
    {
        // Test execution is handled by WarehouseModelTestRunner.
    }

    void EnsureInitialized()
    {
        if (initialized) return;

        if (envRoot == null)
        {
            if (warehouseGenerator != null && warehouseGenerator.transform.parent != null)
                envRoot = warehouseGenerator.transform.parent;
            else if (transform.parent != null)
                envRoot = transform.parent;
            else
                envRoot = transform;
        }

        if (warehouseGenerator != null && !warehouseGenerator.isGenerated)
        {
            if (WarehousePerformance.IsEnabled(p => p.DebugLog))
                Debug.Log("[TrainingManager] Generator未生成 — 強制生成を実行");
            warehouseGenerator.Generate();
        }

        if (entrances.Count == 0) CalculateEntrances();
        if (allSlots.Count  == 0) GenerateSlots();
        if (allShelves.Count == 0) CollectShelves();

        initialized = true;
    }

    void LateInit()
    {
        EnsureInitialized();

        if (autoSpawnCount > 0 && robotAgents.Count == 0)
        {
            for (int i = 0; i < autoSpawnCount; i++)
                robotAgents.Add(SpawnRobot(i));
        }

        foreach (var agent in robotAgents)
        {
            if (agent == null) continue;
            EnsureAgentRegistered(agent);
        }

        foreach (var agent in robotAgents)
        {
            if (agent == null) continue;
            if (!agentStates.ContainsKey(agent)) continue;

            var state = agentStates[agent];
            if (state.targetShelf == null && agent.targetShelfTransform == null)
            {
                if (WarehousePerformance.IsEnabled(p => p.DebugLog))
                    Debug.Log($"[TrainingManager] {agent.name} の初回配置を補完");
                ResetEpisode(agent);
            }
        }

        if (WarehousePerformance.IsEnabled(p => p.DebugLog))
            Debug.Log($"[TrainingManager] 初期化完了 — " +
                      $"入口:{entrances.Count}, 棚:{allShelves.Count}, " +
                      $"スロット:{allSlots.Count}, ロボット:{robotAgents.Count}");
    }

    void EnsureAgentRegistered(WarehouseRobotAgent agent)
    {
        if (agentStates.ContainsKey(agent)) return;

        agent.trainingManager = this;
        agent.envRoot         = envRoot;

        var phero = GetComponent<WarehousePheromone>();
        if (phero != null)
        {
            agent.pheromone = phero;
            phero.EnsureInitialized();
        }

        if (warehouseGenerator != null)
            agent.SetFieldSize(warehouseGenerator.warehouseWidth,
                               warehouseGenerator.warehouseDepth);

        var state = new AgentState();
        if (WarehousePerformance.IsEnabled(p => p.Markers))
        {
            state.shelfMarker = CreateMarker($"ShelfMarker_{agent.name}", shelfMarkerColor);
            state.exitMarker  = CreateMarker($"ExitMarker_{agent.name}",  exitMarkerColor);
            SetMarkerVisible(state.shelfMarker, false);
            SetMarkerVisible(state.exitMarker,  false);
        }
        agentStates[agent] = state;

        if (!robotAgents.Contains(agent))
            robotAgents.Add(agent);
    }

    // ==========================================
    //  入口情報の計算
    //  ※順序は WarehousePheromone.CalcEntranceCount と一致させること:
    //    West → East → South → North
    // ==========================================
    void CalculateEntrances()
    {
        entrances.Clear();

        if (warehouseGenerator == null)
        {
            entrances.Add(new EntranceInfo {
                center    = envRoot.TransformPoint(new Vector3(-2f, 0.3f, 20f)),
                spreadDir = envRoot.TransformDirection(Vector3.forward),
                halfWidth = 2f
            });
            return;
        }

        Transform genTf = warehouseGenerator.transform;
        float hw  = warehouseGenerator.warehouseWidth;
        float hd  = warehouseGenerator.warehouseDepth;
        float y   = robotSize.y / 2f + 0.05f;
        float pd  = warehouseGenerator.entrancePlatformDepth;
        float depthOffset = pd / 2f;
        float dw  = warehouseGenerator.doorWidth;
        float margin     = 0.5f;
        float usableHalf = Mathf.Max(0f, dw / 2f - margin);

        // West (index 0 if doorWest)
        if (warehouseGenerator.doorWest)
            entrances.Add(new EntranceInfo {
                center    = genTf.TransformPoint(new Vector3(-depthOffset, y, hd / 2f)),
                spreadDir = genTf.TransformDirection(Vector3.forward),
                halfWidth = usableHalf
            });

        // East (index 0 or 1)
        if (warehouseGenerator.doorEast)
            entrances.Add(new EntranceInfo {
                center    = genTf.TransformPoint(new Vector3(hw + depthOffset, y, hd / 2f)),
                spreadDir = genTf.TransformDirection(Vector3.forward),
                halfWidth = usableHalf
            });

        // South
        if (warehouseGenerator.doorSouth)
            entrances.Add(new EntranceInfo {
                center    = genTf.TransformPoint(new Vector3(hw / 2f, y, -depthOffset)),
                spreadDir = genTf.TransformDirection(Vector3.right),
                halfWidth = usableHalf
            });

        // North
        if (warehouseGenerator.doorNorth)
            entrances.Add(new EntranceInfo {
                center    = genTf.TransformPoint(new Vector3(hw / 2f, y, hd + depthOffset)),
                spreadDir = genTf.TransformDirection(Vector3.right),
                halfWidth = usableHalf
            });

        if (entrances.Count == 0)
        {
            entrances.Add(new EntranceInfo {
                center    = genTf.TransformPoint(new Vector3(-depthOffset, y, hd / 2f)),
                spreadDir = genTf.TransformDirection(Vector3.forward),
                halfWidth = usableHalf
            });
            Debug.LogWarning("[TrainingManager] 入口が見つかりません。フォールバック設定。");
        }
    }

    // ==========================================
    //  スロット生成
    // ==========================================
    void GenerateSlots()
    {
        allSlots.Clear();
        occupiedSlots.Clear();

        for (int e = 0; e < entrances.Count; e++)
        {
            var  ent     = entrances[e];
            float spacing = Mathf.Max(0.5f, slotSpacing);
            int  halfCount = Mathf.FloorToInt(ent.halfWidth / spacing);

            for (int i = -halfCount; i <= halfCount; i++)
            {
                float offset = i * spacing;
                allSlots.Add(new SpawnSlot {
                    entranceIndex = e,
                    position      = ent.center + ent.spreadDir * offset
                });
            }
        }

        if (WarehousePerformance.IsEnabled(p => p.DebugLog))
            Debug.Log($"[TrainingManager] スポーンスロット {allSlots.Count} 個を生成 " +
                      $"(入口 {entrances.Count} 箇所)");
    }

    // ==========================================
    //  スロット割り当て
    // ==========================================

    int PickFreeSlot()
    {
        List<int> freeIndices = new List<int>();
        for (int i = 0; i < allSlots.Count; i++)
        {
            if (!occupiedSlots.Contains(i))
                freeIndices.Add(i);
        }
        if (freeIndices.Count == 0) return -1;
        return freeIndices[Random.Range(0, freeIndices.Count)];
    }

    void ReleaseSlot(AgentState state)
    {
        if (state.spawnSlotIndex >= 0)
        {
            occupiedSlots.Remove(state.spawnSlotIndex);
            state.spawnSlotIndex = -1;
        }
    }

    // ==========================================
    //  棚の収集
    // ==========================================
    void CollectShelves()
    {
        allShelves.Clear();
        if (warehouseGenerator != null)
            allShelves.AddRange(warehouseGenerator.GetComponentsInChildren<ShelfUnit>());
        else
            allShelves.AddRange(FindObjectsOfType<ShelfUnit>());

        if (allShelves.Count == 0)
            Debug.LogWarning("[TrainingManager] ShelfUnit が見つかりません。");
    }

    // ==========================================
    //  エピソードリセット
    // ==========================================
    public void ResetEpisode(WarehouseRobotAgent agent)
    {
        EnsureInitialized();
        EnsureAgentRegistered(agent);

        var state = agentStates[agent];

        if (allShelves.Count == 0 || allShelves[0] == null)
        {
            if (warehouseGenerator != null && !warehouseGenerator.isGenerated)
                warehouseGenerator.Generate();
            CollectShelves();
        }

        // --- 1. スロットベースのスポーン ---
        ReleaseSlot(state);
        int slotIdx = PickFreeSlot();

        Vector3 spawnPos;
        int     entranceIdx;

        if (slotIdx >= 0)
        {
            state.spawnSlotIndex = slotIdx;
            occupiedSlots.Add(slotIdx);
            spawnPos    = allSlots[slotIdx].position;
            entranceIdx = allSlots[slotIdx].entranceIndex;
        }
        else
        {
            Debug.LogWarning($"[TrainingManager] 空きスロットなし。{agent.name} を入口中心に配置。");
            entranceIdx = Random.Range(0, entrances.Count);
            spawnPos    = entrances[entranceIdx].center;
        }

        agent.transform.position = spawnPos;

        // ==========================================
        //  フェロモン連携: スポーン入口インデックスをエージェントに渡す
        //  WarehousePheromone.StepPheromone (DELIVERING フェーズ) が
        //  pheroDelivering[entranceIdx * shelfCount + shelfIdx] に記録する。
        // ==========================================
        agent.spawnEntranceIndex = entranceIdx;

        // 倉庫の中心を向く
        Transform genTf = warehouseGenerator != null ? warehouseGenerator.transform : envRoot;
        float hw = warehouseGenerator != null ? warehouseGenerator.warehouseWidth  : 30f;
        float hd = warehouseGenerator != null ? warehouseGenerator.warehouseDepth  : 40f;
        Vector3 center  = genTf.TransformPoint(
            new Vector3(hw / 2f, spawnPos.y - genTf.position.y, hd / 2f));
        Vector3 lookDir = center - spawnPos;
        lookDir.y = 0f;
        if (lookDir.magnitude > 0.1f)
            agent.transform.rotation = Quaternion.LookRotation(lookDir);

        // --- 2. ターゲット棚をランダム選択 ---
        if (allShelves.Count > 0)
        {
            if (state.targetShelf != null)
            {
                state.targetShelf.Unhighlight();
                assignedShelves.Remove(state.targetShelf);
            }

            ShelfUnit chosen = PickShelf();
            state.targetShelf           = chosen;
            agent.targetShelfTransform  = chosen != null ? chosen.transform : null;

            if (chosen != null)
            {
                if (avoidDuplicateShelves) assignedShelves.Add(chosen);

                if (state.shelfMarker != null)
                {
                    state.shelfMarker.transform.position = CalculateShelfMarkerPosition(chosen);
                    SetMarkerVisible(state.shelfMarker, true);
                }
                chosen.Highlight();
            }
        }

        SetMarkerVisible(state.exitMarker, false);

        if (agent.targetShelfTransform == null)
        {
            CollectShelves();
            if (allShelves.Count > 0)
            {
                ShelfUnit retry = PickShelf();
                if (retry != null)
                {
                    state.targetShelf          = retry;
                    agent.targetShelfTransform = retry.transform;
                    if (avoidDuplicateShelves) assignedShelves.Add(retry);
                    if (state.shelfMarker != null)
                    {
                        state.shelfMarker.transform.position = CalculateShelfMarkerPosition(retry);
                        SetMarkerVisible(state.shelfMarker, true);
                    }
                    retry.Highlight();
                }
            }

            if (agent.targetShelfTransform == null)
                Debug.LogWarning($"[TrainingManager] {agent.name}: 棚の割り当てに失敗しました " +
                                 $"(allShelves={allShelves.Count})");
        }
    }

    ShelfUnit PickShelf()
    {
        allShelves.RemoveAll(s => s == null);
        if (allShelves.Count == 0) return null;

        if (!avoidDuplicateShelves || assignedShelves.Count == 0)
            return allShelves[Random.Range(0, allShelves.Count)];

        List<ShelfUnit> available = new List<ShelfUnit>();
        foreach (var shelf in allShelves)
        {
            if (shelf != null && !assignedShelves.Contains(shelf))
                available.Add(shelf);
        }

        return available.Count > 0
            ? available[Random.Range(0, available.Count)]
            : allShelves[Random.Range(0, allShelves.Count)];
    }

    // ==========================================
    //  荷物を置いた時の処理
    // ==========================================
    public void OnCargoDropped(WarehouseRobotAgent agent, Vector3 exitPosition)
    {
        EnsureAgentRegistered(agent);
        if (!agentStates.ContainsKey(agent)) return;
        var state = agentStates[agent];

        SetMarkerVisible(state.shelfMarker, false);

        if (state.targetShelf != null)
        {
            state.targetShelf.Unhighlight();
            assignedShelves.Remove(state.targetShelf);
            state.targetShelf = null;
        }

        if (state.exitMarker != null)
        {
            state.exitMarker.transform.position = exitPosition;
            SetMarkerVisible(state.exitMarker, true);
        }
    }

    // ==========================================
    //  入口情報 (Agent から呼ばれる)
    // ==========================================

    /// <summary>
    /// ランダムな入口の座標を返す。
    /// </summary>
    public Vector3 GetRandomEntrance()
    {
        EnsureInitialized();
        if (entrances.Count == 0)
            return envRoot != null ? envRoot.position : Vector3.zero;
        return entrances[Random.Range(0, entrances.Count)].center;
    }

    /// <summary>
    /// ランダムな入口の座標とインデックスを返す。
    /// WarehouseRobotAgent.CompleteDrop / OnEpisodeBegin から呼ばれ、
    /// agent.targetExitIndex に格納されて
    /// WarehousePheromone.StepPheromone (RETURNING フェーズ) に渡される。
    /// </summary>
    public (Vector3 pos, int index) GetRandomEntranceWithIndex()
    {
        EnsureInitialized();
        if (entrances.Count == 0)
        {
            Vector3 fallback = envRoot != null ? envRoot.position : Vector3.zero;
            return (fallback, 0);
        }
        int idx = Random.Range(0, entrances.Count);
        return (entrances[idx].center, idx);
    }

    /// <summary>
    /// 指定位置に最も近い入口の座標を返す。
    /// </summary>
    public Vector3 GetNearestEntrance(Vector3 fromPos)
    {
        EnsureInitialized();
        if (entrances.Count == 0)
            return envRoot != null ? envRoot.position : fromPos;

        Vector3 nearest = entrances[0].center;
        float   minDist = float.MaxValue;
        foreach (var ent in entrances)
        {
            float d = Vector3.Distance(fromPos, ent.center);
            if (d < minDist) { minDist = d; nearest = ent.center; }
        }
        return nearest;
    }

    /// <summary>
    /// 入口の総数を返す (WarehousePheromone と同じ計算)。
    /// </summary>
    public int GetEntranceCount()
    {
        EnsureInitialized();
        return entrances.Count;
    }

    // ==========================================
    //  棚マーカー位置
    // ==========================================
    Vector3 CalculateShelfMarkerPosition(ShelfUnit shelf)
    {
        Vector3 shelfPos = shelf.transform.position;
        float d = shelf.depth;
        float w = shelf.width;
        float centerZ     = shelfPos.z + w / 2f;
        float aisleOffset = 1.5f;
        float markerX     = shelf.sideIndex == 0
            ? shelfPos.x - aisleOffset
            : shelfPos.x + d + aisleOffset;

        float groundY = envRoot != null ? envRoot.position.y : 0f;
        return new Vector3(markerX, groundY, centerZ);
    }

    // ==========================================
    //  マーカー生成
    // ==========================================
    GameObject CreateMarker(string name, Color color)
    {
        var marker  = new GameObject(name);
        float shelfH     = warehouseGenerator != null ? warehouseGenerator.shelfHeight : 3f;
        float poleHeight = shelfH + 1.5f;

        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "MarkerBase";
        cylinder.transform.SetParent(marker.transform, false);
        cylinder.transform.localPosition = Vector3.zero;
        cylinder.transform.localScale    = new Vector3(markerSize, 0.03f, markerSize);
        var matBase = new Material(Shader.Find("Standard"));
        matBase.color = color; SetTransparent(matBase);
        cylinder.GetComponent<Renderer>().material = matBase;
        Destroy(cylinder.GetComponent<Collider>());

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "MarkerPole";
        pole.transform.SetParent(marker.transform, false);
        pole.transform.localPosition = new Vector3(0f, poleHeight / 2f, 0f);
        pole.transform.localScale    = new Vector3(0.06f, poleHeight / 2f, 0.06f);
        var matPole = new Material(Shader.Find("Standard"));
        matPole.color = color; matPole.EnableKeyword("_EMISSION");
        matPole.SetColor("_EmissionColor", color * 0.5f);
        pole.GetComponent<Renderer>().material = matPole;
        Destroy(pole.GetComponent<Collider>());

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "MarkerTop";
        sphere.transform.SetParent(marker.transform, false);
        sphere.transform.localPosition = new Vector3(0f, poleHeight + 0.3f, 0f);
        sphere.transform.localScale    = Vector3.one * 0.5f;
        var matTop = new Material(Shader.Find("Standard"));
        matTop.color = color; matTop.EnableKeyword("_EMISSION");
        matTop.SetColor("_EmissionColor", color * 2f);
        sphere.GetComponent<Renderer>().material = matTop;
        Destroy(sphere.GetComponent<Collider>());

        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "MarkerRing";
        ring.transform.SetParent(marker.transform, false);
        ring.transform.localPosition = new Vector3(0f, shelfH, 0f);
        ring.transform.localScale    = new Vector3(0.6f, 0.04f, 0.6f);
        var matRing = new Material(Shader.Find("Standard"));
        matRing.color = color; matRing.EnableKeyword("_EMISSION");
        matRing.SetColor("_EmissionColor", color * 1.5f);
        ring.GetComponent<Renderer>().material = matRing;
        Destroy(ring.GetComponent<Collider>());

        marker.AddComponent<MarkerPulse>().pulseColor = color;
        return marker;
    }

    void SetTransparent(Material mat)
    {
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }

    void SetMarkerVisible(GameObject marker, bool visible)
    {
        if (marker == null) return;
        foreach (var r in marker.GetComponentsInChildren<Renderer>())
            r.enabled = visible;
    }

    // ==========================================
    //  ロボット自動生成
    // ==========================================
    WarehouseRobotAgent SpawnRobot(int index)
    {
        var robotGo  = GameObject.CreatePrimitive(PrimitiveType.Cube);
        robotGo.name = $"WarehouseRobot_{index}";
        robotGo.transform.localScale = robotSize;

        if (entrances.Count > 0)
            robotGo.transform.position = entrances[index % entrances.Count].center;
        else
        {
            Vector3 fallback = new Vector3(0f, robotSize.y / 2f + 0.05f, 20f);
            robotGo.transform.position = envRoot != null
                ? envRoot.TransformPoint(fallback) : fallback;
        }

        Color agentColor = Color.HSVToRGB(
            (robotColor.r + index * 0.15f) % 1f, 0.7f, 0.9f);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = agentColor;
        robotGo.GetComponent<Renderer>().material = mat;

        var rb = robotGo.AddComponent<Rigidbody>();
        rb.mass = 10f; rb.drag = 1f; rb.angularDrag = 5f;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        var agent = robotGo.AddComponent<WarehouseRobotAgent>();
        agent.trainingManager = this;
        agent.envRoot         = envRoot;
        agent.pheromone       = GetComponent<WarehousePheromone>();
        WarehouseExperimentRuntime.ApplyAgent(agent, WarehouseExperimentRuntime.ActiveConfig);

        if (WarehousePerformance.IsEnabled(p => p.DebugLog))
            Debug.Log($"[TrainingManager] ロボット '{robotGo.name}' を自動生成");
        return agent;
    }

    // ==========================================
    //  ギズモ
    // ==========================================
    void OnDrawGizmosSelected()
    {
        // 入口: 中心=緑球、幅=ワイヤーライン、インデックスラベル
        for (int e = 0; e < entrances.Count; e++)
        {
            var ent = entrances[e];
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(ent.center, 0.5f);
            Gizmos.DrawLine(
                ent.center - ent.spreadDir * ent.halfWidth,
                ent.center + ent.spreadDir * ent.halfWidth);

#if UNITY_EDITOR
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(ent.center + Vector3.up * 1.5f, $"Entrance {e}");
#endif
        }

        // スロット
        for (int i = 0; i < allSlots.Count; i++)
        {
            Gizmos.color = occupiedSlots.Contains(i)
                ? new Color(1f, 0.8f, 0f, 0.8f)
                : new Color(0f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(allSlots[i].position, 0.3f);
        }

        // ターゲット棚
        foreach (var kvp in agentStates)
        {
            if (kvp.Value.targetShelf != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(kvp.Value.targetShelf.transform.position, 1.5f);
            }
        }
    }
}

// ==========================================
//  マーカー パルスアニメーション
// ==========================================
public class MarkerPulse : MonoBehaviour
{
    public Color pulseColor = Color.green;
    private float phase;

    void Update()
    {
        if (!WarehousePerformance.IsEnabled(p => p.Markers)) return;

        phase += Time.deltaTime * 2f;
        float t = (Mathf.Sin(phase) + 1f) * 0.5f;
        var top = transform.Find("MarkerTop");
        if (top != null)
            top.localScale = Vector3.one * (0.25f + t * 0.15f);
    }
}
