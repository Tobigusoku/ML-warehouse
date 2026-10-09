# 研究状況: ML-Warehouse

最終確認日: 2026-10-09

このファイルはコードと研究状況の同期用である。現状、結果、問題、次作業だけを簡潔に保つ。
操作手順は [EXPERIMENT_WORKFLOW.md](EXPERIMENT_WORKFLOW.md) を参照する。記述が競合する場合はコードを優先する。

## 実装済み (Implemented)

### 環境・エージェント

- Unity + ML-Agentsの連続空間倉庫環境。Agentは `入口 -> 棚 -> 出口` を1Episodeとして搬送する。
- 出口はEpisode開始時に選び、棚到達後も変更しない。フェーズは `Delivering` と `Returning`。
- 行動は連続値の移動・旋回。基本観測8、12本のRaycast 48にフェロモン観測を加える。
  `LegacyScalar9` は合計65次元、`VectorField27` は合計83次元。基本情報には速度、角速度、
  フェーズ、棚と出口の相対位置が含まれる。
- 帰還ゴールは選択されたゲートの全幅を線分として扱う。出口観測、距離短縮報酬、成功判定はすべて、
  Agent位置からその線分上の最近傍点までの距離・方向を使う。ゲートindexと観測次元は変更していない。
- ゲートのゴール幅とスポーン幅は分離しており、スポーンは従来どおりゲート端から0.5 m内側に配置する。

### フェロモン

- 2次元グリッドで、次のマップ分割を実装済み。

  ```text
  Shared:            全タスク共通1枚
  TaskSeparated:     入口 x 棚 x 出口
  SubtaskSeparated:  配送=入口 x 棚、帰還=棚 x 出口
  ```

- `TaskSeparated` は配送と帰還で同じ完全タスクマップを使う。`SubtaskSeparated` は両フェーズを別マップにする。
- セル内容は `Scalar` と `Directional` を選択できる。Directionalは分泌量で重み付けした実移動方向を
  蓄積・蒸発し、周囲9セルを `[正規化強度, Agentローカル方向X, Z]` の27値として観測する。
- Scalarだけが `log(訪問前セル値 + 1) * rewardScale` の報酬を返す。Directionalのフェロモン報酬は0。
- `PhaseSeparated` は未使用かつ未実装だったため削除した。既存Asset互換のため `TaskSeparated=3` は維持した。

### 設定・記録

- `WarehouseExperimentConfig` がAgent数、報酬、移動、フェロモン条件を保持する。
- `WarehouseEnvironmentPreset` が倉庫形状、棚、ゲート、障害物、Unity側seedを保持する。
- `Use Experiment Config = false` ではInspector値、trueではConfigのコピーを実効値として使う。
  ScriptableObject本体は実行中に変更しない。
- 起動時に全環境へ同じConfigを適用する。Configの `Agent Count` を1環境あたりの実行数として、
  不足分を指定された `Agent Prefab` から実行時複製し、超過した配置AgentをPlay中だけ無効化する。
  Cubeを動的に組み立てる方式は廃止し、配置個体とMesh・コンポーネント構成をそろえた。
- Managerの参照切れ、Agentの重複・別環境参照・登録漏れは停止し、自動調整後の実数も再検証する。
- `unity_experiment.json` にrun ID、trainer seed、Config/PresetのID・名前・GUID・内容ハッシュ、Scene、
  環境数、設定Agent数、実効Agent数を記録する。
- `Env ML.prefab` には1体を配置し、`taskSaparated_v2` の6体条件では同じ `Agent.prefab` を5体複製する。

### 学習・テスト

- `tr.py` は `tr.txt` のrun列と `trainer_seeds` を使い、各runへ `mlagents-learn --seed` を渡す。
  seed数不一致、負数、重複は開始前に停止する。最終モデルをrun ID名へそろえて `Assets/Models` にコピーする。
- 学習中は次のイベント数を `StatsRecorder` でTensorBoardへ送る。

  ```text
  Warehouse/Task/ShelfReached
  Warehouse/Task/Completed
  Warehouse/Episode/Timeout
  Warehouse/Episode/Fall
  Warehouse/Collision/Wall
  Warehouse/Collision/Shelf
  Warehouse/Collision/Agent
  ```

