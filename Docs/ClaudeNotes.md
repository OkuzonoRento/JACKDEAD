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

## 落下死の持ち物（2026-11-26、ユーザー指定の仕様）
- 落下死した場合、持っていた物は回収できない。マップの落とし穴もこの仕様のまま作る。
- 2026-11-26 追加の指定: 落下したアイテムは壊れて消える。落下死では持ち物をワールドに出さず削除（`PlayerController.BreakAllItems`）。y<-8 まで落ちたアイテムは `Item.Update` が削除（`Item.BreakHeight`）。

## 旅館マップチップ（2026-11-26）
- 24部屋（8形状×A/B/C、`_pit` は落とし穴あり）。Blender 版は `Docs/MapPrototype/Chips_v2`、取り込み元は `Assets/Models/Map/Ryokan`（fbx / textures / rooms.json）。
- `Assets/Editor/RyokanMapImporter` が、マテリアル・プレハブ（当たり判定は本体だけ、影なしの点光源つき）・MapChipData を作り、SampleScene の `allMapChips` を24個へ差し替える。未作成なら読み込み時に一度だけ自動実行。作り直しは「JackDead/Map/旅館マップを取り込む」。元の仮置き8個は残っていて、`allMapChips` に戻せば元通り。
- 当たり判定の決まり: 床以外に、高さ1m以下の上向きの面を当たり判定に入れない（床の高さを誤判定する）。天井は下向きの片面。
- 部屋を作る Blender スクリプトは `Docs/MapPrototype/Chips_v2/scripts`。`core.py` + `rooms.py` + `tail.py` をこの順につないで実行する（引数: 出力先、元のFBXフォルダ、スクリプトの場所。開くファイルはユーザーの `JackDeadMapchips.blend`）。rooms.json は末尾を `dump.py` に替え、`Room.lantern` で位置を `self.lights` に記録して作った。
- 2026-11-26 修正: へこんだ角で壁の内側の面が厚み（0.2m）ぶん足りず、外が見えていた。`core.py` の `build_walls` で内側の面を伸ばして解決し、FBX を差し替え（meta は維持）。壁は内外の片面だけなので、角や端を足すときは隙間に注意。

## 2026-11-26 出入口・スロット・ライト・観戦（ユーザー指定）
- 先に部屋がない出入口は `MapGenerator.BlockUnconnectedDoors` で塞ぐ（トラックの出入口は開ける）。外へ開く出入口が1つも無いマップは作り直す。
- スロット: 置けないときは壁の余白を1.5mに狭め、2周目で1部屋2台まで（台数の決め方は下の節）。
- ライト: 懐中電灯は `PlayerMovement.flashlightIntensityScale`(0.7)、部屋の明かりは `MapGenerator.roomLightScale`(0.7)。しゃがみ中は懐中電灯を消す（スタン・吹き飛び中は消さない）。
- マルチ: 姿勢の送信に点灯状態を追加し、他の人の懐中電灯を表示（`RemotePlayerSync.UpdateRemoteFlashlight`）。状態の送信は20Hzにして、照準・説明文・長押しゲージ・掛け金の倍率も送り、観戦者の画面に出す（`PlayerInteraction.ShowSpectatedHud`、`SlotButtonInteractable`）。通信の形式が変わったので、全員が同じ版で遊ぶこと。
- いずれも再生・マルチでは未確認。

## スロットが2台しか出ない件（2026-11-26）
- ユーザー指定の仕様: ステージ5で2台、ステージ20で8台まで少しずつ増える（抽選はしない。20以降は8台）。
- `MapGenerator.GetSlotCountForLevel` が `slotStartLevel` の `minSlotCount` から `maxStageLevel` の `maxSlotCount` まで直線で増やす。抽選台数の倍率（`slotCountMultiplier`）は廃止した。
- 部屋の当たり判定で配置を再現した限り、24部屋とも置ける。生成時に `[MAP] スロット配置: …` をログに出す。再生では未確認。

## トラックと玄関前の通路（2026-11-26）
- `Assets/Editor/TruckEntranceImporter` が `Assets/Prefabs/Truck.prefab` を書き換える（Approach が無ければ一度だけ自動実行。メニュー「JackDead/Map/トラックと玄関前の通路を作り直す」）。`Truck.blend` は変えない。
- TruckMesh と Lever は同じ形のまま UV と材質を付けたメッシュ（`Assets/Models/TruckEntrance/Generated`）に差し替え、元のつなぎ部分 `Plane` は非表示。文字は黒なので、後ろの板は明るい材質にしている（板の場所は座標で指定しているので、トラックの形を変えたら直す）。
- 通路は長さ10m・内幅6.5m・高さ6m。天井の当たり判定は全面（見た目の屋根は抜けている）。点光源4つ。モデルとテクスチャは `Docs/MapPrototype/Truck/build_truck.py`（`Chips_v2/MapChips_v2.blend` を開いて実行。引数: プロジェクトの場所、確認画像の出力先）。
- Unity での取り込みと再生は未確認。

