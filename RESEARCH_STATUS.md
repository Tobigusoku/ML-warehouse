# 研究状況: ML-Warehouse

最終確認日: 2026-10-01

このファイルはコードと研究状況の同期用である。現状、結果、問題、次作業だけを簡潔に保つ。
操作手順は [EXPERIMENT_WORKFLOW.md](EXPERIMENT_WORKFLOW.md) を参照する。記述が競合する場合はコードを優先する。

## 実装済み (Implemented)

### 環境・エージェント

- Unity + ML-Agentsの連続空間倉庫環境。Agentは `入口 -> 棚 -> 出口` を1Episodeとして搬送する。
- 出口はEpisode開始時に選び、棚到達後も変更しない。フェーズは `Delivering` と `Returning`。
- 行動は連続値の移動・旋回。観測は65次元で、基本情報8、12本のRaycast 48、周囲3 x 3の
  フェロモン9。基本情報には速度、角速度、フェーズ、棚と出口の相対位置が含まれる。
- 現在のゴール判定は棚前面と出口中心の点ベース。ゲート領域全体のゴール判定は未実装。

### フェロモン

- 2次元グリッドのスカラー値を保持する。現在の実装単位は完全タスクである。

  ```text
  routeMap[entrance, shelf, exit][cell]
  マップ数 = 入口数 x 棚数 x 出口数
  ```

- 配送と帰還は同じ完全タスク用マップを使う。各移動ステップで現在セルへ分泌し、周囲9セルを観測する。
- 蒸発、最小・最大値制限、`log(訪問前セル値 + 1) * rewardScale` のフェロモン報酬を実装済み。
- 実際に異なる方式として動くのは `None` と `TaskSeparated`。`Shared` と `PhaseSeparated` はenumのみで、
  マップ構造は未実装。

### 設定・記録

- `WarehouseExperimentConfig` がAgent数、報酬、移動、フェロモン条件を保持する。
- `WarehouseEnvironmentPreset` が倉庫形状、棚、ゲート、障害物、Unity側seedを保持する。
- `Use Experiment Config = false` ではInspector値、trueではConfigのコピーを実効値として使う。
  ScriptableObject本体は実行中に変更しない。
- 起動時に全環境へ同じConfigを適用する。Configの `Agent Count` を1環境あたりの実行数として、
  不足分を実行時生成し、超過した配置AgentをPlay中だけ無効化する。Scene/Prefab自体は変更しない。
- Managerの参照切れ、Agentの重複・別環境参照・登録漏れは停止し、自動調整後の実数も再検証する。
- `unity_experiment.json` にrun ID、trainer seed、Config/PresetのID・名前・GUID・内容ハッシュ、Scene、
  環境数、設定Agent数、実効Agent数を記録する。
- 現在の `taskSeparated_v1` とEnv ML Prefabは、1環境1Agentで一致している。

### 学習・テスト

- `tr.py` は `tr.txt` のrun列と `trainer_seeds` を使い、各runへ `mlagents-learn --seed` を渡す。
  seed数不一致、負数、重複は開始前に停止する。最終モデルをrun ID名へそろえて `Assets/Models` にコピーする。
- 学習中は次のイベント数を `StatsRecorder` でTensorBoardへ送る。

  ```text
  Warehouse/Task/ShelfReached
  Warehouse/Task/Completed
  Warehouse/Episode/Timeout
  Warehouse/Episode/Fall
  ```

- `WarehouseModelTestRunner` は複数ONNX x 複数試行、固定時間/固定達成数、安全タイムアウト、
  決定的推論、試行seed、終了時Play停止に対応する。
- テスト時は学習runのJSONからConfigを自動選択し、内容ハッシュと環境内Agent数を照合する。
  旧モデルや意図的な別条件には `ManualOverride` を使い、その事実をJSONへ記録する。
- 試行ごとにAgent、棚、タスク割当、フェロモン、計測値をリセットし、CSV、要約CSV、
  タスク別フェロモンCSV/PNGを出力する。
- Unityメニュー `Warehouse > Checks > Experiment Startup` に起動・リセット回帰チェック16件がある。

## 実験中 (Currently Testing)

