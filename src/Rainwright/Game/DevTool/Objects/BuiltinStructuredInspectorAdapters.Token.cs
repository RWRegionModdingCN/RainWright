using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.Objects;

internal static partial class BuiltinStructuredInspectorAdapters
{
    private sealed partial class StructuredArrayInspector
    {
        private const string TokenSelectionKey = "builtin.token.unlock";

        private static Type TokenUnlockType(CollectToken.CollectTokenData data) =>
            data.isWhite ? typeof(MoreSlugcats.ChatlogData.ChatlogID) :
            data.isGreen ? typeof(MultiplayerUnlocks.SlugcatUnlockID) :
            data.isRed ? typeof(MultiplayerUnlocks.SafariUnlockID) :
            data.isBlue ? typeof(MultiplayerUnlocks.SandboxUnlockID) :
            typeof(MultiplayerUnlocks.LevelUnlockID);

        private static void AppendCollectTokenSelection(List<EditorPropertySnapshot> result, CollectToken.CollectTokenData data)
        {
            result.Add(EnumProperty(TokenSelectionKey,
                data.isWhite ? "Chatlog" : data.isGreen ? "Slugcat Unlock" : data.isRed ? "Safari Unlock" :
                data.isBlue ? "Sandbox Unlock" : "Level Unlock",
                NativeDataReflectionInspector.GetExtEnumOptions(TokenUnlockType(data), data.tokenString),
                data.tokenString, "Token"));
        }

        private static bool TrySetCollectTokenSelection(CollectToken.CollectTokenData data, string key, EditorPropertyValue value)
        {
            if (key != TokenSelectionKey || value.Kind != EditorPropertyKind.Enum) return false;
            Type type = TokenUnlockType(data);
            string[] options = NativeDataReflectionInspector.GetExtEnumOptions(type, data.tokenString);
            if (value.Integer < 0 || value.Integer >= options.Length) return false;
            string selected = options[value.Integer];
            // Use the native property for this token variant; the other computed selectors are
            // inactive aliases of tokenString and must never be presented as separate editors.
            if (data.isWhite) data.ChatlogCollect = new MoreSlugcats.ChatlogData.ChatlogID(selected);
            else if (data.isGreen) data.SlugcatUnlock = new MultiplayerUnlocks.SlugcatUnlockID(selected);
            else if (data.isRed) data.SafariUnlock = new MultiplayerUnlocks.SafariUnlockID(selected);
            else if (data.isBlue) data.SandboxUnlock = new MultiplayerUnlocks.SandboxUnlockID(selected);
            else data.LevelUnlock = new MultiplayerUnlocks.LevelUnlockID(selected);
            return true;
        }

        private static bool IsCollectTokenManagedProperty(string key)
        {
            string prefix = "native." + typeof(CollectToken.CollectTokenData).FullName + ".";
            if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string member = key.Substring(prefix.Length);
            return member == "tokenString" || member == "LevelUnlock" || member == "SandboxUnlock" ||
                member == "ChatlogCollect" || member == "SlugcatUnlock" || member == "SafariUnlock" ||
                member == "isBlue" || member == "isGreen" || member == "isWhite" || member == "isRed" || member == "isDev";
        }
    }
}