## スクラップの取り込み（2026-11-26）
- 下の提案値で決定。`Assets/Editor/ScrapImporter`（`Scrap_Screw.asset` のプレハブが無ければ一度だけ自動実行。メニュー「JackDead/Item/スクラップを取り込む」）が、`Assets/Models/Scrap` から `Assets/Prefabs/Scrap`・`Assets/Data/Scrap` を作り、`scrapDataList` を11種類に差し替える。作り直しても ItemDataAsset の数値は上書きしない。モデルの書き出しは `Docs/ScrapModels/export_unity.py`。
- スタンガン: 当てるとスタンが長い（プレイヤー3.5秒・敵6秒、`ItemDataAsset` の `playerStunSeconds`/`enemyStunSeconds`）。
- 懐中電灯: 持ち物欄で選んでいる間、自分のライトが強さ2.5倍・距離1.8倍・広さ1.2倍（`heldLight*`）。他の人の画面と観戦にも反映。しゃがみ中は消える。
- 大きいスクラップは、その大きさに合う場所にだけ置く（`MapGenerator.GetScrapRadius`）。

## 落ちているアイテムの光（2026-11-26）
- 爆弾以外の `Item` に `ItemHighlight` が付き、`Resources/Shaders/ItemGlow`（足し算で重ねる、ふちの光と流れる光。周りは照らさない。10〜18mで薄くなる）を材質の後ろに足す。照準が合うと明るくする。調整は `PlayerInteraction` の `itemGlow`。取り込みは確認済みのスクラップに対して追加。シェーダーと見た目は Unity では未確認。

## スクラップ（2026-11-26 の提案時の記録）
- 元データはユーザーの `Downloads\JackDead-Scrap (1).blend`（11種類。変更しない）。`Docs/ScrapModels/build_scrap.py` が UV とテクスチャを付け、`textures/`、`JackDead-Scrap_textured.blend`、`preview.png` を出す。**Unity への取り込みは、金額・重さ・大きさが決まってから**（まだ Assets には入れていない）。
- 提案中の値（レベル1基準の金額 / 重さkg / 一番長い辺m / 壊れやすさ）: スマホ 360/0.2/0.30/1.6、ゲーム機 400/0.4/0.40/1.4、スタンガン 300/0.5/0.35/1.0、バッテリー 260/6/0.45/0.7、大歯車 280/7/0.80/0.3、プロペラ 220/3.5/1.0/0.8、懐中電灯 160/0.6/0.40/1.0、歯車 150/3/0.50/0.3、チェーン 120/4/1.2/0.2、鉄板 110/8/0.80/0.3、ねじ 70/0.8/0.35/0.2。
- 金額の揺れ: `MapGenerator.scrapPriceVariance`(±20%)。1個ごとの売値は `Item.SetValue` / `PlayerController.GetHeldValue` が持ち、拾う・落とす・マルチ同期で引き継ぐ。
- 投げて当てる: `ScrapThrowImpact`（調整は `PlayerController.throwImpact`）。トラックの外でぶつかるたびに減額（0で壊れる）、直撃ならプレイヤーは短いスタン、敵はスタンとダメージ。投げる速さは重さで 11〜4.5m/s（`baseThrowForce` は廃止）。再生・マルチでは未確認。

