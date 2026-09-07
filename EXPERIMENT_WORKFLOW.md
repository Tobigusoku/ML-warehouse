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

## 1. 実験プリセットを一度作る

正式な条件ごとに、環境用の `WarehouseEnvironmentPreset` と、実験条件用の
`WarehouseExperimentConfig` アセットを作る。

1. Projectウィンドウで `Assets` の下に `ExperimentConfigs/Environments` フォルダを作る。
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
- `tr.py` はrun IDを環境変数 `MLAGENTS_RUN_ID` としてUnityアプリへ渡す。そのため、
  `unity_experiment.json` は対応する `results/<run-id>/` に保存される。

### 4-2. `tr.txt` を設定する

例えば `wh_task_v1_s01` をseed 1で一度だけ実行するなら、`tr.txt` を次の考え方で設定する。

```text
runs=1
prefix=wh_task_v1_s
digits=2
start=1

config=warehouse_training.yaml
env=App/ML-warehouse.exe
results=results
models=Assets/Models
behavior=WarehouseRobot

no_graphics=true
force=false
mlagents=mlagents-learn
extra_args=--seed 1
```

これで生成されるrun IDは `wh_task_v1_s01` になる。

同じ条件のseed 2を実行する時は、`start=2` と `extra_args=--seed 2` に変更する。
現在の `tr.py` はseedを自動で連番化しないため、seed集合は手で明示的に管理する。

### 4-3. 実行する

conda環境を有効化したPowerShellで実行する。

```powershell
python tr.py
```

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
さらに、学習時に記録されたConfigとEnvironment Presetの内容ハッシュを照合する。

- Config GUID/ID/内容ハッシュと、Environment Preset GUID/ID/内容ハッシュが一致すれば
  テストを続ける。
- Configが異なる、または学習後に同じAssetを編集してハッシュが異なる場合は、標準設定では
  テストを停止する。
- 学習結果フォルダまたは `unity_experiment.json` がない古いモデルは、自動方式ではテスト
  できない。その場合は手動上書きを使う。
- 古いJSONは現在のフィンガープリント形式を持たないため、アセットIDだけを照合して警告を出す。
  学習時の設定値まで完全に検証したい比較実験では、新しい形式で学習し直す。
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
- `Completed Episodes Per Model`: 目標とする**合計タスク達成数**。
  現在の名前はEpisodeだが、複数エージェント全体の達成数として数える。
- `Timeout Seconds Per Model`: 1モデルあたりの上限時間。
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
  test_experiment.json
  pheromone_final.csv
  pheromone_final.txt
  pheromone_routes/
    summary.csv
    route_e1_s1_x1.png
    route_e1_s1_x1.csv
    ...タスクごとのPNGとCSV...
```

- `logTest.csv` には達成タスク数、壁衝突数、エージェント衝突数、総衝突数、経過時間、
  停止理由を保存する。
- `test_experiment.json` には、テストが自動選択か手動上書きか、学習時のConfig、
  実際に適用したConfig、設定内容の一致判定を保存する。
- `pheromone_routes` のPNGは、完全タスク（入口 x 棚 x 出口）ごとの最終フェロモン分布。
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
- テスト時の達成数、タイムアウト、決定的推論のオン/オフ。

方式だけを変える比較では、Configを複製してフェロモン方式に関わる項目だけを変える。

## 7. よくある確認ポイント

| 症状 | 最初に見る場所 |
| --- | --- |
| 自動Config選択に失敗する | モデル名とrun IDが同じか、`results/<model-name>/unity_experiment.json` があるか |
| 設定ハッシュ不一致で止まる | 学習後に同じConfig Assetを編集していないか。新しいv2 Assetを作る |
| 起動直後にエラー | `Use Experiment Config` がオンなのにAsset未指定ではないか |
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
