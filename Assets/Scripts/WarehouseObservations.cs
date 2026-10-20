using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 倉庫ロボット用レイキャスト観測値収集クラス
///
/// AddObservations.cs (CarAgent用) の構造を参考に、
/// 倉庫環境向けにカスタマイズしたレイキャストセンサー。
///
/// ■ レイ配置 (12方向):
///   前方5本 (30度刻みで扇状) + 後方3本 + 左右2本 + 斜め後方2本
///
/// ■ 各レイの観測値 (4つ):
///   [0] 正規化距離  (0〜1, 1=ヒットなし)
///   [1] 棚フラグ    (1=棚にヒット, 0=それ以外)
///   [2] 壁フラグ    (1=壁/柱/ガードにヒット, 0=それ以外)
///   [3] エージェントフラグ (1=他のロボットにヒット, 0=それ以外)
///
/// ■ 合計: 12レイ × 4 = 48 観測値
///
/// FrontDynamic19 uses 19 forward SphereCasts with distance, type and relative velocity.
/// </summary>
public class WarehouseObservations
{
    public const int LegacyRayCount = 12;
    public const int LegacyObservationsPerRay = 4;
    public const int FrontRayCount = 19;
    public const int FrontObservationsPerRay = 6;
    private const float FrontCastRadius = 0.2f;
    private const int FrontHitCapacity = 32;

    private WarehouseRobotAgent agent;
    private Transform agentTransform;
    private Rigidbody agentBody;

    // ==========================================
    //  レイ方向定義 (ローカル座標系)
    //  (forwardOffset, rightOffset, yawAngle)
    //  forwardOffset: レイ開始位置の前後オフセット
    //  rightOffset:   レイ開始位置の左右オフセット
    //  yawAngle:      レイの射出角度 (0=正面, 90=右, -90=左, 180=後方)
    // ==========================================
    private readonly (float forward, float right, float angle)[] rayDirections = new (float, float, float)[]
    {
        // --- 前方 5本 (扇状) ---
        ( 0.6f,  0.0f,   0f),    // [0] 正面
        ( 0.6f,  0.3f,  15f),    // [1] 正面やや右
        ( 0.6f, -0.3f, -15f),    // [2] 正面やや左
        ( 0.5f,  0.5f,  35f),    // [3] 前方右
        ( 0.5f, -0.5f, -35f),    // [4] 前方左

        // --- 側面 2本 ---
        ( 0.0f,  0.5f,  90f),    // [5] 右
        ( 0.0f, -0.5f, -90f),    // [6] 左

        // --- 斜め後方 2本 ---
        (-0.3f,  0.5f, 135f),    // [7] 右後方
        (-0.3f, -0.5f,-135f),    // [8] 左後方

        // --- 後方 3本 ---
        (-0.6f,  0.3f, 160f),    // [9]  後方やや右
        (-0.6f,  0.0f, 180f),    // [10] 真後ろ
        (-0.6f, -0.3f,-160f),    // [11] 後方やや左
    };

    /// <summary> レイの本数 </summary>
    public int RayCount => agent.rayObservationFormat == WarehouseRayObservationFormat.FrontDynamic19
        ? FrontRayCount : rayDirections.Length;

    /// <summary> レイ1本あたりの観測値数 </summary>
    public const int ObservationsPerRay = LegacyObservationsPerRay;

    /// <summary> 合計観測値数 (RayCount × ObservationsPerRay) </summary>
    public int TotalObservations => agent.rayObservationFormat == WarehouseRayObservationFormat.FrontDynamic19
        ? FrontRayCount * FrontObservationsPerRay : rayDirections.Length * LegacyObservationsPerRay;

    // ==========================================
    //  パフォーマンス最適化用キャッシュ
    // ==========================================
    private RaycastHit[] raycastHits;
    private float[] observationBuffer;
    private readonly RaycastHit[] frontHits = new RaycastHit[FrontHitCapacity];
    private readonly float[] frontObservationBuffer = new float[FrontRayCount * FrontObservationsPerRay];