- `wh_middle2Gate_single_none_v1_03`（trainer seed 3）を学習中。2026-10-01確認時点で約1740万step。
- `None_v1` のseed 1・2は5000万stepまで完了し、段階別StatsRecorderと実効1環境1Agentを確認済み。
- 完了したONNXの固定条件による推論テストは未実施。
- 完全タスク方式からサブタスク方式への変更・比較も未実施。

## 最新結果 (Latest Results)

### フェロモンなし `None_v1`

`wh_middle2Gate_single_none_v1_01` と `02` をPPO、5000万step、8環境 x 各1Agentで学習した。

| run | trainer seed | 最終累積報酬 | 終盤100万step平均報酬 | 最終Episode長 | 終盤100万step平均Episode長 | 終盤100万step完了数 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `01` | 1 | 214.379 | 218.743 | 659.2 | 718.4 | 1,398 |
| `02` | 2 | 220.893 | 218.571 | 701.5 | 745.4 | 1,347 |

- 両runとも終盤1000万stepで平均報酬約219、平均Episode長724～764に安定し、タイムアウトは0。
- 最後に記録されたタイムアウトは `01` が3219万step、`02` が3668万step。以後は終盤まで回復を維持した。
- `01` は約194万step、`02` は約695万stepで最初の完全達成を記録した。02の立ち上がりは遅いが最終性能は近い。
- 学習ログ上は両runとも収束したと判断できる。ただしONNX推論での成功率・衝突数確認までは未実施。

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

## 既知の問題 (Known Problems)

- `None_v1` のseed 1・2は学習ログ上収束したが、ONNX推論での再現性はまだ確認していない。
- Unityの環境生成、タスク抽選、観測ノイズ、スタック回避がグローバル乱数を共有する。
  trainer seedをそろえても、Unity側乱数の完全な条件一致は保証されない。
- `tr.py` は最終ONNXを採用する。固定validation seedでcheckpointを選ぶ仕組みはなく、
  `keep_checkpoints: 5` のため過去の良好な方策を失う可能性がある。
- フェロモン報酬scaleは0.0002、時間ペナルティは-0.001。セル値が約147を超えると、
  フェロモン報酬が時間ペナルティを上回り、濃い場所への滞留を促す可能性がある。原因とは未確定。
- 学習ではフェロモンがEpisodeをまたいで蓄積するが、推論試行はゼロ初期化するため、開始時分布が異なる。
- `maxStepLimit` は50,000。失敗Episodeが長く、タイムアウト信号が疎になる可能性がある。
- Agent同士の衝突は双方で数え得る。達成0時の「衝突/タスク」が0になる表示も不適切。
- Raycast観測は `ShelfUnit` を棚として検出するが、現在の衝突判定 `IsWallObject` は名前 `ShelfUnit` を
  対象に含めない。棚への突撃が `crashToWall` と衝突ペナルティへ記録されない可能性が高い。
- 同じモデルを再テストすると結果を上書きする。テストバッチ別保存と移動距離CSVは未実装。
- `Shared` / `PhaseSeparated`、成功時のみ分泌、品質重み付け、拡散、多チャネルは未実装。
- 完全タスク単位ではマップ数が `入口 x 棚 x 出口` で増え、フェロモン蓄積が希薄になる懸念がある。
- `Middle_2Gate_v1` の `presetId` は `environment_v1`、環境seedは0のまま。正式比較前に版付きIDへ直す。
- 旧runは変更前Configの内容ハッシュを持つため、現在の1体Configとは一致しない。旧ONNXの確認には
  Manual Overrideが必要で、新しい正式runと混同しない。

## 次タスク (Next Tasks)

1. `None_v1_03` の学習完了を待ち、3seedの終盤報酬・Episode長・達成数を比較する。
2. 完了した3つのONNXを同じ試行seed・試行回数で推論し、成功率、ばらつき、進路上の棚を無視して
   衝突する挙動がないか確認する。正式な衝突評価前に棚衝突を報酬と分離して計測できるようにする。