- `WarehouseModelTestRunner` は複数ONNX x 複数試行、固定時間/固定達成数、安全タイムアウト、
  決定的推論、試行seed、終了時Play停止に対応する。
- テスト時は学習runのJSONからConfigを自動選択し、内容ハッシュと環境内Agent数を照合する。
  旧モデルや意図的な別条件には `ManualOverride` を使い、その事実をJSONへ記録する。
- 試行ごとにAgent、棚、タスク割当、フェロモン、計測値をリセットし、CSV、要約CSV、
  選択方式に応じた完全タスク/サブタスク別フェロモンCSV・PNGを出力する。Directionalでは方向X/ZのCSVも出力する。
- 壁・棚・Agent衝突を別々に計測する。配送中の目的棚への接触は棚衝突から除外し、目的外棚と
  帰還中の棚への接触を数える。同じ棚への継続接触と複数Colliderは1回にまとめる。
- テストCSVと要約CSVは壁・棚・Agent別の衝突数を出力し、合計と1タスク当たり衝突には3種を含める。
- Unityメニュー `Warehouse > Checks > Experiment Startup` に起動・リセット回帰チェック20件がある。
- 推論HUDは画面全体で代表1つだけを描画し、Agent数に応じた重複表示を防ぐ。画面高を超える場合は
  スクロールできる。完全タスク方式では3表示、サブタスク方式では実在する2表示を切り替え、
  選択レイヤーを床ヒートマップへ反映する。

## 実験中 (Currently Testing)

- `None_v1` のseed 1～3は5000万stepの学習と、固定条件によるONNX推論テストを完了した。
- 棚衝突の分離計測は実装済み。Unity実機で目的棚除外、継続接触の重複防止、新CSVを確認する段階。
- ゲート全幅のゴール領域化は実装済み。Unity実機で端付近からの帰還成功と表示を確認する段階。
- 推論HUDの単一表示、スクロール、完全タスク層選択は実装済み。多Agentかつ小さいGame Viewでの目視確認待ち。
- 次の推論ではAgent数を1、2、4体と段階的に増やし、単体方策の再現性と多Agent時の混雑を分けて確認する。
- `SubtaskSeparated` とDirectional観測は実装済みだが、Unity実機確認と学習比較は未実施。
- 6Agent推論は `_01`・`_02` の300秒試行を完了。`_03` はUnity Scene Viewの無限エラーで停止し、再実行待ち。

## 最新結果 (Latest Results)

### フェロモンなし `None_v1`

`wh_middle2Gate_single_none_v1_01`～`03` をPPO、5000万step、8環境 x 各1Agentで学習した。

| run | trainer seed | 最終累積報酬 | 終盤100万step平均報酬 | 最終Episode長 | 終盤100万step平均Episode長 | 終盤100万step完了数 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `01` | 1 | 214.379 | 218.743 | 659.2 | 718.4 | 1,398 |
| `02` | 2 | 220.893 | 218.571 | 701.5 | 745.4 | 1,347 |
| `03` | 3 | 232.348 | 220.089 | 838.1 | 727.7 | 1,376 |

- 3runとも終盤100万stepで平均報酬約219～220、平均Episode長約718～745に安定した。
- 最後のタイムアウトは `01` が3219万step、`02` が3668万step、`03` が876万step。終盤1000万stepは全runで0。
- 最初の完全達成は `01` が194万step、`02` が695万step、`03` が226万step。立ち上がりには差があるが終盤性能は近い。

### フェロモンなしONNX推論

各最終ONNXを、学習記録からConfigを自動選択し、内容ハッシュ一致を確認したうえで評価した。
条件は決定的推論、1環境 x 1Agent、trial seed 1～5、固定シミュレーション時間40秒 x 5試行。

| run | 平均達成数 | 達成数標準偏差 | 平均tasks/分 | 平均記録衝突数 | 衝突数標準偏差 | 平均衝突/タスク |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `01` | 2.2 | 0.4 | 3.3 | 1.2 | 0.980 | 0.533 |
| `02` | 2.0 | 0.0 | 3.0 | 2.0 | 1.789 | 1.000 |
| `03` | 2.2 | 0.4 | 3.3 | 1.4 | 1.960 | 0.700 |