    // 壁系タグのHashSet (string比較の高速化)
    private static readonly HashSet<string> wallTags = new HashSet<string>
    {
        "Wall", "Untagged" // Wall タグ + デフォルト (壁・柱・ガードレール)
    };

    // 壁系名前プレフィックス
    private static readonly string[] wallNamePrefixes = new string[]
    {
        "Wall", "Pillar", "Guard", "Post"
    };

    // 棚系名前プレフィックス
    private static readonly string[] shelfNamePrefixes = new string[]
    {
        "Shelf", "Rack"
    };

    // ==========================================
    //  コンストラクタ
    // ==========================================
    public WarehouseObservations(WarehouseRobotAgent agent)
    {
        this.agent = agent;
        this.agentTransform = agent.transform;
        this.agentBody = agent.GetComponent<Rigidbody>();
        this.raycastHits = new RaycastHit[rayDirections.Length];
        this.observationBuffer = new float[rayDirections.Length * ObservationsPerRay];
    }

    // ==========================================
    //  観測値収集 (VectorSensorに直接追加)
    // ==========================================

    /// <summary>
    /// 全レイキャストを実行し、結果を VectorSensor に追加する。
    /// CollectObservations() 内から呼び出す。
    /// </summary>
    public void CollectRayObservations(Unity.MLAgents.Sensors.VectorSensor sensor)
    {
        agentTransform = agent.transform;
        bool drawDebug = WarehousePerformance.IsEnabled(p => p.DebugRays);

        if (agent.rayObservationFormat == WarehouseRayObservationFormat.FrontDynamic19)
        {
            for (int i = 0; i < FrontRayCount; i++)
            {
                CastFront(i, drawDebug, out float distance, out float shelf,
                    out float wall, out float otherAgent, out float relativeX, out float relativeZ);
                sensor.AddObservation(distance);
                sensor.AddObservation(shelf);
                sensor.AddObservation(wall);
                sensor.AddObservation(otherAgent);
                sensor.AddObservation(relativeX);
                sensor.AddObservation(relativeZ);
            }
            return;
        }

        for (int i = 0; i < rayDirections.Length; i++)
        {
            var (fwd, right, angle) = rayDirections[i];

            CastRay(i, fwd, right, angle,
                     out float normalizedDist,
                     out float isShelf,
                     out float isWall,
                     out float isAgent,
                     drawDebug);

            sensor.AddObservation(normalizedDist);
            sensor.AddObservation(isShelf);
            sensor.AddObservation(isWall);
            sensor.AddObservation(isAgent);
        }
    }

    /// <summary>
    /// 全レイキャストを実行し、結果を float配列 で返す。
    /// デバッグやカスタム用途向け。
    /// </summary>
    public float[] CollectRayObservationsArray()
    {
        agentTransform = agent.transform;
        bool drawDebug = WarehousePerformance.IsEnabled(p => p.DebugRays);

        if (agent.rayObservationFormat == WarehouseRayObservationFormat.FrontDynamic19)
        {
            for (int i = 0; i < FrontRayCount; i++)
            {
                CastFront(i, drawDebug, out float distance, out float shelf,
                    out float wall, out float otherAgent, out float relativeX, out float relativeZ);
                int index = i * FrontObservationsPerRay;
                frontObservationBuffer[index] = distance;
                frontObservationBuffer[index + 1] = shelf;
                frontObservationBuffer[index + 2] = wall;
                frontObservationBuffer[index + 3] = otherAgent;
                frontObservationBuffer[index + 4] = relativeX;
                frontObservationBuffer[index + 5] = relativeZ;
            }
            return frontObservationBuffer;
        }

        for (int i = 0; i < rayDirections.Length; i++)
        {
            var (fwd, right, angle) = rayDirections[i];

            CastRay(i, fwd, right, angle,
                     out float normalizedDist,
                     out float isShelf,
                     out float isWall,
                     out float isAgent,
                     drawDebug);

            int baseIdx = i * ObservationsPerRay;
            observationBuffer[baseIdx]     = normalizedDist;
            observationBuffer[baseIdx + 1] = isShelf;
            observationBuffer[baseIdx + 2] = isWall;
            observationBuffer[baseIdx + 3] = isAgent;
        }

        return observationBuffer;
    }

