# 学習・推論テストの手順

このファイルは、このプロジェクトで日常確認、正式な学習、学習済みONNXの推論テストを
行うための手順書である。研究上の現在の実装状態は `RESEARCH_STATUS.md` を参照する。

## まず結論

作業は次の3種類に分ける。

| 用途 | 設定方法 | 結果の扱い |
| --- | --- | --- |
| 日常の動作確認 | Inspectorを直接編集 | 正式結果としては残さない |
| 正式な学習 | `WarehouseExperimentConfig` を選択 | run IDとプリセットIDを記録する |
| ONNX推論テスト | 学習記録からConfigを自動選択。必要時だけ手動上書き | `logTest.csv` とフェロモン図を出力する |

正式な比較実験では、テストランナーが学習記録から**同じConfigアセットを自動選択**する。
学習用とテスト用に同じ値を手で二重入力しない。任意条件を試す時だけ、Inspectorで
`Manual Override`へ切り替える。

## 0. 最初に確認すること

1. Unityでプロジェクトを開く。
2. `Assets/Scenes/ml Training.unity` を学習用Sceneとして開く。
3. `Assets/Scenes/ml test.unity` をONNX推論テスト用Sceneとして開く。
4. Consoleにコンパイルエラーがないことを確認する。
5. Python/conda環境で `mlagents-learn` が実行できることを確認する。

```powershell
mlagents-learn --help
```

`python` がWindowsのStore用エイリアスを指していても、conda環境を有効化した後に
`mlagents-learn` が使えれば問題ない。

### 起動時のConfig・Agent数検証

- 学習Sceneに複数の `Env ML` を配置する場合、全Generatorで同じConfigアセットと
  `Use Experiment Config` の値を選ぶ。Inspectorのrun IDを使う場合も全環境でそろえる。
- 各 `Env ML` の下にGenerator、Manager、Agentを置き、PheromoneはManagerと同じObjectに付ける。
  Managerの `Warehouse Generator` と `Env Root` は同じ環境内を参照させる。
- 配置済みAgentはManagerの `Robot Agents` に全て1回ずつ登録する。
  Configの `Agent Count` と実数が違う、参照切れ、無効なAgent、別環境のAgent、登録漏れは起動エラーになる。
  EditorではPlayが停止し、Playerでは終了コード1になる。
- 現在の `taskSeparated_v1` とEnv ML Prefabは、どちらも1環境あたり1体で一致している。
  旧runのJSONには変更前Config（16体）の内容ハッシュが残るため、旧ONNXを評価すると自動選択で
  フィンガープリント不一致になる。旧モデルだけManual Overrideを使うか、不一致停止を一時的に解除する。
  新しい正式runでは不一致停止を有効に戻し、現在のConfigから学習とテストを行う。
  学習済みConfigを上書きしてエラーを回避しない。
- 配置済みAgentがなく、`Robot Agents` が空の場合は、ConfigのAgent数を自動生成する。
  生成されるBehaviorは `WarehouseRobot`、観測65、連続行動2、DecisionPeriod 1。
  手動設定モードでは既存の `Auto Spawn Count` を使う。
- 全環境の初期化・数の検証が完了してからConsoleとJSONへ実数を記録する。
  並列環境数は自動記録されるため入力不要で、学習8環境・テスト1環境でもよい。
  自動テストでは各倉庫内のAgent数を学習記録と照合する。旧JSONに実数がない場合は警告する。
- 今回のコードをビルド学習へ反映するにはPlayerを再ビルドする。古いAppには反映されない。

`unity_experiment.json` の追加項目:

```text
environmentInstanceCount: このUnityプロセスの有効環境数
totalAgentInstances: このUnityプロセスのAgent総数
environmentInstances[]:
  environmentPath: Scene名と階層パス
  configuredAgentCount: Configの期待数（手動モードでは0）
  effectiveAgentCount: 初期化後の実数
```

`num_envs` による複数Player全体の合計ではない。テストの `test_experiment.json` にも
`environmentInstanceCount` と `effectiveAgentCount` を保存する。

設定検証と試行リセットの回帰チェックはPlayしていない状態で、Unityメニューの
`Warehouse > Checks > Experiment Startup` から実行できる。Consoleに `PASS: 15 checks` が出れば成功。

