using System.Collections;
using UnityEngine;

public class SlotMachineController : MonoBehaviour
{
    [Header("カメラ・視点設定")]
    [SerializeField] private Transform playerCameraTransform;
    [SerializeField] private float slotCameraPitch = 45f;
    [SerializeField] private float cameraTransitionSpeed = 10f;

    [Header("リール設定")]
    [SerializeField] private SlotReelScroller[] reels;
    [SerializeField] private int totalSymbols = 5;

    [Header("ゲーム設定（レート管理）")]
    [SerializeField] private float payoutMultiplier = 5.0f; // ★ この台の配当倍率（例: 5倍、10倍などマシンごとに変更可能）

    private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    private int playerMoney = 5000; // プレイヤーの所持金

    public bool HasEnoughMoney(int amount)
    {
        return playerMoney >= amount;
    }

    private float GetSymbolOffset(int symbolIndex)
    {
        return (float)symbolIndex / totalSymbols;
    }

    public void OnPressBetButton(int betAmount)
    {
        if (isPlaying) return;
        StartCoroutine(PlaySlotSequence(betAmount));
    }

    private IEnumerator PlaySlotSequence(int betAmount)
    {
        isPlaying = true;
        playerMoney -= betAmount;
        Debug.Log($"[カジノ] {betAmount}円を賭けてスロットスタート！ (配当レート: {payoutMultiplier}倍)");

        // 1. カメラを上向きに移行
        float elapsed = 0f;
        Quaternion startRot = playerCameraTransform.localRotation;
        Quaternion targetRot = Quaternion.Euler(slotCameraPitch, startRot.eulerAngles.y, 0f);

        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            playerCameraTransform.localRotation = Quaternion.Slerp(startRot, targetRot, elapsed * cameraTransitionSpeed);
            yield return null;
        }
        playerCameraTransform.localRotation = targetRot;

        // 2. ランダムな出目を決定
        int result1 = Random.Range(0, totalSymbols);
        int result2 = Random.Range(0, totalSymbols);
        int result3 = Random.Range(0, totalSymbols);

        // 3. リール回転開始
        foreach (var reel in reels)
        {
            reel.StartRolling(10f);
        }

        // 4. 1リールずつ停止
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(reels[0].StopAt(GetSymbolOffset(result1), 0.8f));

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(reels[1].StopAt(GetSymbolOffset(result2), 1.0f));

        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(reels[2].StopAt(GetSymbolOffset(result3), 1.2f));

        // 5. 勝敗判定（マシンのレートを乗算して配当を計算）
        bool isWin = (result1 == result2 && result2 == result3);
        if (isWin)
        {
            int reward = Mathf.RoundToInt(betAmount * payoutMultiplier);
            playerMoney += reward;
            Debug.Log($"[ JACKPOT! ] 当たり！レート {payoutMultiplier}倍 ➔ {reward}円獲得！ (所持金: {playerMoney}円)");
        }
        else
        {
            Debug.Log("[ ハズレ ] ざんねん！賭け金は没収されます。");
        }

        yield return new WaitForSeconds(1.5f);

        // 6. カメラを元に戻す
        elapsed = 0f;
        startRot = playerCameraTransform.localRotation;
        targetRot = Quaternion.Euler(0f, startRot.eulerAngles.y, 0f);

        while (elapsed < 0.4f)
        {
            elapsed += Time.deltaTime;
            playerCameraTransform.localRotation = Quaternion.Slerp(startRot, targetRot, elapsed * cameraTransitionSpeed);
            yield return null;
        }
        playerCameraTransform.localRotation = targetRot;

        isPlaying = false;
    }
}