using UnityEngine;

/// <summary>
/// プレイヤーを見分けるための ID。名前を変えても、同じプレイヤーとして扱えるようにする。
///
/// ・Steam を使えるとき：Steam ID（"steam:7656..."）。PC を変えても、同じアカウントなら同じ ID になる
/// ・Steam を使えないとき（Steam 版より前、オフライン開発など）：この PC に保存した固有の ID（GUID）
/// ・オフラインでもオンラインでも、同じ ID を使う（どちらで遊んでも、セーブ内のステータスを引き継げる）
///
/// Steam 版へ移るときに、それまでの PC の ID で保存したステータスを失わないよう、
/// 「別名（Alias）」として PC の ID も一緒に持つ。セーブを読むときは、ID → 別名の順に探し、
/// 別名で見つかったときは、ID を Steam ID に書き換える（GameSaveBridge）。
///
/// Steam を使うには：Steamworks.NET（または Facepunch.Steamworks）を入れ、Player Settings の
/// Scripting Define Symbols に STEAMWORKS_NET を足す。Facepunch の場合は、起動後に PlayerIdentity.SetExternalId("steam:" + ID) を呼ぶ。
/// </summary>
public static class PlayerIdentity
{
    private const string PrefsKey = "jd.pcId";

    private static string localId;
    private static string externalId;

    /// <summary>Steam 以外のサービスから、ID を差し込む（例：Facepunch.Steamworks で "steam:" + SteamClient.SteamId）</summary>
    public static void SetExternalId(string id)
    {
        externalId = string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>この PC に保存した固有の ID。Steam を使えないときは、これが ID になる</summary>
    public static string LocalId
    {
        get
        {
            if (!string.IsNullOrEmpty(localId)) return localId;

            string id = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(id))
            {
                id = System.Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(PrefsKey, id);
                PlayerPrefs.Save();
            }
            localId = id;
            return id;
        }
    }

    private static string TryGetSteamId()
    {
        if (!string.IsNullOrEmpty(externalId)) return externalId;
#if STEAMWORKS_NET
        try
        {
            if (SteamManager.Initialized) return "steam:" + Steamworks.SteamUser.GetSteamID().m_SteamID;
        }
        catch (System.Exception) { }
#endif
        return null;
    }

    /// <summary>いまのプレイヤー ID（Steam が使えれば Steam ID、なければ PC の ID）。Steam の準備ができるたびに、最新を返す</summary>
    public static string Id
    {
        get
        {
            string steam = TryGetSteamId();
            return string.IsNullOrEmpty(steam) ? LocalId : steam;
        }
    }

    /// <summary>同じ人の、以前の ID（Steam ID を使っているとき、それまでの PC の ID）。無ければ空</summary>
    public static string Alias
    {
        get
        {
            string id = Id;
            return id == LocalId ? "" : LocalId;
        }
    }
}
