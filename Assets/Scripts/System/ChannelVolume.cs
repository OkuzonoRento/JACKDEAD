using UnityEngine;

/// <summary>
/// AudioSource に付けると、設定画面の BGM / SE 音量に合わせて音量が変わる。
/// ・BGM 用の AudioSource には Channel = Bgm、効果音用には Se を選ぶ
/// ・インスペクターで決めた元の音量（Base Volume）に、設定の音量を掛けた値が実際の音量になる
/// ・コードから PlayOneShot するときは SettingsManager.SeVolume を volumeScale に渡す
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ChannelVolume : MonoBehaviour
{
    public enum Channel { Bgm, Se }

    public Channel channel = Channel.Bgm;

    private AudioSource source;
    private float baseVolume;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        baseVolume = source.volume;
    }

    private void Update()
    {
        float setting = channel == Channel.Bgm ? SettingsManager.BgmVolume : SettingsManager.SeVolume;
        source.volume = baseVolume * setting;
    }
}
