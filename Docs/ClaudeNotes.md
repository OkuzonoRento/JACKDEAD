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
