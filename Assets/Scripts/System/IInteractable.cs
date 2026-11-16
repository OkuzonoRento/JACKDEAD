using UnityEngine;

public interface IInteractable
{
    // 視点を合わせたときにUIに表示するプロンプト名
    string GetInteractPrompt();

    // 長押しが必要な時間（クリックですぐ反応なら 0f や短めの時間）
    float GetHoldTime();

    // Fキー長押しが完了した（または押された）ときに実行される処理
    void OnInteract();

    // 条件付きでインタラクトできるかどうか
    bool CanInteract();
}