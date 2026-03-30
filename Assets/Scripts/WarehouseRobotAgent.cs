using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// ML-Agents 倉庫移動ロボットエージェント
///
/// ■ タスクフロー:
///   1. 入口に荷物を持った状態でスポーン
///   2. 指定された棚まで移動 → 棚に近づくと自動で荷物を置く
///   3. 入口まで戻ればエピソード成功
///
/// ■ フェーズ:
///   DELIVERING = 荷物を棚へ運搬中
///   RETURNING  = 荷物を置いた後、入口へ帰還中
///
/// ■ BehaviorParameters 設定:
///   Behavior Name:       WarehouseRobot
///   Vector Observation:  Space Size = 56
///   Continuous Actions:  2
///   Discrete Branches:   なし (0)
/// </summary>
public class WarehouseRobotAgent : Agent
{
    // ==========================================
    //  フェーズ定義
    // ==========================================
    public enum Phase
    {
        Delivering, // 荷物を棚へ運搬中
        Returning,  // 入口へ帰還中
    }

    // ==========================================
    //  Inspector 設定
    // ==========================================
    [Header("===== ロボット性能 =====")]
    [Tooltip("前後移動の加速度 (質量に依存しない)")]
    public float moveAccel = 20f;

    [Tooltip("最大速度 (m/s)")]
    public float maxSpeed = 5f;

    [Tooltip("回転速度 (度/秒)")]
    public float turnSpeed = 150f;

    [Tooltip("ロボットの質量")]
    public float robotMass = 10f;

    [Tooltip("移動時の抵抗 (大きいほど止まりやすい)")]
    public float robotDrag = 1f;

    [Tooltip("回転の抵抗")]
    public float robotAngularDrag = 5f;

    [Header("===== レイキャストセンサー =====")]
    [Tooltip("レイの最大検知距離")]
    public float rayDistance = 10f;

    [Tooltip("レイにノイズを加える割合 (0=なし, 0.05=5%)")]
    [Range(0f, 0.2f)]
    public float rayNoise = 0.02f;

    [Header("===== インタラクション =====")]
    [Tooltip("棚に荷物を置ける距離")]
    public float dropRange = 2.5f;

    [Tooltip("入口の判定距離")]
    public float exitRange = 2.5f;

    [Tooltip("棚の前後どちらからでもアクセスできる")]
    public bool allowBackAccess = false;

    [Header("===== 報酬設定 =====")]
    [Tooltip("棚に荷物を置いた時のボーナス")]
    public float rewardDropAtShelf = 1.0f;

    [Tooltip("入口から退出してタスク完了時のボーナス")]
    public float rewardExitComplete = 2.0f;

    [Tooltip("壁衝突時のペナルティ (1回)")]
    public float penaltyWallHit = -0.05f;

    [Tooltip("壁に押し付け続けた時の毎ステップペナルティ")]
    public float penaltyWallStay = -0.01f;

    [Tooltip("他のエージェントとの衝突ペナルティ (1回)")]
    public float penaltyAgentCollision = -0.03f;

    [Tooltip("他のエージェントに押し付け続けた時の毎ステップペナルティ")]
    public float penaltyAgentStay = -0.005f;

    [Tooltip("毎ステップの時間経過ペナルティ")]
    public float penaltyTimeStep = -0.001f;

    [Tooltip("落下時のペナルティ")]
    public float penaltyFall = -1.0f;

    [Header("===== シェーピング報酬 (最短距離更新) =====")]
    [Tooltip("棚への最短距離を1m更新するごとの報酬")]
    public float shelfApproachReward = 0.01f;

    [Tooltip("出口への最短距離を1m更新するごとの報酬")]
    public float exitApproachReward = 0.01f;

    [Header("===== エピソード制限 =====")]
    [Tooltip("最大ステップ数 (0=無制限)。超えたらエピソード強制終了")]
    public int maxStepLimit = 5000;

    [Tooltip("タイムアウト時のペナルティ")]
    public float penaltyTimeout = -0.5f;

    [Header("===== ビジュアル荷物 =====")]
    public Color cargoColor = new Color(0.72f, 0.55f, 0.30f);
    [Range(0.2f, 0.8f)]
    public float cargoSizeRatio = 0.5f;

    [Header("===== 参照 (TrainingManagerが自動セット) =====")]
    public Transform targetShelfTransform;
    public WarehouseTrainingManager trainingManager;
    public WarehousePheromone pheromone;

