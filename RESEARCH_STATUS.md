# 研究状況: ML-Warehouse

最終コード確認日: 2026-09-07

このファイルは、研究上の実装・実験状況を同期するための記録である。記載内容は
コードで確認できた挙動を優先する。ローカルの結果ファイルは記録済みの観測値であり、
手法の有効性を示す結論とは扱わない。

## 対象範囲と現在の起点

- ビルド設定で有効なSceneは `Assets/Scenes/ml Training.unity`。
- `Assets/Scenes/ml test.unity` は推論テスト用として存在するが、現在のビルド設定には
  含まれていない。
- Unity環境の主要コンポーネントは `WarehouseGenerator`、
  `WarehouseTrainingManager`、`WarehouseRobotAgent`、`WarehousePheromone`。
- PPO設定で用いるML-AgentsのBehavior Nameは `WarehouseRobot`。

## 実装済み (Implemented)

### 環境とタスク

- `WarehouseGenerator` が連続3D空間の倉庫を生成する。壁、入口プラットフォーム
  （ドア）、棚、任意の木箱・パレット・柱を生成できる。
- ロボットはランダムに選ばれた入口からタスクを開始し、棚を割り当てられる。
  出口は `WarehouseRobotAgent.OnEpisodeBegin` で一度選ばれ、棚へ置いた後に再抽選
  されない。
- タスクのフェーズは `Delivering`（入口から棚）と `Returning`（棚から出口）。
  割り当て出口に到達するとエージェントのエピソードが終了し、次のタスクが始まる。
- 棚が十分にある間は、`avoidDuplicateShelves` によりエージェント間で同じ棚を
  重複割当しない。
- 目標は現状では点である。配送時は棚前面の位置、帰還時は割り当てられた
  入口/出口の中心位置を用いる。ゲート領域全体をゴールにする処理は未実装。

### エージェントの観測と行動

- ベクトル観測は65次元で、基本観測8、レイキャスト観測48、フェロモン観測9から成る。
- 基本観測は、ローカル前後/左右速度、ヨー角速度、フェーズ、割当棚への相対極座標、
  割当出口への相対極座標である。
- よって方策には常に棚と出口の両方の情報、およびフェーズが入力される。
  `第1目的地` と `第2目的地` という別名の入力はないが、棚/出口の組がその役割を持つ。
- `WarehouseObservations` は水平方向12本のレイを使う。各レイは正規化距離、棚・壁・
  他エージェントの二値指標を追加する（12 x 4 = 48）。
- エージェントは連続値の移動・旋回行動を持つ。PPOのネットワーク設定では観測正規化を
  有効にしている。

### フェロモンマップ

- `WarehousePheromone` は2次元グリッド上にスカラー値を保存する。グリッドサイズは
  `ceil(warehouseWidth / cellSize)` x `ceil(warehouseDepth / cellSize)`。
- 現在の保存単位は完全タスクである。

  ```text
  routeMap[entranceIndex, shelfIndex, exitIndex][gridCell]
  マップ数 = 入口数 x 棚数 x 出口数
  ```

- `Delivering` と `Returning` は同じ完全タスク用マップを参照する。APIにはフェーズ引数が
  残っているが、別マップの選択には使われていない。
- 各行動ステップで、エージェントは自分のタスク用マップの周囲3 x 3セル（9個の生値）を
  観測し、現在セルへ `pheroQ` を加算する。セル値は設定された最小値・最大値に収める。
- フェロモンによる即時報酬は
  `log(訪問前セル値 + 1) * rewardScale`。
- `evapInterval` 回のFixedUpdateごとに全マップ値へ `1 - evapRate` を掛ける。
  値が `0.001` 未満になるとゼロにする。
- `None` ではフェロモンを無効化し、観測にはゼロ9次元を入れる。これにより観測次元数は
  65のまま維持される。
- `TaskSeparated` は現在実装されている完全タスク単位マップを表す。
  `Shared` と `PhaseSeparated` は `WarehousePheromoneMode` と設定値には存在するが、
  現在のマップ確保・参照処理を変更しない。実行時に警告を出す。

### 報酬とエピソード処理

- 棚への配送、出口到達、壁衝突/接触、エージェント衝突/接触、時間経過、落下、
  タイムアウト、棚/出口への距離短縮に対するシェーピング報酬が実装されている。