- 全15試行で2件以上を達成し、合計32件。全試行が40秒の時間条件で正常終了した。
- 3モデルの達成性能はこの試行数では近く、学習ログ上の収束が最終ONNX推論でも再現された。
- Agent衝突は全試行0だが1体条件なので評価材料にならない。記録衝突は壁系のみで、合計は `01` が6、
  `02` が10、`03` が7。
- このテスト実行時の旧判定は `ShelfUnit` を衝突計測に含めないため、上表から棚衝突の有無や
  モデル間の衝突性能差は結論できない。
- `None` 条件のフェロモン出力は全ルートで0であり、設定どおりの結果。

### 棚衝突分離後のスモークテスト

2026-10-02に `wh_middle2Gate_single_none_v1_01` を決定的推論、1Agent、trial seed 1、40秒 x 1試行で確認した。

| 達成数 | 壁衝突 | 棚衝突 | Agent衝突 | 合計衝突 | tasks/分 | 衝突/タスク |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 2 | 0 | 6 | 0 | 6 | 3.0 | 3.0 |

- 新しい試行CSVと要約CSVに、壁・棚・Agent別の列が出力され、合計にも棚衝突が含まれることを確認した。
- 旧計測で見えなかった棚接触が1試行で6回記録された。原因やモデル間差を判断するには複数試行が必要。
- 同一モデルの再実行により `01` の旧5試行CSVは上書きされた。旧集計値は上表に記録済みだが、
  テストバッチ別保存が必要であることを再確認した。

### 旧フェロモンあり予備結果

`wh_middle2Gate_single_v1_01`～`03` をPPO、50,000,000ステップで学習し、各ONNXを
固定シミュレーション時間40秒 x 5試行で評価した。

| run | 最終累積報酬 | 最終Episode長 | 推論時の達成数 |
| --- | ---: | ---: | ---: |
| `01` | -19.704 | 49,999.0 | 0 |
| `02` | 214.487 | 717.7 | 10（各試行2） |
| `03` | -73.616 | 49,999.0 | 0 |

- `02`だけが全試行で達成した。`01`と`03`は最大Episode長付近へ張り付き、未収束とみられる。
- 旧runはtrainer seed未指定（`seed: -1`）。JSONには旧Config値16体が残る一方、当時の実装とPrefabから
  実効1体だった可能性が高いが確定できない。この結果は予備結果であり、方式比較の正式結果ではない。
- 過去のSmall環境結果とフェロモン画像は `results/` にあるが、統制された最新比較ではない。

### 6Agentフェロモンあり予備結果

- `wh_middle2Gate_six_v1_01`～`03` は、学習記録上の実効Agent数が各環境6体。
- 学習終了時は `_01` と `_03` がEpisode長49,999へ張り付き、`_02` はEpisode長約661、累積報酬約210で収束した。
- 2026-10-09に、1環境 x 6Agent、trial seed 1、決定的推論、シミュレーション300秒で評価した。

  | model | 達成 | tasks/分 | 壁 | 棚 | Agent | 合計 | 衝突/タスク |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
  | `_01` | 0 | 0.0 | 4 | 3 | 10 | 17 | 算出不可 |
  | `_02` | 116 | 23.2 | 151 | 110 | 60 | 321 | 2.767 |
  | `_03` | 未完了 | - | - | - | - | - | - |

- `_01` の未達成と `_02` の高い達成数は学習ログの収束差と整合する。ただし各1試行のみで、
  衝突数はAgent数や達成数で正規化した追試が必要。
- `_03` は推論中にUnity EditorのScene Viewが0サイズRenderTextureを連続生成し、ログ増大とフリーズが発生したため手動停止した。
  CSVはヘッダのみで結果として扱わない。

## 既知の問題 (Known Problems)

- `None_v1` のseed 1～3は1体・40秒 x 5試行でONNX推論の達成を確認したが、長時間・多Agent条件は未評価。
- Unityの環境生成、タスク抽選、観測ノイズ、スタック回避がグローバル乱数を共有する。
  trainer seedをそろえても、Unity側乱数の完全な条件一致は保証されない。
- `tr.py` は最終ONNXを採用する。固定validation seedでcheckpointを選ぶ仕組みはなく、
  `keep_checkpoints: 5` のため過去の良好な方策を失う可能性がある。
