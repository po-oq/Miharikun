using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Miharikun.Core.Storage;

namespace Miharikun.Core.Install;

public enum HookInstallState
{
    /// <summary>全イベントに登録済みで、Hook exe も最新。</summary>
    Installed,
    NotInstalled,
    /// <summary>一部のイベントだけ登録されている、または登録先のパスが今の場所と違う。</summary>
    Partial,
    /// <summary>登録は揃っているが、導入先の Hook exe が無い、または同梱のものと違う（更新）。</summary>
    ExeOutdated,
    /// <summary>hooks.json を読めない・想定外の形。何も変更しない。</summary>
    Unreadable,
}

public sealed record HookInstallResult(bool Success, string Message, string? BackupPath = null);

/// <summary>
/// Cursor の hooks.json への導入・削除（要件 8章）。既存の設定は壊さずにマージし、自分のエントリだけを足す／外す。
/// Cursor 専用（別のエージェントが hook を使うようになったら、抽象化する）。
/// 変更するときは必ず hooks.json.bak-{日時} にバックアップしてから、アトミックに書き込む。
/// </summary>
public sealed class HookInstaller
{
    /// <summary>Hook の名前から拡張子を除いたもの。<see cref="IsOurs"/> は両 OS の名前に当たるよう、これで見る。</summary>
    public const string HookStemName = "Miharikun.Hook";

    /// <summary>Hook の実行ファイル名（Windows は <c>Miharikun.Hook.exe</c>、mac は拡張子なしの <c>Miharikun.Hook</c>）。</summary>
    public static readonly string HookExeName = OperatingSystem.IsWindows() ? HookStemName + ".exe" : HookStemName;
    public const string CursorDirEnvVar = "MIHARIKUN_CURSOR_DIR";
    private const int TimeoutSeconds = 5;

    /// <summary>登録するイベント（要件 7章）。</summary>
    public static readonly IReadOnlyList<string> Events =
    [
        "sessionStart", "sessionEnd", "beforeSubmitPrompt", "preToolUse", "postToolUse", "postToolUseFailure",
        "subagentStart", "subagentStop", "afterFileEdit", "afterAgentResponse", "afterAgentThought", "preCompact", "stop",
    ];

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _hooksJsonPath;
    private readonly string _installedExePath;
    private readonly string? _bundledExePath;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _createExeDirectory;

    /// <param name="createExeDirectory">導入先のフォルダが無いときに作るか。設定した置き場所（hookDir）は作らない（要件 8.2）。</param>
    public HookInstaller(string hooksJsonPath, string installedExePath, string? bundledExePath, Func<DateTimeOffset>? clock = null,
        bool createExeDirectory = true)
    {
        _hooksJsonPath = hooksJsonPath;
        _installedExePath = installedExePath;
        _bundledExePath = bundledExePath;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _createExeDirectory = createExeDirectory;
    }

    /// <summary>
    /// 既定の場所：hooks.json は %USERPROFILE%\.cursor\（MIHARIKUN_CURSOR_DIR で上書き可）、
    /// 導入先は <paramref name="hookDir"/>（settings.json の hookDir。null なら %LOCALAPPDATA%\Miharikun\bin\）、
    /// 同梱の Hook exe は App と同じフォルダ。設定した置き場所はフォルダを作らない。
    /// </summary>
    public static HookInstaller CreateDefault(AppPaths paths, string appDirectory, string? hookDir = null)
    {
        var cursorDir = ResolveCursorDir();

        var bundled = Path.Combine(appDirectory, HookExeName);
        return new HookInstaller(
            Path.Combine(cursorDir, "hooks.json"),
            Path.Combine(hookDir ?? DefaultHookDir(paths), HookExeName),
            File.Exists(bundled) ? bundled : null,
            createExeDirectory: hookDir is null);
    }

    /// <summary>Hook exe の既定の置き場所（%LOCALAPPDATA%\Miharikun\bin）。</summary>
    public static string DefaultHookDir(AppPaths paths) => Path.Combine(paths.Root, "bin");