- スクリプト上の初期値はあるが、実効値はInspectorまたは実験設定で上書きできる。
  したがって、ソース中の初期値を特定実験の使用値とみなしてはならない。
- フェロモンの分泌とフェロモン報酬は、成功時だけでなく移動中の各ステップで発生する。
  経路長・時間による重み付け、拡散は未実装。

### 実験設定

- `WarehouseExperimentConfig` は環境、エージェント、報酬、フェロモンの値を保持する
  `ScriptableObject`。
- `WarehouseEnvironmentPreset` は、倉庫形状、棚、ゲート、障害物、柱、Unity側の環境seedを
  保持するバージョン付き `ScriptableObject`。新しい正式Configでは、これを
  `WarehouseExperimentConfig.environmentPreset` から参照する。
- アセットの標準配置は `Assets/ExperimentConfigs/`（実験Config）と
  `Assets/ExperimentConfigs/Environments/`（倉庫環境Preset）。
- `WarehouseGenerator` は `useExperimentConfig`、`experimentConfig`、
  `experimentRunId` をInspectorに公開している。フラグがオフなら従来どおりInspector値を
  用いる。
- `WarehouseGenerator` の `Environment Preset` にアセットを割り当てると、既存の
  Generatorフィールドへ値をコピーして表示する。リンクを残したまま生成・Playすると
  再適用されるため、手入力へ戻す時はEnvironment Presetを空にする。
- フラグがオンの場合、実行時にアセットの値をGenerator、Manager、Pheromone、Agentへ
  コピーする。アセットそのものは実行中に変更しない。
- フラグがオンでアセット未指定の場合はエラーを出し、初期化を停止する。
- 起動時に、設定元、設定ID、フェロモン方式、蒸発率、分泌量、エージェント数を一度だけ
  Consoleへ出力する。
- 使用プリセットの対応情報を `unity_experiment.json` として一度保存する。run IDが
  `MLAGENTS_RUN_ID`、`--run-id`、`experimentRunId` のいずれかで得られれば、保存先は
  `results/<run-id>/unity_experiment.json`。得られなければ `Logs/`。
- JSONには全設定値を複製せず、実験Configと環境PresetのID、アセット名、内容ハッシュ、
  Unity Editor上ではGUIDを保存する。
- リポジトリ内には `WarehouseExperimentConfig` と `WarehouseEnvironmentPreset` の実アセットが
  まだ存在しない。仕組みは実装済みだが、`Base_v1` などの正式プリセットは未作成・未検証である。

### 学習、モデル管理、テスト

- PPO設定は `warehouse_training.yaml` と `warehouse_training_curiosity.yaml` にある。
  どちらも `WarehouseRobot` を対象とし、後者だけがcuriosity報酬を追加する。
- `tr.py` は `tr.txt` を読み、連番run IDで `mlagents-learn` を繰り返し起動する。
  run IDを `MLAGENTS_RUN_ID` として渡し、結果フォルダ内で選ばれた最終モデルを
  `<run-id>.onnx` または `<run-id>.nn` へ改名し、`Assets/Models/`へコピーする。
- `WarehouseModelTestRunner` はONNXモデルの配列を順に推論テストできる。決定的CPU推論、
  目標達成数またはタイムアウトでの停止、`logTest.csv`保存、フェロモンレポート出力、
  テスト終了後のPlay Mode停止に対応する。
- テストランナーの標準設定は `AutoFromTrainingRecord`。モデル名と同じrun IDの
  `results/<model-name>/unity_experiment.json` を読み、学習時のConfigをGenerator初期化前に
  自動適用する。テストバッチ内の全モデルについて、ConfigとEnvironment PresetのID、アセットGUID、
  設定内容ハッシュを照合し、不一致なら停止する。
- `ManualOverride` を選ぶと、Inspectorで指定した任意のConfigを使ってテストできる。
  各モデルの結果フォルダに `test_experiment.json` を保存し、自動/手動の設定元と学習時Configとの
  一致判定を記録する。
- `WarehousePheromoneFinalReport` は最終集計レポートを出力し、完全タスクごとのマップを
  CSVと濃淡付きPNGとして出力する。テストランナー経由のモデル別出力先は
  `results/<model-name>/`。

## 実験中 (Currently Testing)