- フェロモン報酬scaleは0.0002、時間ペナルティは-0.001。セル値が約147を超えると、
  フェロモン報酬が時間ペナルティを上回り、濃い場所への滞留を促す可能性がある。原因とは未確定。
- 学習ではフェロモンがEpisodeをまたいで蓄積するが、推論試行はゼロ初期化するため、開始時分布が異なる。
- `maxStepLimit` は50,000。失敗Episodeが長く、タイムアウト信号が疎になる可能性がある。
- Agent同士の衝突は双方で数え得る。達成0時の「衝突/タスク」が0になる表示も不適切。
- 棚衝突の分離計測とCSV出力は1モデル x 1試行で実機確認済み。目的棚除外と継続接触の判定は
  回帰チェック済みだが、モデル間比較には同一条件での再テストが必要。
- 同じモデルを再テストすると結果を上書きする。テストバッチ別保存と移動距離CSVは未実装。
- 成功時のみ分泌、品質重み付け、拡散、衝突・混雑等の多チャネルは未実装。
- 完全タスク単位ではマップ数が `入口 x 棚 x 出口` で増え、フェロモン蓄積が希薄になる懸念がある。
- `Middle_2Gate_v1` の `presetId` は `environment_v1`、環境seedは0のまま。正式比較前に版付きIDへ直す。
- 旧runは変更前Configの内容ハッシュを持つため、現在の1体Configとは一致しない。旧ONNXの確認には
  Manual Overrideが必要で、新しい正式runと混同しない。
- ゲート全幅化により出口観測、距離短縮報酬、成功判定の意味が変わった。既存ONNXは動作確認には使えるが、
  正式比較には新仕様での再学習が必要。
- 床ヒートマップは選択レイヤーごとの最大値で色を正規化するため、異なるレイヤー間で同じ色が同じ絶対値を
  表すとは限らない。共通スケール表示と複数レイヤー同時重畳は未実装。
- Directionalの最終方向値はCSVへ出るが、HUD/床表示は強度ヒートマップのみで方向矢印は未実装。
- `VectorField27` は観測数が83へ変わるため、65入力の既存ONNXとは互換性がなく再学習が必要。
- 2026-10-09の `_03` 推論中、Unity Editorが `RenderTexture.Create failed: width & height must be larger than 0` と
  `Graphics.SetRenderTarget called with bad depth RenderBuffer` を大量出力した。スタックは `UnityEditor.SceneView.DoOnGUI`
  のみで倉庫コードを含まず、Scene Viewのレイアウトサイズ0が原因とみられる。手動停止後は出力も停止。
  再実行前にSceneタブのサイズを戻すかEditor Layoutをリセットする。当該 `Editor.log` は約2.28 GB。

## 次タスク (Next Tasks)

1. Unity EditorのScene Viewレイアウトを正常化し、`wh_middle2Gate_six_v1_03` の300秒推論を再実行する。
2. Unityメニューの20件版回帰チェックを実行し、`SubtaskSeparated + Directional + VectorField27` の
   短時間学習/推論で観測数83、サブタスク分離、方向CSVを確認する。
3. Agent数を2、4体と増やして、棚衝突、Agent衝突、
   ゲート混雑、観測不足を切り分ける。Agent同士の衝突時に両者を終了・再生成する条件も比較候補とする。
4. 衝突を主要評価に使う前に、Agent衝突の双方計上とゼロ達成時指標を修正する。
5. 必要になった段階で、テストバッチ別保存、checkpoint選択、Unity乱数分離を実装する。
6. `pheromoneRewardScale = 0` と現行条件を比較し、異常挙動が報酬由来か切り分ける。
7. 成功runのEpisode長を基準に、`maxStepLimit`短縮候補を決める。
8. `None`、Scalarの`TaskSeparated`、Directionalの`SubtaskSeparated`を同一seed集合で比較する。

## 検討中 (Ideas Under Consideration)

- 成功時のみ分泌、経路長・所要時間による重み付け、浸透・拡散。
- 成功経路、衝突・混雑を別チャネルで保持する方式。移動方向チャネル自体は実装済み。
- 推論中に障害物を追加し、蓄積と蒸発による動的環境への適応を評価する。
- 衝突原因を、棚への衝突、Agent同士の回避失敗、ゲート付近の混雑、観測不足に分けて検証する。
- 棚衝突へのペナルティ導入は、分離計測の結果を見てから決める。今回の計測実装では報酬を変更しない。
- Agent同士が衝突した場合に両者を同時終了・再生成する条件を設け、連続衝突や詰まりへの影響を比較する。
- 離散環境との比較、連続空間での道・車線の自己組織化。

