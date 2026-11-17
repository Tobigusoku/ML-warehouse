# 16Agent・成功経路のみ分泌する方式の比較

## 2026-11-06: 実装・実験計画

### 目的と仮説

完全搬送に成功した経路だけを後から共有すると、現行の毎ステップ分泌より
有用な履歴になり、滞留によるフェロモン蓄積も抑えられる可能性がある。
これは未検証の仮説であり、有効性を示す結果はまだない。

### 実装された条件

- `Pheromone Deposit Timing = OnTaskCompleted` では、1エピソード中に訪問したセルを
  配送と帰還で別々に記録し、棚を経て出口に到達した時だけ両方のマップへ分泌する。
  落下・タイムアウト等の未完了エピソードでは分泌しない。
- 同じフェーズで同じセルを何度訪れても、そのエピソードによる分泌は1回。
  Scalarのフェロモン報酬も、そのフェーズ・セルの初回訪問時だけ、訪問前の共有マップ値から計算する。
  他Agentの成功経路から報酬を得ることはあるが、同じセルにとどまって繰り返し得ることはない。
- 完了するまでは配送中の経路も共有されない。初回の完全搬送が起きるまではマップが空で、
  フェロモン由来の観測・報酬もない。完了さえすれば遠回りや衝突の多い経路も残すため、
  「良い経路」の品質判定にはなっていない。
- `EveryStep` は従来の分泌・報酬動作を維持する。観測形式、マップ分割、蒸発、
  目的棚・出口の抽選、基本報酬は変更しない。Directionalでは従来どおりフェロモン報酬0。
  Directionalを成功経路方式で使う場合、各セルで最初に記録された有効な移動方向を1回だけ分泌する。
- `STseparated_successRoute_v1` は `STseparated_v4` を基に分泌時期だけを切り替えた16Agent用Config。
  `STseparated_successRoute_noRw_v1` は `STseparated_noRw_v1` を基に同じ切り替えをしたConfig。
  いずれもEnvironment Presetは `Middle_2Gate_v2`、観測は `FrontDynamic19 + LegacyScalar9`。

### 比較予定

まずフェロモン報酬0の `STseparated_noRw_v1` と `STseparated_successRoute_noRw_v1` を
同じPPO設定・trainer seed・trial seed・評価時間で比較すると、分泌時期と
セル当たり分泌回数の変更による影響を見られる。分泌の遅延と回数の効果は分離できない。

報酬ありの `STseparated_v4` と `STseparated_successRoute_v1` の比較では、
分泌時期・セル当たり分泌回数に加え、フェロモン報酬の付与頻度も変わる。
差が出ても、分泌時期だけの効果とは解釈しない。

成功経路方式の推論は未実施。基準となる `STseparated_noRw_v1` の3モデルは
推論まで完了し、結果は [フェロモン報酬有無の比較](pheromone_reward_ablation_16_v1.md) に記録した。
新しいrun ID、各Config/Presetの内容ハッシュ、trainer seed、
`num_envs`、試行seed、300秒試行の完了数・接触数等は実施後に追記する。
既存Configと学習済みrunは上書きしない。

## 2026-11-09: 成功経路・報酬ありのseed 1学習

- run ID: `wh_middle2Gate_STseparated_successRoute_v1_01`
- Config: `STseparated_successRoute_v1`、Environment Preset: `Middle_2Gate_v2`
- trainer seed 1、`num_envs=2`、1 Player内6環境・合計96Agent、目標5000万step
- `run_logs/training_status.json` の最終checkpointは50,000,660 step。
  `unity_experiment.json` にConfig ID・run ID・seedが一致して記録され、ONNXも保存された。
- `tr.py` の初回モデルコピーはUnity記録のUTF-8 BOMを読めずに失敗した。
  修正後に既存ONNXをrun ID名で `Assets/Models/` へ回収した。学習のやり直しは不要。
- 推論結果はまだないため、性能の評価は未実施。

## 2026-11-12: 成功経路・報酬ありの3モデル推論

### 条件と記録

- 対象は `wh_middle2Gate_STseparated_successRoute_v1_01` ～ `_03`（trainer seed 1～3）。
  `configuration.yaml` で各runの `num_envs=2`、`unity_experiment.json` で学習時の
  Configと環境Presetを確認した。
- 各最終ONNXを、1環境・16Agent、決定的推論、trial seed 1～5、固定300シミュレーション秒で評価。
  3件の `test_experiment.json` は学習記録からConfigを自動選択し、ConfigとPresetの
  内容ハッシュが一致したことを示す。
