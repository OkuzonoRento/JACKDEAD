# JACKDEAD 開発メモ（Claude Code の引き継ぎ用）

別の PC の Claude Code が、これまでの経緯を読んで続きから作業できるようにするためのメモ。
最終更新: 2026-11-24。コードを変えたら、ここも直すこと。

## 作業の進め方（この環境で分かったこと）
- Unity のコンパイル・実行は Claude からはできない。エラーや動作は `%LOCALAPPDATA%\Unity\Editor\Editor.log` を読んで確かめる（`error CS`、`[ボイス]`、`[Box爆発]` などで検索）。
- 日本語を含むスクリプトを PowerShell 5.1 で書き換えると文字化けする。Write / Edit ツール、または bash の sed を使う。
- この環境には Python / Node が無い。JSON の検証は PowerShell の `ConvertFrom-Json` で行う。
- シーン上の UI は、実行時に作らず、Editor のビルダー（`LobbyUIBuilder` / `PauseUIBuilder` / `KeyBindingsUIBuilder`）で、あらかじめシーンに置く方針。

## マルチプレイの基本
- UGS（Authentication / Lobby / Relay）+ Netcode for GameObjects。Relay は WebSocket（wss、443 番ポート）。ホストが全部を決め、参加者はお願いを送る形。
- 開始の条件: 部屋の全員の回線（ping）が測れていること（`NetworkSession.AllPingsMeasured`）。
- 通信（`RemotePlayerSync` / `GameplaySync` / `EnemyBrain`）: 止まっていても送る（スキップしない）。位置は 0.05 秒、状態は 0.1 秒、敵は 0.05 秒おき。
- アイテムと Box: 落とした人（敵の Box はホスト）が、動いている間 0.05 秒おきに位置を送る（`ItemPose`）。受け取る側は `RemoteItemFollower` で追う。止まったら `ItemSettle` で最終位置を合わせる。
- 通信の上限: `MaxPacketQueueSize = 8192`、`MaxPayloadSize = 16384`（`NetworkSession.cs`）。
- 声の通知: `GameplaySync.NotifyVoiceNoise`（参加者 → ホストだけ。敵が反応する）。

## プレイヤー ID とセーブ
- `PlayerIdentity.Id`: Steam が使えれば Steam ID、なければ PC ごとの GUID。オフラインでもオンラインでも同じ。Steam に移ったときは、PC の ID を別名（`Alias`）として引き継ぐ。
- Steam を使うには、Steamworks.NET を入れて `STEAMWORKS_NET` を定義する（または `PlayerIdentity.SetExternalId`）。
- セーブ（`GameSaveData.players`）に、プレイヤーごとの体力・正気度・ドーパミンを ID で保存する（`GameSaveBridge`）。同じ PC で複数起動して ID が重なったときは、`ID#席番号` で見分ける。
- ステージクリア・ゲームオーバーの演出は、同じ流れ（暗転 → タイトルカード → 次）に統一した（`ResultUIController`）。

## 近接ボイスチャット（Vivox）
- 実装: `Assets/Scripts/Network/ProximityVoice.cs`。Vivox のパッケージ（`com.unity.services.vivox`）が入っているときだけ有効（`Assets/Editor/VivoxDefineSetter.cs` が `JD_VIVOX` を付ける）。無いときは空の実装。
- 生きている人: 部屋コードごとの位置つきチャンネル（聞こえる距離 24m）。観戦者: 別の観戦者だけのチャンネル。ソロでは動かない（マルチの部屋でゲームシーンに入ったときだけ）。
- マイク: 設定（キー設定タブ）の「マイクの使い方」で、自動 / プッシュトゥトーク。キーは B = ミュート、V = 話す（キー設定で変更可。入力アクション `MicMute` / `PushToTalk`）。
- 設定の「ボイスチャット音量」（0〜200%、`SettingsManager.KEY_VOICE`）。
- 敵は、生きている人の声に反応する（声の大きさで 8〜20m）。観戦者の声には反応しない。
- 失敗したら 5 秒後に入り直す。