## 1. 実験プリセットを一度作る

正式な条件ごとに、環境用の `WarehouseEnvironmentPreset` と、実験条件用の
`WarehouseExperimentConfig` アセットを作る。

1. Projectウィンドウで `Assets` の下に `ExperimentConfigs/Experiments` フォルダを作る。
2. `Create > Warehouse > Environment Preset` を選び、現行の倉庫条件を
   `Small_2Gate_v1` のような名前で作る。
3. Environment Preset内の `Preset Id` を一意なIDへ設定する。ここには倉庫形状、棚、ゲート、
   障害物、柱、Unity側の環境seedを入れる。
4. `ExperimentConfigs` 側で `Create > Warehouse > Experiment Config` を選び、例として
   `TaskSeparated_v1` を作る。
5. Configの `Config Id` を一意なIDへ設定し、`Environment Preset` に手順2のAssetを割り当てる。

例:

```text
Asset name: TaskSeparated_v1
Config Id: task_v1
Environment Preset: Small_2Gate_v1
Pheromone Mode: TaskSeparated
```

Experiment Configには、フェロモンの分泌量・蒸発率・セルサイズ・エージェント数・
移動パラメータ・報酬を入れる。環境形状はEnvironment Preset側だけで管理する。

注意:

- 現時点で実際に動くフェロモン方式は `None` と `TaskSeparated` だけ。
- `Shared` と `PhaseSeparated` は選べるが、マップ構造を変えない未実装の設定値である。
  正式実験の条件として使わない。
- 値を変える時は既存Assetを上書きせず、`TaskSeparated_v2` のように新しいAssetを作る。

## 2. 日常の動作確認をする場合

一時的に挙動を見たいだけなら、従来どおりInspector値を使う。

1. 対象Sceneで `Env ML` の中にある `WarehouseGenerator` を選ぶ。
2. `Use Experiment Config` をオフにする。
3. 倉庫形状をプリセットで確認するなら、Generator上部の `Environment Preset` にAssetを
   割り当てる。選択時に既存のGenerator値へ反映され、生成時・Play時にも再適用される。
   完全な手入力に戻す時は、この欄を空にする。
4. `WarehouseGenerator`、`WarehousePheromone`、`WarehouseTrainingManager`、
   `WarehouseRobotAgent` のInspector値を必要な範囲で変更する。
5. UnityのPlayを押す。

Consoleには `Source: Inspector` と表示される。これは動作確認用であり、正式比較の設定として
残す運用には向かない。

## 3. 正式な学習をUnity Editorで行う場合

### 3-1. Unity側の準備

1. `ml Training.unity` を開く。
2. `Env ML` 内の `WarehouseGenerator` を選ぶ。
3. `Use Experiment Config` をオンにする。
4. `Experiment Config` に、今回使うConfigアセットを割り当てる。
5. `Experiment Run Id` に、今回の学習run IDを正確に入力する。

例:

```text
Experiment Config: TaskSeparated_v1
Experiment Run Id: wh_task_v1_s01
```

Editorから学習する場合、`mlagents-learn --run-id` の値はUnity Editorへ自動では渡らない。
そのため、`Experiment Run Id` を同じ文字列で手入力する必要がある。

6. Play前に、Agent prefabの `Behavior Parameters` のBehavior Nameが
   `WarehouseRobot` であることを確認する。

### 3-2. Python/conda側で学習を開始

conda環境を有効化したPowerShellで、プロジェクト直下へ移動して実行する。

```powershell
mlagents-learn warehouse_training.yaml --run-id wh_task_v1_s01 --results-dir results --seed 1
```

`Waiting for Unity environment` と表示されたら、Unity EditorでPlayを押す。

開始直後のUnity Consoleで、次を確認する。

```text
Experiment Config
ID: task_v1
Source: Preset
Asset: TaskSeparated_v1
Pheromone Mode: TaskSeparated
```

この表示が期待と違う場合は、学習を進めず設定を見直す。

### 3-3. 学習後に確認するファイル

```text
results/wh_task_v1_s01/
  configuration.yaml
  unity_experiment.json
  ...ML-Agentsが出力する学習結果...
```

