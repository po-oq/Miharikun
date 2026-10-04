using Miharikun.Core.Agents;

namespace Miharikun.Core.Meta;

/// <summary>
/// メタの取得と更新（保存つき）。UI スレッドから使う前提で、読み込み済みの値はメモリに保持する。
/// 保存先は、セッションのエージェント（SessionKey.AgentId）ごとの MetaStore（meta\{agent}\）に切り替える。
/// </summary>
public sealed class SessionMetaService(Func<string, MetaStore> storeFor, Func<DateTimeOffset>? clock = null, Action<string>? log = null)
{
    private readonly Dictionary<string, MetaStore> _stores = [];
    private readonly Dictionary<SessionKey, SessionMeta> _cache = [];
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public event Action<SessionKey>? Changed;

    public DateTimeOffset Now => _clock();

    private MetaStore StoreFor(SessionKey key)
    {
        if (!_stores.TryGetValue(key.AgentId, out var store))
            _stores[key.AgentId] = store = storeFor(key.AgentId);
        return store;
    }

    public SessionMeta Get(SessionKey key)
    {
        if (!_cache.TryGetValue(key, out var meta))
            _cache[key] = meta = StoreFor(key).Load(key.SessionId);
        return meta;
    }

    /// <summary>変更を適用して保存し、Changed を発生させる。保存に失敗しても画面上の値は更新する。</summary>
    public SessionMeta Update(SessionKey key, Func<SessionMeta, DateTimeOffset, SessionMeta> change)
    {
        var updated = change(Get(key), _clock());
        _cache[key] = updated;
        try
        {
            StoreFor(key).Save(key.SessionId, updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"メタの保存に失敗（{key.SessionId}）: {ex.Message}");
        }
        Changed?.Invoke(key);
        return updated;
    }
}