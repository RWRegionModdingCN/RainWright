using Menu.Remix.MixedUI;
using UnityEngine;

namespace Menu.Remix.MixedUI
{
    public class UIfocusable : UIelement
    {
        public enum NextDirection { Up, Right, Down, Left, Back }
        public UIfocusable[] NextFocusable { get; set; } = new UIfocusable[5];
        public void SetNextFocusable(NextDirection direction, UIfocusable target) => NextFocusable[(int)direction] = target;
    }
    public class DyeableRect
    {
        public enum HiddenSide { None, Left, Right, Top, Bottom }
        public HiddenSide hiddenSide;
        public float fillAlpha;
        public Vector2 pos, addSize;
        public void GrafUpdate(float timeStacker) { }
    }
}
namespace Menu.Remix
{
    public static class ConfigContainer
    {
        public static OptionInterface ActiveInterface;
        public static ConfigMenuTab menuTab;
    }
    public class ConfigMenuTab { public ConfigTabController tabCtrler; }
    public class ConfigTabController
    {
        public Vector2 pos = new(520, 120);
        public TabSelectButton[] _tabButtons;
        public DyeableRect _rectCanvas = new() { fillAlpha = .5f };
        public class TabSelectButton : UIfocusable
        {
            public FLabel _label = new() { rotation = -90, alignment = FLabelAlignment.Left, text = "Native tab" };
            public DyeableRect _rect = new() { fillAlpha = .5f, hiddenSide = DyeableRect.HiddenSide.Right };
            public DyeableRect _rectH = new() { hiddenSide = DyeableRect.HiddenSide.Right };
        }
    }
}
