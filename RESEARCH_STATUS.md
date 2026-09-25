# 研究状況: ML-Warehouse

最終コード確認日: 2026-09-25

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
- 現在のアセット配置は `Assets/ExperimentConfigs/`（実験Config）と
  `Assets/ExperimentConfigs/Experiments/`（倉庫環境Preset）。
- `WarehouseGenerator` は `useExperimentConfig`、`experimentConfig`、
  `experimentRunId` をInspectorに公開している。フラグがオフなら従来どおりInspector値を
  用いる。
- `WarehouseGenerator` の `Environment Preset` にアセットを割り当てると、既存の
  Generatorフィールドへ値をコピーして表示する。リンクを残したまま生成・Playすると
  再適用されるため、手入力へ戻す時はEnvironment Presetを空にする。
- フラグがオンの場合、実行時にアセットの値をGenerator、Manager、Pheromone、Agentへ
  コピーする。アセットそのものは実行中に変更しない。
- 起動時に有効な全環境を検証してから、各環境へConfigを適用する。従来のstaticフラグにより
  最初の1環境だけへ適用される問題を修正した。並列環境は同じConfigアセット、使用フラグ、
  run IDを選ぶ。Play中の環境追加は未対応。
- 各環境はGeneratorと同じ親の下にManagerを1つ置き、ManagerにPheromoneを付ける。
  ManagerのGenerator/Env Root参照、Agentの参照切れ・重複・無効化・他環境所属・登録漏れを検査する。
- ConfigのAgent数と配置済みAgent数が不一致なら、警告だけで続行せずPlayを停止する。
  Playerでは終了コード1で終了する。既存Agentは勝手に増減しない。
- Agent一覧が空で配置済みの有効Agentもない場合は、従来の自動生成を使いConfigのAgent数を生成する。
  自動生成Agentは親・参照・Config・Behavior Parametersを設定後に有効化する。
  Behaviorは既存Agent Prefabに合わせてWarehouseRobot、観測65、連続行動2、DecisionPeriod 1。
  Managerへの二重登録を防ぎ、生成後にも実数を検証する。
- フラグがオンでアセット未指定の場合はエラーを出し、初期化を停止する。
- 起動時に、設定元、設定ID、フェロモン方式、蒸発率、分泌量、エージェント数を一度だけ
  Consoleへ出力する。
- 使用プリセットの対応情報を `unity_experiment.json` として一度保存する。run IDが
  `MLAGENTS_RUN_ID`、`--run-id`、`experimentRunId` のいずれかで得られれば、保存先は
  `results/<run-id>/unity_experiment.json`。得られなければ `Logs/`。
- JSONには全設定値を複製せず、実験Configと環境PresetのID、アセット名、内容ハッシュ、
  Unity Editor上ではGUIDを保存する。
- 全Managerの初期化・Agent数検証後、一度だけJSONを保存する。追加項目は
  `environmentInstanceCount`、`totalAgentInstances`、各環境のパス・設定Agent数・実効Agent数を
  持つ `environmentInstances`。数値は当該Unityプロセス内のみであり、trainerの `num_envs` を推測しない。
  並列環境数はConfigの研究条件ではなく、自動記録する実行情報として扱う。
- Domain Reloadを無効にしたPlayにも備え、起動時に設定適用・記録のstatic状態を初期化する。
- ローカルには `taskSeparated_v1` Configと、`Small_2Gate_v1`、`Middle_2Gate_v1` の環境Presetが
  作成されている。`taskSeparated_v1` は `Middle_2Gate_v1` を参照している。

### 学習、モデル管理、テスト

- PPO設定は `warehouse_training.yaml` と `warehouse_training_curiosity.yaml` にある。
  どちらも `WarehouseRobot` を対象とし、後者だけがcuriosity報酬を追加する。
- `tr.py` は `tr.txt` を読み、連番run IDで `mlagents-learn` を繰り返し起動する。
  run IDを `MLAGENTS_RUN_ID`、resultsの絶対パスを `WAREHOUSE_RESULTS_DIR` として渡す。
  Unity側の設定JSONとML-Agents出力を同じ `results/<run-id>/` に保存し、選ばれた最終モデルを
  `<run-id>.onnx` または `<run-id>.nn` へ改名し、`Assets/Models/`へコピーする。
