# 24Agent・SubtaskSeparated + Scalar 推論（16Agentとの参考比較）

確認日: 2026-11-05。

## 目的・条件

- 同じ倉庫でAgent数を16体から24体へ増やしたときの搬送数と接触数を確認する。
  24体のNoneとの比較は、その推論記録を確認してから行う。
- 24体: `STseparated_v5`、`wh_middle2Gate_ST_24_v1_01`～`03`。
  16体の参照条件: `STseparated_v4`、`wh_middle2Gate_ST_16_v4_01`～`03`。
  両Configアセットの公開フィールドは、名前・ID・`agentCount`（16→24）以外同じ。
- 共通の倉庫Presetは `Middle_2Gate_v2`。観測は `FrontDynamic19` と
  `LegacyScalar9`、フェロモン方式は `SubtaskSeparated + Scalar`。
  24体の学習記録では各Unityプロセスに6環境 x 各24Agent、PPO 5,000万step、
  trainer seed 1～3。`_01` は途中から再開している。
- 各最終ONNXを1環境 x 24Agent、決定的推論、固定シミュレーション300秒 x
  trial seed 1～5で評価。3件の `test_experiment.json` は学習Config・環境Presetの
  内容ハッシュ一致、実効24Agent、`AutoFromTrainingRecord` を記録する。
- 元データ: `results/wh_middle2Gate_ST_24_v1_01`～`03` の
  `unity_experiment.json`、`test_experiment.json`、`logTest.csv`、
  `logTest_summary.csv`。16体の参照結果は
  [3方式比較](pheromone_mode_front_dynamic_v1.md) と対応runのCSV。
  `results/` はGit管理外。

## 観察された事実

全15試行は300秒の `simulation_duration_target` で終了した。
接触は同一Agentペアの継続接触を1件とした開始イベント数。
表の値は各runの5試行平均、最下段は15試行平均。

| trainer seed | 完了数 | Agent接触 | 接触なし完了数 | 壁接触 | 棚接触 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 454.6 | 361.4 | 267.4 | 181.8 | 746.6 |
| 2 | 536.2 | 354.8 | 328.8 | 177.0 | 652.2 |
| 3 | 506.2 | 323.8 | 321.6 | 143.8 | 582.4 |
| **24体・15試行平均** | **499.0** | **346.7** | **305.9** | **167.5** | **660.4** |

24体STの平均は、Agentあたり完了数20.79件/300秒、Agent接触/完了0.695、
Agent非接触完了率61.3%（305.9/499.0）。

同じ方式の16体ST v4（3 trainer seed x 5 trial seed）の平均は、完了385.5件、
Agent接触155.9件、Agentあたり完了24.10件、Agent接触/完了0.404、
Agent非接触完了率74.3%。24体では総完了数が+113.5件（+29.4%）、
Agentあたり完了数が-13.7%、Agent接触/完了が0.404→0.695となった。
3つのtrainer seedはいずれも16体より総完了数とAgent接触数が多い。

## 解釈と限界

- 24体STでは総搬送量は増えたが、Agentあたりの搬送量は減り、
  完了1件あたりのAgent接触は増えた。混雑の寄与は可能性として扱う。
  接触の発生位置、停止時間、ゲートでの滞留はこのCSVから分からない。
- 16体と24体は別々に学習した方策である。学習時の並列Player数も同一ではないため、
  推論差をAgent数だけの因果効果と断定しない。
- 24体Noneの学習済みONNXはseed 1～3の3件あるが、2026-11-05の確認時点で
  対応する `logTest.csv` と `test_experiment.json` は `results/` に見つからなかった。
  24体でのフェロモン有無の優劣は未判定。

## 次の検証

1. 24体Noneを同じ300秒、trial seed 1～5、決定的推論で評価し、
   Config・環境Presetのハッシュ一致と実効24Agentを確認する。
2. NoneとSTの総完了数、Agentあたり完了数、Agent接触/完了、
   非接触完了率をtrainer seed対応で比較する。
3. 接触位置・停止時間・ゲート滞留を計測し、密度増加時の低下要因を検証する。

## 2026-11-05追記

上記の「24体Noneの推論記録は未確認」は初回確認時点の記録。
同日にNoneの3モデル x 5試行の結果を確認した。
有無比較は [24Agent・None/ST比較](agent_count_24_none_vs_st_v1.md) に記録する。