    private void CastFront(int index, bool drawDebug, out float distance, out float shelf,
        out float wall, out float otherAgent, out float relativeX, out float relativeZ)
    {
        distance = 1f;
        shelf = wall = otherAgent = relativeX = relativeZ = 0f;
        Vector3 origin = agentTransform.position + Vector3.up * 0.5f + agentTransform.forward * 0.55f;
        Vector3 direction = Quaternion.Euler(0f, -90f + 10f * index, 0f) * agentTransform.forward;
        if (drawDebug) Debug.DrawRay(origin, direction * agent.rayDistance, Color.cyan);

        int hitCount = Physics.SphereCastNonAlloc(origin, FrontCastRadius, direction, frontHits,
            agent.rayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        RaycastHit selected = default;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = frontHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(agentTransform) || hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            selected = hit;
        }
        if (selected.collider == null) return;

        distance = Mathf.Clamp01(selected.distance / agent.rayDistance);
        if (agent.rayNoise > 0f)
            distance = Mathf.Clamp01(distance * Random.Range(1f - agent.rayNoise, 1f + agent.rayNoise));

        GameObject hitObject = selected.collider.gameObject;
        WarehouseRobotAgent detectedAgent = hitObject.GetComponentInParent<WarehouseRobotAgent>();
        if (detectedAgent != null && detectedAgent != agent)
        {
            otherAgent = 1f;
            Rigidbody otherBody = detectedAgent.GetComponent<Rigidbody>();
            Vector3 relativeVelocity = (otherBody != null ? otherBody.velocity : Vector3.zero) -
                (agentBody != null ? agentBody.velocity : Vector3.zero);
            Vector3 localVelocity = agentTransform.InverseTransformDirection(relativeVelocity);
            float speedScale = Mathf.Max(0.01f, 2f * agent.maxSpeed);
            relativeX = Mathf.Clamp(localVelocity.x / speedScale, -1f, 1f);
            relativeZ = Mathf.Clamp(localVelocity.z / speedScale, -1f, 1f);
        }
        else if (IsShelf(hitObject.name, selected.collider.tag, hitObject)) shelf = 1f;
        else if (IsWall(hitObject.name, selected.collider.tag)) wall = 1f;

        if (drawDebug)
            Debug.DrawRay(origin, direction * selected.distance,
                otherAgent > 0f ? Color.green : shelf > 0f ? Color.blue : wall > 0f ? Color.red : Color.yellow);
    }

    // ==========================================
    //  レイキャスト実行
    // ==========================================
    private void CastRay(
        int rayIndex,
        float forwardOffset,
        float rightOffset,
        float yawAngle,
        out float normalizedDist,
        out float isShelf,
        out float isWall,
        out float isAgent,
        bool drawDebug = false)
    {
        normalizedDist = 1f;
        isShelf = 0f;
        isWall  = 0f;
        isAgent = 0f;

        Vector3 origin = agentTransform.position
                       + Vector3.up * 0.5f
                       + agentTransform.forward * forwardOffset
                       + agentTransform.right * rightOffset;

        Quaternion rot = Quaternion.Euler(0f, yawAngle, 0f);
        Vector3 dir = rot * agentTransform.forward;

        if (drawDebug)
            Debug.DrawRay(origin, dir * agent.rayDistance, Color.cyan);

        bool hit = Physics.Raycast(origin, dir, out raycastHits[rayIndex], agent.rayDistance);

        if (!hit) return;

        RaycastHit hitInfo = raycastHits[rayIndex];

        if (hitInfo.distance > 0f)
        {
            normalizedDist = hitInfo.distance / agent.rayDistance;

            if (agent.rayNoise > 0f)
            {
                float noiseMultiplier = Random.Range(1f - agent.rayNoise, 1f + agent.rayNoise);
                normalizedDist = Mathf.Clamp01(normalizedDist * noiseMultiplier);
            }
        }

        string objectName = hitInfo.collider.gameObject.name;
        string objectTag  = hitInfo.collider.tag;
        GameObject hitGo  = hitInfo.collider.gameObject;

        if (IsOtherAgent(hitGo))
        {
            isAgent = 1f;
            if (drawDebug) Debug.DrawRay(origin, dir * hitInfo.distance, Color.green);
        }
        else if (IsShelf(objectName, objectTag, hitGo))
        {
            isShelf = 1f;
            if (drawDebug) Debug.DrawRay(origin, dir * hitInfo.distance, Color.blue);
        }
        else if (IsWall(objectName, objectTag))
        {
            isWall = 1f;
            if (drawDebug) Debug.DrawRay(origin, dir * hitInfo.distance, Color.red);
        }
        else
        {
            if (drawDebug) Debug.DrawRay(origin, dir * hitInfo.distance, Color.yellow);
        }
    }

