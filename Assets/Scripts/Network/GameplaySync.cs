using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゲーム中の出来事（アイテムの拾得・投擲、スロットの稼働）を他プレイヤーへ同期する。
/// マップとスクラップ・スロットの配置は共通シードで全員同じになるので、それぞれに連番ID（NetId）を振り、
/// 「どれが拾われた／どれが回っている」をIDで伝える。参加者のイベントはホストが中継する。
/// ゲームシーンに入ったときに NetworkSession が自動生成する。
/// </summary>
public class GameplaySync : MonoBehaviour
{
    private const string MsgEvent = "jd.ev";
    // NetId の範囲：マップのスクラップ＝ステージ番号×100000＋連番（ステージごとに重ならない）/ 落とした物＝10億＋席番号×1億＋連番
    private const int DropIdBase = 1000000000;
    private const int DropIdStride = 100000000;

    private enum EventType : byte
    {
        ItemPick = 1,   // id = Item.NetId
        ItemDrop = 2,   // id = 新しいNetId / text = ItemDataAsset名 / v1 = 位置 / v2 = 初速
        SlotBegin = 3,  // id = SlotMachineController.NetId
        SlotRoll = 4,   // id = 同上 / text = 各リールの出目（カンマ区切り）
        SlotEnd = 5,
        PickRequest = 6, // 参加者 → ホスト：このアイテムを拾いたい（id = Item.NetId）
        PickGrant = 7,   // ホスト → 要求者：拾ってよい
        PickDeny = 8,    // ホスト → 要求者：先に他の人が拾った
        MoneyDelta = 9,  // id = 共有所持金の増減額
        ItemSettle = 10, // id = NetId / v1 = 静止した位置 / v2 = 静止した回転（オイラー角）
        PlayerDead = 11,  // id = 倒れたプレイヤーの席番号（clientId）
        PlayerAlive = 12, // id = 復活したプレイヤーの席番号
        EnemyPose = 13,   // id = 敵のNetId / v1 = 位置 / v2 = (向きY, 状態, 0)。ホスト → 参加者
        Explosion = 14,   // v1 = 爆発位置 / v2 = (半径, 最大ダメージ, 0)。所持中の爆弾が爆発したとき
        Voice = 15,       // v1 = 音が出た位置 / v2 = (音の大きさ, 0, 0)。参加者 → ホストだけ（敵が声やジャンプの音などに反応するため）
        ItemPose = 16,    // id = アイテムのID / v1 = 位置 / v2 = 回転（オイラー角）。落とした／投げた本人 → 全員。動いている間、短い間隔で送る
        ItemValue = 17,   // id = アイテムのID / text = ぶつけて変わった売値（0 = 壊れた）。投げた本人 → 全員
        PlayerStun = 18,  // id = 当てられたプレイヤーの席番号 / v1.x = スタンする秒数。投げた本人 → 当てられた人
        EnemyHit = 19     // id = 敵のNetId / v1 = (ダメージ, スタン秒数, 0)。参加者 → ホストだけ（敵を動かしているのはホスト）
    }

    private struct NetEvent
    {
        public EventType type;
        public int id;
        public string text;
        public Vector3 v1;
        public Vector3 v2;
    }

    public static GameplaySync Instance { get; private set; }

    private NetworkManager networkManager;
    private int dropCounter;
    private static Dictionary<string, ItemDataAsset> itemDataByName;

