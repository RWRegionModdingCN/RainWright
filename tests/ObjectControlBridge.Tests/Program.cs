using System;
using System.Linq;
using DevInterface;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static string Raw(LegacyControlSnapshot control)=>control.Path.Split('~')[0];
    private static void Check(bool value,string text) { if(!value) throw new Exception(text); checks++; }
    private static T Add<T>(DevUINode parent,T node) where T:DevUINode { node.parentNode=parent; node.owner=parent.owner; parent.subNodes.Add(node); return node; }
    private static Panel Window(DevUINode parent,string title)
    { var panel=Add(parent,new Panel()); panel.fLabels.Add(new FLabel {text=title}); return panel; }
    private static void Main()
    {
        var root=new PlacedObjectRepresentation {owner=new DevUI()};
        var slider=Add(root,new Slider()); Add(slider,new DevUILabel()); Add(slider,new DevUILabel()); Add(slider,new Slider.SliderNub());
        var panel=Window(slider,"Special Window"); var action=Add(panel,new CountingButton {Text="Apply"});
        var controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.Length==2 && controls[0].Kind==LegacyControlKind.Slider && controls[1].Kind==LegacyControlKind.Button,"Popup controls survive atomic slider capture without duplicate chrome.");
        Check(Raw(controls[1])=="0.3.0" && controls[1].PanelPath=="0.3" && controls[1].PanelTitle=="Special Window","Panel titles and action indices remain attached to the correct window.");
        Check(!LegacyDevInterfaceBridge.IsAtomicAdaptedControl(slider),"Diagnostics cannot skip a live popup.");
        Check(LegacyDevInterfaceBridge.ClickButton(root.owner,root,controls[1].Path) && action.Clicks==1,"Nested popup button invokes its original callback once.");
        panel.subNodes.Remove(action); var replacement=Add(panel,new CountingButton {Text="Updated"});
        Check(LegacyDevInterfaceBridge.ClickButton(root.owner,root,"0.3.0") && action.Clicks==1 && replacement.Clicks==1,"Actions resolve the current dynamic node tree rather than stale node references.");
        var select=Add(root,new ButtonWithSelectPanel {makeSelectPanel=_=>throw new Exception("Factory must never run during capture")});
        controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.Last().Kind==LegacyControlKind.Button,"Closed selectors retain their original opener without speculative factory construction.");
        var stock=Add(select,new SelectPanel {items=new[]{"A","B"}}); select.selectPanel=stock; Add(stock,new Button());
        controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.Last().Kind==LegacyControlKind.Select && controls.Count(c=>c.Path.StartsWith("1."))==0,"Stock choices appear once as a combo.");
        select.subNodes.Clear(); var custom=Add(select,new SearchWindow {items=new[]{"A","B"}}); custom.fLabels.Add(new FLabel {text="Search"}); select.selectPanel=custom;
        Add(custom,new CountingButton {Text="Next page"}); controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.Any(c=>Raw(c)=="1.0.0" && c.Label=="Next page" && c.PanelTitle=="Search"),"Custom selector windows retain pagination/search controls.");
        var color=Add(root,new PopupColorButton()); var choice=Add(root,new PopupChoiceButton());
        controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.First(c=>Raw(c)=="2").Kind==LegacyControlKind.Button && controls.First(c=>Raw(c)=="3").Kind==LegacyControlKind.Button,"actualValue alone does not authorize bypassing original color/select callbacks.");
        Check(LegacyDevInterfaceBridge.ClickButton(root.owner,root,"2") && LegacyDevInterfaceBridge.CaptureRoot(root).Any(c=>c.PanelTitle=="Color"),"Original color opener reveals its nested controls.");
        Check(!LegacyDevInterfaceBridge.SetColor(root.owner,root,"2",.5f,.5f,.5f,1) && color.actualValue.r==.1f,"Color data is not force-written when no original commit boundary exists.");
        var committed=Add(root,new CommittedColorButton());
        Check(LegacyDevInterfaceBridge.SetColor(root.owner,root,"4",.5f,.6f,.7f,1) && committed.Commits==1 && committed.actualValue.r==.5f,"Explicit original value commits remain available to native color presentation.");
        var boolean=Add(root,new PopupBooleanButton());
        Check(!LegacyDevInterfaceBridge.ResetLegacyBoolean(root.owner,root,"5",true) && !boolean.actualValue && boolean.subNodes.Count==1,"Opening a boolean helper window cannot silently force author data or claim successful mutation.");
        var broken=Add(root,new BrokenButton());
        Check(!LegacyDevInterfaceBridge.ClickButton(root.owner,root,"6") && EditorDiagnostics.LogSource.Messages.Last().Contains("original callback failed") && EditorDiagnostics.LogSource.Messages.Last().Contains(nameof(BrokenButton)),"Original callback failures retain their full diagnostic stack.");
        root.owner.mouseDown=true; root.owner.mouseClick=true; root.owner.draggedNode=slider;
        var polling=Add(root,new PollingPanel()); var polled=Add(polling,new CountingButton());
        Check(LegacyDevInterfaceBridge.ClickButton(root.owner,root,"7.0") && polling.SawQuietMouse && root.owner.mouseDown && root.owner.mouseClick && root.owner.draggedNode==slider,"Deferred window synchronization disables hidden mouse transitions and restores input ownership.");
        VerifyExtremeTrees();
        VerifyCachedProtocols();
        checks+=ObjectTransactionTests.Run();
        Console.WriteLine("PASS: "+checks+" production bridge assertions using detached UI protocol fixtures.");
    }
    private class CountingButton:Button { public int Clicks; public override void Clicked()=>Clicks++; }
    private static void VerifyCachedProtocols()
    {
        var root = new PlacedObjectRepresentation {owner=new DevUI()};
        var choice = Add(root,new LiveChoiceButton());
        Check(LegacyDevInterfaceBridge.CaptureRoot(root).Single().Options.SequenceEqual(new[]{"A","B"}), "Cold protocol schema exposes original choices.");
        choice.values=new[]{"C","D"}; choice.actualValue="D";
        var snapshot=LegacyDevInterfaceBridge.CaptureRoot(root).Single();
        Check(snapshot.Options.SequenceEqual(new[]{"C","D"}) && snapshot.ValueText=="D", "Cached member layouts never freeze current values or dynamic choices.");
        var property=LegacyControlReflection.Property(typeof(ShadowProtocol),"Value");
        Check(property.DeclaringType==typeof(ShadowProtocol) && (string)property.GetValue(new ShadowProtocol())=="derived", "Cached properties retain derived member precedence.");
        Check(LegacyControlReflection.Method(typeof(ShadowProtocol),"Set",new[]{typeof(string)}).GetParameters()[0].ParameterType==typeof(string)
            && LegacyControlReflection.Method(typeof(ShadowProtocol),"Set",new[]{typeof(float)}).DeclaringType==typeof(BaseProtocol), "Method keys distinguish overloads and inherited signatures.");
        DevUINode deep=null;
        for(int i=0;i<4096;i++) deep=new DevUINode {parentNode=deep};
        Check(LegacyDevInterfaceBridge.IsValidParentChain(deep),"An allocation-free parent audit still accepts its exact 4096-node budget.");
        Check(!LegacyDevInterfaceBridge.IsValidParentChain(new DevUINode {parentNode=deep}),"Over-budget parent chains still fail safely.");
        var cycle=new DevUINode();cycle.parentNode=cycle;
        Check(!LegacyDevInterfaceBridge.IsValidParentChain(cycle),"Self-referencing parents still fail without temporary sets.");
        const int loops=50000;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        for(int i=0;i<loops;i++) LegacyControlReflection.Field(typeof(LiveChoiceButton),"missing");
        timer.Stop(); double cached=timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        for(int i=0;i<loops;i++)
            for(var current=typeof(LiveChoiceButton);current!=null;current=current.BaseType)
                if(current.GetField("missing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly)!=null) break;
        timer.Stop();
        Console.WriteLine("Reflection metadata, 50,000 absent-member lookups: cached="+cached.ToString("F2")+" ms, hierarchy scan="+timer.Elapsed.TotalMilliseconds.ToString("F2")+" ms (detached CLR fixture).");
    }
    private class LiveChoiceButton:Button { public string actualValue="A"; public string[] values={"A","B"}; public void SetValue(string value)=>actualValue=value; }
    private class BaseProtocol { public string Value=>"base"; public void Set(float value){} }
    private class ShadowProtocol:BaseProtocol { public new string Value=>"derived"; public void Set(string value){} }
    private class SearchWindow:SelectPanel {}
    private class PopupColorButton:Button { public Color actualValue=new(.1f,.2f,.3f); public override void Clicked(){var panel=Window(this,"Color");Add(panel,new CountingButton());} }
    private class CommittedColorButton:PopupColorButton { public int Commits; public void SetValue(Color value){Commits++;actualValue=value;} }
    private class PopupChoiceButton:Button { public string actualValue="A"; public string[] values={"A","B"}; }
    private class PopupBooleanButton:Button { public bool actualValue=false; public override void Clicked()=>Window(this,"Bool options"); }
    private class BrokenButton:Button { public override void Clicked()=>throw new InvalidOperationException("original callback failed"); }
    private class PollingPanel:Panel { public bool SawQuietMouse; public override void Update()=>SawQuietMouse=!owner.mouseClick&&!owner.mouseDown&&owner.draggedNode==null; }
    private static void VerifyExtremeTrees()
    {
        var root=new PlacedObjectRepresentation {owner=new DevUI()};
        var first=Add(root,new CountingButton()); var old=LegacyDevInterfaceBridge.CaptureRoot(root).Single();
        Check(old.Path==LegacyDevInterfaceBridge.CaptureRoot(root).Single().Path,"A stable original node keeps a stable published identity.");
        root.subNodes.Clear(); var second=Add(root,new CountingButton());
        Check(!LegacyDevInterfaceBridge.ClickButton(root.owner,root,old.Path) && second.Clicks==0,"A stale published action cannot hit a replacement control at the same index.");
        Check(!LegacyDevInterfaceBridge.ClickButton(root.owner,root,"0~invalid") && !LegacyDevInterfaceBridge.ClickButton(root.owner,root,"0~-1"),"Malformed identity tokens fail without author mutations.");
        Add(root,new ThrowingValueButton()); Add(root,new CountingButton {Text="Healthy sibling"});
        var controls=LegacyDevInterfaceBridge.CaptureRoot(root);
        Check(controls.Any(c=>c.Kind==LegacyControlKind.ReadOnly&&c.ValueText.Contains("bad getter")) && controls.Any(c=>c.Label=="Healthy sibling"),"One bad getter becomes a read-only diagnostic while healthy sibling controls remain available.");
        Check(LegacyDevInterfaceBridge.HasUnsupportedNodes(root,out _),"Read failures cannot certify complete compatibility.");
        var cyclic=new PlacedObjectRepresentation {owner=new DevUI()}; Add(cyclic,new CountingButton()); cyclic.subNodes.Add(cyclic);
        Check(LegacyDevInterfaceBridge.CaptureRoot(cyclic).Length==1 && LegacyDevInterfaceBridge.HasUnsupportedNodes(cyclic,out _),"Cyclic trees preserve usable controls without infinite traversal or false coverage.");
        var deep=new PlacedObjectRepresentation {owner=new DevUI()}; DevUINode cursor=deep;
        for(int i=0;i<2048;i++) cursor=Add(cursor,new PositionedDevUINode());
        var deepest=Add(cursor,new CountingButton()); controls=LegacyDevInterfaceBridge.CaptureRoot(deep);
        Check(controls.Length==1 && LegacyDevInterfaceBridge.ClickButton(deep.owner,deep,controls[0].Path) && deepest.Clicks==1,"A 2048-level original control tree remains editable without recursion overflow.");
        deep.pos=new Vector2(10,20); deepest.pos=new Vector2(3,4);
        var position=LegacyNodePosition.Absolute(deepest);
        Check(position.x==13&&position.y==24,"Deep default position chains are evaluated iteratively without changing native coordinate semantics.");
        var customPosition=Add(deep,new CustomPositionNode()); var child=Add(customPosition,new Handle {pos=new Vector2(1,2)});
        position=LegacyNodePosition.Absolute(child);
        Check(position.x==101&&position.y==202&&customPosition.Reads==1,"Original virtual position overrides remain authoritative and are read once.");
        var invalid=new PlacedObjectRepresentation {owner=new DevUI()}; var cyclicHandle=Add(invalid,new Handle()); invalid.parentNode=cyclicHandle;
        Check(LegacyDevInterfaceBridge.CaptureRoot(invalid).Single().Kind==LegacyControlKind.ReadOnly&&!LegacyDevInterfaceBridge.ClickButton(invalid.owner,invalid,"0"),"Cyclic parent links become explicit noneditable diagnostics.");
        var wide=new PlacedObjectRepresentation {owner=new DevUI()}; for(int i=0;i<4098;i++) Add(wide,new CountingButton());
        controls=LegacyDevInterfaceBridge.CaptureRoot(wide);
        Check(controls.Length<=4097 && controls.Any(c=>c.Kind==LegacyControlKind.ReadOnly&&c.ValueText.Contains("budget")) && LegacyDevInterfaceBridge.HasUnsupportedNodes(wide,out _),"Oversized trees retain a bounded partial view and explicitly report the uncovered remainder.");
        var options=new PlacedObjectRepresentation {owner=new DevUI()}; Add(options,new InfiniteOptionsButton()); Add(options,new CountingButton());
        controls=LegacyDevInterfaceBridge.CaptureRoot(options);
        Check(controls.Length==2 && controls[0].Kind==LegacyControlKind.ReadOnly && controls[1].Kind==LegacyControlKind.Button,"An endless null-valued option iterator cannot hang or erase sibling controls.");
        var slider=Add(root,new TrackingSlider());
        Check(!LegacyDevInterfaceBridge.SetSlider(root.owner,root,"3",float.NaN) && !LegacyDevInterfaceBridge.SetSlider(root.owner,root,"3",float.PositiveInfinity) && slider.Drags==0,"Nonfinite drag factors never reach original author callbacks.");
        var color=Add(root,new CommittedColorButton());
        Check(!LegacyDevInterfaceBridge.SetColor(root.owner,root,"4",float.NaN,0,0,1) && color.Commits==0,"Nonfinite colors never reach explicit original commits.");
        var reject=Add(root,new RejectingTextButton());
        Check(!LegacyDevInterfaceBridge.SetText(root.owner,root,"5","same"),"A false original text commit is honored even when its visible value equals the requested text.");
    }
    private class ThrowingValueButton:Button { public object actualValue=>throw new InvalidOperationException("bad getter"); }
    private class InfiniteOptionsButton:Button
    {
        public string actualValue="x"; public System.Collections.IEnumerable values {get {while(true) yield return null;}}
        public void SetValue(string value)=>actualValue=value;
    }
    private class TrackingSlider:Slider { public int Drags; public override void NubDragged(float factor)=>Drags++; }
    private class RejectingTextButton:Button { public string actualValue="same"; public bool TrySetValue(string value,bool signal)=>false; }
    private class CustomPositionNode:PositionedDevUINode { public int Reads; public override Vector2 absPos {get {Reads++;return new(100,200);}} }
}
