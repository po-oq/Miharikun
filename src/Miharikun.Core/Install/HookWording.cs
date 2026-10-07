namespace Miharikun.Core.Install;

/// <summary>Hook の案内の文のうち、OS で言い方が違う所（要件 12.12）。</summary>
public static class HookWording
{
    /// <summary>「Hook exe」（Windows）／「Hook」（mac）。</summary>
    public static string Noun => OperatingSystem.IsWindows() ? "Hook exe" : "Hook";

    /// <summary>同梱の Hook を置く場所の言い方。</summary>
    public static string BundledPlace => OperatingSystem.IsWindows() ? "Miharikun.exe と同じフォルダ" : "Miharikun.app の中";

    /// <summary>「Hook なし」の警告の帯の文（要件 12.11・12.12）。</summary>
    public static string NoHookWarning(int count) => OperatingSystem.IsWindows()
        ? $"⚠ Cursor の Hook が記録していません（Hook なし {count} 件）。この PC で Hook exe の実行が止められている可能性があります。⚙ →「設定…」で置き場所を変えてください。"
        : $"⚠ Cursor の Hook が記録していません（Hook なし {count} 件）。この Mac で Hook の実行が止められている可能性があります（「隔離」の印など。8.1）。⚙ →「Hook を導入」でもう一度導入するか、⚙ →「設定…」で置き場所を変えてください。";

    /// <summary>設定画面の「Hook の置き場所」の欄の名前。</summary>
    public static string PlacementLabel => OperatingSystem.IsWindows() ? "Cursor の Hook exe の置き場所" : "Cursor の Hook の置き場所";

    /// <summary>置き場所が完全なパスでないときの誤りの文。</summary>
    public static string NotAbsoluteMessage => OperatingSystem.IsWindows()
        ? "C:\\ から始まるフォルダを指定してください"
        : "/ から始まるフォルダを指定してください";
}