### 未解決: 音声が届かない（`RTP Timeout (1058)`）
- 症状: チャンネルへの参加はできるが、すぐ `RTP Timeout (1058)` で切断され、お互いの声が聞こえない。
- この PC で直したもの: ファイアウォールに、`Unity 6000.3.12f1 Editor` の受信をブロックする設定（プライベート / パブリック）があった。管理者の PowerShell で無効にした。
  `Get-NetFirewallRule -DisplayName "Unity 6000.3.12f1 Editor" | Where-Object { $_.Action -eq 'Block' } | Disable-NetFirewallRule`
- それでも直らなかった。この PC から外への UDP 全般（NTP・DNS）は通る。VPN・プロキシ・他のセキュリティソフトは無い（有線、Windows Defender のみ）。
- 推測: Vivox の音声（UDP 12000〜52000、第三者の資料。未確認）が、ネットワーク側（学校など）で止められている。
- 選択肢: ①ネットワーク管理者に許可を頼む ②別のネットワークで試す（テザリングはできない）③ルーターの設定を見る ④TCP/443 に切り替わる別サービス（LiveKit、Agora など）⑤自前の方式（Netcode の名前付きメッセージで、16kHz・モノラル・8bit μ-law・20ms ごとにホスト経由で送り、アバターの 3D AudioSource で再生。ネットワークの影響を受けないが、音質・遅延・作る量が不利）⑥このまま既知の制限にする。
- 決まっていること: 自前の方式は、まだやらない。まず別のネットワークでの確認を待つ。
- 提案中（未実装）: 失敗が続いたら、再試行を 60 秒おきに減らし、画面右下に「接続できません」を出す。

## その他の決定・状態
- アイテムの「使用」は、効果がまだ無いので無効（`PlayerController.itemUseEnabled = false`）。キー設定や項目は残してある。
- `Run In Background` は、いまオフ（`ProjectSettings` の `runInBackground: 0`）。通信が切れやすくなるので、オンにする提案中。
- 近接ボイスチャット用の、設定画面のシーン上の部品（ボイス音量のつまみ、マイクの使い方の行）は、Editor のビルダーが自動でシーンに置く。置いたあとは、シーンの保存と差分の確認が必要。
- 吹き飛びの方向（Box の爆発）は、計算上は正しい。体が床のない場所へ飛び出すと落下死する（ワープ処理は、ユーザーの指示で削除）。

## 敵の気づき方（REPO風、2026-11-25）
- `EnemyBrain`: プレイヤーごとの「気づきかけ」（0〜1）。立ち: 0.5秒（遠距離）、しゃがみ: 2.2秒。しゃがみは11m（立ちは22m）まで。すぐ気づく距離は立ち3m / しゃがみ1.5m。気づきかけ0.35以上で立ち止まって見る。見えなくなると2秒で消える。気づいた直後（0.7超）はしゃがんでも見失わない。
- しゃがみ足音は `EnemyNoise.Crouch = 1.2m`。数値は Enemy_01 のインスペクターで変えられる。Unity では未確認。
- 2026-11-25 追記: 視線は頭・胴・腰の3本（見える本数が少ないほど気づくのが遅い: 2本 1.4倍、1本 2.2倍）。歩きとしゃがみの足音は無音（`EnemyNoise.Walk/Crouch = 0`）。走りは16m。他プレイヤーの走り判定は、0.35秒の平均速さが7.5超。（上の「しゃがみ足音 1.2m」は古い）

## デバッグ表示（2026-11-25）
- `Assets/Scripts/System/DebugOverlay.cs`（全体が `#if UNITY_EDITOR`。ビルドに含まれない）。再生中に F3 で ON/OFF、F4 視覚、F5 音、F6 状態。敵の判断はソロ/ホストのみ。
- 敵側の公開情報は `EnemyBrain` の `#if UNITY_EDITOR` の範囲。
- コンパイル確認は、Unity を閉じなくても、`Library/Bee/artifacts/*E.dag/Assembly-CSharp.rsp` を元に（出力先を作業フォルダへ変え、ソース一覧を作り直して）Unity 同梱の `csc.dll` で実行できる。