- `tr.txt` の `trainer_seeds` にrunごとのseedを列挙し、`tr.py` が各runへ `--seed` を明示する。
  seed数とrun数の不一致、負数、重複は学習開始前にエラーとする。seedはML-Agentsの
  `configuration.yaml` に加え、環境変数経由で `unity_experiment.json` にも記録する。
- Agentは棚到達、完全タスク達成、ステップ上限タイムアウト、落下を `StatsRecorder` の
  Sum指標として送る。新しい学習では `Warehouse/Task/ShelfReached`、
  `Warehouse/Task/Completed`、`Warehouse/Episode/Timeout`、`Warehouse/Episode/Fall` を
  TensorBoardで確認できる。報酬値とEpisode終了条件は変更していない。
- 旧ビルドが誤って `App/results/<run-id>/unity_experiment.json` へ書いた場合は、学習終了後に
  `tr.py` が正規の結果フォルダへコピーする互換処理を持つ。
- `WarehouseModelTestRunner` はONNXモデルの配列を順に推論テストできる。各モデルに共通の
  試行回数を設定し、試行ごとに「固定タスク達成数」または「固定シミュレーション時間」を
  終了条件として選べる。決定的CPU推論、実時間の安全タイムアウト、テスト終了後の
  Play Mode停止にも対応する。
- 各試行ではManagerの試行リセットAPIから、フェロモン、棚に成功時追加された荷物、
  棚の重量・ハイライト、スポーンスロット・棚割当、Agentの達成数・衝突数・移動距離を
  一括リセットする。倉庫生成時から棚にあった荷物は初期状態として保持する。
  全Agentを新しいEpisodeへ再配置してから指標をゼロにし、物理Transformを同期して計測を始める。
  任意でUnity乱数を `baseTrialSeed + 試行番号 - 1` に再設定し、全モデルで同じ試行seed集合を使える。
- Agent初期化時とEpisode再配置時に移動距離の基準位置を更新し、初期配置・Episode間の
  テレポートを走行距離へ加算しない。移動距離のCSV出力自体はまだ未実装。
- モデルごとの `logTest.csv` は1試行1行で逐次保存し、`logTest_summary.csv` に平均と
  母標準偏差を保存する。フェロモンレポートは
  `results/<model-name>/pheromone_trials/trial_XX/` へ試行別に出力する。
- テストランナーの標準設定は `AutoFromTrainingRecord`。モデル名と同じrun IDの
  `results/<model-name>/unity_experiment.json` を読み、学習時のConfigをGenerator初期化前に
  自動適用する。テストバッチ内の全モデルについて、ConfigとEnvironment PresetのID、アセットGUID、
  設定内容ハッシュを照合し、不一致なら停止する。
- ビルド版学習ではEditor専用のAssetDatabaseを使えず、設定JSONのアセットGUIDは空になる。
  Editorテストではプロジェクト内のConfigを検索し、ID、名前、内容ハッシュから自動解決する。
  候補が一意でなければ勝手に選択せず停止する。Player版テストでは `configFallbacks` を使う。
- `ManualOverride` を選ぶと、Inspectorで指定した任意のConfigを使ってテストできる。
  各モデルの結果フォルダに `test_experiment.json` を保存し、自動/手動の設定元と学習時Configとの
  一致判定を記録する。
- テスト開始は固定0.5秒待機から、Manager初期化と実験記録の完了待ちへ変更した。
  開始前にAgent一覧が空でも自動生成を待つ。30実秒以内に準備が整わなければ停止する。
  自動Config選択に失敗した場合も、誤った設定で環境を動かし続けず停止する。
- 自動テストは新形式の学習JSONにある環境内Agent数とテストの実数も照合する。
  並列環境数の違いは許可する。実数記録がない旧JSONは警告し、Configの期待数のみ検証する。
  `test_experiment.json` にもテストの環境数と実効Agent数を記録する。
