# 16Agent・Directionalのマップ分割比較

初回確認日: 2026-10-26。2026-10-27にTaskSeparatedの推論結果を追記。
以下の初回記録で「学習中」とある箇所は2026-10-26時点の状態を示す。

## 目的・条件

- 問い: Directionalの移動方向がTaskSeparatedの配送・帰還共有マップで相殺されるか。
  フェーズ別マップを持つSubtaskSeparatedと推論性能・方向分布を比較する。
- ST: `STseparated_vector_v1_`、run `wh_middle2Gate_STvector_16_v1_01`～`03`。
  Task: `taskSeparated_vector_v1_`、run `wh_middle2Gate_taskVector_16_v1_01`～`03`。
- 両Configの差は名前・IDとマップ分割方式だけ。`Directional + VectorField27`、
  `FrontDynamic19`、`Middle_2Gate_v2`、16Agent、分泌量1、蒸発率0.05/100 FixedUpdate。
  Directionalではフェロモン報酬は0。学習時の観測数は149。
- STは配送24枚・帰還24枚、Taskは両フェーズ共有48枚。同じマップ総数でも蓄積単位は異なる。
- いずれもPPO 5,000万step、6学習環境 x 各16Agent、trainer seed 1～3を予定。
  STのテストは最終ONNX、決定的推論、1環境 x 16Agent、300シミュレーション秒 x
  trial seed 1～5。学習/テストConfigとEnvironment Presetのハッシュが3runとも一致。
- 生データ: `results/wh_middle2Gate_STvector_16_v1_0{1,2,3}/` と
  `results/wh_middle2Gate_taskVector_16_v1_0{1,2,3}/`。`results/` はGit管理外。

## 観察された事実

- STvectorはseed 1～3の学習を完了し、各runで300秒 x 5試行を終了した。
  各値は5試行平均。`Agent接触`は接触ペアの開始イベント数。

| trainer seed | 完了数 | Agent接触 | Agent非接触完了数 | 壁接触 | 棚接触 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 358.0 | 161.8 | 264.4 | 91.8 | 505.4 |
| 2 | 315.0 | 180.2 | 226.2 | 133.4 | 435.4 |
| 3 | 307.6 | 126.4 | 234.6 | 141.8 | 419.0 |
| 15試行平均 | 326.9 | 156.1 | 241.7 | 122.3 | 453.3 |

- 同じ分割のScalar ST v4は15試行平均で完了385.5、Agent接触155.9、
  Agent非接触完了286.5。STvectorの完了数は平均58.7件少ない（約15.2%）。
  Agent接触の絶対数はほぼ同じで、接触/完了はSTvector約0.478、Scalar ST約0.404。
- 2026-10-26の確認時点でTaskVector `_01` の最終ONNXがある。
  `_02` には学習記録とcheckpointがあるが最終ONNXはなく、`_03` の記録はない。
  TaskVectorの推論CSVは未作成。方式間の結果比較はまだできない。

## 解釈・限界

- STvectorとScalar STの比較では、方向内容、観測形式（149対131次元）、
  フェロモン報酬（0対有り）が同時に変わる。完了数の差をベクトル情報の悪影響と断定しない。
- TaskVectorの完了と推論後に、同じtrainer seed・trial seed集合でSTvectorと比べる。
  強度に対する方向ベクトルの残存率も見て、逆方向移動による相殺仮説を検証する。
- 試行終了時のマップだけでは、行動時に読んだ方向情報の有用性は分からない。

## 2026-10-26時点の次の検証

1. TaskVector `_01`～`03` の学習完了とConfigハッシュを確認する。
2. STvectorと同じテスト設定でTaskVectorを推論し、搬送・接触・方向分布を比較する。
3. 必要ならフェロモン報酬0かつ149次元の対照条件を作り、方向情報の寄与を分ける。

## 2026-10-27追記: TaskVector推論完了

- `wh_middle2Gate_taskVector_16_v1_01`～`03` は各5,000万stepの最終ONNXがあり、
  trainer seed 1～3で学習した。学習記録は6環境 x 各16Agent、`Middle_2Gate_v2`。
  STvectorとTaskVectorのConfigアセットの差は名前・ID・マップ分割方式のみで、
  trainer設定YAMLもrun ID以外は一致する。
- 各runを決定的推論、1環境 x 16Agent、300シミュレーション秒 x trial seed 1～5で評価。
  全15試行が時間到達で終了し、学習/テストのConfig・環境Presetハッシュは一致した。
  生データは各runの `logTest.csv`、`test_experiment.json`、
  `pheromone_trials/trial_XX/pheromone_routes/` にある。

| trainer seed | ST完了 | Task完了 | ST Agent接触 | Task Agent接触 | ST非接触完了 | Task非接触完了 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 358.0 | 340.0 | 161.8 | 147.4 | 264.4 | 265.4 |
| 2 | 315.0 | 341.4 | 180.2 | 144.4 | 226.2 | 255.2 |
| 3 | 307.6 | 350.4 | 126.4 | 167.0 | 234.6 | 251.6 |
| 15試行平均 | 326.9 | 343.9 | 156.1 | 152.9 | 241.7 | 257.4 |

- Taskは平均完了数がSTより17.1件多いが、seed 1ではSTのほうが18.0件多い。
  Agent接触は平均でTaskが3.2件少ない一方、seed 3では40.6件多い。
  完了数・接触数とも、3学習seedだけで方式の優劣を断定しない。
- 最終マップの強度CSVと方向X/Z CSVから、強度が0.001を超えるセルについて
  `sum(強度 * sqrt(方向X^2 + 方向Z^2)) / sum(強度)` を全15試行・全48マップで算出した。
  方向CSVは各セルの強度で正規化された平均方向で、小数第3位までの出力を使う。
  値はSTが0.9478、Taskが0.9184。Taskのほうが最終的な方向のまとまりはやや低いが、
  相殺の発生場所・時刻や、エージェントがその情報を読んだかは分からない。
  最終マップの総強度は1試行平均でSTが24,942.4、Taskが25,721.8。

### 解釈と次の検証

- 同じDirectional観測・報酬条件下では、今回の搬送数にTaskSeparatedの明確な劣位は見られない。
  ただしTaskとSTでは走行経路や蓄積位置も変わり得るため、方向のまとまりの差を
  配送・帰還の混合だけに帰属できない。
- 相殺仮説を直接調べるなら、配送と帰還の分泌寄与を計測時だけ別集計し、
  同一セルでの逆方向寄与と再訪間隔を測る。現在の最終CSVからは復元できない。
- Scalarとの比較は観測次元とフェロモン報酬も変わるため、方向情報単独の効果とは区別する。