- `configuration.yaml`: PPOなどPython/ML-Agents側の設定記録。
- `unity_experiment.json`: Unity側で使ったプリセットID・アセット名・Scene・保存日時の記録。

`unity_experiment.json` のrun IDが空、またはConfig IDが違う場合は、その学習結果を
正式な比較対象に入れない。

## 4. `tr.py` で連続学習する場合

`tr.py` は、ビルド済みの `App/ML-warehouse.exe` を起動して連続学習するための補助である。
Unity Editorを接続する手順とは別である。

### 4-1. 実行前の注意

- 実験Configを割り当てた状態で、Unityから `App/ML-warehouse.exe` をビルドしておく。
- `tr.py` はビルド済みアプリを使うため、Editor上だけで変更したConfigを使う場合は再ビルドが
  必要。
- `tr.py` はrun IDを `MLAGENTS_RUN_ID`、プロジェクト直下のresults絶対パスを
  `WAREHOUSE_RESULTS_DIR` としてUnityアプリへ渡す。そのため、`unity_experiment.json` と
  ML-Agentsの出力は同じ `results/<run-id>/` に保存される。
- 修正前の古いビルドが `App/results/<run-id>/unity_experiment.json` へ保存した場合も、
  現在の `tr.py` は学習終了後に正規の結果フォルダへコピーする。

### 4-2. `tr.txt` を設定する

例えば `wh_task_v1_s01`～`s03` をseed 1～3で実行するなら、未使用のprefixを選び、
`tr.txt` を次の考え方で設定する。

```text
runs=3
prefix=wh_task_v1_s
digits=2
start=1
trainer_seeds=1,2,3

config=warehouse_training.yaml
env=App/ML-warehouse.exe
results=results
models=Assets/Models
behavior=WarehouseRobot

no_graphics=true
force=false
mlagents=mlagents-learn
extra_args=
```

これでrun ID `wh_task_v1_s01`、`s02`、`s03` にtrainer seed 1、2、3が順番に対応する。
`trainer_seeds` の個数が `runs` と違う、負数や重複がある場合は学習開始前に停止する。
各seedは `mlagents-learn --seed` へ渡され、ML-Agentsの `configuration.yaml` と
Unityの `unity_experiment.json` に記録される。`extra_args` へ `--seed` を重ねて指定しない。

`start=auto` では既存のrun番号の続きが使われるため、run ID末尾とseed番号が一致するとは
限らない。正式比較で `s01 = seed 1` のように揃える場合は、条件ごとに新しいprefixを使い、
`start` と `trainer_seeds` を対応させる。

### 4-3. 実行する

conda環境を有効化したPowerShellで実行する。

```powershell
python tr.py
```

### 4-4. 学習中の段階別指標を確認する

新しい学習では、次のイベント数がML-Agentsのsummary区間ごとの合計としてTensorBoardへ出る。

```text
Warehouse/Task/ShelfReached
Warehouse/Task/Completed
Warehouse/Episode/Timeout
Warehouse/Episode/Fall
```

棚到達だけ増えて完全達成が増えない場合は棚から出口の区間、両方増えずTimeoutだけ増える場合は
棚へ向かう区間を優先して調べられる。これはイベント数であり、成功率そのものではない。

学習終了後、選ばれた最終モデルは次の名前にそろえられる。

```text
results/wh_task_v1_s01/wh_task_v1_s01.onnx
Assets/Models/wh_task_v1_s01.onnx
```

拡張子はML-Agentsの出力により `.onnx` または `.nn` になる。

## 5. ONNX推論テストを行う

### 5-1. 自動方式の前提

自動方式では、モデル名を学習run IDとして扱う。たとえば
`wh_task_v1_s01.onnx` をテストする時、ランナーは次を読む。

```text
results/wh_task_v1_s01/unity_experiment.json
```

そのJSONの `presetAssetGuid` を使って、学習時のConfigアセットを自動で選択する。
ビルド版で学習してGUIDが記録されていない場合、Editorテストではプロジェクト内のConfigを
検索し、Config ID、アセット名、内容ハッシュから自動選択する。その後、学習時に記録された
ConfigとEnvironment Presetの内容ハッシュを照合する。