- `WarehousePheromoneFinalReport` は最終集計レポートを出力し、完全タスクごとのマップを
  CSVと濃淡付きPNGとして出力する。テストランナー経由では各試行のフォルダへ出力する。

## 実験中 (Currently Testing)

- 現在実行中の学習・推論テストはない。
- `wh_middle2Gate_single_v1_01`～`03` の50,000,000ステップ学習と、各モデル5試行の
  推論テストは完了した。3run中、明確にタスクを達成できたのは `02` だけだった。
- この結果は学習安定性の問題を示す予備結果である。trainer seedは未固定だった。また旧runの
  JSONにはConfig値16体が記録されているが、当時の実装は配置済み1体を維持していたため、実効値は
  1環境1体だった可能性が高い。旧JSONには実効数がなく確定できないため、方式の優劣を示す正式結果とは扱わない。
- 完全タスク単位マップは現状の実装済み方式である。サブタスク単位マップの実装・実験は
  まだ行われていない。

## 最新結果 (Latest Results)

### Middle 2Gate・3runの学習結果

`wh_middle2Gate_single_v1_01`～`03` は `taskSeparated_v1` Config、`Middle_2Gate_v1`
Environment Preset、PPO、最大50,000,000ステップで学習した。trainer seedは明示されず、
保存された `configuration.yaml` では `seed: -1` だった。学習用Sceneには `Env ML` が8個ある。
旧runのJSONに保存されたConfig値は1環境あたり16体だが、当時の実装は配置済みAgentを増減せず、
Env ML Prefabは1体構成だった。ユーザーの意図と実行時の認識も1体であるため実効値は1体だった
可能性が高いが、旧JSONには実効Agent数がなく、過去のPlayer実行値を事後に確定はできない。

TensorBoardイベントから読み取った学習終了時の値は以下。報酬とEpisode長はML-Agentsが
出力した集計値であり、棚到達数・完全達成数・タイムアウト数の内訳は現状記録されていない。

| モデル | 最終累積報酬 | 最終Episode長 | 学習中の最大累積報酬 |
| --- | ---: | ---: | ---: |
| `wh_middle2Gate_single_v1_01` | -19.704 | 49,999.0 | 208.168 |
| `wh_middle2Gate_single_v1_02` | 214.487 | 717.7 | 256.204 |
| `wh_middle2Gate_single_v1_03` | -73.616 | 49,999.0 | 45.875 |

`01` と `03` は学習終了時に最大ステップ付近へ張り付いており、未収束と判断する。`02` は
短いEpisode長と正の報酬を示し、3runの中では学習に成功している。`01` は途中で報酬が
208付近まで上がった記録がある一方、最終値は負になっており、最終モデルが学習途中の
良い状態を保持していない可能性がある。

### Middle 2Gate・ONNX推論結果

推論は1環境・実効Agent 1体、決定的CPU推論、固定シミュレーション時間40秒、各モデル5試行、
試行seed 1～5で実施した。各試行前に達成数、衝突数、フェロモンをリセットした。テスト時に
生成されたマップは30 x 30セル、完全タスク用96マップだった。Configの `agentCount` は16だが、
テスト用Prefabに既存Agentが1体いたため、当時の適用処理は1体を保持して警告した。
現在の起動検証ではこの不一致は停止対象である。

| モデル | 達成数（5試行合計） | 1試行あたり達成数 | 壁衝突 | Agent衝突 |
| --- | ---: | ---: | ---: | ---: |
| `wh_middle2Gate_single_v1_01` | 0 | 0, 0, 0, 0, 0 | 0 | 0 |
| `wh_middle2Gate_single_v1_02` | 10 | 2, 2, 2, 2, 2 | 5 | 0 |
| `wh_middle2Gate_single_v1_03` | 0 | 0, 0, 0, 0, 0 | 2 | 0 |

