# ML-Warehouse

Unity ML-Agents で訓練されたマルチエージェント倉庫ロボットのシミュレーションを紹介するプロジェクトです。

## 🚀 概要

この環境では、自律ロボットが倉庫の入口で荷物を受け取り、指定された棚まで運搬し、入口に戻るタスクを学習します。シングルエージェントおよびマルチエージェントの構成が可能です。

主な特徴：

- 手続き的に生成される倉庫 (`WarehouseGenerator`)。
- 物理ベースの移動、レイキャストセンサー、報酬シェーピングを備えたロボットエージェント (`WarehouseRobotAgent`)。
- スポーン、ターゲット割り当て、マルチエージェント調整を担当する訓練マネージャ (`WarehouseTrainingManager`)。
- 好奇心駆動トレーニング設定のオプション。

## 🧩 プロジェクト構成

```
Assets/
  Scripts/          # ジェネレータ、エージェント、訓練マネージャ、デバッグ HUD などの C# コンポーネント
warehouse_training.yaml            # PPO 訓練設定
warehouse_training_curiosity.yaml  # 好奇心モジュール付き PPO 設定
```

## 🛠️ 前提条件

- Unity（2021.3 LTS 以降でテスト済み）。
- プロジェクトにインストールされた [Unity ML-Agents](https://github.com/Unity-Technologies/ml-agents) パッケージ（`Window > Package Manager` から追加）。
- 訓練用 Python 3.8+ 環境（`mlagents-learn` CLI）。

## 📁 はじめ方

1. **プロジェクトを開く**
   - Unity を起動し、`ML-warehouse` プロジェクトフォルダーを開きます。
   - 例として用意されたシーン（例: `Assets/Scenes/WarehouseScene.unity`）を読み込みます。

2. **シーンを設定する**
   - シーンに `WarehouseGenerator` プレハブを配置し、幅・奥行き・扉などを調整します。
   - `WarehouseRobotAgent` プレハブを1つ以上配置するか、`WarehouseTrainingManager` に自動生成させます。
   - `Behavior Parameters` コンポーネントで Behavior Name を `WarehouseRobot` に設定します。

3. **訓練を実行する**
   - ターミナル（PowerShell/コマンドプロンプト）を開き、プロジェクトルートに移動します:
     ```powershell
     cd "c:\Users\ban60\Documents\research\Unity\ML-warehouse"
     ```
   - Python 3.8+ の仮想環境を作成・有効化し、ML-Agents をインストールします:
     ```powershell
     python -m venv .venv
     .\.venv\Scripts\activate
     pip install mlagents
     ```
   - 必要であれば TensorBoard もインストールします:
     ```powershell
     pip install tensorboard
     ```
   - 設定ファイル（通常版または好奇心版）を指定してトレーナーを起動します。**エディタを使用する場合**は次のように実行します:
     ```powershell
     mlagents-learn warehouse_training.yaml --run-id=warehouse_run_01
     # または好奇心版:
     mlagents-learn warehouse_training_curiosity.yaml --run-id=warehouse_curiosity_01
     ```
     Unity エディタの Play ボタンを押すと自動的に接続されます。

   - **ビルド済みアプリを使う場合**は、`App/` 内の実行ファイルを `--env` で指定します:
     ```powershell
     mlagents-learn warehouse_training.yaml --run-id=warehouse_run_01 --env="App\ML-warehouse.exe"
     ```
     （エディタからの起動では `--env` は不要です。）
   - ターミナルに **"Waiting for Unity environment"** と表示されたら、Unity Editor に切り替えて **Play** を押します。
   - 訓練中は TensorBoard でメトリクスを可視化できます:
     ```powershell
     tensorboard --logdir results
     ```
   - 訓練の停止はターミナルで Ctrl+C。`.nn` モデルは `results/<run-id>/` に保存されます。

4. **推論／再生**
   - 訓練後、`Behavior Parameters` にエクスポートされた `.nn` モデル（例: `Assets/Models/myModel.nn`）を指定します。
   - `train` フラグをオフにするか、ビルドを実行して、訓練済みエージェントの動作を確認します。


### 🏭 スタンドアロンアプリとして実行（訓練または推論）

*Unity の CLI ヘルプに示されている通り、`mlagents-learn` は多数のオプションがあります。主に `--env`、`--run-id`、`--results-dir` 等を使用します。
* ファイル名は Windows では `ML-warehouse.exe` になります。

エディタ外で実行したい場合は、ビルドしてヘッドレスプレーヤーを起動します。

1. **File > Build Settings** で使用するシーンを追加します。
2. 対象プラットフォーム（Windows/Mac/Linux）を選び、ログが必要なら **Development Build** を有効に。
3. ヘッドレス訓練には **Run In Background** を有効にし、コマンドラインで `-batchmode -nographics` を指定します。
4. プロジェクトをビルドすると、実行ファイルが `App/` フォルダー直下に配置されます。
5. ターミナルから起動します。例:
   ```powershell
   .\App\ML-warehouse.exe -batchmode -nographics -logFile train.log
   ```
6. Python トレーナーを通常通り起動すると、自動的に接続されます。
7. 推論の場合は `--train` を省略し、起動時にモデルをロードするか設定で指定します。

> ⚠️ ビルド時も Behavior Name を `WarehouseRobot` に設定するか、コマンドラインで上書きしてください。

この方法はクラウド実行や CI、長時間実験に便利です。

## 🧪 訓練設定

- `warehouse_training.yaml`: PPO ベースラインのハイパーパラメータ。
- `warehouse_training_curiosity.yaml`: 探索のために好奇心報酬を追加。

`batch_size`、`learning_rate`、`max_steps`、報酬の重みなどを調整して実験してください。

## 📊 結果

訓練結果は `results/` ディレクトリのランごとのサブフォルダー（例: `warehouse_robot_01`）に保存されます。TensorBoard で可視化します。

```bash
tensorboard --logdir results
```

## 📝 注意事項・ヒント

- `avoidDuplicateShelves` を有効にすると、各エージェントに重複しない棚が割り当てられます。
- デバッグ HUD (`WarehouseDebugHUD`) を使用すると、観察値・アクション・ステートをトレーニング中に確認できます。
- 棚ユニットを手動で配置したり、報酬パラメータを実行時に変更して迅速なプロトタイピングが可能です。

## 🧠 環境の拡張

- `WarehouseObservations` を編集して、新しいセンサーや観察を追加します。
- 荷物のピックアップ／ドロップやドアの操作など、エージェントアクションをカスタマイズします。
- ソーティングや充電ステーション、敵対的エージェントなど、新タスクを実装します。

## 📄 ライセンス

このプロジェクトは研究および教育目的のために提供されています。適宜ライセンスを調整してください。

---

_フォークして独自の ML‑Agents 実験に利用してください！_