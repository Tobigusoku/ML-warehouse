# 狭い2ゲート倉庫・成功経路方式の蒸発率比較

確認日: 2026-11-17。

## 目的・条件

成功経路だけを分泌する方式で、蒸発率を0.05から0.01へ下げると、
フェロモンの残存量と搬送成績がどう変わるかを調べる。

- 蒸発率0.05: `STseparated_successRoute_v2`、run ID
  `wh_middle2Gate_STseparated_successRoute_v2_01` ～ `_03`。
  蒸発率0.01: `STseparated_successRoute_v3`、run ID
  `wh_small2Gate_STseparated_successRoute_v3_01` ～ `_03`。
  run IDの `middle` / `small` は命名差で、両条件のPresetは同じ
  `Small_2Gate_12Shelves`。6runのPreset内容ハッシュも一致する。
- 2つのConfigアセットの公開フィールド差は名前、ID、`pheromoneEvaporationRate` だけ。
  両条件とも16Agent、SubtaskSeparated + Scalar、成功時の経路一括分泌、
  フェロモン報酬スケール0.0002、セル2m、周囲3×3セル観測、前方19方向観測。
- PPO 5000万step、trainer seed 1～3、`num_envs=2`、各Playerに6環境×16Agent。
  全6runで最終チェックポイントは5000万step以上。
  `configuration.yaml` の差はrun IDと `base_port`（5005 / 5105）のみ。
- 各最終ONNXを1環境×16Agentで、決定的推論、trial seed 1～5、
  固定300シミュレーション秒で評価。全30試行が300秒まで完走。
  `test_experiment.json` は全runで学習Configとの一致を記録した。
- 元データ: 各runの `configuration.yaml`、`unity_experiment.json`、
  `run_logs/training_status.json`、`test_experiment.json`、`logTest.csv`、
  `logTest_summary.csv`、`pheromone_trials/trial_XX/pheromone_routes/summary.csv`。
  `results/` はGit管理外。

## 観察された事実

各seed値は5試行平均、全体は15試行平均。Agent接触はペアイベント数。

| trainer seed | 0.05 完了 | 0.01 完了 | 完了差 | 0.05 Agent接触 | 0.01 Agent接触 | 0.05 棚接触 | 0.01 棚接触 |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 278.6 | 238.4 | -40.2 | 249.0 | 253.8 | 1077.4 | 931.4 |
| 2 | 193.8 | 202.4 | +8.6 | 195.2 | 277.8 | 973.2 | 1048.4 |
| 3 | 182.6 | 259.2 | +76.6 | 209.2 | 285.4 | 1215.4 | 897.2 |
| **15試行平均** | **218.3** | **233.3** | **+15.0** | **217.8** | **272.3** | **1088.7** | **959.0** |

0.01の完了数は平均で15.0件（約6.9%）多いが、trainer seed 1では40.2件少ない。
同じtrainer/test seedの15組中、0.01の完了数が多いのは9組。
Agent接触は平均54.5件多く、完了1件あたりでも0.998から1.167件へ増えた。
棚接触は平均129.7件少なく、完了1件あたりでは4.99から4.11件へ減った。

各試行終了時の48枚のSubtaskSeparatedマップを集計した。
マップ値の合計は、15試行平均で0.05が1145.9、0.01が5095.6（約4.45倍）。
各試行の最大セル値の平均は2.74対8.26（約3.01倍）。
48枚の活性セル数を合計した試行平均は3740.7対3900.3。
活性セル数はマップ間の重複を含み、Agentが判断時に観測した非ゼロセル数ではない。

参考として、同じPresetでの毎ステップ分泌 `STseparated_v6` は完了278.9件、
Agent接触263.9件、棚接触917.6件の15試行平均だった。
詳細は[分泌方式比較](small_2gate_success_route_16_v1.md)に記録した。

## 解釈・未実施の検証

蒸発率を下げると、試行終了時のフェロモンマップの値は明らかに大きくなった。
一方、完了数の増加はtrainer seedごとに方向が揃わず、0.01が安定して優れるとは言えない。
Agent接触/完了も悪化している。毎ステップ分泌との完了数の差も残る。

終了時マップの値だけでは、Agentが判断時にどの値を観測し、行動に利用したかは分からない。
判断時の観測値分布や停滞位置は未計測。同じPresetのフェロモンなし（None）も未比較で、
この環境でのフェロモン自体の寄与は未確定である。