    // ==========================================
    //  オブジェクト判定
    // ==========================================

    /// <summary> 棚かどうかを判定 </summary>
    private bool IsShelf(string name, string tag, GameObject go)
    {
        // ShelfUnit コンポーネントの有無 (最も確実)
        if (go.GetComponent<ShelfUnit>() != null) return true;
        // 親にShelfUnitがある場合 (棚の子オブジェクト)
        if (go.GetComponentInParent<ShelfUnit>() != null) return true;

        // 名前プレフィックスで判定
        for (int i = 0; i < shelfNamePrefixes.Length; i++)
        {
            if (name.StartsWith(shelfNamePrefixes[i])) return true;
        }

        return false;
    }

    /// <summary> 壁・柱・ガードレールかどうかを判定 </summary>
    private bool IsWall(string name, string tag)
    {
        for (int i = 0; i < wallNamePrefixes.Length; i++)
        {
            if (name.StartsWith(wallNamePrefixes[i])) return true;
        }
        return false;
    }

    /// <summary> 自分以外のエージェントかどうかを判定 </summary>
    private bool IsOtherAgent(GameObject go)
    {
        // 自分自身は除外
        if (go == agent.gameObject) return false;

        // WarehouseRobotAgent コンポーネントの有無
        if (go.GetComponent<WarehouseRobotAgent>() != null) return true;
        // 親にある場合 (荷物ビジュアル等の子オブジェクト)
        if (go.GetComponentInParent<WarehouseRobotAgent>() != null)
        {
            // 自分の子オブジェクトは除外
            return go.GetComponentInParent<WarehouseRobotAgent>() != agent;
        }

        // 名前で判定
        if (go.name.StartsWith("WarehouseRobot")) return true;

        return false;
    }

    // ==========================================
    //  デバッグ情報
    // ==========================================

    /// <summary> デバッグ用: 現在のレイキャスト結果をログ出力 </summary>
    public string GetDebugInfo()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[WarehouseObservations] Ray Results:");
        sb.AppendLine($"  Ray Count: {RayCount}, Total Obs: {TotalObservations}");

        float[] data = CollectRayObservationsArray();
        if (agent.rayObservationFormat == WarehouseRayObservationFormat.FrontDynamic19)
        {
            for (int i = 0; i < FrontRayCount; i++)
            {
                int offset = i * FrontObservationsPerRay;
                sb.AppendLine($"  [{-90 + 10 * i} deg] dist={data[offset]:F3}, shelf={data[offset + 1]}, " +
                    $"wall={data[offset + 2]}, agent={data[offset + 3]}, " +
                    $"relativeX={data[offset + 4]:F3}, relativeZ={data[offset + 5]:F3}");
            }
            return sb.ToString();
        }

        string[] rayLabels = new string[]
        {
            "Front", "FrontR15", "FrontL15", "FrontR35", "FrontL35",
            "Right", "Left",
            "BackR", "BackL",
            "BackR160", "Back", "BackL160"
        };

        for (int i = 0; i < RayCount; i++)
        {
            int idx = i * ObservationsPerRay;
            string label = i < rayLabels.Length ? rayLabels[i] : $"Ray{i}";
            sb.AppendLine($"  [{label}] dist={data[idx]:F3}, shelf={data[idx+1]}, wall={data[idx+2]}, agent={data[idx+3]}");
        }

        return sb.ToString();
    }
}
