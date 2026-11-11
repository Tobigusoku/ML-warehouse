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
