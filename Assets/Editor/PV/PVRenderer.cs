using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

/// <summary>
/// PV（約60秒の紹介映像）を、Editor の中で1コマずつ描いて、音つきの MP4 に書き出す。再生（Play）はしない。
///   メニュー JackDead/PV/PVを書き出す       → Recordings/JACKDEAD_PV.mp4
///   メニュー JackDead/PV/静止画で確認する   → Recordings/preview/sheet.png
/// 書き出しの間は SampleScene を開いて一時的に組み替える。シーンは保存せず（保存も止める）、終わったら元の開き方に戻す。
/// GPU のメモリがたまらないように、エディタの更新ごとに数コマずつ描く（一度に全部描くと、エディタが落ちる）。
/// 外部から頼むときは Recordings/pv_request.txt に「full」または「preview 秒 秒 …」と書く。
/// </summary>
[InitializeOnLoad]
public static class PVRenderer
{
    public const int Width = 1920;
    public const int Height = 1080;
    public const int Fps = 30;

    private const string GameScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDir = "Recordings";
    private const string RequestPath = OutputDir + "/pv_request.txt";
    private const string StatusPath = OutputDir + "/pv_status.txt";
    private const string MoviePath = OutputDir + "/JACKDEAD_PV.mp4";
    private const string ProgressPath = OutputDir + "/pv_progress.txt";

    private const int FramesPerTick = 2;
    /// <summary>書き出し中に、描画用のメモリがこれ以上増えたら、落ちる前にやめる</summary>
    private const long MemoryLimitBytes = 3L * 1024 * 1024 * 1024;

    private static double nextPoll;
    private static IEnumerator job;

    public static bool IsRunning => job != null;

