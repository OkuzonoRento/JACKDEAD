using UnityEngine;

/// <summary>
/// サンタ（敵）の音。重い足音とそりの鈴（歩くほど鳴るので、近づいてくるのが分かる）、
/// 見つけたときの笑い声、うろついている間の低い含み笑い、投げる前のうなりと、投げる音。
/// 敵の動き（EnemyBrain.MotionState）と位置だけを見るので、マルチプレイの参加者の画面でも同じように鳴る。
/// EnemyBrain が自動で付ける。
/// </summary>
public class EnemyAudio : MonoBehaviour
{
    private const int Run = 2;
    private const int Throw = 3;
    private const float LaughInterval = 9f;
    private const float ThrowMinWindup = 0.55f; // これより短い構えは、中断されたとみなして、投げる音を出さない

    private EnemyBrain brain;
    private Vector3 lastPosition;
    private float travelled;
    private int lastState;
    private float stateTime;
    private float lastLaugh = -100f;
    private float nextChuckle;
    private bool otherFoot;

    private void Start()
    {
        brain = GetComponent<EnemyBrain>();
        lastPosition = transform.position;
        nextChuckle = Time.time + Random.Range(6f, 20f);
    }

    private void Update()
    {
        if (brain == null || Time.deltaTime <= 0f) return;

        Vector3 mouth = transform.position + Vector3.up * 1.6f;
        int state = brain.MotionState;
        stateTime += Time.deltaTime;

        // 足音と鈴：進んだ距離で鳴らす
        Vector3 moved = transform.position - lastPosition;
        lastPosition = transform.position;
        moved.y = 0f;
        float distance = moved.magnitude;
        if (distance < 2f && distance / Time.deltaTime > 0.4f) // 大きく動いたのは、瞬間移動（最初の位置合わせなど）
        {
            bool running = state == Run;
            travelled += distance;
            if (travelled >= (running ? 1.7f : 1.1f))
            {
                travelled = 0f;
                otherFoot = !otherFoot;
                GameAudio.PlayAt(otherFoot ? GameSound.EnemyStep1 : GameSound.EnemyStep2, transform.position,
                    running ? 1f : 0.75f, Random.Range(0.92f, 1.06f), running ? 28f : 20f);
                if (otherFoot) // 鈴は2歩に1回（鳴り続けると、うるさい）
                    GameAudio.PlayAt(GameSound.Bells, mouth, running ? 0.3f : 0.2f, Random.Range(0.85f, 1.1f), running ? 28f : 20f);
            }
        }

        // 声
        if (state != lastState)
        {
            if (state == Run && lastState != Throw && Time.time - lastLaugh > LaughInterval)
            {
                lastLaugh = Time.time;
                GameAudio.PlayAt(GameSound.EnemyLaugh, mouth, 1f, Random.Range(0.94f, 1.05f), 38f);
            }
            if (state == Throw) GameAudio.PlayAt(GameSound.EnemyGrunt, mouth, 1f, Random.Range(0.92f, 1.08f), 30f);
            else if (lastState == Throw && stateTime >= ThrowMinWindup) GameAudio.PlayAt(GameSound.EnemyThrow, mouth, 1f, 1f, 30f);
            lastState = state;
            stateTime = 0f;
        }

        if (Time.time >= nextChuckle)
        {
            nextChuckle = Time.time + Random.Range(16f, 32f);
            if (state < Run) GameAudio.PlayAt(GameSound.EnemyChuckle, mouth, 0.85f, Random.Range(0.9f, 1.05f), 22f);
        }
    }
}