## PV（紹介映像）（2026-11-27）
- `Assets/Editor/PV/` が、約60秒の PV を Editor 内で描いて MP4 に書き出す（再生はしない）。メニュー JackDead/PV/PVを書き出す → `Recordings/JACKDEAD_PV.mp4`（git には入らない）。静止画の確認は「静止画で確認する」→ `Recordings/preview/sheet.png`。
- 場面とテロップは `PVDirector.cs`、音は `PVAudio.cs`（スロットはゲームの `SlotWinSounds`、ほかは計算で作る）、重ねる文字と右下の注意書きは `PVComposer.cs`、JACKPOT の演出は `PVSlotWin.cs`（`SlotWinEffects` の時間と動きを組み直したもの。あちらを変えたら見直す）。
- 新しい旅館マップ・トラック・スクラップを使う。カメラは `FindClearSection` が見通しのよい区間を選ぶ。場面の秒数を変えたら、音の時刻もそろえる。
- 映すのは実装済みの要素だけ（ルーレットなどの他の台、人体賭博、病院は入れていない）。
- 2026-11-27 の直し（ユーザー指定）: テロップ「暗闇の中を探れ。」／スロットは回る前に当たりの目を見せない（`PVSlot.BeforeSpin`）／追跡の場面はサンタが立ち止まって投げる（腕の角度と時間は `EnemyBrain` と同じ値を `PVDirector.ThrowArmAngle` に写している。体のひねりは PV だけ）／仲間の場面も箱が飛んでくるのを映す／全体の音量は `PVAudio.Master` = 0.45。
- 注意: 全コマを1回の更新で描くと GPU メモリ不足でエディタが落ちる（`d3d12 8007000e`）。数コマずつ描く形にしてある。URP の距離の霧は、この描き方では画面が真っ黒になるので使わない。
- 書き出し中は SampleScene を一時的に組み替える（保存はしない）。未保存のシーンがあると実行しない。

## 音（効果音・曲）（2026-11-27）
- ユーザーの指定: 全体の効果音と BGM を入れる。追われているときは怖く。
- **スロットの音は、一度ゲームの雰囲気に合わせて作り直したが、ユーザーの指示で元に戻した**（`Slot/SlotSynth.cs` は下の「スロットの音と当たり演出」の版のまま）。
- **「BGM が騒がしすぎる」との指摘で、曲を静かにした**: 追跡の曲は鈴・板を叩く音・16分の刻み・割り込むオルゴールを削り、164→112 BPM の低い弦と重い一打に。環境音・タイトル曲は音数と大きさを減らした。サンタの鈴は2歩に1回・小さめ、遠くの物音も小さめ。**これ以上にぎやかにしない。**
- 素材なし。部品は `Audio/SynthKit.cs`、効果音と曲は `Audio/GameSynth.cs`（UnityEngine を使わず、Unity の外でも書き出せる）。雰囲気は「古い旅館・借金・サンタ」。差し替えは `Resources/SlotSounds`・`Resources/GameSounds`。
- 再生は `GameAudio`（効果音。UI の重ね・押下とダメージ音もここ）、`GameMusic`（タイトル曲 / 環境音 / 追跡の曲＋心音。`EnemyBrain.NearestChaserDistance` が 25m 以内で追跡。振り切って 4 秒後に消える）、`EnemyAudio`（足音と鈴・笑い声・うなり）、`FootstepAudio`（仲間の足音）。
- シーンには `Editor/FeelEffectBuilder` が `GameAudio`（両シーン）と `HighAura`（SampleScene の PlayerStatusUI の最初の子）を置く。作り直しは「JackDead/Game/ハイのオーラと音をシーンに作成」。
- 曲は `GameMusic.Start` が別スレッドで作る。BGM / SE の音量は設定画面の値。
- 確認: コンパイル、WAV への書き出し（`Recordings/sounds/demo_*.wav`、git には入らない）、Editor の再生で曲が流れ追跡の曲に切り替わること。**耳で聞いての確認、マルチ、タイトル画面の再生は未確認。**
## 操作方法・仕様の資料（2026-11-27）
- Claude Docs「JACKDEAD 操作方法・仕様まとめ」 https://claude.ai/artifact/AkjCDRyQtGNpMp9WHaJrgc 。ゲーム画面は `Docs/Manual/Screenshots/`（再生して撮ったもの。立ち位置・所持金・持ち物は撮影用に設定）。仕様の数値を変えたら資料も直す。
- 調べて分かった未修正の点: コントローラーの十字キー上が「スロット2」と「押して話す」、下が「投げる」と「ミュート」で重なっている。PV のスロットの掛け金表示は仮の値（100/500/1000 基準）で、ゲームは 1000/5000/10000 基準。シーンの走る速さは 10（コードの初期値 8 と違う）。

## 設定の初めの値（2026-11-27、ユーザー指定）
- まだ保存していない人の値: 全体・BGM・SE = 50%、ボイス = 100%（0〜200% の真ん中）、明るさ = 50%、マウス感度 = 0.8、フレームレート = 60（`SettingsManager` の `Initial…`）。`DefaultSensitivity`(1) は速さの基準なので変えない。
- すでに保存した値がある PC では、保存した値のまま。