- Config GUID/ID/内容ハッシュと、Environment Preset GUID/ID/内容ハッシュが一致すれば
  テストを続ける。
- Configが異なる、または学習後に同じAssetを編集してハッシュが異なる場合は、標準設定では
  テストを停止する。
- 学習結果フォルダまたは `unity_experiment.json` がない古いモデルは、自動方式ではテスト
  できない。その場合は手動上書きを使う。
- 古いJSONは現在のフィンガープリント形式を持たないため、アセットIDだけを照合して警告を出す。
  学習時の設定値まで完全に検証したい比較実験では、新しい形式で学習し直す。
- 同じConfig IDのアセットが複数あり、内容ハッシュでも一意に決められない場合は、誤った
  Configを適用せずテストを停止する。正式Configには一意なConfig IDを付ける。
- 1回のテストバッチには、同じConfigで学習したモデルだけを入れる。異なるConfigのモデルが
  混ざると、最初の不一致モデルでバッチを停止する。

### 5-2. テストランナーを設定する

1. `ml test.unity` を開く。
2. `Env ML` に付いている `WarehouseModelTestRunner` を選ぶ。
3. **Playを押す前に**、次を設定する。

- `Models`: テストするONNXを順番に追加する。モデル名は学習run IDと一致している必要がある。
- `Config Source`: 通常は `Auto From Training Record`。
- `Test Batch Id`: テスト全体を表すID。例: `test_task_v1_s01`。
- `Fail On Config Fingerprint Mismatch`: 正式比較ではオン。
- `Trials Per Model`: 各モデルを同じ条件で繰り返す回数。
- `Stop Condition`: 1試行の終了条件。
  `Fixed Completed Tasks` は全エージェントの合計タスク達成数、`Fixed Duration` は
  シミュレーション内の経過秒数で終了する。
- `Completed Tasks Per Trial`: `Fixed Completed Tasks` での1試行の目標達成数。
- `Duration Seconds Per Trial`: `Fixed Duration` での1試行のシミュレーション時間。
- `Safety Timeout Seconds Per Trial`: 停滞時に試行を打ち切る実時間の上限。0で無効。
- `Reseed Each Trial`: オンにすると各試行前にUnity乱数を再設定する。正式比較ではオンを推奨。
- `Base Trial Seed`: 試行1のUnity seed。試行Nには `Base Trial Seed + N - 1` を使うため、
  モデル間で同じ試行seed集合を共有できる。
- `Is Test Mode`: オン。
- `Run On Play`: オンにすると、Play開始時に自動でテストする。
- `Deterministic Inference`: 比較実験ではオンを推奨。
- `Stop Play Mode When Finished`: オンにすると全モデル完了時にPlay Modeを自動停止する。

自動方式では、`WarehouseGenerator` の `Experiment Config` をテストのために手で設定しない。
テストランナーがGeneratorの初期化前に設定する。

テストランナーを使う場合、Behavior Parametersへ各ONNXを手で設定する必要はない。
ランナーがModels配列のモデルを各Agentへ順に設定する。

### 5-3. 任意Configで手動上書きする場合

学習時とは違う条件で挙動を見たい時だけ、`WarehouseModelTestRunner` の設定を次のようにする。

```text
Config Source: Manual Override
Manual Experiment Config: 任意のWarehouseExperimentConfig
```

この場合も、テスト結果の `test_experiment.json` に `ManualOverride` と、学習時Configとの
一致/不一致が保存される。正式な再現テストと混同しない。

### 5-4. テストを実行・確認する

1. Playを押す。
2. Consoleで `Config source: AutoFromTrainingRecord` と選択されたConfig名を確認する。
3. 続いて `Experiment Config` の表示で、`Source: Preset`、Config ID、Asset名が期待どおり
   であることを確認する。
4. 全モデルのテスト終了後、Play Modeが自動停止することを確認する。

モデルごとの出力先:

```text
results/<model-name>/
  logTest.csv
  logTest_summary.csv
  test_experiment.json
  pheromone_trials/
    trial_01/
      pheromone_final.csv
      pheromone_final.txt
      pheromone_routes/
        summary.csv
        route_e1_s1_x1.png
        route_e1_s1_x1.csv
        ...タスクごとのPNGとCSV...
    trial_02/
      ...
```

