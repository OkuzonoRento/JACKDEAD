using UnityEngine;

/// <summary>他のプレイヤー（仲間の姿）の足音。進んだ距離と速さで鳴らす。RemotePlayerSync が仲間の姿に付ける</summary>
public class FootstepAudio : MonoBehaviour
{
    private Vector3 lastPosition;
    private float travelled;

    private void OnEnable()
    {
        lastPosition = transform.position;
        travelled = 0f;
    }

    private void Update()
    {
        if (Time.deltaTime <= 0f) return;
        Vector3 moved = transform.position - lastPosition;
        lastPosition = transform.position;
        float rise = Mathf.Abs(moved.y) / Time.deltaTime;
        moved.y = 0f;
        float distance = moved.magnitude;
        float speed = distance / Time.deltaTime;

        // 瞬間移動・跳んでいる間・ほぼ止まっているときは、鳴らさない
        if (distance > 3f || rise > 2f || speed < 1f) return;

        travelled += distance;
        bool running = speed > 6.5f;
        if (travelled < (running ? 2.3f : 1.8f)) return;
        travelled = 0f;
        GameAudio.PlayStep(speed < 3.5f ? 0.25f : (running ? 0.9f : 0.55f), transform.position + Vector3.down * 0.8f);
    }
}