## 2026-11-25 追加
- 机・椅子の下に潜る（しゃがみ + 頭の上0.7m以内に当たり判定）と気づかれにくい: 見える距離6m、気づくまで4秒、すぐ気づく距離0.8m（`EnemyBrain.IsUnderCover`）。
- ジャンプ（9m）と着地（11〜24m、落下速度で変化。5.5m/s未満は無音、出現直後1.5秒は除外）、吹き飛びの衝突が敵に聞こえる。参加者の音は `GameplaySync.NotifyNoise` でホストへ送る（声・物を落とす音も同じ）。
- 設定にフレームレート（30/60/120、初期60）: `FrameRateRow` + `KeyBindingsUIBuilder.EnsureFrameRateRow`（シーンに自動配置）。
- コンパイル確認は、実行用・ビルド用（UNITY_EDITORなし）・Editor用の3つを同梱の csc で行える。
- 2026-11-25 視界は頭（頭の骨）から前へのコーン: 左右60度・上下45度、10度下向き（`sightHalfAngle` / `sightVerticalHalfAngle` / `sightDownTilt`）。部位ごとにコーン内かを判定。足元の近くは死角になる。F3 で立体表示。
- 2026-11-25 気づくまでの時間を長くした（x=近く, y=遠く）: 立ち 0.9〜2.8秒、しゃがみ 2〜4.5秒、潜り 3.5〜6秒（`standingNoticeSeconds` など）。すぐ気づく距離は 3 / 1.5 / 0.8m のまま。デバッグの視界は地面の薄い扇形表示、立体コーンは F7。

## 敵の追跡の流れ（2026-11-25、REPO風）
- `EnemyBrain` の状態: 徘徊 → 物音を調べる / 発見して身構える(0.6s) → 追跡(投げ攻撃) → 見失うと最後の場所へ(最大3s) → 机の下に潜った相手は覗き込む(2.5s) → 周りを探す(8s) → 立ち去る(20m, 最大10s) → 徘徊。
- 覗き込みはクリップが無いので上体を曲げる。数値は私が決めたもので、本家の値は確認できていない。

## 追跡の流れの修正（2026-11-25、ユーザー指定。上の「追跡の流れ」は古い）
- 身構え・覗き込み・立ち去るは削除。状態は 徘徊 / 物音を調べる / 追跡 / 歩いて探す。
- 気づきかけは立ち止まらず、見えたあたりへ歩いて向かう。
- 追跡: 見失ったら最後に見た場所へ走る。3秒以内に着いたら、相手の移動方向から逃げた先（約9m）を予測して走る。3秒を超えたら歩いて探す。
- 歩いて探す: 周り6mを5秒 → 徘徊。

## 音の修正（2026-11-25、ユーザー指定）
- 投げた／落とした物の音は、投げた瞬間ではなく、ぶつかった場所で出る（`Item/ItemImpactNoise`）。物理を動かす本人のPCだけに付け、2m/s以上の衝突で鳴り、物が止まったら外れる。Box は9m、ほかは10m。敵が投げた Box にも付く。
- スロットは、回している間ずっと0.5秒ごとに音（30m）を出す（`SlotMachineController.Update`。他の人が回している台もホストで鳴る）。終了の通知が来ない場合は10秒で打ち切る。
- 2026-11-25 物音を調べるときは走らず歩く（`patrolSpeed`）。歩きでは遠い音に届かないため、打ち切りは6秒から25秒（`investigateMaxSeconds`）へ。古い `noiseInvestigateSpeed` / `loseSightTime` は削除（prefab に値が残っていても無視される）。