    static PVRenderer()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Abort;
    }

    private static void Tick()
    {
        if (job != null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Abort(); Report("NG: 再生が始まったので中止しました。"); return; }
            try
            {
                if (!job.MoveNext()) job = null;
            }
            catch (Exception e)
            {
                job = null; // 例外が出た時点で、後片づけ（finally）は済んでいる
                Debug.LogException(e);
                Report("NG: " + e);
            }
            InternalEditorUtility.RepaintAllViews(); // 1コマ分の描画を終わらせて、使い終わったメモリを返す
            return;
        }

        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1.0;
        if (!File.Exists(RequestPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        string request = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        string[] parts = request.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        if (parts[0] == "full") Run(null);
        else Run(parts.Skip(1).Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray());
    }

    [MenuItem("JackDead/PV/PVを書き出す")]
    private static void RenderMovie() { Run(null); }

    [MenuItem("JackDead/PV/静止画で確認する")]
    private static void RenderPreview()
    {
        Run(new[] { 3.5f, 8.5f, 13f, 18f, 23.5f, 28f, 33.5f, 37f, 41f, 45f, 49f, 53.5f, 58f });
    }

    [MenuItem("JackDead/PV/書き出しを中止する")]
    private static void Abort()
    {
        if (job == null) return;
        IEnumerator running = job;
        job = null;
        (running as IDisposable)?.Dispose(); // 後片づけ（finally）を実行する
    }

    /// <summary>previewTimes が null なら動画、それ以外はその秒の静止画を並べた画像を書き出す</summary>
    private static void Run(float[] previewTimes)
    {
        if (job != null) { Debug.LogWarning("[PV] いま書き出し中です。"); return; }
        Directory.CreateDirectory(OutputDir);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (!SceneManager.GetSceneAt(i).isDirty) continue;
            Report("NG: 未保存のシーンがあります。保存してから、もう一度実行してください。");
            return;
        }

        job = Job(previewTimes);
    }

    private static IEnumerator Job(float[] previewTimes)
    {
        SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        PVDirector director = null;
        PVComposer composer = null;
        int progress = Progress.Start("PVを書き出し中");
        AssetDatabase.DisallowAutoRefresh();

        try
        {
            EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            composer = new PVComposer();
            director = new PVDirector(composer);
            director.Build();
            yield return null;

            IEnumerator work = previewTimes == null
                ? WriteMovie(director, composer, progress)
                : WritePreview(director, composer, previewTimes);
            try
            {
                while (work.MoveNext()) yield return null;
            }
            finally
            {
                (work as IDisposable)?.Dispose(); // 途中でやめても、動画のファイルを閉じる
            }
        }
        finally
        {
            Progress.Remove(progress);
            if (director != null) director.Dispose();
            if (composer != null) composer.Dispose();
            UnityEngine.Random.state = randomState;

            // 組み替えたシーンは保存せずに捨てて、元の開き方に戻す
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (setup != null && setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else
                EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            AssetDatabase.AllowAutoRefresh();
        }
    }

    private static IEnumerator WriteMovie(PVDirector director, PVComposer composer, int progress)
    {
        int frames = Mathf.RoundToInt(PVDirector.Length * Fps);
        float[] audio = PVAudio.Build(PVDirector.Length);
        int samplesPerFrame = PVAudio.SampleRate / Fps;

        VideoTrackAttributes video = new VideoTrackAttributes
        {
            frameRate = new MediaRational(Fps),
            width = Width,
            height = Height,
            includeAlpha = false,
            bitRateMode = VideoBitrateMode.High
        };
        AudioTrackAttributes sound = new AudioTrackAttributes
        {
            sampleRate = new MediaRational(PVAudio.SampleRate),
            channelCount = 2,
            language = "ja"
        };

        long baseMemory = Profiler.GetAllocatedMemoryForGraphicsDriver();
        using (MediaEncoder encoder = new MediaEncoder(Path.GetFullPath(MoviePath), video, sound))
        using (NativeArray<float> buffer = new NativeArray<float>(samplesPerFrame * 2, Allocator.Persistent))
        {
            NativeArray<float> samples = buffer;
            for (int f = 0; f < frames; f++)
            {
                director.Evaluate(f / (float)Fps);
                encoder.AddFrame(composer.Render(director.SceneCamera));
                NativeArray<float>.Copy(audio, f * samplesPerFrame * 2, samples, 0, samples.Length);
                encoder.AddSamples(samples);

                if (f % 90 == 0)
                {
                    long used = Profiler.GetAllocatedMemoryForGraphicsDriver();
                    Progress.Report(progress, f / (float)frames, $"{f}/{frames} コマ");
                    Report($"進行中 {f}/{frames} 描画メモリ {used >> 20} MB（開始時 {baseMemory >> 20} MB）", ProgressPath);
                    if (used - baseMemory > MemoryLimitBytes) throw new Exception($"描画のメモリが増えすぎたので中止しました（{f}コマ目）。");
                }
                if (f % FramesPerTick == FramesPerTick - 1) yield return null;
            }
        }

        Report("OK: " + Path.GetFullPath(MoviePath));
    }

    private static IEnumerator WritePreview(PVDirector director, PVComposer composer, float[] times)
    {
        const int tw = 640, th = 360, columns = 3;
        int rows = Mathf.CeilToInt(times.Length / (float)columns);
        Texture2D sheet = new Texture2D(tw * columns, th * rows, TextureFormat.RGBA32, false);
        sheet.SetPixels32(new Color32[sheet.width * sheet.height]);
        RenderTexture small = new RenderTexture(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        for (int i = 0; i < times.Length; i++)
        {
            director.Evaluate(times[i]);
            Texture2D frame = composer.Render(director.SceneCamera);
            Graphics.Blit(frame, small);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = small;
            sheet.ReadPixels(new Rect(0, 0, tw, th), (i % columns) * tw, (rows - 1 - i / columns) * th);
            RenderTexture.active = previous;
            yield return null;
        }

        sheet.Apply();
        Directory.CreateDirectory(OutputDir + "/preview");
        File.WriteAllBytes(OutputDir + "/preview/sheet.png", sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        small.Release();
        UnityEngine.Object.DestroyImmediate(small);
        Report("OK: preview " + string.Join(" ", times.Select(t => t.ToString(CultureInfo.InvariantCulture))) + "\n" + director.Summary);
    }

    private static void Report(string message, string path = StatusPath)
    {
        Directory.CreateDirectory(OutputDir);
        File.WriteAllText(path, DateTime.Now.ToString("HH:mm:ss") + " " + message);
        if (path == StatusPath) Debug.Log("[PV] " + message);
    }
}

/// <summary>PV の書き出し中は、組み替えたシーンが保存されないようにする</summary>
public class PVSaveGuard : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (!PVRenderer.IsRunning) return paths;
        Debug.LogWarning("[PV] 書き出し中は、シーンを保存しません。");
        return paths.Where(p => !p.EndsWith(".unity")).ToArray();
    }
}
