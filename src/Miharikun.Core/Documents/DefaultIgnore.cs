namespace Miharikun.Core.Documents;

/// <summary>ドキュメントタブの既定の除外パターン（要件 12.7）。.cursor/ は除外しない。</summary>
public static class DefaultIgnore
{
    public const string Text = """
        .git/
        node_modules/
        bin/
        obj/
        .vs/
        .idea/
        .venv/
        venv/
        __pycache__/
        dist/
        build/
        out/
        target/
        packages/
        .gradle/
        .next/
        coverage/
        """;
}