- 元データは各runの `logTest.csv`、`logTest_summary.csv`、`test_experiment.json`、
  `unity_experiment.json`、`configuration.yaml`（2026-11-12確認）。
- 参考比較は毎ステップ分泌・報酬ありの `wh_middle2Gate_ST_16_v4_01` ～ `_03`。
  同じ16Agent・Preset・観測・trainer/test seed集合だが、旧方式の学習は `num_envs=1`。
  成功経路方式では分泌時期・セル当たり分泌回数・報酬付与頻度も変わる。

### 観察された事実

各値は5 trial seedの平均。Agent接触はペアイベント数。

| trainer seed | 成功経路 完了数 | 毎ステップ 完了数 | 成功経路 Agent接触 | 毎ステップ Agent接触 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 376.4 | 408.0 | 180.2 | 119.4 |
| 2 | 388.4 | 372.2 | 149.6 | 184.2 |
| 3 | 380.4 | 376.4 | 145.8 | 164.2 |
| 3 seed平均 | 381.7 | 385.5 | 158.5 | 155.9 |

成功経路方式のseed 3・trial seed 4は壁時計の安全タイムアウトで、
300秒目標に対して283.94シミュレーション秒で終了した（完了338件、Agent接触176件）。
ほかの14試行と旧方式の15試行は300秒に到達した。この短い試行を両方式から除いて
同じ14組で集計すると、完了数は成功経路384.9対毎ステップ386.0、
Agent接触は157.3対154.9、棚衝突は393.0対448.2。15試行をそのまま集計した
棚衝突の平均は成功経路393.3対毎ステップ451.5。

### 解釈・仮説と未実施の検証

この3 seedでは成功経路方式の一貫した改善は観察されない。完了数の優劣はseedごとに
入れ替わり、14組の同時間比較でも平均差は約1件にとどまる。分泌時期、セル当たりの
分泌回数、報酬付与頻度が同時に変わるため、分泌時期単独の効果は切り分けられない。
`num_envs` の差は学習時の並列数の違いとして記録する。推論条件には影響せず、
今回の成績差を説明する要因かどうかは検証していない。

報酬なしの `STseparated_successRoute_noRw_v1` は学習済みだが推論未実施。
これを既存の `STseparated_noRw_v1` と同じtrial seed集合で比較し、
報酬なし条件で成功経路方式がどう働くか確認する。分泌の遅延とセル当たり分泌回数は
この比較でも分離できない。

## 2026-11-13: 成功経路・報酬なしの3モデル推論

### 条件と記録

- 対象は `wh_middle2Gate_STseparated_successRoute_noRw_v1_01` ～ `_03`。
  比較対象は毎ステップ分泌・報酬なしの `wh_middle2Gate_ST_16_noRw_v1_01` ～ `_03`。
  両条件とも16Agent、`Middle_2Gate_v2`、trainer seed 1～3、学習時 `num_envs=2`。
- 各最終ONNXを1環境・16Agent、決定的推論、trial seed 1～5、目標300シミュレーション秒で評価。
  新方式の3件の `test_experiment.json` は学習記録からConfigを自動選択し、
  Config・Presetの内容ハッシュ一致を記録する。
- 元データは上記各runの `logTest.csv`、`logTest_summary.csv`、`test_experiment.json`、
  `unity_experiment.json`、`configuration.yaml`（2026-11-13確認）。

### 観察された事実

各値は5 trial seedの平均。Agent接触はペアイベント数。

| trainer seed | 成功経路 完了数 | 毎ステップ 完了数 | 成功経路 Agent接触 | 毎ステップ Agent接触 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 397.8 | 386.6 | 171.0 | 173.6 |
| 2 | 385.6 | 374.6 | 131.6 | 162.8 |
| 3 | 332.8 | 376.4 | 130.6 | 166.2 |
| 3 seed平均 | 372.1 | 379.2 | 144.4 | 167.5 |

新方式の15試行はすべて300秒に到達。旧方式のseed 1・trial seed 1は
壁時計の安全タイムアウトで298.86秒に終了した。同じ14組の300秒試行に絞ると、
完了数は成功経路370.3対毎ステップ378.4、Agent接触は141.3対167.4。

### 解釈・仮説と未実施の検証

報酬なし条件でも成功経路方式による搬送数の一貫した改善は観察されない。
seed 1・2では増え、seed 3では大きく減った。Agent接触は3 seedとも少なかったが、
搬送数への寄与は確認できない。分泌の遅延とセル当たり分泌回数の効果は分離できない。
成功経路マップが実際にどの程度観測され、判断に使われたかは未計測。