- このコード確認時点で、リポジトリから実行中と確認できる学習・推論はない。
- 最新のローカル推論テスト記録は、6個の `16small*` モデルに対するもの。
  これらは履歴上の出力であり、フェロモンのマップ構造間の統制比較は未完了。
- 完全タスク単位マップは現状の実装済み方式である。サブタスク単位マップの実装・実験は
  まだ行われていない。

## 最新結果 (Latest Results)

以下はローカルの `results/*/logTest.csv` から読み取った値。各テストの目標はタスク達成
10回だった。`completed_episodes` は過去の列名であり、実際には全エージェント合計の
成功タスク数を表す。

| モデル | 達成タスク数 | 壁衝突 | エージェント衝突 | 総衝突数 | 所要時間（秒） |
| --- | ---: | ---: | ---: | ---: | ---: |
| `16small01` | 10 | 0 | 84 | 84 | 12.136 |
| `16small02` | 10 | 0 | 72 | 72 | 11.876 |
| `16small03` | 10 | 0 | 120 | 120 | 15.035 |
| `16smallbase01` | 11 | 0 | 62 | 62 | 9.513 |
| `16smallbase02` | 10 | 0 | 138 | 138 | 12.064 |
| `16smallbase03` | 10 | 1 | 102 | 103 | 17.249 |

これら6モデルにはフェロモンの集計結果もある。グリッドは10 x 28、完全タスク用マップは
24枚で、タスク別のCSV/PNGを出力している。これは記述的な出力であり、統計的な結論は
まだ出していない。

## 既知の問題 (Known Problems)

- `Shared` と `PhaseSeparated` は実際のマップ方式として未実装。選択しても完全タスク用
  マップが使用されるため、現時点で実験条件として使ってはならない。
- マップ数が 入口数 x 棚数 x 出口数 で増える。これが現在検討している蓄積希薄化の問題。
- フェロモン値は生のスカラー値として観測される。PPO側で観測正規化は有効だが、実行間で
  フェロモン値の規模が大きく変わり得る。
- 現在の規則は、成功しなかった経路も含めて移動ステップごとに訪問セルを強化する。
  成功時のみの分泌、品質重み付け、拡散を比較することはまだできない。
- 複数エージェントが同一フレームで達成すると、テストランナーの達成数は目標を超え得る。
  `16smallbase01` は目標10に対して11を記録した。
- `logTest.csv` には経路長、タスクごとの所要時間、報酬、混雑、seed、Scene、実効設定IDを
  まだ記録していない。
- 自動テストには、モデル名と一致する `results/<model-name>/unity_experiment.json` が必要。
  既存の古いモデル結果にこのJSONがない場合は、自動方式を使えず `ManualOverride` が必要。
- 古い形式のJSONは現在のフィンガープリント形式を持たないため、アセットIDのみを照合して
  警告する。学習時の値まで完全に検証できるのは、現在以降の学習結果である。
- `unity_experiment.json` は環境起動時に一度だけ作られる。テストバッチ自体の記録は
  `results/<testBatchId>/`、モデルごとの選択記録は `results/<model-name>/test_experiment.json` に
  分かれる。
- `WarehouseGenerator.seed` は倉庫生成時の `UnityEngine.Random` を制御するが、ML-Agentsの
  trainer seedとは別である。タスク割当やスタック回避にも `UnityEngine.Random` を使うため、
  trainer seedだけをそろえてもUnity側の乱数列は保証されない。
- 現在のビルド設定には学習Sceneだけが含まれる。テストにはEditorでテスト用Sceneを開くか、
  ビルド設定を変更する必要がある。
- ソースコメントには、配送/帰還で別マップだった時期の説明が一部残っている。実際のコードは
  完全タスク単位の共通マップを使用する。

## 次タスク (Next Tasks)

1. Unity上で、現行の倉庫を表す `WarehouseEnvironmentPreset` と、これを参照する
   `WarehouseExperimentConfig` アセットを作成・確認する。安定した `presetId` と `configId` を
   決め、学習とONNXテストの両方で使う。
2. サブタスク単位マップの仕様を、実装前に確定する。候補は配送用の `入口 x 棚` マップと、
   帰還用の `棚 x 出口` マップであり、各フェーズでの観測・分泌対象、出力ファイル名を決める。