    // ホストが管理する「すでに誰かが拾ったアイテムID」。同時に拾っても先着1人だけが取れるようにする
    private readonly HashSet<int> claimedIds = new HashSet<int>();
    // 参加者側：ホストの許可待ちのアイテム
    private readonly Dictionary<int, Item> pendingPicks = new Dictionary<int, Item>();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            Destroy(gameObject);
            return;
        }

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(MsgEvent, OnEventMessage);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (networkManager != null && networkManager.CustomMessagingManager != null)
        {
            networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(MsgEvent);
        }
    }

    // ------------------------------------------------------------------
    // 呼び出し口（オフライン・未接続のときは何もしない）
    // ------------------------------------------------------------------

    // 倒れている（観戦中の）プレイヤーの席番号
    private readonly HashSet<ulong> deadClients = new HashSet<ulong>();

    public static bool IsDead(ulong clientId)
    {
        return Instance != null && Instance.deadClients.Contains(clientId);
    }

    /// <summary>自分が倒れた／復活したことを全員へ伝える</summary>
    public static void NotifyLocalDead(bool dead)
    {
        if (Instance == null || Instance.networkManager == null) return;

        ulong me = Instance.networkManager.LocalClientId;
        if (dead) Instance.deadClients.Add(me); else Instance.deadClients.Remove(me);
        Instance.Send(new NetEvent { type = dead ? EventType.PlayerDead : EventType.PlayerAlive, id = (int)me });
    }

    /// <summary>プレイヤーが落とした／投げたアイテム用の、全員で重ならないID</summary>
    public static int AllocateDropId()
    {
        if (Instance == null || Instance.networkManager == null) return 0;
        Instance.dropCounter++;
        return DropIdBase + (int)Instance.networkManager.LocalClientId * DropIdStride + Instance.dropCounter;
    }

    /// <summary>
    /// 拾う前の確認。他の人がすでに拾っていたら true（拾えない）。ホストが自分で拾うとき用
    /// </summary>
    public static bool IsClaimed(Item item)
    {
        return Instance != null && item != null && item.NetId != 0
            && Instance.networkManager != null && Instance.networkManager.IsHost
            && Instance.claimedIds.Contains(item.NetId);
    }

    /// <summary>
    /// 参加者がアイテムを拾うときは、ホストの許可が要る。許可待ちにした（＝呼び出し側はここで中断する）なら true。
    /// ホスト・オフライン・同期対象外のアイテムは false（そのまま拾ってよい）
    /// </summary>
    public static bool RequestPickIfClient(Item item)
    {
        if (Instance == null || item == null || item.NetId == 0) return false;
        if (Instance.networkManager == null || Instance.networkManager.IsHost) return false;

        // 返事待ちの間に連打されても、要求は1回だけ
        if (Instance.pendingPicks.ContainsKey(item.NetId)) return true;

        Instance.pendingPicks[item.NetId] = item;
        Instance.Send(new NetEvent { type = EventType.PickRequest, id = item.NetId });
        return true;
    }

    /// <summary>ホストが自分で拾ったとき、全員に知らせる</summary>
    public static void NotifyItemPicked(Item item)
    {
        if (Instance == null || item == null || item.NetId == 0) return;
        Instance.claimedIds.Add(item.NetId);
        Instance.Send(new NetEvent { type = EventType.ItemPick, id = item.NetId });
    }

    /// <param name="bombFuse">時限爆弾なら導火線の残り（秒）。負なら指定なし</param>
    /// <param name="value">その1個の売値。負なら基準の価格</param>
    public static void NotifyItemDropped(Item item, ItemDataAsset data, Vector3 position, Vector3 velocity, float bombFuse = -1f, int value = -1)
    {
        if (Instance == null || item == null || item.NetId == 0 || data == null) return;
        Instance.claimedIds.Remove(item.NetId);
        // "アセット名|導火線の残り|売値"
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        string text = $"{data.name}|{bombFuse.ToString("F2", invariant)}|{value.ToString(invariant)}";
        Instance.Send(new NetEvent
        {
            type = EventType.ItemDrop, id = item.NetId, text = text, v1 = position, v2 = velocity
        });

        // 着地点が全員でずれないよう、投げた本人のPCで静止した位置を後から全員へ配る
        if (item.GetComponent<DroppedItemSettler>() == null) item.gameObject.AddComponent<DroppedItemSettler>();
    }

    public static void NotifyItemSettled(Item item, Vector3 position, Vector3 eulerAngles)
    {
        if (Instance == null || item == null || item.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.ItemSettle, id = item.NetId, v1 = position, v2 = eulerAngles });
    }

    /// <summary>落とした／投げた本人：動いているアイテムの位置と回転を、全員へ配る（短い間隔。止まっていても、止まるまで送る）</summary>
    public static void NotifyItemPose(Item item, Vector3 position, Vector3 eulerAngles)
    {
        if (Instance == null || item == null || item.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.ItemPose, id = item.NetId, v1 = position, v2 = eulerAngles });
    }

    /// <summary>投げた本人：ぶつけて売値が変わった（0 = 壊れた）ことを全員へ伝える</summary>
    public static void NotifyItemValue(Item item, int value)
    {
        if (Instance == null || item == null || item.NetId == 0) return;
        Instance.Send(new NetEvent
        {
            type = EventType.ItemValue, id = item.NetId, text = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    /// <summary>投げた物を他のプレイヤーに当てた：当てられた人の画面でスタンさせる</summary>
    public static void NotifyPlayerStun(ulong clientId, float seconds)
    {
        if (Instance == null) return;
        Instance.Send(new NetEvent { type = EventType.PlayerStun, id = (int)clientId, v1 = new Vector3(seconds, 0f, 0f) });
    }

    /// <summary>投げた物を敵に当てた：ソロとホストはその場で、参加者はホストに頼んで、ダメージとスタンを与える</summary>
    public static void HitEnemy(EnemyBrain enemy, float damage, float stunSeconds)
    {
        if (enemy == null) return;

        if (Instance == null || Instance.networkManager == null || !Instance.networkManager.IsListening
            || Instance.networkManager.IsHost)
        {
            enemy.TakeThrownHit(damage, stunSeconds);
            return;
        }

        Instance.WriteAndSend(
            new NetEvent { type = EventType.EnemyHit, id = enemy.NetId, v1 = new Vector3(damage, stunSeconds, 0f) },
            NetworkManager.ServerClientId);
    }

    /// <summary>ホスト：敵の位置・向き・状態を全員へ配る（参加者側の敵は見た目だけ）</summary>
    public static void NotifyEnemyPose(int enemyId, Vector3 position, float yaw, int state)
    {
        if (Instance == null) return;
        Instance.Send(new NetEvent
        {
            type = EventType.EnemyPose, id = enemyId, v1 = position, v2 = new Vector3(yaw, state, 0f)
        });
    }

    /// <summary>持っていた爆弾が爆発したとき、周りの人にも爆発を伝える</summary>
    public static void NotifyExplosion(Vector3 position, float radius, float damage)
    {
        if (Instance == null) return;
        Instance.Send(new NetEvent { type = EventType.Explosion, v1 = position, v2 = new Vector3(radius, damage, 0f) });
    }

    /// <summary>
    /// 自分が立てた音（声・ジャンプ・着地・物を落とすなど）を、敵に知らせる。敵の判断はホストが行うので、参加者はホストだけに送る
    /// </summary>
    public static void NotifyNoise(Vector3 position, float loudness)
    {
        if (Instance == null || Instance.networkManager == null || !Instance.networkManager.IsListening)
        {
            EnemyNoise.Emit(position, loudness); // ソロ
            return;
        }

        if (Instance.networkManager.IsHost)
        {
            EnemyNoise.Emit(position, loudness);
            return;
        }
        Instance.WriteAndSend(new NetEvent { type = EventType.Voice, v1 = position, v2 = new Vector3(loudness, 0f, 0f) }, NetworkManager.ServerClientId);
    }

    /// <summary>所持金は全員共通。増減分を全員へ配る</summary>
    public static void NotifyMoneyDelta(int amount)
    {
        if (Instance == null || amount == 0) return;
        Instance.Send(new NetEvent { type = EventType.MoneyDelta, id = amount });
    }

    public static void NotifySlotBegin(SlotMachineController slot)
    {
        if (Instance == null || slot == null || slot.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.SlotBegin, id = slot.NetId });
    }

    public static void NotifySlotRoll(SlotMachineController slot, int[] results)
    {
        if (Instance == null || slot == null || slot.NetId == 0 || results == null) return;
        Instance.Send(new NetEvent { type = EventType.SlotRoll, id = slot.NetId, text = string.Join(",", results) });
    }

    /// <param name="aborted">攻撃を受けて途中で中止したとき true（他の人の画面でも回転を止める）</param>
    public static void NotifySlotEnd(SlotMachineController slot, bool aborted = false)
    {
        if (Instance == null || slot == null || slot.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.SlotEnd, id = slot.NetId, text = aborted ? "abort" : "" });
    }

    // ------------------------------------------------------------------
    // 送受信
    // ------------------------------------------------------------------

    private void Send(NetEvent e)
    {
        if (networkManager == null || !networkManager.IsListening) return;

        if (networkManager.IsHost)
        {
            Relay(e, ulong.MaxValue);
        }
        else
        {
            WriteAndSend(e, NetworkManager.ServerClientId);
        }
    }

    private void Relay(NetEvent e, ulong exceptClientId)
    {
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == networkManager.LocalClientId || clientId == exceptClientId) continue;
            WriteAndSend(e, clientId);
        }
    }

    /// <summary>
    /// 敵の位置のように毎秒何回も送るものは、信頼性なし（古いものは捨てる）で送る。
    /// 信頼性ありで大量に送ると送信キューが詰まり、通信が切断されるため。敵が倒された通知だけは確実に届ける
    /// </summary>
    private static NetworkDelivery DeliveryFor(NetEvent e)
    {
        bool isDeadNotice = e.type == EventType.EnemyPose && (int)e.v2.y == 9;
        if (e.type == EventType.Voice || e.type == EventType.ItemPose) return NetworkDelivery.UnreliableSequenced;
        if (e.type == EventType.EnemyPose && !isDeadNotice) return NetworkDelivery.UnreliableSequenced;
        return NetworkDelivery.ReliableSequenced;
    }

    private void WriteAndSend(NetEvent e, ulong targetClientId)
    {
        string text = e.text ?? "";
        using (FastBufferWriter writer = new FastBufferWriter(96 + text.Length * 2, Allocator.Temp))
        {
            writer.WriteValueSafe((byte)e.type);
            writer.WriteValueSafe(e.id);
            writer.WriteValueSafe(text);
            writer.WriteValueSafe(e.v1);
            writer.WriteValueSafe(e.v2);
            networkManager.CustomMessagingManager.SendNamedMessage(
                MsgEvent, targetClientId, writer, DeliveryFor(e));
        }
    }

    private void OnEventMessage(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte type);
        NetEvent e = new NetEvent { type = (EventType)type };
        reader.ReadValueSafe(out e.id);
        reader.ReadValueSafe(out e.text);
        reader.ReadValueSafe(out e.v1);
        reader.ReadValueSafe(out e.v2);

        // 拾う許可の要求はホストだけが処理する（中継しない）
        if (e.type == EventType.PickRequest)
        {
            if (networkManager.IsHost) HandlePickRequest(senderId, e.id);
            return;
        }

        // 参加者が立てた音は、ホストだけが受けて、敵に聞かせる（中継しない）
        if (e.type == EventType.Voice)
        {
            if (networkManager.IsHost) EnemyNoise.Emit(e.v1, e.v2.x);
            return;
        }

        // 参加者が敵に物を当てた：敵を動かしているホストだけが処理する（中継しない）
        if (e.type == EventType.EnemyHit)
        {
            EnemyBrain enemy = networkManager.IsHost ? EnemyBrain.Find(e.id) : null;
            if (enemy != null) enemy.TakeThrownHit(e.v1.x, e.v1.y);
            return;
        }

        // ホストは、送ってきた人以外の参加者へ中継する
        if (networkManager.IsHost) Relay(e, senderId);
        Apply(e);
    }

    /// <summary>ホスト：先着1人にだけ拾わせる。許可した人以外には「拾われた」を、要求者には許可/拒否を返す</summary>
    private void HandlePickRequest(ulong requesterId, int itemId)
    {
        Item item = FindItem(itemId);
        if (item == null || claimedIds.Contains(itemId))
        {
            WriteAndSend(new NetEvent { type = EventType.PickDeny, id = itemId }, requesterId);
            return;
        }

        claimedIds.Add(itemId);
        // 要求者へ許可を先に送り、その後に他の全員へ「拾われた」を伝える
        WriteAndSend(new NetEvent { type = EventType.PickGrant, id = itemId }, requesterId);
        Relay(new NetEvent { type = EventType.ItemPick, id = itemId }, requesterId);
        Destroy(item.gameObject);
    }

    // ------------------------------------------------------------------
    // 反映
    // ------------------------------------------------------------------

    private void Apply(NetEvent e)
    {
        switch (e.type)
        {
            case EventType.ItemPick:
            {
                Item item = FindItem(e.id);
                if (item != null) Destroy(item.gameObject);
                break;
            }
            case EventType.ItemDrop:
                claimedIds.Remove(e.id);
                SpawnRemoteDroppedItem(e);
                break;
            case EventType.PlayerDead:
                deadClients.Add((ulong)e.id);
                break;
            case EventType.PlayerAlive:
                deadClients.Remove((ulong)e.id);
                break;
            case EventType.EnemyPose:
                EnemyBrain.ApplyRemotePose(e.id, e.v1, e.v2.x, (int)e.v2.y);
                break;
            case EventType.Explosion:
                BombBox.ExplodeAt(e.v1, e.v2.x, e.v2.y);
                break;
            case EventType.PickGrant:
                CompletePick(e.id);
                break;
            case EventType.PickDeny:
                pendingPicks.Remove(e.id);
                break;
            case EventType.MoneyDelta:
            {
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) pc.ApplySharedMoneyDelta(e.id);
                break;
            }
            case EventType.ItemPose:
            {
                Item item = FindItem(e.id);
                if (item == null) break;
                RemoteItemFollower follower = item.GetComponent<RemoteItemFollower>();
                if (follower == null) follower = item.gameObject.AddComponent<RemoteItemFollower>();
                follower.SetTarget(e.v1, Quaternion.Euler(e.v2));
                break;
            }
            case EventType.ItemValue:
            {
                Item item = FindItem(e.id);
                if (item != null && int.TryParse(e.text, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int value))
                {
                    item.ApplyDamagedValue(value);
                }
                break;
            }
            case EventType.PlayerStun:
            {
                if (networkManager == null || (ulong)e.id != networkManager.LocalClientId) break;
                PlayerController pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) pc.Stun(e.v1.x);
                break;
            }
            case EventType.ItemSettle:
            {
                Item item = FindItem(e.id);
                if (item == null) break;
                RemoteItemFollower follower = item.GetComponent<RemoteItemFollower>();
                if (follower != null) Destroy(follower);
                item.transform.SetPositionAndRotation(e.v1, Quaternion.Euler(e.v2));
                Rigidbody rb = item.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.Sleep();
                }
                break;
            }
            case EventType.SlotBegin:
            {
                SlotMachineController slot = FindSlot(e.id);
                if (slot != null) slot.SetRemoteBusy(true);
                break;
            }
            case EventType.SlotRoll:
            {
                SlotMachineController slot = FindSlot(e.id);
                if (slot != null) slot.PlayRemoteRoll(ParseResults(e.text));
                break;
            }
            case EventType.SlotEnd:
            {
                SlotMachineController slot = FindSlot(e.id);
                if (slot != null)
                {
                    slot.SetRemoteBusy(false);
                    if (e.text == "abort") slot.AbortVisual();
                }
                break;
            }
        }
    }

    /// <summary>参加者：ホストが拾うことを許可した。インベントリに入れて、ワールドのアイテムを消す</summary>
    private void CompletePick(int itemId)
    {
        if (!pendingPicks.TryGetValue(itemId, out Item item)) return;
        pendingPicks.Remove(itemId);
        if (item == null) return;

        PlayerController pc = FindAnyObjectByType<PlayerController>();
        BombBox bomb = item.GetComponent<BombBox>();
        float fuse = bomb != null ? bomb.Remaining : -1f;
        if (pc != null && item.ItemData != null && pc.TryAddItem(item.ItemData, fuse, item.CurrentValue))
        {
            Destroy(item.gameObject);
            return;
        }

        // 許可を待つ間にインベントリが埋まった：その場に落とし直して、全員にもう一度見えるようにする
        if (item.ItemData != null)
        {
            NotifyItemDropped(item, item.ItemData, item.transform.position, Vector3.zero, fuse, item.CurrentValue);
        }
    }

    // アイテムの位置は毎秒何十回も届くので、見つけた物は覚えておく（毎回、全部を探さない）
    private static readonly Dictionary<int, Item> itemCache = new Dictionary<int, Item>();

    private static readonly Dictionary<int, float> itemMissUntil = new Dictionary<int, float>();

    private static Item FindItem(int netId)
    {
        if (itemCache.TryGetValue(netId, out Item cached))
        {
            if (cached != null && cached.NetId == netId) return cached;
            itemCache.Remove(netId);
        }

        // 見つからなかった物（すでに拾われたなど）を、続けて全部探さないようにする
        if (itemMissUntil.TryGetValue(netId, out float until) && Time.unscaledTime < until) return null;

        foreach (Item item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (item.NetId != 0) itemCache[item.NetId] = item;
        }
        if (itemCache.TryGetValue(netId, out Item found) && found != null)
        {
            itemMissUntil.Remove(netId);
            return found;
        }
        itemMissUntil[netId] = Time.unscaledTime + 0.5f;
        return null;
    }

    private static SlotMachineController FindSlot(int netId)
    {
        foreach (SlotMachineController slot in FindObjectsByType<SlotMachineController>(FindObjectsSortMode.None))
        {
            if (slot.NetId == netId) return slot;
        }
        return null;
    }

    private static int[] ParseResults(string text)
    {
        if (string.IsNullOrEmpty(text)) return new int[0];
        string[] parts = text.Split(',');
        int[] results = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out results[i]);
        return results;
    }

    /// <summary>アセット名から ItemDataAsset を引く（空文字は null）</summary>
    public static ItemDataAsset ResolveItemData(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return null;

        if (itemDataByName == null) RebuildItemDataIndex();
        if (itemDataByName.TryGetValue(assetName, out ItemDataAsset found) && found != null) return found;

        // 見つからないときは、敵の出現設定（Box のデータを持つ）を読み込んでから、一覧を作り直して、もう一度探す
        EnemySpawnConfig.Load();
        RebuildItemDataIndex();
        itemDataByName.TryGetValue(assetName, out found);
        return found != null ? found : null;
    }

    /// <summary>
    /// 読み込まれているアイテムデータの一覧を作る。
    /// Resources.LoadAll("") は、Resources の中のすべて（フォントの画像など）を読み込んでしまい重く、警告も出るので使わない。
    /// </summary>
    private static void RebuildItemDataIndex()
    {
        itemDataByName = new Dictionary<string, ItemDataAsset>();
        foreach (ItemDataAsset data in Resources.FindObjectsOfTypeAll<ItemDataAsset>())
        {
            itemDataByName[data.name] = data;
        }
    }

    /// <summary>他プレイヤーが落とした／投げたアイテムを、こちらにも出す（PlayerController.DropSelectedInventoryItem と同じ作り）</summary>
    private void SpawnRemoteDroppedItem(NetEvent e)
    {
        if (FindItem(e.id) != null) return;

        // "アセット名|導火線の残り|売値" で届く（導火線と売値は、負なら指定なし）
        string[] parts = (e.text ?? "").Split('|');
        string assetName = parts[0];
        float fuse = -1f;
        int value = -1;
        System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length > 1 && !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, invariant, out fuse)) fuse = -1f;
        if (parts.Length > 2 && !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, invariant, out value)) value = -1;

        ItemDataAsset data = ResolveItemData(assetName);
        if (data == null || data.ItemPrefab == null)
        {
            Debug.LogWarning($"[ONLINE] 落とされたアイテム '{e.text}' が見つからないため表示できません。");
            return;
        }

        GameObject obj = Instantiate(data.ItemPrefab, e.v1, Quaternion.identity);
        obj.layer = LayerMask.NameToLayer("Items");

        Item item = obj.GetComponent<Item>();
        if (item == null) item = obj.AddComponent<Item>();
        item.SetItemData(data);
        item.NetId = e.id;
        item.SetValue(value);

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = e.v2;

        BombBox bomb = obj.GetComponent<BombBox>();
        if (bomb != null && fuse >= 0f) bomb.SetRemaining(fuse);
    }
}