## ドーパミンとハイのオーラ（2026-11-27、ユーザー指定）
- 勝ったときのドーパミン = `dopaminePerSymbolMultiplier`(6) × 出目の倍率（×1.5→9、×2→12、×3→18、×5→30、×7→42）。台の倍率は入れていない。負けは -5 のまま。
- `Player/HighAura` + `Resources/Shaders/HighAura`: ドーパミン 35% から画面の端に色の移り変わるオーラ。多いほど濃く広い。増えた瞬間は少し強まる。観戦中は相手の値。

## 敵の追尾の強化（2026-11-27、ユーザー指定「追尾性能が低い」）
- 原因: 追う速さ 4.5 が歩き 5 より遅い／投げる合間も立ち止まる／近いと後ずさりして相手を見失う／一度見失うと気づき直しに数秒かかる。
- 変更: `chaseSpeed` 6.2（プレハブも）。立ち止まるのは構えの間だけで `holdDistance` 4.5m まで詰める（後ずさりは削除）。見失って 1.5 秒は相手の動きに付いていく（机の下は除く）。追跡・探索中は視界 左右120度・上下75度、4m 以内は向きに関係なく発見、追っていた相手はすぐ気づき直す。追跡中の物音で行き先を直す。
- 上の「追跡の流れの修正」の流れと「3秒以内に着いたら予測」は維持（数える起点は 1.5 秒のあと）。
- Editor の再生で、歩いて逃げる相手に約 4〜7m で付いてくることを確認（その後 `holdDistance` を 3.5→4.5 にした）。走って逃げる場合とマルチは未確認。

## ゲーム開始時のステージ表示（2026-11-26）
- `ResultUIController.OpeningCardRoutine`: ゲームシーン開始時に、暗転したまま `STAGE n` / ノルマ / 借金を出してから明転する。表示中は `IsTransitioning` なので無敵で、ポーズは開けず、操作と正気度減少も止める。`showOpeningCard` でオフにできる。再生では未確認。

## スロットの音と当たり演出（2026-11-26、CloverPit 風の見た目・重い鉄の音）
- ユーザーの要望の経緯: 画面全体の派手な演出は「微妙」→ CloverPit 風 → 「ホラー・不気味感が薄れた」「ボタンで『てっててっててん』」「実機の効果音みたいに」→ 「音はジャグラー」「実機の音がいい」（著作物なので入れていない）→ **「音が可愛いので可愛くないように」「ホラーテイストでなく CloverPit 風に」が最新**。血の垂れる文字・髑髏・停電・666 は消した。
- 見た目: そろった目にピンクの枠 → 台の上の掲示板（クリーム色）に倍率 → リールの上で「+金額」が数え上がる → 黄色く光る窓の枠・ピンクの星・コイン。WIN は金額だけ、BIG WIN は見出し＋金額、JACKPOT は見出しだけ。
- 音: `SlotSynth`（素材なし）。重い鉄の音は「低すぎ」とされ、実機との中間の高さにした（`NoteBase`・`ImpactPitch` で調整）。明るい和音は使わない。曲は自作。**素材に差し替えるには `Assets/Resources/SlotSounds` に `SlotSynth.Sound` と同じ名前の音声ファイルを置く**（一覧は `Docs/SlotFx/音の差し替え方.md`）。
- 流れ: ボタン → 開始音（0.85秒後に回り出す）→ 停止 → 枠 → 倍率 → 数え上げ → 仕上げ（BIG WIN 以上は一撃の音と曲）。調整は `SlotMachineController` の `celebration`（固定の余韻、音量、揺れ、コイン、`windowFrameDepth`＝窓の枠の出っ張り）。
- 組み立ては `SlotWinEffects`、絵は `SlotPixelArt`、シェーダーは `Resources/Shaders/SlotPixel`・`SlotCoin`。他の人へは `GameplaySync` の `SlotBegin`・`SlotWin`(20)（全員同じビルドが必要）。
- Unity の外での確認: `Docs/SlotFx/PreviewProgram.cs`（WAV と絵を書き出す。鳴る順は手で合わせているので `RunWin` を変えたら直す）、`preview_render.py`（台のモデルに重ねた画像）。出力は `Docs/SlotFx/sounds`・`*.png`。
- 再生中の確認は台の `SlotMachineController` の ︙ メニュー「演出テスト」（お金は動かない）。この版はコンパイルと Unity 外の確認まで。Unity の再生・マルチでは未確認。
- 気づいた点（未修正）: リールの UV と描き方から計算すると、目が横に約2倍伸びて表示される。