    [HideInInspector]
    public Transform envRoot;
    [HideInInspector]
    public int completedCount = 0;
    [HideInInspector]
    public float totalMoveDistance = 0;
    [HideInInspector]
    public int crashToWall = 0;
    [HideInInspector]
    public int crashToAgent = 0;
    // ==========================================
    //  内部変数
    // ==========================================
    private Rigidbody rb;
    private Vector3 lastPosition;
    private Phase currentPhase;
    private GameObject cargoVisual;
    private Renderer robotRenderer;
    private Color originalRobotColor;
    private float episodeTimer;
    // レイキャストセンサー
    private WarehouseObservations raySensor;

    // 倉庫サイズ (正規化用)
    private float fieldWidth = 30f;
    private float fieldDepth = 40f;

    // 帰還フェーズのターゲット出口 (荷物を置いた時にランダム選択)
    private Vector3 targetExitPosition;

    // シェーピング報酬: エピソード中の最短距離記録
    private float bestDistToShelf = float.MaxValue;
    private float bestDistToExit  = float.MaxValue;

    // ==========================================
    //  デバッグ用 公開プロパティ (HUDから参照)
    // ==========================================
    [HideInInspector] public float debugMoveInput;
    [HideInInspector] public float debugTurnInput;
    [HideInInspector] public float debugSpeed;
    [HideInInspector] public float debugAngularVelY;
    [HideInInspector] public float debugEpisodeTimer;
    [HideInInspector] public float debugCumulativeReward;
    [HideInInspector] public float debugStepReward;
    [HideInInspector] public int   debugCompletedCount;
    [HideInInspector] public int   debugStepCount;
    [HideInInspector] public float debugDistToShelf;
    [HideInInspector] public float debugAngleToShelf;
    [HideInInspector] public float debugDistToExit;
    [HideInInspector] public float debugAngleToExit;
    [HideInInspector] public Phase debugPhase;
    [HideInInspector] public bool  debugIsPushingWall;
    [HideInInspector] public float[] debugRayData;

    // ==========================================
    //  初期化
    // ==========================================
    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();

        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.useGravity = true;
        rb.mass = robotMass;
        rb.drag = robotDrag;
        rb.angularDrag = robotAngularDrag;

        // --- 引っかかり防止: CapsuleCollider + 低摩擦マテリアル ---
        SetupSmoothCollider();

        // DecisionRequester 自動追加
        var dr = GetComponent<Unity.MLAgents.DecisionRequester>();
        if (dr == null)
        {
            dr = gameObject.AddComponent<Unity.MLAgents.DecisionRequester>();
            dr.DecisionPeriod = 5;
        }

        robotRenderer = GetComponent<Renderer>();
        if (robotRenderer != null)
            originalRobotColor = robotRenderer.material.color;

        // --- trainingManager 自動検出 ---
        FindTrainingManager();

        if (trainingManager != null && trainingManager.warehouseGenerator != null)
        {
            fieldWidth = trainingManager.warehouseGenerator.warehouseWidth;
            fieldDepth = trainingManager.warehouseGenerator.warehouseDepth;
        }

        // envRoot を取得
        if (trainingManager != null && trainingManager.envRoot != null)
            envRoot = trainingManager.envRoot;
        else if (transform.parent != null)
            envRoot = transform.parent;
        else
            envRoot = transform;

        CreateCargoVisual();

