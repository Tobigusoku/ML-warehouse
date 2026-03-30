using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 棚ユニット1つに対応するスクリプト。
/// WarehouseGenerator が生成時に自動でアタッチします。
/// 棚の情報管理・ハイライト表示・荷物の追加/削除などを行います。
/// </summary>
public class ShelfUnit : MonoBehaviour
{
    // ==========================================
    //  棚の基本情報 (生成時に自動セット)
    // ==========================================
    [Header("===== 棚ID・位置 =====")]
    [Tooltip("棚の一意なID (自動付与)")]
    public string shelfID;

    [Tooltip("棚の列番号")]
    public int rowIndex;

    [Tooltip("列内の棚番号")]
    public int columnIndex;

    [Tooltip("背中合わせの表裏 (0=表 / 1=裏)")]
    public int sideIndex;

    // ==========================================
    //  棚のサイズ情報 (生成時に自動セット)
    // ==========================================
    [Header("===== サイズ =====")]
    public float width;
    public float height;
    public float depth;
    public int levelCount = 3;

    // ==========================================
    //  棚の状態
    // ==========================================
    [Header("===== 状態 =====")]
    [Tooltip("各段に荷物があるか")]
    public bool[] levelOccupied;

    [Tooltip("棚の重量 (荷物の合計, kg)")]
    public float currentWeightKg = 0f;

    [Tooltip("棚の最大積載量 (kg)")]
    public float maxWeightKg = 500f;

    [Tooltip("棚をハイライトしているエージェント数")]
    [System.NonSerialized]
    public int highlightCount = 0;

    /// <summary> 1人以上がハイライトしているか </summary>
    public bool isHighlighted => highlightCount > 0;

