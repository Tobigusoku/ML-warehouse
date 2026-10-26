# 16Agent・Directionalのマップ分割比較

確認日: 2026-10-26。TaskSeparated側は学習中で、方式間の結論はまだない。

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

## 次の検証

1. TaskVector `_01`～`03` の学習完了とConfigハッシュを確認する。
2. STvectorと同じテスト設定でTaskVectorを推論し、搬送・接触・方向分布を比較する。
3. 必要ならフェロモン報酬0かつ149次元の対照条件を作り、方向情報の寄与を分ける。
