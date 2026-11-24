using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// マルチプレイで自分が倒れたとき、まだ生きている仲間を観戦する。
/// ・観戦先は ← → キー、またはマウスの左右クリックで切り替えられる（生きている人が複数いる場合）
/// ・カメラは観戦先の背後から追従する。観戦中は操作・移動はできない
/// ・次のステージへ進むと PlayerController が復活させ、観戦を終える
/// ・生きている人が誰もいなくなったら、PlayerController にゲームオーバーを依頼する
/// PlayerController が倒れたときに追加する。
/// </summary>
public class SpectatorController : MonoBehaviour
{
    private PlayerController player;
    private Camera cam;
    private float savedFov;
    private Transform cameraTransform;
    private Vector3 savedCamLocalPos;
    private Quaternion savedCamLocalRot;

    private readonly List<ulong> aliveIds = new List<ulong>();
    private ulong targetId;
    private bool hasTarget;

    public static SpectatorController Begin(PlayerController owner)
    {
        SpectatorController s = owner.gameObject.AddComponent<SpectatorController>();
        s.player = owner;
        return s;
    }

    private void Start()
    {
        cam = Camera.main;
        if (cam != null)
        {
            cameraTransform = cam.transform;
            savedCamLocalPos = cameraTransform.localPosition;
            savedCamLocalRot = cameraTransform.localRotation;
            savedFov = cam.fieldOfView;
        }
        RefreshAlive();
        if (aliveIds.Count > 0) { targetId = aliveIds[0]; hasTarget = true; }
    }

    /// <summary>観戦を終えて、カメラを元の位置（プレイヤーの目の位置）へ戻す</summary>
    public void End()
    {
        if (cameraTransform != null)
        {
            cameraTransform.localPosition = savedCamLocalPos;
            cameraTransform.localRotation = savedCamLocalRot;
        }
        if (cam != null) cam.fieldOfView = savedFov;
        RemotePlayerSync.SpectatedClientId = -1;
        Destroy(this);
    }

    private void OnDestroy()
    {
        RemotePlayerSync.SpectatedClientId = -1;
    }

    private void RefreshAlive()
    {
        aliveIds.Clear();
        if (RemotePlayerSync.Instance != null) RemotePlayerSync.Instance.GetAliveRemoteIds(aliveIds);
    }

    private void Update()
    {
        RefreshAlive();

        // 生きている人が誰もいない → 全滅。ゲームオーバーへ
        if (aliveIds.Count == 0)
        {
            hasTarget = false;
            player.BeginGameOverFromSpectating();
            return;
        }

        // 観戦先が倒れた／退出した場合は、生きている人の先頭へ
        if (!hasTarget || !aliveIds.Contains(targetId))
        {
            targetId = aliveIds[0];
            hasTarget = true;
        }

        RemotePlayerSync.SpectatedClientId = (long)targetId;

        if (aliveIds.Count > 1 && !PauseMenu.IsOpen)
        {
            int step = 0;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            if ((kb != null && kb.rightArrowKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame)) step = 1;
            if ((kb != null && kb.leftArrowKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame) || (pad != null && pad.leftShoulder.wasPressedThisFrame)) step = -1;

            if (step != 0)
            {
                int index = aliveIds.IndexOf(targetId);
                index = (index + step + aliveIds.Count) % aliveIds.Count;
                targetId = aliveIds[index];
            }
        }
    }

    private void LateUpdate()
    {
        if (cameraTransform == null || !hasTarget || RemotePlayerSync.Instance == null) return;

        // 観戦先の本人のカメラと、位置・向き・視野角を完全に一致させる（同じ映像が見える）
        if (!RemotePlayerSync.Instance.TryGetAvatarView(targetId, out Vector3 pos, out Quaternion rot, out float fov)) return;

        cameraTransform.SetPositionAndRotation(pos, rot);
        if (cam != null) cam.fieldOfView = fov;
    }

    private void OnGUI()
    {
        if (!hasTarget || RemotePlayerSync.Instance == null) return;

        string targetName = RemotePlayerSync.Instance.GetAvatarName(targetId);
        string text = $"観戦中: {targetName}";
        if (aliveIds.Count > 1) text += "\n← → / 左右クリック / LB・RB で観戦先を切り替え";
        text += "\n次のステージで復活します";

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(0f, Screen.height - 160f, Screen.width, 140f), text, style);
    }
}