    // ==========================================
    //  ハイライト設定
    // ==========================================
    [Header("===== ハイライト =====")]
    public Color highlightColor = new Color(1f, 0.9f, 0.2f, 1f);
    public Color overweightColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Tooltip("ハイライト時の発光強度")]
    public float emissionIntensity = 1.5f;

    // ==========================================
    //  内部変数
    // ==========================================
    private List<Renderer> shelfRenderers = new List<Renderer>();
    private List<Color> originalColors = new List<Color>();
    private BoxCollider shelfCollider;
    private List<GameObject> cratesOnShelf = new List<GameObject>();
    private GameObject highlightBeacon;
    private float pulsePhase;

    // ==========================================
    //  初期化
    // ==========================================
    void Awake()
    {
        // コライダー取得
        shelfCollider = GetComponent<BoxCollider>();

        // 段の占有状態を初期化
        if (levelOccupied == null || levelOccupied.Length != levelCount)
            levelOccupied = new bool[levelCount];
    }

    void Start()
    {
        CacheRenderers();
        ScanExistingCrates();
    }

    /// <summary>
    /// 子オブジェクトのRendererをキャッシュ (ハイライト用)
    /// </summary>
    void CacheRenderers()
    {
        shelfRenderers.Clear();
        originalColors.Clear();

        // 棚板・支柱のみ (Crate は除く)
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r.gameObject.name.StartsWith("Post") ||
                r.gameObject.name.StartsWith("Board"))
            {
                shelfRenderers.Add(r);
                originalColors.Add(r.material.color);
            }
        }
    }

    /// <summary>
    /// 生成済みの荷物を検出してリストに登録
    /// </summary>
    void ScanExistingCrates()
    {
        cratesOnShelf.Clear();
        foreach (Transform child in transform)
        {
            if (child.name == "Crate")
                cratesOnShelf.Add(child.gameObject);
        }

        // 各段の占有状態を更新
        UpdateLevelOccupancy();
    }

    // ==========================================
    //  公開メソッド: ハイライト (参照カウント方式)
    // ==========================================

    /// <summary>
    /// ハイライトを追加する。
    /// 複数のエージェントが同じ棚をターゲットにしてもOK。
    /// 最後の1人が Unhighlight するまでハイライトが維持される。
    /// </summary>
    public void Highlight()
    {
        highlightCount++;

        if (highlightCount == 1 && WarehousePerformance.IsEnabled(p => p.ShelfHighlight))
        {
            ApplyHighlightVisual();
        }
    }

    /// <summary>
    /// ハイライトを解除する (参照カウント方式)。
    /// カウントが0になった時だけビジュアルを元に戻す。
    /// </summary>
    public void Unhighlight()
    {
        highlightCount = Mathf.Max(0, highlightCount - 1);

        // 最後の1人が解除した時だけビジュアルを元に戻す
        if (highlightCount == 0)
        {
            RemoveHighlightVisual();
        }
    }

    /// <summary>
    /// 強制的にハイライトを全解除する (エピソード全リセット用)
    /// </summary>
    public void ForceUnhighlight()
    {
        highlightCount = 0;
        RemoveHighlightVisual();
    }

    // ==========================================
    //  ハイライト ビジュアル適用 / 解除
    // ==========================================
    void ApplyHighlightVisual()
    {
        if (shelfRenderers.Count == 0) CacheRenderers();

        // Start前で子Rendererが取得できなかった場合、遅延リトライ
        if (shelfRenderers.Count == 0)
        {
            Invoke(nameof(RetryHighlight), 0.1f);
            return;
        }

        Color col = (currentWeightKg > maxWeightKg) ? overweightColor : highlightColor;

        for (int i = 0; i < shelfRenderers.Count; i++)
        {
            if (shelfRenderers[i] == null) continue;
            var mat = shelfRenderers[i].material;
            mat.color = col;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", col * emissionIntensity);

            // ハイライト中は影を落とさない
            shelfRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        CreateBeacon(col);
        pulsePhase = 0f;
    }

    /// <summary>
    /// CacheRenderers が空だった場合の遅延リトライ
    /// </summary>
    void RetryHighlight()
    {
        if (highlightCount > 0 && shelfRenderers.Count == 0)
        {
            CacheRenderers();
            if (shelfRenderers.Count > 0)
                ApplyHighlightVisual();
        }
    }

    void RemoveHighlightVisual()
    {
        for (int i = 0; i < shelfRenderers.Count; i++)
        {
            if (shelfRenderers[i] == null) continue;
            var mat = shelfRenderers[i].material;
            mat.color = originalColors[i];
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);

            // 影を元に戻す
            shelfRenderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        DestroyBeacon();
    }

    // ==========================================
    //  ビーコン (棚の上空に光るマーカー)
    // ==========================================
    void CreateBeacon(Color col)
    {
        DestroyBeacon();

        highlightBeacon = new GameObject("HighlightBeacon");
        highlightBeacon.transform.SetParent(transform, false);
        highlightBeacon.transform.localPosition = new Vector3(
            depth / 2f, height + 0.8f, width / 2f);

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "BeaconSphere";
        sphere.transform.SetParent(highlightBeacon.transform, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * 0.4f;
        var mat = new Material(Shader.Find("Standard"));
        mat.color = col;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", col * 3f);
        sphere.GetComponent<Renderer>().material = mat;
        sphere.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(sphere.GetComponent<Collider>());

        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "BeaconPole";
        pole.transform.SetParent(highlightBeacon.transform, false);
        pole.transform.localPosition = new Vector3(0f, -0.4f, 0f);
        pole.transform.localScale = new Vector3(0.03f, 0.4f, 0.03f);
        var poleMat = new Material(Shader.Find("Standard"));
        poleMat.color = col;
        poleMat.EnableKeyword("_EMISSION");
        poleMat.SetColor("_EmissionColor", col * 1f);
        pole.GetComponent<Renderer>().material = poleMat;
        pole.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(pole.GetComponent<Collider>());
    }

    void DestroyBeacon()
    {
        if (highlightBeacon != null)
        {
            Destroy(highlightBeacon);
            highlightBeacon = null;
        }
    }

    // ==========================================
    //  パルスアニメーション (ハイライト中のみ)
    // ==========================================
    void Update()
    {
        if (highlightCount <= 0) return;
        if (!WarehousePerformance.IsEnabled(p => p.ShelfHighlight)) return;

        pulsePhase += Time.deltaTime * 3f;
        float t = (Mathf.Sin(pulsePhase) + 1f) * 0.5f;

        Color col = (currentWeightKg > maxWeightKg) ? overweightColor : highlightColor;
        float intensity = Mathf.Lerp(emissionIntensity * 0.5f, emissionIntensity * 2f, t);

        for (int i = 0; i < shelfRenderers.Count; i++)
        {
            if (shelfRenderers[i] == null) continue;
            shelfRenderers[i].material.SetColor("_EmissionColor", col * intensity);
        }

        if (highlightBeacon != null)
        {
            float bob = Mathf.Sin(pulsePhase) * 0.15f;
            highlightBeacon.transform.localPosition = new Vector3(
                depth / 2f, height + 0.8f + bob, width / 2f);

            var beaconSphere = highlightBeacon.transform.Find("BeaconSphere");
            if (beaconSphere != null)
                beaconSphere.localScale = Vector3.one * (0.3f + t * 0.2f);
        }
    }

    // ==========================================
    //  公開メソッド: 荷物の管理
    // ==========================================

    /// <summary>
    /// 指定した段に荷物を追加する
    /// </summary>
    /// <param name="level">段番号 (0〜levelCount-1)</param>
    /// <param name="weightKg">荷物の重量 (kg)</param>
    /// <returns>生成された荷物のGameObject (積載オーバーなら null)</returns>
    public GameObject AddCrate(int level, float weightKg = 10f)
    {
        if (level < 0 || level >= levelCount)
        {
            Debug.LogWarning($"[ShelfUnit {shelfID}] 段番号 {level} は範囲外です (0〜{levelCount - 1})");
            return null;
        }

        if (currentWeightKg + weightKg > maxWeightKg)
        {
            Debug.LogWarning($"[ShelfUnit {shelfID}] 積載オーバー: {currentWeightKg + weightKg}kg > {maxWeightKg}kg");
            return null;
        }

        float levelHeight = height / levelCount;
        float baseY = level * levelHeight + 0.05f; // 棚板の厚み分オフセット

        float crateW = Random.Range(0.3f, 0.65f);
        float crateH = Random.Range(0.25f, Mathf.Min(0.7f, levelHeight - 0.1f));
        float crateD = Random.Range(0.3f, 0.55f);

        var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        crate.name = "Crate";
        crate.transform.SetParent(transform);
        crate.transform.localPosition = new Vector3(
            Random.Range(crateD / 2f + 0.05f, depth - crateD / 2f - 0.05f),
            baseY + crateH / 2f,
            Random.Range(crateW / 2f + 0.05f, width - crateW / 2f - 0.05f)
        );
        crate.transform.localScale = new Vector3(crateD, crateH, crateW);

        // 段ボール色
        crate.GetComponent<Renderer>().material.color =
            new Color(0.72f + Random.Range(-0.08f, 0.08f),
                      0.55f + Random.Range(-0.08f, 0.08f),
                      0.30f + Random.Range(-0.08f, 0.08f));

        // コライダーを削除 (親のBoxColliderに任せる)
        Destroy(crate.GetComponent<Collider>());

        cratesOnShelf.Add(crate);
        currentWeightKg += weightKg;
        levelOccupied[level] = true;

        return crate;
    }

    /// <summary>
    /// 棚の荷物をすべて削除する
    /// </summary>
    public void ClearAllCrates()
    {
        foreach (var crate in cratesOnShelf)
        {
            if (crate != null)
                Destroy(crate);
        }
        cratesOnShelf.Clear();
        currentWeightKg = 0f;

        for (int i = 0; i < levelOccupied.Length; i++)
            levelOccupied[i] = false;
    }

    /// <summary>
    /// 指定した段の荷物を削除する
    /// </summary>
    public void ClearLevel(int level)
    {
        if (level < 0 || level >= levelCount) return;

        float levelHeight = height / levelCount;
        float levelBase = level * levelHeight;
        float levelTop = levelBase + levelHeight;

        var toRemove = new List<GameObject>();
        foreach (var crate in cratesOnShelf)
        {
            if (crate == null) continue;
            float cy = crate.transform.localPosition.y;
            if (cy >= levelBase && cy < levelTop)
                toRemove.Add(crate);
        }

        foreach (var crate in toRemove)
        {
            cratesOnShelf.Remove(crate);
            Destroy(crate);
        }

        levelOccupied[level] = false;
        RecalculateWeight();
    }

    // ==========================================
    //  公開メソッド: 情報取得
    // ==========================================

    /// <summary>
    /// 棚の荷物の数を返す
    /// </summary>
    public int GetCrateCount()
    {
        cratesOnShelf.RemoveAll(c => c == null);
        return cratesOnShelf.Count;
    }

    /// <summary>
    /// 棚が空かどうか
    /// </summary>
    public bool IsEmpty()
    {
        return GetCrateCount() == 0;
    }

    /// <summary>
    /// 積載率を返す (0〜1)
    /// </summary>
    public float GetLoadRatio()
    {
        return maxWeightKg > 0 ? Mathf.Clamp01(currentWeightKg / maxWeightKg) : 0f;
    }

    /// <summary>
    /// 棚の表示用ラベルを返す
    /// </summary>
    public string GetLabel()
    {
        return $"R{rowIndex}-C{columnIndex}-S{sideIndex}";
    }

    // ==========================================
    //  内部ユーティリティ
    // ==========================================

    void UpdateLevelOccupancy()
    {
        if (levelOccupied == null || levelOccupied.Length != levelCount)
            levelOccupied = new bool[levelCount];

        float levelHeight = height / levelCount;

        for (int lv = 0; lv < levelCount; lv++)
        {
            float levelBase = lv * levelHeight;
            float levelTop = levelBase + levelHeight;
            bool found = false;

            foreach (var crate in cratesOnShelf)
            {
                if (crate == null) continue;
                float cy = crate.transform.localPosition.y;
                if (cy >= levelBase && cy < levelTop)
                {
                    found = true;
                    break;
                }
            }
            levelOccupied[lv] = found;
        }
    }

    void RecalculateWeight()
    {
        // 簡易: 荷物1つ10kg として再計算
        cratesOnShelf.RemoveAll(c => c == null);
        currentWeightKg = cratesOnShelf.Count * 10f;
    }

    // ==========================================
    //  ギズモ (エディタ表示)
    // ==========================================
    void OnDrawGizmosSelected()
    {
        // 棚の範囲をワイヤーフレームで表示
        Gizmos.color = isHighlighted ? highlightColor : Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(
            new Vector3(depth / 2f, height / 2f, width / 2f),
            new Vector3(depth, height, width));

        // 各段を点線で表示
        float levelHeight = height / levelCount;
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        for (int i = 1; i < levelCount; i++)
        {
            float y = i * levelHeight;
            Gizmos.DrawWireCube(
                new Vector3(depth / 2f, y, width / 2f),
                new Vector3(depth, 0.02f, width));
        }
    }
}