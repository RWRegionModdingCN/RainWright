using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;

internal static partial class Program
{
    private static void VerifyLegacySelectionCycles(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        T Raw<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var settings = Raw<RoomSettings>(); settings.Reset();
        var room = Raw<Room>(); room.roomSettings = settings;
        var state = Raw<RainWorldGame>(); var camera = Raw<RoomCamera>(); camera.room = room; state.cameras = new[] { camera };
        var owner = Raw<DevInterface.DevUI>(); owner.game = state;
        var sessionType = assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorSession", true);
        object session = FormatterServices.GetUninitializedObject(sessionType);
        sessionType.GetField("<Owner>k__BackingField", flags).SetValue(session, owner);
        var bridge = assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.LegacySoundTriggerSelectionBridge", true);
        var first = Raw<DevInterface.Panel>(); var second = Raw<DevInterface.Panel>();
        first.parentNode = second; second.parentNode = first; owner.draggedNode = first;
        var log = (BepInEx.Logging.ManualLogSource)assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorDiagnostics", true).GetProperty("LogSource", flags).GetValue(null);
        int reports = 0;
        log.LogEvent += (_, e) => { if (e.Data.ToString().Contains("selection parent chain")) reports++; };
        foreach (string method in new[] { "SynchronizeSound", "SynchronizeTrigger" })
        {
            Task work = Task.Run(() => bridge.GetMethod(method, flags).Invoke(null, new[] { session }));
            Check(work.Wait(2000), "A cyclic original " + method + " parent chain cannot block the update thread.");
            work.GetAwaiter().GetResult();
        }
        Check(reports == 1, "One unchanged bad node emits one full diagnostic across repeated sound/trigger observations.");
        first.parentNode = null;
        foreach (string method in new[] { "SynchronizeSound", "SynchronizeTrigger" })
            bridge.GetMethod(method, flags).Invoke(null, new[] { session });
        Check(reports == 1 && owner.draggedNode == first, "Repairing a third-party parent link resumes normal observation without modifying its nodes.");
    }
}
