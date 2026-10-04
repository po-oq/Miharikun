using Miharikun.Core.Agents;

namespace Miharikun.Core.Sessions;

public enum SessionDeltaKind
{
    /// <summary>保持しているイベントの後ろに足す。空の列なら何もしない。</summary>
    Append,
    /// <summary>保持しているイベントを丸ごと置き換える。空の列は Remove と同じ。</summary>
    Replace,
    /// <summary>セッションを消す。</summary>
    Remove,
}

/// <summary>Source が前回から変わったセッション 1 つ分の差分（計画 8.1）。</summary>
public sealed record SessionDelta(SessionKey Key, SessionDeltaKind Kind, IReadOnlyList<AgentEvent> Events);

/// <summary>監視するフォルダ。CreateIfMissing が true のものだけ、無ければ作る（Cursor の events。Claude の .claude は作らない）。</summary>
public sealed record WatchTarget(string Directory, string Filter, bool CreateIfMissing);

/// <summary>
/// エージェントごとの読み込み。ファイルの追記分を読み、共通イベントの差分にして返す。
/// ProjectEventStore が差分を適用し、保持・要約・変更通知を行う。
/// </summary>
public interface ISessionSource
{
    string AgentId { get; }

    /// <summary>いま監視してほしいフォルダ。呼ぶたびに最新を返す（あとからできたフォルダも拾えるように）。</summary>
    IReadOnlyList<WatchTarget> WatchTargets { get; }

    /// <summary>前回の読み込み以降に変わった分の差分。最初の呼び出しでは、すでにあるものを全部返す。</summary>
    IReadOnlyList<SessionDelta> ReadNew();
}