推論結果は学習曲線と整合し、`02` だけが全seedでタスクを達成した。これによりONNX適用と
複数試行テスト機能は動作したと確認できる。一方、学習時は多数Agentが同時にフェロモンを
生成・共有していたかは旧記録から確定できない。テストは1体かつ各試行ゼロ初期化なので、少なくとも
フェロモン蓄積状態は学習後半と一致しない。また当時のコードはConfig適用をプロセス全体で一度に制限しており、全環境への
適用を保証できていなかった。修正後の再学習・比較は未実施である。
3run中2runが達成0だった原因として、条件不一致、フェロモン報酬、マップ細分化、
長いEpisode上限、seed差などを疑っているが、このテストだけでは原因を特定していない。

### 過去のSmall環境結果

以下は過去の `results/*/logTest.csv` から読み取った値。各テストの目標はタスク達成10回。
`completed_episodes` は過去の列名であり、実際には全エージェント合計の成功タスク数を表す。

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

- 直近3runは旧実装でtrainer seedを明示しておらず、`configuration.yaml` では `seed: -1` である。
  新しいseed指定と段階別指標は次回学習から有効で、過去runへ遡って適用はできない。
- `tr.py` は学習終了時の最終ONNXを採用する。PPOは学習途中より最終方策が悪化する場合が
  あるが、現在は固定seedの推論評価でcheckpointを選ぶ仕組みがない。また
  `keep_checkpoints: 5` のため、古い良好なcheckpointは保持されない。
- 現在のフェロモン報酬は `log(訪問前セル値 + 1) * 0.0002`、時間ペナルティは1ステップ
  `-0.001`。セル値が約147を超えるとフェロモン報酬が時間ペナルティを上回る計算になり、
  濃いセルの再訪・滞留を促す可能性がある。直近の異常挙動の原因とはまだ断定していない。
- 学習中はフェロモンマップがEpisodeをまたいで蓄積するが、現在の推論テストは各試行前に
  マップをゼロへ戻す。学習後半とテスト開始時の観測分布が異なる可能性がある。
- `maxStepLimit` は50,000で、失敗した `01` と `03` の最終Episode長は49,999だった。
  失敗Episodeが長時間継続し、タイムアウトの学習信号が疎になる可能性がある。
- 現在の `taskSeparated_v1` の `agentCount` は1で、Env ML Prefabの1体と一致する。旧runのJSONは
  変更前Configの内容ハッシュ（16体）を保持するため、自動選択ではフィンガープリント不一致になる。
  旧モデルの評価ではManual Overrideまたは不一致停止の一時解除が必要。新runでは現在のConfigと
  実効Agent数が記録される。
- 環境生成・タスク抽選・観測ノイズなどがUnityのグローバル乱数を共有する。
  Config適用と試行状態リセットは修正済みだが、環境間の乱数干渉はまだ解消していない。
- Agent同士の衝突は両者で数え得る。達成0件で衝突/タスクが0となる表示も未修正。
- テスト結果の再実行時上書き、実効設定スナップショット、ビルド鮮度検証、Package情報の
  Git管理整理は未対応。結果・設定を修正済みの条件として扱わない。
- `Shared` と `PhaseSeparated` は実際のマップ方式として未実装。選択しても完全タスク用
  マップが使用されるため、現時点で実験条件として使ってはならない。
- マップ数が 入口数 x 棚数 x 出口数 で増える。これが現在検討している蓄積希薄化の問題。
- フェロモン値は生のスカラー値として観測される。PPO側で観測正規化は有効だが、実行間で
  フェロモン値の規模が大きく変わり得る。
- 現在の規則は、成功しなかった経路も含めて移動ステップごとに訪問セルを強化する。
  成功時のみの分泌、品質重み付け、拡散を比較することはまだできない。
- 複数エージェントが同一フレームで達成すると、テストランナーの達成数は目標を超え得る。
  `16smallbase01` は目標10に対して11を記録した。
- `logTest.csv` には試行seedを記録するが、経路長、タスクごとの所要時間、報酬、混雑、
  Scene、実効設定IDはまだ記録していない。Sceneと設定対応は別のJSONに記録される。
