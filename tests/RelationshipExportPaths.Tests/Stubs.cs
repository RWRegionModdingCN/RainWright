using System;
using System.Collections.Generic;

namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath;
    }
}

namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static readonly List<string> Messages = new();
        internal static readonly List<string> Errors = new();

        internal static void Info(string message) => Messages.Add(message);
        internal static void Error(string message, Exception error = null) =>
            Errors.Add(message + (error == null ? "" : " " + error));
    }
}

namespace DevInterface
{
    internal sealed class RelationshipKey
    {
        internal readonly string value;
        internal RelationshipKey(string value) => this.value = value;
    }

    internal static class RelationshipPage
    {
        internal static readonly Dictionary<RelationshipKey, Dictionary<RelationshipKey, float>>
            changedRelationships = new();

        internal static string EstablishRelationshipCodeString(
            RelationshipKey subject, RelationshipKey target, float strength) =>
            subject.value + " -> " + target.value + " = " +
            strength.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