## 設計判断 (Decisions)

- 出口はEpisode開始時に決め、棚へ向かう段階から最終出口を観測へ含める。
- 帰還ゴールは選択ゲートの中心点ではなく全幅の線分とし、観測、距離報酬、成功判定を同じ最近傍点定義へそろえる。
- マップ分割とセル内容を独立したConfig項目とし、組み合わせを起動時に制限しない。
- サブタスク方式は配送を `入口 x 棚`、帰還を `棚 x 出口` に分ける。完全タスク方式は従来互換で両フェーズ共有。
- 既存ONNX用の65次元観測を残し、新しい方向方式は明示的に83次元を選ぶ。
- HUDは完全タスク方式では3表示、サブタスク方式では実在する `入口→棚` / `棚→出口` の2表示を使う。
- Unity固有条件はScriptableObject、PPO・trainer seed・`num_envs`はML-Agents側で管理する。
- 環境内Agent数は研究条件。並列環境数は実行情報として記録し、Config項目にはしない。
- Config使用時の `Agent Count` を実行値の正とし、Scene/Prefabの配置数は初期Agentの用意にだけ使う。
  手動設定モードでは従来の配置と `Auto Spawn Count` を維持する。
- 不足AgentはManagerの `Agent Prefab` からのみ複製する。Prefab未設定時は別形状の代替個体を作らず起動を停止する。
- 正式比較は版付きConfig/Presetを使い、既存Assetを上書きせずv2を作る。
- ONNXテストは学習記録からConfigを自動選択する。異なる条件はManual Overrideとして明示する。
- checkpoint選択用validation seedと最終報告用test seedは分ける方針。

## 最近変更した主要ファイル (Files Changed Recently)

直近の主な更新: 不足Agentを `Agent.prefab` から複製するようにし、手動配置個体と外観・構成を統一。

- `Assets/Scripts/WarehouseExperimentConfig.cs`
- `Assets/Scripts/WarehouseModelTestRunner.cs`
- `Assets/Scripts/WarehouseTrainingManager.cs`
- `Assets/Scripts/WarehouseRobotAgent.cs`
- `Assets/Scripts/WarehouseDebugHUD.cs`
- `Assets/Scripts/WarehousePheromone.cs`
- `Assets/Scripts/Shelfunit.cs`
- `Assets/Scripts/Editor/WarehouseStartupChecks.cs`
- `Assets/ExperimentConfigs/`
- `Assets/Prefab/Env ML.prefab`、`Assets/Scenes/ml test.unity`
- `tr.py`、`tr.txt`、`EXPERIMENT_WORKFLOW.md`

確認済み: 棚衝突版のUnity回帰チェック17件、6Agent・300秒推論の `_01`・`_02` CSV保存、
Prefab複製変更後のC#本体・Editorコードの警告0・エラー0ビルド。20件版Unity回帰チェック、
短時間推論、本番Player再ビルド、trainer接続、新仕様での実学習確認は未実施。

## 次回メモ (Notes for Next Session)

- 作業開始時にこのファイル、学習・テスト前に `EXPERIMENT_WORKFLOW.md` を読む。
- 次回はScene Viewのレイアウトを直して `_03` を再実行し、追加の5体も円柱Prefabで生成されることを目視確認する。
- HUDはGame View右側に1枚だけ表示されること、`E×S×X` / `E→S` / `S→X` の選択と床表示が一致すること、
  Agent行が増えたとき縦スクロールできることを確認する。
- 現在の推論結果は旧中心点仕様の1体条件として保持する。正式比較はゲート全幅仕様で再学習して行う。
- 既存Configは `LegacyScalar9` のまま65次元。方向方式の新Configだけ `VectorField27` を選び83次元で新規学習する。
- `Shared` と `SubtaskSeparated` はコード実装済みだが、有効性を示す実験結果はまだない。
- 現在の未コミット差分にはUnityタイマー、IDE、QualitySettings、UserSettingsのローカル変更がある。
  研究ソースの変更として扱わない。