- 試行seedをそろえても、方策によって実行中の乱数消費順が変わるため、各試行の初期乱数列を
  そろえる以上の厳密な乱数同期は保証されない。
- 自動テストには、モデル名と一致する `results/<model-name>/unity_experiment.json` が必要。
  既存の古いモデル結果にこのJSONがない場合は、自動方式を使えず `ManualOverride` が必要。
- 古い形式のJSONは現在のフィンガープリント形式を持たないため、アセットIDのみを照合して
  警告する。学習時の値まで完全に検証できるのは、現在以降の学習結果である。
- `unity_experiment.json` は環境起動時に一度だけ作られる。テストバッチ自体の記録は
  `results/<testBatchId>/`、モデルごとの選択記録は `results/<model-name>/test_experiment.json` に
  分かれる。
- `Middle_2Gate_v1` の `presetId` は初期値 `environment_v1` のままで、正式比較前に一意なIDへ
  変更する必要がある。現在の環境seedも0である。
- `WarehouseGenerator.seed` は倉庫生成時の `UnityEngine.Random` を制御するが、ML-Agentsの
  trainer seedとは別である。タスク割当やスタック回避にも `UnityEngine.Random` を使うため、
  trainer seedだけをそろえてもUnity側の乱数列は保証されない。
- 現在のビルド設定には学習Sceneだけが含まれる。テストにはEditorでテスト用Sceneを開くか、
  ビルド設定を変更する必要がある。
- ソースコメントには、配送/帰還で別マップだった時期の説明が一部残っている。実際のコードは
  完全タスク単位の共通マップを使用する。

## 次タスク (Next Tasks)

全環境へのConfig適用・Agent数検証・実数記録は実装済み。直近は以下の順で進める。

1. ConfigとテストSceneの1体条件は起動確認済み。学習Sceneでも各倉庫1体で起動することを確認し、
   修正済みコードを使うPlayerを再ビルドする。並列環境数はテストと一致させる必要はない。
2. trainer seedの明示と段階別StatsRecorder指標は実装済み。Unityの乱数系列を用途・環境ごとに
   分離する作業は、方式間の厳密な比較前まで保留する。
3. 衝突の数え方とゼロ達成時の指標を、衝突を主要評価へ使う前に修正する。
4. テスト出力をバッチ別に保存し、実効設定のスナップショット・ビルド記録・Git管理を整える。
5. 最終モデルを無条件採用せず、保持したcheckpointを学習とは別の固定validation seed集合で
   推論評価し、達成率、所要時間、衝突数などから採用モデルを選べるようにする。モデル選択用
   seedと最終報告用test seedは分ける。
6. 観測・分泌・蒸発などを固定したまま `pheromoneRewardScale = 0` のConfigを作り、現行の
   フェロモン報酬あり条件と比較する。異常挙動が報酬誘導によるものかを切り分ける。
7. 成功モデル `02` のEpisode長や経路時間を基準に `maxStepLimit` の候補を決め、失敗Episodeが
   50,000ステップ継続しない現実的な上限へ短縮する。値は変更前後の比較で決定する。
8. 基盤整備後、同一Config・同一seed集合で学習と複数試行推論をやり直し、3run中の
   成功率とばらつきを確認する。
9. サブタスク単位マップの仕様を、実装前に確定する。候補は配送用の `入口 x 棚` マップと、
   帰還用の `棚 x 出口` マップであり、各フェーズでの観測・分泌対象、出力ファイル名を決める。
10. マップ構造を変更する前に、影響範囲と移行方法を説明する。主な対象は
   `WarehousePheromone`、`WarehouseRobotAgent`、`WarehousePheromoneFinalReport`、
   実験設定・記録コードになる見込み。
11. 完全タスク方式とサブタスク方式の比較では、環境、エージェント数、PPO設定、分泌量、
   蒸発率、Unity/trainerのseed集合を固定する。
