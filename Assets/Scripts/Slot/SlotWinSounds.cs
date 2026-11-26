using UnityEngine;

/// <summary>
/// スロットの効果音。波形は SlotSynth がコードで作る（一度作ったら使い回す）。
/// 素材の音に差し替えるには、Assets/Resources/SlotSounds に、SlotSynth.Sound と同じ名前の音声ファイルを置く
/// （例: StartEerie.wav、ReelStop.mp3）。置いた音は、作った音の代わりに使う。
/// </summary>
public static class SlotWinSounds
{
    private static AudioClip[] clips;

    public static AudioClip Get(SlotSynth.Sound sound)
    {
        if (clips == null) clips = new AudioClip[System.Enum.GetValues(typeof(SlotSynth.Sound)).Length];

        int index = (int)sound;
        if (clips[index] == null) clips[index] = Resources.Load<AudioClip>("SlotSounds/" + sound);
        if (clips[index] == null)
        {
            float[] data = SlotSynth.Build(sound);
            clips[index] = AudioClip.Create("Slot_" + sound, data.Length, 1, SlotSynth.SampleRate, false);
            clips[index].SetData(data, 0);
        }
        return clips[index];
    }
}