        // レイキャストセンサー初期化
        raySensor = new WarehouseObservations(this);
    }

    /// <summary>
    /// trainingManager が未設定なら、シーン内から自動検出する。
    /// 同じ envRoot 配下 → 親階層 → シーン全体の順に探索。
    /// </summary>
    void FindTrainingManager()
    {
        if (trainingManager != null && pheromone != null) return;

        // --- TrainingManager ---
        if (trainingManager == null)
        {
            trainingManager = GetComponentInParent<WarehouseTrainingManager>();
            if (trainingManager == null)
            {
                var managers = FindObjectsOfType<WarehouseTrainingManager>();
                if (managers.Length == 1)
                    trainingManager = managers[0];
                else if (managers.Length > 1)
                {
                    float minDist = float.MaxValue;
                    foreach (var m in managers)
                    {
                        float d = Vector3.Distance(transform.position, m.transform.position);
                        if (d < minDist) { minDist = d; trainingManager = m; }
                    }
                }
            }
        }

        // --- Pheromone (TrainingManager と同じ GameObject にある想定) ---
        if (pheromone == null)
        {
            if (trainingManager != null)
                pheromone = trainingManager.GetComponent<WarehousePheromone>();

            if (pheromone == null)
                pheromone = FindObjectOfType<WarehousePheromone>();
        }
    }

    // ==========================================
    //  エピソード開始
    // ==========================================
    public override void OnEpisodeBegin()
    {
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // trainingManager がまだ null なら再検出
        FindTrainingManager();

        // フェーズ1: 荷物を持って入口からスタート
        currentPhase = Phase.Delivering;
        ShowCargoVisual();

        if (trainingManager != null)
        {
            trainingManager.ResetEpisode(this);
        }

        // 帰還ターゲット出口を初期値で設定 (荷物を置いた時にランダム再選択)
        if (trainingManager != null)
            targetExitPosition = trainingManager.GetRandomEntrance();
        else
            targetExitPosition = GetFallbackExitPosition();

        episodeTimer = 0f;
        stuckTimer = 0f;
        isTouchingWall = false;
        isPushingIntoWall = false;
        isPushingIntoAgent = false;
        bestDistToShelf = float.MaxValue;
        bestDistToExit  = float.MaxValue;
    }

    /// <summary>
    /// TrainingManager の LateInit 完了後に呼ばれる。
    /// 荷物ビジュアルとフェーズを正しい状態に強制リフレッシュする。
    /// </summary>
    public void ForceRefreshVisual()
    {
        currentPhase = Phase.Delivering;
        ShowCargoVisual();
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        if (WarehousePerformance.IsEnabled(p => p.DebugLog)) Debug.Log("[Robot] 初回配置完了 — 入口に荷物を持ってスタンバイ");
    }

    // ==========================================
    //  観測値の収集
    //  基本観測 8 + レイキャスト 48 = 合計 56
    //
    //  BehaviorParameters の Space Size = 56 に設定してください
    // ==========================================
    public override void CollectObservations(VectorSensor sensor)
    {
        // === A. 基本観測 (8) ===

        // --- 1. ローカル速度 (前後・左右) ---
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        sensor.AddObservation(localVel.z / maxSpeed);                       // [0] 前後速度
        sensor.AddObservation(localVel.x / maxSpeed);                       // [1] 左右速度

        // --- 2. 角速度 (Y軸回転) ---
        float angVelY = rb.angularVelocity.y;
        sensor.AddObservation(angVelY / (turnSpeed * Mathf.Deg2Rad));       // [2] 正規化角速度

        // --- 3. 現在のフェーズ (0=運搬中, 1=帰還中) ---
        sensor.AddObservation(currentPhase == Phase.Delivering ? 0f : 1f);  // [3]

        // --- 4. ターゲット棚への極座標 (距離, 角度) ---
        if (targetShelfTransform != null)
        {
            var shelf = targetShelfTransform.GetComponent<ShelfUnit>();
            Vector3 targetPos = shelf != null
                ? GetShelfFrontPosition(shelf)
                : targetShelfTransform.position;
            AddPolarObservation(sensor, targetPos);                         // [4] 距離, [5] 角度
        }
        else
        {
            sensor.AddObservation(1f);  // 最大距離
            sensor.AddObservation(0f);  // 正面
        }

        // --- 5. ターゲット出口への極座標 (距離, 角度) ---
        AddPolarObservation(sensor, targetExitPosition);                    // [6] 距離, [7] 角度

        // === B. レイキャストセンサー (12本 × 4 = 48) ===
        // [8..55] 各レイ: 正規化距離, 棚フラグ, 壁フラグ, エージェントフラグ
        raySensor.CollectRayObservations(sensor);

        // 合計: 8 + 48 = 56
    }

    /// <summary>
    /// ターゲットへの極座標観測値を追加する
    ///   - 正規化距離: 0〜1 (fieldDiagonal で正規化)
    ///   - 相対角度: -1〜1 (-π〜π を π で割る。0=正面, ±1=真後ろ)
    /// </summary>
    void AddPolarObservation(VectorSensor sensor, Vector3 targetWorldPos)
    {
        Vector3 toTarget = targetWorldPos - transform.position;
        toTarget.y = 0f;

        // 距離 (倉庫の対角線で正規化)
        float diagonal = Mathf.Sqrt(fieldWidth * fieldWidth + fieldDepth * fieldDepth);
        float dist = toTarget.magnitude;
        sensor.AddObservation(Mathf.Clamp01(dist / diagonal));

        // 相対角度 (自分の正面を0とした符号付き角度)
        Vector3 forward = transform.forward;
        forward.y = 0f;
        float angle = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        sensor.AddObservation(angle / 180f);  // -1〜1 に正規化
    }

    // ==========================================
    //  行動
    // ==========================================
    public override void OnActionReceived(ActionBuffers actions)
    {
        episodeTimer += Time.fixedDeltaTime;

        // --- 連続行動: 移動 & 回転 ---
        float moveInput = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float turnInput = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);

        // 回転
        transform.Rotate(0f, turnInput * turnSpeed * Time.fixedDeltaTime, 0f);

        // 力で加速 (Acceleration: 質量に依存しない = 直感的な加速度指定)
        Vector3 force = transform.forward * moveInput * moveAccel;
        rb.AddForce(force, ForceMode.Acceleration);

        // 速度制限 (水平方向のみ、重力のY成分は維持)
        Vector3 vel = rb.velocity;
        Vector3 hVel = new Vector3(vel.x, 0f, vel.z);
        if (hVel.magnitude > maxSpeed)
        {
            hVel = hVel.normalized * maxSpeed;
            rb.velocity = new Vector3(hVel.x, vel.y, hVel.z);
        }

        // スタック検知 & 自動脱出
        CheckAndResolveStuck(moveInput);

        // --- フェーズ判定 (排他: 同フレームで両方走らないようにする) ---
        if (currentPhase == Phase.Delivering)
        {
            CheckAutoDropAtShelf();
        }
        else if (currentPhase == Phase.Returning)
        {
            CheckExitReached();
        }

        // --- 時間ペナルティ ---
        AddReward(penaltyTimeStep);

        // --- 壁押し付けペナルティ ---
        if (isPushingIntoWall)
        {
            AddReward(penaltyWallStay);
            isPushingIntoWall = false;
        }

        // --- エージェント押し付けペナルティ ---
        if (isPushingIntoAgent)
        {
            AddReward(penaltyAgentStay);
            isPushingIntoAgent = false;
        }

        // --- シェーピング報酬 ---
        AddShapingReward();

        // --- フェロモン報酬 (毎ステップ: 通ったセルにフェロモンを残す) ---
        if (pheromone != null && currentPhase == Phase.Delivering && targetShelfTransform != null)
        {
            var shelf = targetShelfTransform.GetComponent<ShelfUnit>();
            if (shelf != null)
            {
                float pheroReward = pheromone.StepPheromone(transform.position, shelf);
                AddReward(pheroReward);
            }
        }

        // --- 落下検知 (環境の地面から-1m以下) ---
        if (transform.position.y < GetEnvGroundY() - 1f)
        {
            Debug.LogWarning("[Robot] Fell off the platform! Resetting episode.");
            AddReward(penaltyFall);
            EndEpisode();
            return;
        }

        // --- ステップ数制限 ---
        if (maxStepLimit > 0 && StepCount >= maxStepLimit)
        {
        if (WarehousePerformance.IsEnabled(p => p.DebugLog))     Debug.Log($"[Robot] Step limit reached ({maxStepLimit}). Resetting episode.");
            AddReward(penaltyTimeout);
            EndEpisode();
            return;
        }

        // --- デバッグ値更新 (HUD用、学習時はスキップ) ---
        if (WarehousePerformance.IsEnabled(p => p.HUD))
            UpdateDebugData(moveInput, turnInput);
    }

    void FixedUpdate()
    {
        float moveDistance = Vector3.Distance(lastPosition, transform.position);
        totalMoveDistance += moveDistance;

        lastPosition = transform.position;
    }

    void UpdateDebugData(float moveInput, float turnInput)
    {
        debugMoveInput = moveInput;
        debugTurnInput = turnInput;

        Vector3 hVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        debugSpeed = hVel.magnitude;
        debugAngularVelY = rb.angularVelocity.y;
        debugEpisodeTimer = episodeTimer;
        debugCumulativeReward = GetCumulativeReward();
        debugCompletedCount = completedCount;
        debugStepCount = StepCount;
        debugPhase = currentPhase;
        debugIsPushingWall = isPushingIntoWall || isTouchingWall || isPushingIntoAgent;

        // 棚への極座標 (距離 + 角度)
        if (targetShelfTransform != null)
        {
            var shelf = targetShelfTransform.GetComponent<ShelfUnit>();
            Vector3 shelfTarget = shelf != null
                ? GetShelfFrontPosition(shelf)
                : targetShelfTransform.position;
            debugDistToShelf = HorizontalDistance(transform.position, shelfTarget);
            debugAngleToShelf = SignedAngleTo(shelfTarget);
        }
        else
        {
            debugDistToShelf = -1f;
            debugAngleToShelf = 0f;
        }

        // 出口への極座標 (距離 + 角度)
        debugDistToExit = HorizontalDistance(transform.position, targetExitPosition);
        debugAngleToExit = SignedAngleTo(targetExitPosition);

        // レイキャストデータ (HUDで表示用)
        if (raySensor != null)
            debugRayData = raySensor.CollectRayObservationsArray();
    }

    /// <summary>
    /// ターゲットへの符号付き角度を返す (度)。正面=0, 右=+, 左=-
    /// </summary>
    float SignedAngleTo(Vector3 targetWorldPos)
    {
        Vector3 toTarget = targetWorldPos - transform.position;
        toTarget.y = 0f;
        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        return Vector3.SignedAngle(fwd, toTarget, Vector3.up);
    }

    // ==========================================
    //  ヒューリスティック (WASD / 矢印キーのみ)
    // ==========================================
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuous = actionsOut.ContinuousActions;
        continuous[0] = Input.GetAxis("Vertical");
        continuous[1] = Input.GetAxis("Horizontal");
    }

    // ==========================================
    //  棚の前面に来たら自動で荷物を置く
    //
    //  判定条件:
    //   1. ロボットが棚の通路側 (前面) にいる
    //   2. 棚の前面からの距離が dropRange 以内
    //   3. Z方向が棚の幅の範囲内 (±マージン)
    //
    //  棚レイアウト (背中合わせ):
    //    side=0: 通路は X- 方向 → 前面 = shelfPos.x
    //    side=1: 通路は X+ 方向 → 前面 = shelfPos.x + depth
    // ==========================================
    void CheckAutoDropAtShelf()
    {
        if (targetShelfTransform == null) return;

        var shelf = targetShelfTransform.GetComponent<ShelfUnit>();
        if (shelf == null)
        {
            // ShelfUnit がない場合はフォールバック (距離のみ)
            float d = HorizontalDistance(transform.position, targetShelfTransform.position);
            if (d <= dropRange) CompleteDrop(shelf);
            return;
        }

        if (!IsInFrontOfShelf(shelf)) return;

        CompleteDrop(shelf);
    }

    /// <summary>
    /// ロボットが棚の前面 (allowBackAccess時は背面も) にいるかどうかを判定する
    /// </summary>
    bool IsInFrontOfShelf(ShelfUnit shelf)
    {
        Vector3 shelfPos = shelf.transform.position;
        Vector3 robotPos = transform.position;
        float d = shelf.depth;
        float w = shelf.width;
        float margin = 0.5f;

        // --- Z方向: 棚の幅の範囲内か (±マージン) ---
        float zMin = shelfPos.z - margin;
        float zMax = shelfPos.z + w + margin;
        if (robotPos.z < zMin || robotPos.z > zMax)
            return false;

        // --- X方向: 前面チェック ---
        // 前面 (sideIndex 側)
        if (CheckFrontAccess(shelfPos, robotPos, d, shelf.sideIndex))
            return true;

        // 背面 (allowBackAccess 時のみ)
        if (allowBackAccess)
        {
            int backSide = shelf.sideIndex == 0 ? 1 : 0;
            if (CheckFrontAccess(shelfPos, robotPos, d, backSide))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 指定した side 方向からのアクセス判定
    /// </summary>
    bool CheckFrontAccess(Vector3 shelfPos, Vector3 robotPos, float depth, int side)
    {
        if (side == 0)
        {
            float frontX = shelfPos.x;
            float distToFront = frontX - robotPos.x;
            return distToFront > -0.3f && distToFront < dropRange;
        }
        else
        {
            float frontX = shelfPos.x + depth;
            float distToFront = robotPos.x - frontX;
            return distToFront > -0.3f && distToFront < dropRange;
        }
    }

    /// <summary>
    /// 荷物を棚に置く共通処理
    /// </summary>
    void CompleteDrop(ShelfUnit shelf)
    {
        currentPhase = Phase.Returning;
        HideCargoVisual();
        AddReward(rewardDropAtShelf);

        // フェーズ切替: 最短距離記録をリセット
        bestDistToExit = float.MaxValue;

        if (shelf != null)
        {
            shelf.AddCrate(0, 10f);
        }

        // ランダムな出口を選択して帰還ターゲットに設定
        if (trainingManager != null)
        {
            targetExitPosition = trainingManager.GetRandomEntrance();
            trainingManager.OnCargoDropped(this, targetExitPosition);
        }
        else
        {
            targetExitPosition = GetFallbackExitPosition();
        }

        if (WarehousePerformance.IsEnabled(p => p.DebugLog)) Debug.Log($"[Robot] Auto-drop at shelf front! -> Returning to exit ({targetExitPosition.x:F1}, {targetExitPosition.z:F1})");
    }

    // ==========================================
    //  入口到達チェック (帰還フェーズで自動判定)
    // ==========================================
    void CheckExitReached()
    {
        float dist = Vector3.Distance(transform.position, targetExitPosition);

        if (dist <= exitRange)
        {
            completedCount++;
            AddReward(rewardExitComplete);
        if (WarehousePerformance.IsEnabled(p => p.DebugLog))     Debug.Log($"[Robot] 入口から退出! タスク完了! (累計:{completedCount})");
            EndEpisode();
        }
    }

    // ==========================================
    //  シェーピング報酬 (最短距離更新)
    //
    //  エピソード中の最短距離を記録し、新記録を出した時だけ報酬。
    //  - 近づく → 新記録 → 正の報酬
    //  - 遠ざかる (迂回) → 記録更新なし → 報酬0 (罰なし!)
    //  - 壁に張り付き → 距離変わらず → 報酬0 + 時間ペナルティだけ
    // ==========================================
    void AddShapingReward()
    {
        if (currentPhase == Phase.Delivering)
        {
            if (targetShelfTransform != null && shelfApproachReward > 0f)
            {
                var shelf = targetShelfTransform.GetComponent<ShelfUnit>();
                float dist = shelf != null
                    ? DistanceToShelfFront(shelf)
                    : HorizontalDistance(transform.position, targetShelfTransform.position);

                if (dist < bestDistToShelf)
                {
                    float improvement = bestDistToShelf == float.MaxValue
                        ? 0f  // 初回は報酬なし (基準値の設定のみ)
                        : bestDistToShelf - dist;
                    bestDistToShelf = dist;
                    AddReward(improvement * shelfApproachReward);
                }
            }
        }
        else
        {
            if (exitApproachReward > 0f)
            {
                float dist = HorizontalDistance(transform.position, targetExitPosition);

                if (dist < bestDistToExit)
                {
                    float improvement = bestDistToExit == float.MaxValue
                        ? 0f
                        : bestDistToExit - dist;
                    bestDistToExit = dist;
                    AddReward(improvement * exitApproachReward);
                }
            }
        }
    }

    /// <summary>
    /// TrainingManager未接続時のフォールバック出口位置 (envRoot基準)
    /// </summary>
    Vector3 GetFallbackExitPosition()
    {
        Vector3 localExit = new Vector3(-1.5f, transform.position.y - GetEnvGroundY(), fieldDepth / 2f);
        return envRoot != null ? envRoot.TransformPoint(localExit) : localExit;
    }

    /// <summary>
    /// 環境の地面Y座標を返す
    /// </summary>
    float GetEnvGroundY()
    {
        return envRoot != null ? envRoot.position.y : 0f;
    }

    /// <summary>
    /// XZ平面上の水平距離を返す (高さは無視)
    /// </summary>
    float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ==========================================
    //  棚のアクセス面の座標計算
    //
    //  通常: sideIndex 側の前面中央を返す
    //  allowBackAccess: 前面と背面のうちロボットに近い方を返す
    // ==========================================

    /// <summary>
    /// 棚のアクセス面中央の座標を返す (ロボットの目標地点)
    /// </summary>
    Vector3 GetShelfFrontPosition(ShelfUnit shelf)
    {
        Vector3 shelfPos = shelf.transform.position;
        float centerZ = shelfPos.z + shelf.width / 2f;
        float frontOffset = 1.0f;

        Vector3 frontPos = shelf.sideIndex == 0
            ? new Vector3(shelfPos.x - frontOffset, transform.position.y, centerZ)
            : new Vector3(shelfPos.x + shelf.depth + frontOffset, transform.position.y, centerZ);

        if (!allowBackAccess)
            return frontPos;

        // 背面側の座標
        Vector3 backPos = shelf.sideIndex == 0
            ? new Vector3(shelfPos.x + shelf.depth + frontOffset, transform.position.y, centerZ)
            : new Vector3(shelfPos.x - frontOffset, transform.position.y, centerZ);

        // ロボットに近い方を返す
        float distFront = HorizontalDistance(transform.position, frontPos);
        float distBack  = HorizontalDistance(transform.position, backPos);
        return distFront <= distBack ? frontPos : backPos;
    }

    /// <summary>
    /// ロボットから棚の前面までの水平距離
    /// </summary>
    float DistanceToShelfFront(ShelfUnit shelf)
    {
        Vector3 frontPos = GetShelfFrontPosition(shelf);
        return HorizontalDistance(transform.position, frontPos);
    }

    // ==========================================
    //  引っかかり防止: Collider設定
    //
    //  1. 既存Colliderを削除し CapsuleCollider に差替え
    //  2. center を少し浮かせて床の継ぎ目をまたぐ
    //  3. 低摩擦 PhysicMaterial で壁・棚を滑る
    //  4. Continuous Collision Detection で高速時のすり抜け防止
    // ==========================================
    void SetupSmoothCollider()
    {
        // 既存のColliderをすべて削除
        foreach (var col in GetComponents<Collider>())
        {
            Destroy(col);
        }

        // CapsuleCollider (丸いので角に引っかからない)
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // Y軸

        // localScale を考慮した正しいサイズ計算
        Vector3 s = transform.localScale;
        float worldRadius = Mathf.Max(s.x, s.z) * 0.5f;
        float worldHeight = s.y;

        // CapsuleCollider は localScale で自動スケールされるので
        // ローカル空間で指定する
        capsule.radius = worldRadius / Mathf.Max(s.x, s.z);
        capsule.height = worldHeight / s.y;

        // center を少し浮かせて床の継ぎ目を越える
        capsule.center = new Vector3(0f, 0.05f, 0f);

        // 低摩擦 PhysicMaterial
        var pm = new PhysicMaterial("RobotSmooth");
        pm.dynamicFriction = 0.05f;
        pm.staticFriction  = 0.05f;
        pm.bounciness      = 0f;
        pm.frictionCombine  = PhysicMaterialCombine.Minimum;
        pm.bounceCombine    = PhysicMaterialCombine.Minimum;
        capsule.material = pm;

        // 連続衝突検出 (高速移動時のすり抜け防止)
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // ==========================================
    //  スタック検知 & 脱出
    //
    //  力を入れているのに動かない場合に小さな横方向の力で押し出す
    // ==========================================
    private float stuckTimer = 0f;
    private const float STUCK_SPEED_THRESHOLD = 0.15f;
    private const float STUCK_TIME_LIMIT = 0.8f;
    private const float STUCK_NUDGE_FORCE = 8f;

    void CheckAndResolveStuck(float moveInput)
    {
        Vector3 hVel = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        bool tryingToMove = Mathf.Abs(moveInput) > 0.1f;
        bool barelyMoving = hVel.magnitude < STUCK_SPEED_THRESHOLD;

        if (tryingToMove && barelyMoving)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer > STUCK_TIME_LIMIT)
            {
                // 横方向にランダムな力を加えて脱出
                Vector3 nudge = transform.right * (Random.value > 0.5f ? 1f : -1f) * STUCK_NUDGE_FORCE;
                rb.AddForce(nudge, ForceMode.Acceleration);
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    // ==========================================
    //  衝突
    // ==========================================
    private bool isTouchingWall = false;
    private bool isPushingIntoWall = false;
    private bool isPushingIntoAgent = false;

    void OnCollisionEnter(Collision collision)
    {
        string n = collision.gameObject.name;
        if (IsWallObject(n))
        {
            AddReward(penaltyWallHit);
            crashToWall++;
            Debug.Log("wall hit");
            // Debug.Break();
        }
        else if (IsOtherAgent(collision.gameObject))
        {
            AddReward(penaltyAgentCollision);
            crashToAgent++;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.contactCount == 0) return;

        Vector3 normal = collision.contacts[0].normal;
        normal.y = 0f;
        if (normal.magnitude < 0.01f) return;
        normal.Normalize();

        Vector3 vel = rb.velocity;
        Vector3 hVel = new Vector3(vel.x, 0f, vel.z);
        float normalComponent = Vector3.Dot(hVel, normal);

        // --- 壁 ---
        string n = collision.gameObject.name;
        if (IsWallObject(n))
        {
            isTouchingWall = true;

            if (normalComponent < -0.1f)
            {
                isPushingIntoWall = true;
                // スライド補正
                hVel -= normal * normalComponent;
                rb.velocity = new Vector3(hVel.x, vel.y, hVel.z);
            }
            return;
        }

        // --- 他のエージェント ---
        if (IsOtherAgent(collision.gameObject))
        {
            if (normalComponent < -0.1f)
            {
                isPushingIntoAgent = true;
            }
        }
    }

    void OnCollisionExit(Collision collision)
    {
        string n = collision.gameObject.name;
        if (IsWallObject(n))
        {
            isTouchingWall = false;
            isPushingIntoWall = false;
        }
        else if (IsOtherAgent(collision.gameObject))
        {
            isPushingIntoAgent = false;
        }
    }

    bool IsWallObject(string name)
    {
        return name.StartsWith("Wall") || name.StartsWith("Pillar") || name.StartsWith("Guard")
            || name.StartsWith("Post") || name.StartsWith("Rack");
            // return name.StartsWith("Wall") || name.StartsWith("Pillar") || name.StartsWith("Guard")
            // || name.StartsWith("Post") || name.StartsWith("Shelf") || name.StartsWith("Rack");
    }

    bool IsOtherAgent(GameObject go)
    {
        if (go == gameObject) return false;
        var otherAgent = go.GetComponent<WarehouseRobotAgent>();
        if (otherAgent != null) return true;
        var parentAgent = go.GetComponentInParent<WarehouseRobotAgent>();
        return parentAgent != null && parentAgent != this;
    }

    // ==========================================
    //  荷物ビジュアル
    // ==========================================
    void CreateCargoVisual()
    {
        if (!WarehousePerformance.IsEnabled(p => p.CargoVisual)) return;
        if (cargoVisual != null) return;

        cargoVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cargoVisual.name = "CargoVisual";
        cargoVisual.transform.SetParent(transform, false);

        Vector3 rs = transform.localScale;
        float sz = Mathf.Min(rs.x, rs.z) * cargoSizeRatio;
        float h  = rs.y * cargoSizeRatio * 0.8f;

        cargoVisual.transform.localScale = new Vector3(sz / rs.x, h / rs.y, sz / rs.z);
        cargoVisual.transform.localPosition = new Vector3(0f, 0.5f + (h / rs.y) * 0.5f + 0.05f, 0f);

        var rend = cargoVisual.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = cargoColor;

        var c = cargoVisual.GetComponent<Collider>();
        if (c != null) Destroy(c);

        cargoVisual.SetActive(false);
    }

    void ShowCargoVisual()
    {
        if (cargoVisual != null) cargoVisual.SetActive(true);
        if (!WarehousePerformance.IsEnabled(p => p.CargoVisual)) return;
        if (robotRenderer != null)
            robotRenderer.material.color = new Color(
                originalRobotColor.r * 0.8f,
                originalRobotColor.g * 1.1f,
                originalRobotColor.b * 0.8f);
    }

    void HideCargoVisual()
    {
        if (cargoVisual != null) cargoVisual.SetActive(false);
        if (!WarehousePerformance.IsEnabled(p => p.CargoVisual)) return;
        if (robotRenderer != null)
            robotRenderer.material.color = originalRobotColor;
    }

    // ==========================================
    //  公開プロパティ
    // ==========================================
    public Phase CurrentPhase => currentPhase;
    public bool IsCarrying => currentPhase == Phase.Delivering;
    public int CompletedCount => completedCount;

    public void SetFieldSize(float w, float d)
    {
        fieldWidth = w;
        fieldDepth = d;
    }
}