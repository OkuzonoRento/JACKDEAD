using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>
/// Vivox のパッケージ（com.unity.services.vivox）が入っているときだけ、スクリプトの条件 JD_VIVOX を付ける。
/// ProximityVoice（近接ボイスチャット）は、この条件が付いたときだけ本物になる。
/// パッケージが無いときは空の実装になるので、パッケージが無くてもプロジェクトはコンパイルできる。
/// Package Manager で Vivox を入れる（または外す）と、自動で付く（外れる）。
/// </summary>
[InitializeOnLoad]
public static class VivoxDefineSetter
{
    private const string Define = "JD_VIVOX";
    private const string PackageName = "com.unity.services.vivox";

    static VivoxDefineSetter()
    {
        EditorApplication.delayCall += Check;
        Events.registeredPackages += args => Check();
    }

    private static void Check()
    {
        bool installed = false;
        foreach (UnityEditor.PackageManager.PackageInfo p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
        {
            if (p.name == PackageName) { installed = true; break; }
        }

        bool changed = false;
        foreach (NamedBuildTarget group in new[] { NamedBuildTarget.Standalone })
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(group);
            List<string> list = new List<string>(current.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            bool has = list.Contains(Define);
            if (installed && !has) { list.Add(Define); changed = true; }
            else if (!installed && has) { list.Remove(Define); changed = true; }
            else continue;
            PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list));
        }
        if (changed) Debug.Log("[Vivox] " + (installed ? "Vivox を検出したので、近接ボイスチャットを有効にしました。" : "Vivox が無いので、近接ボイスチャットを無効にしました。"));
    }
}