12. テストログへtrainer seed/run ID、経路長、タスクごとの所要時間、報酬、混雑を追加する。
   併せて複数エージェント時の目標超過の扱いを決める。

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
- 並列環境数は学習の実行情報として自動記録し、Configの条件項目には追加しない。
  環境内Agent数は研究条件としてConfigで管理する。学習・テストの環境数は異なってよい。
  並列化が学習過程へ全く影響しないという保証ではなく、乱数分離も今後の作業である。
- ONNXテストは学習記録からConfigを自動選択することを標準とする。任意Configを使うテストは
  `ManualOverride` として結果へ明示記録する。
- 完全タスク方式とサブタスク方式の比較へ進む前に、seed、学習中メトリクス、checkpoint選択、
  フェロモン報酬、Episode上限、実効Agent数を整理し、学習・評価基盤を安定させる。
- checkpointの選択には学習と別のvalidation seed集合を使い、最終的な結果報告にはさらに別の
  test seed集合を使う方針とする。学習報酬だけでは採用モデルを決めない。
- ビルド生成物、学習結果、モデルバイナリはGit管理対象外とする。再現時には別途扱う必要が
  ある。

## 最近変更した主要ファイル (Files Changed Recently)

直近の研究ワークフロー関連コミットは `ff48b8c`
（`Add environment preset workflow`）。主な対象ファイルは以下。

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

未コミットの作業として、テストランナーの複数試行・2種類の終了条件・試行別出力、
ビルド学習でGUIDが空の場合のConfig自動解決、results絶対パス対応を追加している。
今回さらに、全環境の起動検証・Config適用、Agent数の厳密検証・記録、テスト開始待機を追加した。
主要変更は `WarehouseExperimentConfig.cs`、`WarehouseTrainingManager.cs`、
`WarehouseModelTestRunner.cs`、`Warehousegenerator.cs`。
続けて試行リセットをManagerへ集約し、`Shelfunit.cs`、`WarehouseRobotAgent.cs`、
`WarehouseModelTestRunner.cs` を更新した。
`Assets/Scripts/Editor/WarehouseStartupChecks.cs` にEdit Modeの起動検証15件を追加した。

今回の検証:
- `dotnet build Assembly-CSharp.csproj --no-restore -p:BuildProjectReferences=false` 成功。
- AnacondaのPythonで `tr.py` の構文確認と `trainer_seeds=1,2,3` の読込確認に成功。
- `DefineConstants=TRACE` によるEditor専用コードを除いたC#コンパイルも成功。
  これは実際のPlayerビルド・実行を代替するものではない。
- 分離した一時Unity 2022.3.62f3プロジェクトでEdit Modeの起動検証15件が成功。
- 同じ一時プロジェクトのPlay Modeで2環境 x 自動生成2体が起動し、Agentの質量・速度・
  Behavior設定、二重登録がないこと、JSONの環境数2・総Agent数4を確認した。さらに、
  各環境で成功時追加荷物、棚状態、Agent指標、次タスク割当の試行リセットを確認した。
- 本番のテストSceneと既存ONNXは、現在の1体ConfigをManual Overrideで選び、旧runの内容ハッシュ
  不一致を明示的に扱うことで起動確認済み。trainer接続、学習Scene確認、Playerの再ビルド・実行、
  完全なテストバッチ完走は未実施。今回の確認は機能確認であり、学習成績の改善を示す実験ではない。

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
- trainer seedの明示と学習中の段階別指標は実装済み。報酬やEpisode上限は変更していない。
- 次の短い学習で、`configuration.yaml` と `unity_experiment.json` のseed、およびTensorBoardの
  4指標が実際に出ることを確認する。Unity乱数の用途別分離は正式な方式比較前まで保留する。
- 今回の修正をビルド学習に使うにはUnity Playerの再ビルドが必要。
- Unityメニュー `Warehouse > Checks > Experiment Startup` でEdit Mode検証15件を再実行できる。
  元のScene・アセット・resultsを書き換えず、一時オブジェクトを破棄する。
- 新しい複数試行テストは `FixedDuration` 40秒 x 5試行で動作確認済み。
  `FixedCompletedTasks` はコード実装済みだが、変更後のUnity実機確認がまだ必要。
