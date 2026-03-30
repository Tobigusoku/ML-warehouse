using UnityEngine;

/// <summary>
/// 学習時のパフォーマンス設定を一括管理する。
///
/// ■ 使い方:
///   シーン内に1つ配置し、各スクリプトが WarehousePerformance.I で参照する。
///   学習時は trainingMode = true にするだけで不要な視覚処理がすべてOFFになる。
///
/// ■ trainingMode = true で無効になるもの:
///   - 棚のハイライト (色変え・発光・パルス・ビーコン)
///   - 棚マーカー / 出口マーカーの生成・表示
///   - マーカーパルスアニメーション
///   - レイキャストのDebug.DrawRay
///   - Debug.Log 出力
///   - HUD 表示
///   - ロボットの色変更
///   - 荷物のビジュアル生成
/// </summary>
public class WarehousePerformance : MonoBehaviour
{
    // ==========================================
    //  シングルトン
    // ==========================================
    private static WarehousePerformance _instance;

    /// <summary>
    /// シーン内のインスタンス。存在しない場合は全てON (デフォルト動作)。
    /// </summary>
    public static WarehousePerformance I
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType<WarehousePerformance>();
            return _instance;
        }
    }

    // ==========================================
    //  設定
    // ==========================================
    [Header("===== モード =====")]
    [Tooltip("ONにすると学習に不要な視覚処理を一括OFF")]
    public bool trainingMode = false;

    [Header("===== 個別設定 (trainingMode=false 時に有効) =====")]
    [Tooltip("棚のハイライト (色変え・発光・パルス・ビーコン)")]
    public bool enableShelfHighlight = true;

    [Tooltip("棚マーカー / 出口マーカー")]
    public bool enableMarkers = true;

    [Tooltip("レイキャストのDebug.DrawRay 描画")]
    public bool enableDebugRays = true;

    [Tooltip("Debug.Log 出力")]
    public bool enableDebugLog = true;

    [Tooltip("DebugHUD 表示")]
    public bool enableHUD = true;

    [Tooltip("荷物のビジュアル生成")]
    public bool enableCargoVisual = true;

    // ==========================================
    //  プロパティ (trainingMode が ON なら全てOFF)
    // ==========================================
    public bool ShelfHighlight  => !trainingMode && enableShelfHighlight;
    public bool Markers         => !trainingMode && enableMarkers;
    public bool DebugRays       => !trainingMode && enableDebugRays;
    public bool DebugLog        => !trainingMode && enableDebugLog;
    public bool HUD             => !trainingMode && enableHUD;
    public bool CargoVisual     => !trainingMode && enableCargoVisual;

    // ==========================================
    //  ヘルパー (null安全)
    // ==========================================

    /// <summary> WarehousePerformance が存在しない場合は true を返す (デフォルトON) </summary>
    public static bool IsEnabled(System.Func<WarehousePerformance, bool> prop)
    {
        var inst = I;
        return inst == null || prop(inst);
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }
}
