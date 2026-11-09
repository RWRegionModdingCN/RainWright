using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public float sqrMagnitude=>x*x+y*y; public Vector2 normalized=>new(x/(float)Math.Sqrt(sqrMagnitude),y/(float)Math.Sqrt(sqrMagnitude)); public static Vector2 up=>new(0,1); public static float Dot(Vector2 a,Vector2 b)=>a.x*b.x+a.y*b.y; public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y); }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;} public static Color white=>new(1,1,1); }
    public static class Mathf { public static float Clamp01(float x)=>Math.Clamp(x,0,1); public static float InverseLerp(float a,float b,float x)=>Clamp01((x-a)/(b-a)); }
}
public class PlacedObject {}
public class FLabel { public string text; }
public class ExtEnumBase { public int Index; public string value; public static string[] GetNames(Type type)=>Array.Empty<string>(); public static object Parse(Type type,string name,bool register)=>null; }
namespace DryCycle.DevUI.DevTool.Core
{
    public static class EditorDiagnostics { public static Logger LogSource = new(); }
    public class Logger { public List<string> Messages = new(); public void LogWarning(object value)=>Messages.Add(value.ToString()); }
}
namespace DevInterface
{
    public class DevUI { public Page activePage; public bool mouseClick,mouseDown; public DevUINode draggedNode; }
    public class DevUISignalType { public static readonly DevUISignalType ButtonClick = new(); }
    public interface IDevUISignals { void Signal(DevUISignalType type, DevUINode sender,string message); }
    public class DevUINode
    {
        public DevUI owner; public DevUINode parentNode; public string IDstring="node";
        public List<DevUINode> subNodes=new(); public List<FLabel> fLabels=new();
        public virtual void Refresh() {} public virtual void Update() {} public virtual void ClearSprites() {}
    }
    public class PositionedDevUINode : DevUINode { public UnityEngine.Vector2 pos; public virtual UnityEngine.Vector2 absPos=>pos+(parentNode is PositionedDevUINode positioned?positioned.absPos:new UnityEngine.Vector2()); }
    public class RectangularDevUINode : PositionedDevUINode {}
    public class DevUILabel : RectangularDevUINode { public string Text="Label"; }
    public class Button : DevUILabel { public virtual void Clicked() {} }
    public class ArrowButton : DevUINode { public virtual void Clicked() {} }
    public class Handle : PositionedDevUINode {}
    public class PlacedObjectRepresentation : Handle { public PlacedObject pObj; }
    public class Page : PositionedDevUINode {}
    public class ObjectsPage : Page {}
    public class Panel : RectangularDevUINode {}
    public class Slider : PositionedDevUINode
    {
        public bool inheritButton; public string NumberText=".5"; public float SliderStartCoord;
        public virtual void NubDragged(float factor) {} public void RefreshNubPos(float value) {} public virtual void ClickedResetToInherent() {}
        public class SliderNub : PositionedDevUINode {}
    }
    public class Cycler : Button { public List<string> alternatives=new(); public int currentAlternative; public string baseName; }
    public class IntegerControl : PositionedDevUINode { public string NumberLabelText="0"; public virtual void Increment(int direction) {} }
    public class SelectPanel : Panel { public string[] items=Array.Empty<string>(); }
    public class ButtonWithSelectPanel : Button
    {
        public SelectPanel selectPanel; public Func<ButtonWithSelectPanel,SelectPanel> makeSelectPanel;
        public virtual void OnValueChange(string value) {}
    }
}