3. マップ構造を変更する前に、影響範囲と移行方法を説明する。主な対象は
   `WarehousePheromone`、`WarehouseRobotAgent`、`WarehousePheromoneFinalReport`、
   実験設定・記録コードになる見込み。
4. 完全タスク方式とサブタスク方式の比較では、環境、エージェント数、PPO設定、分泌量、
   蒸発率、Unity/trainerのseed集合を固定する。
5. テストログへUnity seed、trainer seed/run ID、経路長、タスクごとの所要時間、報酬、混雑を
   追加する。併せて複数エージェント時の目標超過の扱いを決める。
6. あらかじめ決めた複数seedで推論評価を繰り返し、単発の結果ではなく平均とばらつきを
   集計する。

## 検討中のアイデア (Ideas Under Consideration)

- 成功時のみのフェロモン分泌。
- 経路長、完了時間などの経路品質による分泌量の重み付け。
- 近傍セルへのフェロモン拡散・浸透。
- 成功経路、衝突/混雑、移動方向を表す追加チャネル。
- 推論中に障害物を動的に出現させ、蓄積・蒸発による適応を検証する。
- 現在のゲート中心目標を、ゲート領域全体のゴールに置き換える。
- 離散環境との比較、および連続空間における自己組織的な経路・車線形成の分析。

この節の内容は、`実装済み` にも記載されていない限り未実装である。

## 設計上の決定 (Decisions)

- 現在は、同じ完全タスクに属する配送・帰還でフェロモンマップを共有する。棚へ向かう段階
  から既知の出口も考慮した経路選択を方策に学習させる、という意図による。
- 出口は棚への配送後ではなく、タスク開始時に選択する。これにより、入口・棚・出口の
  タスク識別子をエピソード中一貫して保持する。
- 環境固有の実験条件はScriptableObjectで管理する。PPOハイパーパラメータ、trainer seed、
  `num_envs` はML-AgentsのYAMLまたは起動設定で管理する。
- 日常的な動作確認ではInspector値を使えるよう残す。正式比較実験ではプリセットアセットと
  そのIDを使う。
- ONNXテストは学習記録からConfigを自動選択することを標準とする。任意Configを使うテストは
  `ManualOverride` として結果へ明示記録する。
- ビルド生成物、学習結果、モデルバイナリはGit管理対象外とする。再現時には別途扱う必要が
  ある。

## 最近変更した主要ファイル (Files Changed Recently)

直近の研究ワークフロー関連コミットは `fe6a8fd`
（`Add experiment configuration and testing workflow`）。主な対象ファイルは以下。

- `Assets/Scripts/WarehouseExperimentConfig.cs`
- `Assets/Scripts/WarehouseEnvironmentPreset.cs`
- `Assets/Scripts/WarehouseModelTestRunner.cs`
- `Assets/Scripts/WarehousePheromoneFinalReport.cs`
- `Assets/Scripts/WarehousePheromone.cs`
- `Assets/Scripts/WarehouseRobotAgent.cs`
- `Assets/Scripts/WarehouseTrainingManager.cs`
- `Assets/Scripts/Warehousegenerator.cs`
- `tr.py`、`tr.txt`、`.gitignore`
- `Assets/Scenes/ml Training.unity`、`Assets/Scenes/ml test.unity`
- `Assets/Prefab/Agent.prefab`、`Assets/Prefab/Env ML.prefab`
- `EXPERIMENT_WORKFLOW.md`（学習・推論テストの運用手順書）

現在の作業ツリーには、Unityが生成したタイマー/Editor設定の変更と未追跡のモデル成果物も
ある。これらはこの状態ファイルでは研究ソースの変更として扱わない。

## 次回作業メモ (Notes for Next Session)

- 実装作業を始める前にこのファイルを読み、コードまたは実験状態を変更したら更新する。
- 学習またはONNXテストを実行する前に `EXPERIMENT_WORKFLOW.md` を読み、run IDとConfig IDの
  対応を決める。
- フェロモン構造を変更する前に、マップの添字式、観測次元、レポートファイル名、既存モデル
  との互換性を明文化する。
- `Shared` または `PhaseSeparated` は、保存・参照ロジックが実際に異なるまでは、完了した
  実験条件として扱わない。
- 正式な結果を追加する時は、プリセットID、trainer run ID、seed、評価手順、決定的推論を
  使用したかどうかを記録する。
