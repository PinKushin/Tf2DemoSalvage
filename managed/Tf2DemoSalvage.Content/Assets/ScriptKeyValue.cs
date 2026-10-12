using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One key's value out of a KeyValues script, without parsing the rest of it.
/// </summary>
/// <remarks>
/// **Deliberately not a KeyValues parser**, for the reason <c>ClassScript</c> already gave: the
/// scripts nest deeply, a real reader needs a tree API, and nothing here wants the tree. Finding
/// the first quoted value after a quoted key is a smaller and more testable problem.
///
/// Shared because two callers now need exactly it — a class script's <c>model</c> and a weapon
/// script's <c>WeaponType</c> — and a second copy of a text scan is a second place for the quoting
/// rules to drift.
///
/// **Only the first match is taken**, which is the engine's own answer: a key repeated later in a
/// block overrides in some readers and is ignored in others, and agreeing with Valve's parser is
/// what matters here.
/// </remarks>
internal static class ScriptKeyValue
{
    /// <summary>The value of the first occurrence of a key.</summary>
    /// <param name="script">The script's bytes, already decrypted if it was encrypted.</param>
    /// <param name="key">The key to look for, without quotes.</param>
    /// <returns>The value, or <c>null</c> when the key is absent or unterminated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public static string? First(ReadOnlySpan<byte> script, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string text = Encoding.UTF8.GetString(script);
        bool keyed = false;
        int at = 0;

        // **Token by token, because a `//` comment is not a key** (B523). `KeyValues::ReadToken` eats white space and
        // then `CUtlBuffer::EatCPPComment` (`tier1/utlbuffer.cpp:422-440`) — the rest of the line, whenever a token
        // would START with `//`. This was a search for the quoted key anywhere in the text, and the shipped
        // `soldier.ctx` carries `//"DontDoNewJump"	"1"`: every soldier jumped as `ACT_MP_JUMP`, which no soldier
        // sequence has, and was drawn in the reference pose.
        while (at < text.Length)
        {
            char next = text[at];

            if (char.IsWhiteSpace(next))
            {
                at++;
            }
            else if (next == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                int line = text.IndexOf('\n', at);
                at = line < 0 ? text.Length : line + 1;
            }
            else if (next == '"')
            {
                int close = text.IndexOf('"', at + 1);

                if (close < 0)
                {
                    return null;
                }

                string token = text[(at + 1)..close];

                if (keyed)
                {
                    return token;
                }

                keyed = string.Equals(token, key, StringComparison.OrdinalIgnoreCase);
                at = close + 1;
            }
            else
            {
                // A brace or a bare word: never the key, which the scripts always quote, and never its value.
                at++;
            }
        }

        return null;
    }
}
