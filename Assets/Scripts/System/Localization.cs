using System;
using UnityEngine;

/// <summary>表示言語。設定画面の LANGUAGE スライダーの値（0 / 1）と対応する</summary>
public enum GameLanguage
{
    Japanese = 0,
    English = 1,
}

/// <summary>
/// 言語設定の保持と、文字列の切り替え。
/// 使い方: Localization.T("再開", "Resume")  → 現在の言語に合わせた文字列を返す。
/// 画面の文字を後から切り替えたいときは LanguageChanged を購読する。
/// </summary>
public static class Localization
{
    public const string KEY_LANGUAGE = "Language";

    public static event Action LanguageChanged;

    private static GameLanguage? cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cached = null;
        LanguageChanged = null;
    }

    public static GameLanguage Current
    {
        get
        {
            if (!cached.HasValue)
            {
                // 初回起動時は OS の言語に合わせる（日本語 OS なら日本語、それ以外は英語）
                int def = Application.systemLanguage == SystemLanguage.Japanese ? 0 : 1;
                cached = (GameLanguage)Mathf.Clamp(PlayerPrefs.GetInt(KEY_LANGUAGE, def), 0, 1);
            }
            return cached.Value;
        }
        set
        {
            if (cached.HasValue && cached.Value == value) return;
            cached = value;
            PlayerPrefs.SetInt(KEY_LANGUAGE, (int)value);
            LanguageChanged?.Invoke();
        }
    }

    public static bool IsEnglish => Current == GameLanguage.English;

    /// <summary>現在の言語に合わせて日本語 / 英語の文字列を選ぶ</summary>
    public static string T(string ja, string en)
    {
        return Current == GameLanguage.English ? en : ja;
    }
}
