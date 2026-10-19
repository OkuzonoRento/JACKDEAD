public interface IInteractable
{
    string GetInteractPrompt(); // 「[F] 〇〇を拾う」などのテキスト用
    float GetHoldTime();        // 操作に必要な長押し時間
    void OnInteract(PlayerInteraction interactor);
}