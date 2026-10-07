using Miharikun.Core.Agents;
using Miharikun.Core.Meta;
using Miharikun.Core.Sessions;
using Miharikun.Tests.Presentation;

namespace Miharikun.UiTests;

/// <summary>見た目の確認用のダミーのデータ（隔離。本物のデータは使わない）。状態・ステータス・エージェントの違うセッションを並べる。</summary>
internal static class Scene
{
    private static readonly DateTimeOffset T = MainVmHarness.Now;

    private static readonly Dictionary<SessionKey, IReadOnlyList<AgentEvent>> EventsByKey = [];

    /// <summary>終わり方（<paramref name="outcome"/>）を選べるセッション。null は実行中（入力だけ）。</summary>
    private static SessionSnapshot Session(string id, string prompt, int minutesAgo, string agent, TurnOutcome? outcome, string? reply = "返事です。", string? edited = null)
    {
        var at = T.AddMinutes(-minutesAgo);
        var key = MainVmHarness.KeyOf(id, agent);
        var events = new List<AgentEvent>();
        long seq = 0;
        events.Add(new(key, ++seq, at.AddSeconds(-3), AgentEventKind.PromptSubmitted, Text: prompt));
        if (outcome is not null)
        {
            if (edited is not null)
                events.Add(new(key, ++seq, at.AddSeconds(-2), AgentEventKind.FileEdited, FilePath: edited));
            if (reply is not null)
                events.Add(new(key, ++seq, at.AddSeconds(-1), AgentEventKind.AssistantMessage, Text: reply));
            events.Add(new(key, ++seq, at, AgentEventKind.TurnEnded, Outcome: outcome));
        }
        EventsByKey[key] = events;
        var summary = SessionAnalyzer.Analyze(key, events);
        return new SessionSnapshot(summary, SessionSearch.BuildSearchText(summary, events));
    }

    /// <summary>6 件を入れて、2 件目を選ぶ（詳細のヘッダーも出る）。</summary>
    public static void Fill(MainVmHarness h)
    {
        h.EventsOf = key => EventsByKey.TryGetValue(key, out var e) ? e : [];
        void Meta(string id, string agent, Func<SessionMeta, DateTimeOffset, SessionMeta> change) =>
            h.Meta.Update(MainVmHarness.KeyOf(id, agent), change);

        Meta("s2", "claude", (m, at) => m.WithStatus(SessionStatus.Working, at).EditSummary("ログイン画面のバリデーションを直した。残りはエラー文言の統一。", at).WithMemo("明日、文言をデザイナーに確認する", at));
        Meta("s3", "cursor", (m, at) => m.WithStatus(SessionStatus.Paused, at));
        Meta("s5", "cursor", (m, at) => m.WithStatus(SessionStatus.Done, at).EditSummary("リリース手順の文書を更新した。", at));

        h.Add(
            Session("s1", "設定画面の保存ボタンが押せないときがある", 1, "cursor", outcome: null),
            Session("s2", "ログイン画面のバリデーションを直して", 12, "claude", TurnOutcome.Completed, "入力チェックを追加し、空欄のときのメッセージを出すようにしました。テストも通っています。", edited: "src/Login/Validator.cs"),
            Session("s3", "一覧の並び替えが遅い原因を調べて", 35, "cursor", TurnOutcome.Aborted, reply: null),
            Session("s4", "README の英語版を作って", 90, "claude", TurnOutcome.Error, reply: null),
            Session("s5", "リリース手順を文書にまとめて", 240, "cursor", TurnOutcome.Completed, "docs/release.md に手順を追記しました。"),
            Session("s6", "ログの出力先を環境変数で変えられるようにして", 600, "claude", TurnOutcome.Completed, "MIHARIKUN_DATA_DIR で変えられるようにしました。"));
        h.Vm.Selected = h.Card("s2");
    }
}