3. 学習ログと推論結果が一致するか確認し、正式なフェロモンなし基準結果として固定する。
4. 帰還時のゴールを現在のゲート中心点ではなく、**ゲート幅全体を到達可能なゴール領域として扱う**
   ように変更する。成功判定だけを広げるのではなく、出口に関する観測と距離短縮報酬も同じ定義に
   そろえる。具体的には、各ゲートを `center ± spreadDir * halfWidth` で表される線分として扱い、
   エージェント位置からその線分上の最近傍点を求める。観測では最近傍点への距離・方向、
   シェーピング報酬ではゲート領域までの最短距離の短縮量、成功判定ではゲート線分までの距離が
   `exitRange` 以下かを用いる。出口の識別自体は従来どおり `targetExitIndex` を使い、
   フェロモンのタスク識別 `入口 x 棚 x 出口` は変更しない。この変更はフェロモン方式の提案ではなく、
   Base/Phero双方に共通する環境・タスク設計の修正として正式比較前に行う。
5. 衝突を主要評価に使う前に、二重計上とゼロ達成時指標を修正する。
6. 必要になった段階で、テストバッチ別保存、checkpoint選択、Unity乱数分離を実装する。
7. `pheromoneRewardScale = 0` と現行条件を比較し、異常挙動が報酬由来か切り分ける。
8. 成功runのEpisode長を基準に、`maxStepLimit`短縮候補を決める。
9. 基盤確認後、`入口 x 棚` と `棚 x 出口` のサブタスク単位マップを設計・実装する。

## 検討中 (Ideas Under Consideration)

- 成功時のみ分泌、経路長・所要時間による重み付け、浸透・拡散。
- 成功経路、衝突・混雑、移動方向を別チャネルで保持する方式。
- 推論中に障害物を追加し、蓄積と蒸発による動的環境への適応を評価する。
- 離散環境との比較、連続空間での道・車線の自己組織化。

## 設計判断 (Decisions)

- 出口はEpisode開始時に決め、棚へ向かう段階から最終出口を観測へ含める。
- 現行の配送・帰還は同じ完全タスクマップを共有する。
- Unity固有条件はScriptableObject、PPO・trainer seed・`num_envs`はML-Agents側で管理する。
- 環境内Agent数は研究条件。並列環境数は実行情報として記録し、Config項目にはしない。
- Config使用時の `Agent Count` を実行値の正とし、Scene/Prefabの配置数は初期Agentの用意にだけ使う。
  手動設定モードでは従来の配置と `Auto Spawn Count` を維持する。
- 正式比較は版付きConfig/Presetを使い、既存Assetを上書きせずv2を作る。
- ONNXテストは学習記録からConfigを自動選択する。異なる条件はManual Overrideとして明示する。
- checkpoint選択用validation seedと最終報告用test seedは分ける方針。

## 最近変更した主要ファイル (Files Changed Recently)

直近の主な更新: Agent数の実行時調整とフェロモンなし基準Configを追加。

- `Assets/Scripts/WarehouseExperimentConfig.cs`
- `Assets/Scripts/WarehouseModelTestRunner.cs`
- `Assets/Scripts/WarehouseTrainingManager.cs`
- `Assets/Scripts/WarehouseRobotAgent.cs`
- `Assets/Scripts/Shelfunit.cs`
- `Assets/Scripts/Editor/WarehouseStartupChecks.cs`
- `Assets/ExperimentConfigs/`
- `Assets/Prefab/Env ML.prefab`、`Assets/Scenes/ml test.unity`
- `tr.py`、`tr.txt`、`EXPERIMENT_WORKFLOW.md`

確認済み: C#本体・Editorコードのビルド成功、Python構文・seed読込成功。以前の分離Unityプロジェクトでは
Edit Mode 15件と2環境 x 2AgentのPlay確認成功。Agent数自動調整を加えた16件版はUnityライセンスIPCが
起動できず未実行。本番Player再ビルド、trainer接続、新指標の実学習確認も未実施。

## 次回メモ (Notes for Next Session)

- 作業開始時にこのファイル、学習・テスト前に `EXPERIMENT_WORKFLOW.md` を読む。
- 次回の最初の作業は `None_v1_03` 完了確認と、3モデルの統一条件による推論テスト。
- ゲート全体をゴール領域にする変更は、次の正式な再学習より前に行う。成功判定、出口観測、
  距離短縮報酬をすべてゲート線分上の最近傍点基準へそろえ、Base/Phero共通仕様とする。
- 報酬、観測、Episode上限は最新コミットでは変更していない。
- `Shared` と `PhaseSeparated` を実装済み条件として扱わない。
- 現在の未コミット差分にはUnityタイマー、IDE、QualitySettings、UserSettingsのローカル変更がある。
  研究ソースの変更として扱わない。