- `logTest.csv` には1試行を1行として、試行番号、試行seed、達成タスク数、衝突数、
  シミュレーション時間、実時間、単位時間あたりの達成数、1タスクあたりの衝突数、停止理由を保存する。
- `logTest_summary.csv` には、その時点までに完了した試行の主要指標の平均と母標準偏差を保存する。
- CSVは各試行の終了ごとに更新するため、途中停止しても完了済み試行は残る。
- 各試行の開始時に、成功時に棚へ追加された荷物、棚の重量・ハイライト、タスク割当、
  フェロモン、Agentの位置・物理状態・達成数・衝突数・移動距離をリセットする。
  倉庫生成時から棚に置かれていた荷物は残す。再配置後から計測するため、テレポートは移動距離に含めない。
- `test_experiment.json` には、テストが自動選択か手動上書きか、学習時のConfig、
  実際に適用したConfig、設定内容の一致判定に加えて、終了条件、試行回数、試行seed設定を保存する。
- 各 `pheromone_trials/trial_XX/pheromone_routes` のPNGは、その試行終了時点の
  完全タスク（入口 x 棚 x 出口）ごとの最終フェロモン分布。
- 同じモデル名を再テストすると、同じ場所の `logTest.csv` とフェロモン出力を上書きする。
  現状では、再テスト前に結果を別名で退避するか、モデル/run IDを新しくする必要がある。

`results/<Test Batch Id>/unity_experiment.json` はテスト環境の起動時に一度だけ書かれる。
モデルごとの自動/手動選択の記録は、それぞれの `test_experiment.json` を参照する。

## 6. 比較実験で固定するもの

完全タスク方式と、将来実装するサブタスク方式を比較する時は、少なくとも次をそろえる。

- 倉庫形状、棚数、入口/出口数、障害物生成の有無。
- エージェント数。
- 移動パラメータと報酬。
- PPO YAMLと学習ステップ数。
- フェロモンの分泌量、蒸発率、セルサイズ。
- Unity側の `environmentSeed`。
- ML-Agentsの `--seed`。
- テスト時の終了条件、試行回数、達成数またはシミュレーション時間、試行seed集合、
  安全タイムアウト、決定的推論のオン/オフ。

方式だけを変える比較では、Configを複製してフェロモン方式に関わる項目だけを変える。

## 7. よくある確認ポイント

| 症状 | 最初に見る場所 |
| --- | --- |
| 自動Config選択に失敗する | モデル名とrun IDが同じか、`results/<model-name>/unity_experiment.json` があるか |
| 設定ハッシュ不一致で止まる | 学習後に同じConfig Assetを編集していないか。新しいv2 Assetを作る |
| 起動直後にエラー | `Use Experiment Config` がオンなのにAsset未指定ではないか |
| Agent Count不一致で停止する | Configの数と、ManagerのRobot Agents・実際に有効なAgentの数をそろえる |
| 並列環境のConfig不一致で停止する | 全Env MLで同じConfigアセット・使用フラグ・run IDにする |
| 学習結果にJSONがない | Editor学習なら `Experiment Run Id` がrun IDと同じか |
| テストが始まらない | `Is Test Mode`、`Run On Play`、Models配列、Manager/Agent参照 |
| テスト後もPlayが止まらない | `Stop Play Mode When Finished`、Consoleのエラー、タイムアウト値 |
| `Shared`/`PhaseSeparated`の比較にならない | 現在は未実装。`TaskSeparated`のまま動く警告が出る |
| 同じ結果が消えた | 同名モデルの出力先が上書きされていないか |

## 8. 現時点での命名例

```text
Config asset: TaskSeparated_v1
Config ID:    task_v1

Training run IDs:
wh_task_v1_s01
wh_task_v1_s02
wh_task_v1_s03

Test batch ID:
test_task_v1_s01
```

Config IDは「環境条件」、run ID末尾の `s01` などは「乱数seed」を表すようにすると、
フォルダ名だけでも対応関係を追いやすい。