    /// <summary>
    /// settings.json の hookDir に保存する値。正規化（<c>GetFullPath</c> ＋末尾の区切りを落とす）して、
    /// 既定の場所と同じ（大文字小文字は無視）なら null（キーを消す）。起動時の受け入れと設定画面の保存で共通。
    /// </summary>
    public static string? ToSettingValue(string dir, AppPaths paths)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        var defaultDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(DefaultHookDir(paths)));
        return normalized.Equals(defaultDir, StringComparison.OrdinalIgnoreCase) ? null : normalized;
    }

    /// <summary>Cursor の設定フォルダ。既定は %USERPROFILE%.cursor（MIHARIKUN_CURSOR_DIR で上書き可）。</summary>
    public static string ResolveCursorDir()
    {
        var cursorDir = Environment.GetEnvironmentVariable(CursorDirEnvVar);
        return string.IsNullOrWhiteSpace(cursorDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cursor")
            : cursorDir;
    }

    public string HooksJsonPath => _hooksJsonPath;
    public string InstalledExePath => _installedExePath;

    /// <summary>同梱の Hook exe が見つかるか。無ければ導入できない。</summary>
    public bool CanInstall => _bundledExePath is not null;

    /// <summary>hooks.json に書くコマンド。フルパスを / 区切りにし、空白を含むときは引用符で囲む（空白の扱いは Step 0 で要確認）。</summary>
    public static string BuildCommand(string exePath)
    {
        var path = Path.GetFullPath(exePath);
        if (OperatingSystem.IsWindows())
            path = path.Replace('\\', '/');   // mac の \ はふつうのファイル名の文字なので、替えない
        return (path.Contains(' ') ? $"\"{path}\"" : path) + " --agent cursor";
    }

    /// <summary>自分のエントリか。導入先が移動して古いパスになっていても見分けられるよう、exe 名で判定する。</summary>
    public static bool IsOurs(string? command) =>
        command is not null && command.Contains(HookStemName, StringComparison.OrdinalIgnoreCase);

    private static bool IsHookFileName(string name) =>
        name.Equals(HookStemName, StringComparison.OrdinalIgnoreCase) || name.Equals(HookStemName + ".exe", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- 登録の読み取り（Issue #17。要件 8.2）

    /// <summary>
    /// hooks.json のコマンドから Hook exe のフルパスを取り出す（<see cref="BuildCommand"/> の逆）。
    /// 先頭が引用符なら次の引用符まで、そうでなければ最初の「 --agent」の手前まで（空白入りのパスを引用符なしで手書きしても拾う）。
    /// 完全なパスでない（相対パス）・使えない文字・末尾が Hook exe の名前でないときは null。
    /// </summary>
    public static string? TryParseExePath(string? command) => TrySplit(command, out var path, out _) ? path : null;

    /// <summary>
    /// コマンドが <paramref name="exePath"/> を指し、パスの後ろに <c>--agent cursor</c> が続けて並ぶか。
    /// 区切り（/ と \）・引用符・大文字小文字・空白の数の違いは問わない（Hook は引数が無いと何も記録しない）。
    /// </summary>
    public static bool SameRegistration(string? command, string exePath)
    {
        if (!TrySplit(command, out var path, out var args))
            return false;
        if (!path.Equals(NormalizeFull(exePath), StringComparison.OrdinalIgnoreCase))
            return false;
        return HasAgentCursor(args);
    }

    private static bool HasAgentCursor(string args)
    {
        var tokens = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i] == "--agent" && tokens[i + 1] == "cursor")
                return true;
        }
        return false;
    }

    private static string? NormalizeFullOrNull(string path)
    {
        try { return NormalizeFull(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string NormalizeFull(string path) => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

    private static bool TrySplit(string? command, out string path, out string args)
    {
        path = args = "";
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var text = command.Trim();
        string rawPath;
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close < 0)
                return false;
            rawPath = text[1..close];
            args = text[(close + 1)..];
        }
        else
        {
            var agent = text.IndexOf(" --agent", StringComparison.Ordinal);
            rawPath = agent < 0 ? text : text[..agent];
            args = agent < 0 ? "" : text[agent..];
        }

        rawPath = rawPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        if (rawPath.Length == 0 || rawPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(rawPath))
            return false;
        if (NormalizeFullOrNull(rawPath) is not { } full
            || !IsHookFileName(Path.GetFileName(full)))
            return false;

        path = full;
        return true;
    }

    /// <summary>
    /// hooks.json の Miharikun の登録が、登録する全イベントで同じ 1 つの別の場所を指し、その exe が実在するときの exe のフルパス
    /// （要件 8.2 の受け入れ。条件は計画 7.1 の表）。設定の場所と同じ・条件に合わない（引数なし・場所がばらばら・exe なし・一部だけ）ときは null。
    /// </summary>
    public string? FindRegisteredElsewhere()
    {
        if (!TryLoad(out var root, out _))
            return null;

        string? found = null;
        foreach (var evt in Events)
        {
            if (FindArray(root, evt) is not { } array)
                return null;
            var mine = array.OfType<JsonObject>().Where(IsOurEntry).ToList();
            if (mine.Count == 0)
                return null;

            foreach (var entry in mine)
            {
                var command = (string?)entry["command"];
                if (!TrySplit(command, out var path, out var args) || !HasAgentCursor(args))
                    return null;
                if (found is not null && !found.Equals(path, StringComparison.OrdinalIgnoreCase))
                    return null;
                found = path;
            }
        }

        if (found is null || found.Equals(NormalizeFullOrNull(_installedExePath), StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(found) ? found : null;
    }

    // ---------------------------------------------------------------- 状態

    public HookInstallState GetState()
    {
        if (!TryLoad(out var root, out _))
            return HookInstallState.Unreadable;

        int exact = 0, ours = 0;
        foreach (var evt in Events)
        {
            if (FindArray(root, evt) is not { } array)
                continue;
            var mine = array.OfType<JsonObject>().Where(IsOurEntry).ToList();
            if (mine.Count > 0) ours++;
            if (mine.Any(e => SameRegistration((string?)e["command"], _installedExePath))) exact++;
        }

        if (ours == 0) return HookInstallState.NotInstalled;
        if (exact < Events.Count) return HookInstallState.Partial;
        return ExeIsCurrent() ? HookInstallState.Installed : HookInstallState.ExeOutdated;
    }

    private bool ExeIsCurrent()
    {
        if (!File.Exists(_installedExePath))
            return false;
        // 同梱が無い（開発時など）なら、導入済みのものをそのまま使う。
        return _bundledExePath is null || SameContent(_bundledExePath, _installedExePath);
    }

    // ---------------------------------------------------------------- 導入

    public HookInstallResult Install()
    {
        if (_bundledExePath is null)
            return new(false, $"{HookExeName} が見つかりません（{HookWording.BundledPlace}に置いてください）。");
        if (!_createExeDirectory && Path.GetDirectoryName(_installedExePath) is { } exeDir && !Directory.Exists(exeDir))
            return new(false, $"Hook の置き場所 {exeDir} が見つかりません（⚙ → 設定… で直してください）。");
        if (!TryLoad(out var root, out var error))
            return new(false, $"hooks.json を読めないため、何も変更しませんでした。\n{error}");

        var command = BuildCommand(_installedExePath);
        var hooks = EnsureObject(root, "hooks", out var shapeError);
        if (hooks is null)
            return new(false, shapeError!);

        var changed = false;
        if (root["version"] is null)
        {
            root["version"] = 1;
            changed = true;
        }

        foreach (var evt in Events)
        {
            var array = EnsureArray(hooks, evt, out shapeError);
            if (array is null)
                return new(false, shapeError!);

            var mine = array.OfType<JsonObject>().Where(IsOurEntry).ToList();
            if (mine.Count == 0)
            {
                JsonNode entry = new JsonObject { ["command"] = command, ["timeout"] = TimeoutSeconds };
                array.Add(entry);   // Add<T> ではなく JsonNode 版を使う（AOT 互換）
                changed = true;
                continue;
            }

            // 重複は追加しない。パスが古ければ今の場所に直し、同じものが複数あれば1つにする。
            var first = mine[0];
            if ((string?)first["command"] != command)
            {
                first["command"] = command;
                changed = true;
            }
            if (first["timeout"] is null)
            {
                first["timeout"] = TimeoutSeconds;
                changed = true;
            }
            foreach (var duplicate in mine.Skip(1))
            {
                array.Remove(duplicate);
                changed = true;
            }
        }

        try
        {
            // hooks が指す先の exe を先に用意する。
            var copied = CopyExe();
            string? backup = null;
            if (changed)
            {
                backup = Backup();
                AtomicFile.WriteAllText(_hooksJsonPath, Serialize(root));
            }

            if (!changed && !copied)
                return new(true, "すでに導入済みです（変更はありません）。");

            return new(true,
                (changed ? "hooks.json に登録しました。" : "Hook exe を更新しました。") +
                (backup is null ? "" : $"\nバックアップ: {backup}"),
                backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"導入に失敗しました: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- 削除

    /// <summary>自分のエントリだけを取り除く。Hook exe とイベントのデータは残す。</summary>
    public HookInstallResult Uninstall()
    {
        if (!File.Exists(_hooksJsonPath))
            return new(true, "hooks.json がないので、削除するものはありません。");
        if (!TryLoad(out var root, out var error))
            return new(false, $"hooks.json を読めないため、何も変更しませんでした。\n{error}");

        if (root["hooks"] is not JsonObject hooks)
            return new(true, "Miharikun の hook は登録されていません。");

        var removed = 0;
        foreach (var evt in hooks.Select(p => p.Key).ToList())
        {
            if (hooks[evt] is not JsonArray array)
                continue;

            var mine = array.OfType<JsonObject>().Where(IsOurEntry).ToList();
            if (mine.Count == 0)
                continue;

            foreach (var entry in mine)
                array.Remove(entry);
            removed += mine.Count;

            // 自分のエントリを外して空になった配列は、キーごと消す（導入前に無かった配列を残さず、往復で元の設定に戻すため）。
            // 自分のエントリを含まない配列（もともと空のものを含む）は、上の continue で触らない。
            if (array.Count == 0)
                hooks.Remove(evt);
        }

        if (removed == 0)
            return new(true, "Miharikun の hook は登録されていません。");

        try
        {
            var backup = Backup();
            AtomicFile.WriteAllText(_hooksJsonPath, Serialize(root));
            return new(true, $"hooks.json から Miharikun の hook を外しました（{removed}件）。\nバックアップ: {backup}", backup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"削除に失敗しました: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- 内部

    private static bool IsOurEntry(JsonObject entry) => entry["command"] is JsonValue v && v.TryGetValue<string>(out var s) && IsOurs(s);

    private static JsonArray? FindArray(JsonObject root, string evt) =>
        root["hooks"] is JsonObject hooks && hooks[evt] is JsonArray array ? array : null;

    private static JsonObject? EnsureObject(JsonObject parent, string name, out string? error)
    {
        error = null;
        if (parent[name] is null)
            parent[name] = new JsonObject();
        if (parent[name] is JsonObject obj)
            return obj;
        error = $"hooks.json の \"{name}\" がオブジェクトではないため、何も変更しませんでした。";
        return null;
    }

    private static JsonArray? EnsureArray(JsonObject parent, string name, out string? error)
    {
        error = null;
        if (parent[name] is null)
            parent[name] = new JsonArray();
        if (parent[name] is JsonArray array)
            return array;
        error = $"hooks.json の hooks.{name} が配列ではないため、何も変更しませんでした。";
        return null;
    }

    /// <summary>無ければ空のオブジェクト。壊れている・オブジェクトでないときは false。</summary>
    private bool TryLoad(out JsonObject root, out string? error)
    {
        root = new JsonObject();
        error = null;
        if (!File.Exists(_hooksJsonPath))
            return true;

        string text;
        try
        {
            text = File.ReadAllText(_hooksJsonPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
            return true;

        try
        {
            // コメントや末尾カンマは許さない（書き戻すと失われるため、読めないものは触らない）。
            if (JsonNode.Parse(text) is JsonObject parsed)
            {
                root = parsed;
                return true;
            }
            error = "最上位がオブジェクトではありません。";
        }
        catch (JsonException ex)
        {
            error = ex.Message;
        }
        return false;
    }

    private bool CopyExe()
    {
        if (_bundledExePath is null || (File.Exists(_installedExePath) && SameContent(_bundledExePath, _installedExePath)))
            return false;

        if (_createExeDirectory)
            Directory.CreateDirectory(Path.GetDirectoryName(_installedExePath)!);
        // Cursor が hook を実行している最中は上書きできないことがあるので、少し待ってやり直す。
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    File.Copy(_bundledExePath, _installedExePath, overwrite: true);
                    return true;
                }

                // mac：同じフォルダの一時ファイルに写してから名前を付け替える（新しいファイルになる）。署名つきの実行ファイルを
                // 同じ場所で上書きすると次の起動で止められることがあり、Cursor が Hook を動かしている最中の書き換えも避けられる（計画 7.12）。
                var temp = _installedExePath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
                try
                {
                    File.Copy(_bundledExePath, temp, overwrite: true);
                    File.Move(temp, _installedExePath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                return true;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private string? Backup()
    {
        if (!File.Exists(_hooksJsonPath))
            return null;

        var backup = $"{_hooksJsonPath}.bak-{_clock():yyyyMMdd-HHmmss}";
        File.Copy(_hooksJsonPath, backup, overwrite: true);
        return backup;
    }

    private static string Serialize(JsonObject root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            root.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray()) + Environment.NewLine;
    }

    private static bool SameContent(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);
        if (infoA.Length != infoB.Length)
            return false;
        return Hash(a).AsSpan().SequenceEqual(Hash(b));
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }
}