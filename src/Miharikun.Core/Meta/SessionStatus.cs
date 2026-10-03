using System.Text.Json;
using System.Text.Json.Serialization;

namespace Miharikun.Core.Meta;

/// <summary>ユーザーが手で設定するセッションの進み具合（要件 6章・12章）。未設定は null で表す。自動判定の「状態」（SessionState）とは別物。</summary>
public enum SessionStatus
{
    Working,
    Paused,
    Done,
}

/// <summary>meta json では "working" / "paused" / "done" の文字列。知らない値・型違いは未設定（null）として読む。AOT 互換のため手書き。</summary>
internal sealed class SessionStatusJsonConverter : JsonConverter<SessionStatus?>
{
    public override SessionStatus? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 数値・オブジェクトなどが入っていても、読み飛ばして未設定にする（他の項目を道連れにしない）。
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }
        return reader.GetString() switch
        {
            "working" => SessionStatus.Working,
            "paused" => SessionStatus.Paused,
            "done" => SessionStatus.Done,
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, SessionStatus? value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case SessionStatus.Working: writer.WriteStringValue("working"); break;
            case SessionStatus.Paused: writer.WriteStringValue("paused"); break;
            case SessionStatus.Done: writer.WriteStringValue("done"); break;
            default: writer.WriteNullValue(); break;
        }
    }
}
