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
    private const int DropIdBase = 1 << 20; // これ未満はマップ生成時のスクラップ、以上はプレイヤーが落とした物

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
        ItemSettle = 10  // id = NetId / v1 = 静止した位置 / v2 = 静止した回転（オイラー角）
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
    private Dictionary<string, ItemDataAsset> itemDataByName;

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

    /// <summary>プレイヤーが落とした／投げたアイテム用の、全員で重ならないID</summary>
    public static int AllocateDropId()
    {
        if (Instance == null || Instance.networkManager == null) return 0;
        Instance.dropCounter++;
        return DropIdBase * (int)(Instance.networkManager.LocalClientId + 1) + Instance.dropCounter;
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

    public static void NotifyItemDropped(Item item, ItemDataAsset data, Vector3 position, Vector3 velocity)
    {
        if (Instance == null || item == null || item.NetId == 0 || data == null) return;
        Instance.claimedIds.Remove(item.NetId);
        Instance.Send(new NetEvent
        {
            type = EventType.ItemDrop, id = item.NetId, text = data.name, v1 = position, v2 = velocity
        });

        // 着地点が全員でずれないよう、投げた本人のPCで静止した位置を後から全員へ配る
        if (item.GetComponent<DroppedItemSettler>() == null) item.gameObject.AddComponent<DroppedItemSettler>();
    }

    public static void NotifyItemSettled(Item item, Vector3 position, Vector3 eulerAngles)
    {
        if (Instance == null || item == null || item.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.ItemSettle, id = item.NetId, v1 = position, v2 = eulerAngles });
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

    public static void NotifySlotEnd(SlotMachineController slot)
    {
        if (Instance == null || slot == null || slot.NetId == 0) return;
        Instance.Send(new NetEvent { type = EventType.SlotEnd, id = slot.NetId });
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
                MsgEvent, targetClientId, writer, NetworkDelivery.ReliableSequenced);
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
            case EventType.ItemSettle:
            {
                Item item = FindItem(e.id);
                if (item == null) break;
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
                if (slot != null) slot.SetRemoteBusy(false);
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
        if (pc != null && item.ItemData != null && pc.TryAddItem(item.ItemData))
        {
            Destroy(item.gameObject);
            return;
        }

        // 許可を待つ間にインベントリが埋まった：その場に落とし直して、全員にもう一度見えるようにする
        if (item.ItemData != null)
        {
            NotifyItemDropped(item, item.ItemData, item.transform.position, Vector3.zero);
        }
    }

    private static Item FindItem(int netId)
    {
        foreach (Item item in FindObjectsByType<Item>(FindObjectsSortMode.None))
        {
            if (item.NetId == netId) return item;
        }
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

    private ItemDataAsset ResolveItemData(string assetName)
    {
        if (itemDataByName == null)
        {
            itemDataByName = new Dictionary<string, ItemDataAsset>();
            foreach (ItemDataAsset data in Resources.FindObjectsOfTypeAll<ItemDataAsset>())
            {
                itemDataByName[data.name] = data;
            }
        }
        itemDataByName.TryGetValue(assetName, out ItemDataAsset found);
        return found;
    }

    /// <summary>他プレイヤーが落とした／投げたアイテムを、こちらにも出す（PlayerController.DropSelectedInventoryItem と同じ作り）</summary>
    private void SpawnRemoteDroppedItem(NetEvent e)
    {
        if (FindItem(e.id) != null) return;

        ItemDataAsset data = ResolveItemData(e.text);
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

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.linearVelocity = e.v2;
    }
}
