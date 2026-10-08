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

    /// <summary>タイムラインの見た目の確認用：入力・思考・ツール・返事（長いものも）・圧縮の入った 3 ターン。</summary>
    private static IReadOnlyList<AgentEvent> Rich(SessionKey key)
    {
        var events = new List<AgentEvent>();
        long seq = 0;
        var at = T.AddMinutes(-20);
        AgentEvent E(AgentEventKind kind, string? text = null, string? tool = null, string? command = null, TurnOutcome? outcome = null)
            => new(key, ++seq, at = at.AddSeconds(8), kind, Text: text, ToolName: tool, Command: command, Outcome: outcome);

        events.Add(E(AgentEventKind.PromptSubmitted, "ログイン画面のバリデーションを直して"));
        events.Add(E(AgentEventKind.AssistantThought, "まず入力チェックの場所を探す。Validator.cs にまとまっているはず。"));
        events.Add(E(AgentEventKind.ToolStarted, tool: "Grep", command: "grep -rn \"Validate\" src"));
        events.Add(E(AgentEventKind.ToolSucceeded, tool: "Grep"));
        events.Add(E(AgentEventKind.AssistantMessage, "入力チェックは src/Login/Validator.cs にありました。空欄のときのメッセージが出ていないのが原因です。"));
        events.Add(E(AgentEventKind.TurnEnded, outcome: TurnOutcome.Completed));
        events.Add(E(AgentEventKind.PromptSubmitted, "テストも足して。エラー文言は日本語で"));
        events.Add(E(AgentEventKind.AssistantMessage,
            string.Join("\n", Enumerable.Range(1, 14).Select(i => $"{i}. 空欄・空白だけ・長すぎる入力のそれぞれで、日本語のエラー文言を返すテストを追加しました（長い返事の見え方の確認用）。"))));
        events.Add(E(AgentEventKind.TurnEnded, outcome: TurnOutcome.Completed));
        events.Add(E(AgentEventKind.Compacted));
        events.Add(E(AgentEventKind.PromptSubmitted, "ありがとう。残りは文言の統一だけ"));
        events.Add(E(AgentEventKind.AssistantMessage, "了解です。文言の一覧を作って、デザイナーに確認してもらいましょう。"));
        events.Add(E(AgentEventKind.TurnEnded, outcome: TurnOutcome.Completed));
        return events;
    }

    /// <summary>性能の確認用：<paramref name="events"/> 件ほどのイベント（1 ターン = 入力・思考・ツール 2・返事・終了の 6 件）。</summary>
    public static IReadOnlyList<AgentEvent> Big(SessionKey key, int events)
    {
        var list = new List<AgentEvent>(events + 6);
        long seq = 0;
        var at = T.AddDays(-1);
        AgentEvent E(AgentEventKind kind, string? text = null, string? tool = null, string? command = null, TurnOutcome? outcome = null)
            => new(key, ++seq, at = at.AddSeconds(3), kind, Text: text, ToolName: tool, Command: command, Outcome: outcome);
        for (var turn = 1; list.Count < events; turn++)
        {
            list.Add(E(AgentEventKind.PromptSubmitted, $"{turn} 番目の依頼：{new string('あ', 40 + turn % 60)}"));
            list.Add(E(AgentEventKind.AssistantThought, $"考え中 {turn}：{new string('い', 80)}"));
            list.Add(E(AgentEventKind.ToolStarted, tool: "Read", command: $"read file{turn}.cs"));
            list.Add(E(AgentEventKind.ToolSucceeded, tool: "Read"));
            list.Add(E(AgentEventKind.AssistantMessage, $"{turn} 番目の返事：{string.Join("\n", Enumerable.Repeat(new string('う', 60), 1 + turn % 12))}"));
            list.Add(E(AgentEventKind.TurnEnded, outcome: TurnOutcome.Completed));
        }
        return list;
    }

    /// <summary>6 件を入れて、2 件目を選ぶ（詳細のヘッダーも出る）。</summary>
    public static void Fill(MainVmHarness h)
    {
        var s2 = MainVmHarness.KeyOf("s2", "claude");
        h.EventsOf = key => key == s2 ? Rich(key) : EventsByKey.TryGetValue(key, out var e) ? e : [];
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
