using System;
using System.Collections.Generic;
using System.Drawing;
using CodexQuota;
using Rect = System.Windows.Rect;

internal static class PositionTests
{
    private static int count;
    private static void Check(bool condition, string message)
    {
        count++;
        if (!condition) throw new Exception(message);
    }
    private static AnchorResult Locate(double y, double scale, string mode, IList<Rect> occupied)
    {
        return TaskbarAnchor.ComputeGeometry(new Rectangle(0, 0, 1920, 1080), new NativeRect { Left = 0, Top = 1032, Right = 1920, Bottom = 1080 }, new Rect(700, 1032, 48, 48), occupied, 78, 36, 20, scale, mode, -70, y);
    }
    private static int Main()
    {
        try
        {
            AnchorResult baseline_ = Locate(0, 1, "StartLeft", new List<Rect>());
            AnchorResult raised_ = Locate(-20, 1, "StartLeft", new List<Rect>());
            Check(!baseline_.IsFallback && !raised_.IsFallback, "Y offset must not change anchor mode.");
            Check(baseline_.Top - raised_.Top == 20 && baseline_.Left == raised_.Left, "Negative Y offset moves the overlay linearly.");
            AnchorResult above_ = Locate(10, 1, "Above", new List<Rect>());
            AnchorResult aboveZero_ = Locate(0, 1, "Above", new List<Rect>());
            Check(above_.Top - aboveZero_.Top == 10, "Positive Y offset also works in Above mode.");
            AnchorResult scaled_ = Locate(-10, 1.5, "Above", new List<Rect>());
            AnchorResult scaledZero_ = Locate(0, 1.5, "Above", new List<Rect>());
            Check(scaledZero_.Top - scaled_.Top == 15, "Offsets use physical monitor scaling.");
            AnchorResult collision_ = Locate(-20, 1, "StartLeft", new List<Rect> { new Rect(635,1038,20,36) });
            Check(collision_.IsFallback, "Visible ring overlapping a pinned item must fall back.");
            AnchorResult transparentPadding_ = Locate(0, 1, "StartLeft", new List<Rect> { new Rect(620,1038,5,36) });
            Check(!transparentPadding_.IsFallback, "Transparent window padding must not count as visible collision.");
            AnchorResult narrow_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), new NativeRect { Left=0,Top=1056,Right=1920,Bottom=1080 }, new Rect(700,1056,24,24), new List<Rect>(),78,36,20,1,"StartLeft",-70,-20);
            Check(!narrow_.IsFallback, "A 36-DIP transparent window can fit its 20-DIP rings in a 24-DIP taskbar.");
            Check(narrow_.Top == 1030, "Thin taskbar still respects Y offset.");
            AnchorResult clamped_ = Locate(-2000,1,"StartLeft",new List<Rect>());
            Check(clamped_.Top == -8 && !clamped_.IsFallback, "Only painted rings are clamped to the screen boundary; transparent padding may extend beyond it.");
            AnchorResult positiveOne_ = Locate(7, 1, "StartLeft", new List<Rect>());
            AnchorResult positiveTwo_ = Locate(8, 1, "StartLeft", new List<Rect>());
            Check(positiveOne_.Top == baseline_.Top + 7 && positiveTwo_.Top == baseline_.Top + 8, "Bottom taskbar keeps Y steps beyond the transparent HWND's old screen clamp.");
            AnchorResult screenEdge_ = Locate(300, 1, "StartLeft", new List<Rect>());
            Check(screenEdge_.Top + screenEdge_.Height / 2 + 10 == 1080, "Downward Y clamp stops only when a visible ring reaches the screen edge.");
            Check(screenEdge_.IsYClamped && screenEdge_.RequestedTop == baseline_.Top + 300, "Geometry preserves the requested top and reports a visible-screen clamp.");
            Check(!positiveOne_.IsYClamped && positiveOne_.RequestedTop == positiveOne_.Top, "Legal Y adjustment is not reported as limited.");
            AnchorResult thinZero_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), new NativeRect { Left=0,Top=1056,Right=1920,Bottom=1080 }, new Rect(700,1056,24,24), new List<Rect>(),78,36,20,1,"StartLeft",-70,0);
            AnchorResult thinMinusOne_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), new NativeRect { Left=0,Top=1056,Right=1920,Bottom=1080 }, new Rect(700,1056,24,24), new List<Rect>(),78,36,20,1,"StartLeft",-70,-1);
            AnchorResult thinMinusTwo_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), new NativeRect { Left=0,Top=1056,Right=1920,Bottom=1080 }, new Rect(700,1056,24,24), new List<Rect>(),78,36,20,1,"StartLeft",-70,-2);
            Check(thinZero_.Top == 1050 && thinMinusOne_.Top == 1049 && thinMinusTwo_.Top == 1048, "A compact bottom taskbar applies zero, minus one and minus two Y distinctly.");
            Check(thinZero_.Top + thinZero_.Height / 2 == 1068, "A 36-DIP HWND stays centered on a compact 24-DIP Start button.");
            AnchorResult zeroGap_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), PaddedBarForGap(), new Rect(700,1032,48,48), new List<Rect> { new Rect(615,1038,15,36) },72,36,20,1,"StartLeft",-70,0,0,2);
            AnchorResult wideGap_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), PaddedBarForGap(), new Rect(700,1032,48,48), new List<Rect> { new Rect(615,1038,15,36) },112,36,20,1,"StartLeft",-70,0,40,2);
            Check(!zeroGap_.IsFallback && wideGap_.IsFallback, "Collision geometry uses the configured visible gap width.");
            AnchorResult oneRing_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080), PaddedBarForGap(), new Rect(700,1032,48,48), new List<Rect>(),50,36,28,1,"StartLeft",-70,0,40,1);
            Check(!oneRing_.IsFallback, "Explicit one-ring mode ignores a stored two-ring gap.");
            NativeRect paddedBar_ = new NativeRect { Left=0,Top=1032,Right=1920,Bottom=1080 };
            Rect shorterStart_ = new Rect(700,1034,40,32);
            AnchorResult startCentered_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080),paddedBar_,shorterStart_,new List<Rect>(),78,36,20,1,"StartLeft",-70,0);
            Check(startCentered_.Top + startCentered_.Height / 2 == shorterStart_.Top + shorterStart_.Height / 2, "Ring center follows Start content rather than the padded taskbar center.");
            Check(startCentered_.Top == 1032 && !startCentered_.IsFallback, "A shorter Start content area remains a valid taskbar anchor.");
            AnchorResult startRaised_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080),paddedBar_,shorterStart_,new List<Rect>(),78,36,20,1,"StartLeft",-70,-6);
            AnchorResult startLowered_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080),paddedBar_,shorterStart_,new List<Rect>(),78,36,20,1,"StartLeft",-70,6);
            Check(startRaised_.Top == startCentered_.Top - 6 && startLowered_.Top == startCentered_.Top + 6, "Start-relative baseline accepts both upward and downward adjustments.");
            Check(startRaised_.Left == startCentered_.Left && startLowered_.Left == startCentered_.Left, "Y adjustment preserves the selected horizontal anchor.");
            Rect scaledStart_ = new Rect(1000,1015,60,45);
            AnchorResult startScaled_ = TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080),paddedBar_,scaledStart_,new List<Rect>(),78,36,20,1.5,"StartLeft",-70,-4);
            Check(startScaled_.Top + startScaled_.Height / 2 == scaledStart_.Top + scaledStart_.Height / 2 - 6, "Start baseline and Y offset share physical pixels at 150 percent DPI.");
            CheckManualHorizontalOffsets();
            Check(TaskbarAnchor.GetStartCandidateScore("StartButton", "Other") > TaskbarAnchor.GetStartCandidateScore("", "Start"), "StartButton automation id outranks a named child.");
            Check(TaskbarAnchor.GetStartCandidateScore("startbutton", "") == 2, "Start automation id matching is case insensitive.");
            Check(TaskbarAnchor.GetStartCandidateScore("", "开始") == 1 && TaskbarAnchor.GetStartCandidateScore("", "开始按钮") == 1, "Localized Start names remain supported.");
            Check(TaskbarAnchor.GetStartCandidateScore("PinnedButton", "Visual Studio") == 0, "Pinned applications cannot become Start candidates.");
            Check(NativeMethods.IsShellIdentity("Anything", "StartMenuExperienceHost"), "Start is excluded from fullscreen hiding.");
            Check(NativeMethods.IsShellIdentity("Anything", "SearchHost"), "Search is excluded from fullscreen hiding.");
            Check(NativeMethods.IsShellIdentity("Anything", "explorer"), "Explorer shell excluded.");
            Check(NativeMethods.IsShellIdentity("WorkerW", "unknown"), "Desktop excluded by window class.");
            Check(!NativeMethods.IsShellIdentity("Chrome_WidgetWin_1", "msedge"), "Browser remains eligible for fullscreen hiding.");
            Check(!NativeMethods.IsShellIdentity("WindowsForms10.Window", "sample"), "Non-shell app remains eligible.");
            Check(!NativeMethods.IsShellIdentity("Windows.UI.Core.CoreWindow", "sample"), "A generic UWP window must not be mistaken for the shell.");
            Check(NativeMethods.CoversScreen(new NativeRect { Left=0,Top=0,Right=1920,Bottom=1080 },new Rectangle(0,0,1920,1080)), "Full screen bounds detected.");
            Check(!NativeMethods.CoversScreen(new NativeRect { Left=0,Top=0,Right=1920,Bottom=1032 },new Rectangle(0,0,1920,1080)), "Maximized work-area window is not full screen.");
            Console.WriteLine("PASS: " + count + " positioning and shell checks.");
            return 0;
        }
        catch (Exception exception_)
        {
            Console.Error.WriteLine("FAIL: " + exception_.Message);
            return 1;
        }
    }
    private static NativeRect PaddedBarForGap() { return new NativeRect { Left=0,Top=1032,Right=1920,Bottom=1080 }; }
    private static AnchorResult Horizontal(double x, double y, double scale, NativeRect bar, Rect start, IList<Rect> occupied, string mode)
    {
        return TaskbarAnchor.ComputeGeometry(new Rectangle(0,0,1920,1080),bar,start,occupied,78,36,20,scale,mode,x,y,6,2);
    }
    private static void CheckManualHorizontalOffsets()
    {
        NativeRect bottomBar_ = PaddedBarForGap();
        NativeRect topBar_ = new NativeRect { Left=0,Top=0,Right=1920,Bottom=48 };
        Rect start_ = new Rect(700,1032,48,48);
        List<Rect> empty_ = new List<Rect>();
        AnchorResult zero_ = Horizontal(0,0,1,bottomBar_,start_,empty_,"StartLeft");
        foreach (double x_ in new double[] { -200,-70,-1,0,1,20,50,100,200 })
        {
            AnchorResult shifted_ = Horizontal(x_,0,1,bottomBar_,start_,empty_,"StartLeft");
            Check(shifted_.Left == zero_.Left + x_, "Signed X moves linearly even across the Start button: " + x_);
            Check(!shifted_.IsFallback && shifted_.Top == zero_.Top, "Manual X never switches a valid automatic baseline: " + x_);
            Check(!shifted_.IsXClamped && shifted_.RequestedLeft == shifted_.Left, "Unclamped X reports its actual requested coordinate: " + x_);
        }
        List<Rect> occupied_ = new List<Rect> { new Rect(640,1032,20,48) };
        AnchorResult occupiedZero_ = Horizontal(0,0,1,bottomBar_,start_,occupied_,"StartLeft");
        foreach (double x_ in new double[] { -200,-70,0,50,200 })
        {
            AnchorResult shifted_ = Horizontal(x_,-10,1,bottomBar_,start_,occupied_,"StartLeft");
            Check(occupiedZero_.IsFallback && shifted_.IsFallback, "Occupied automatic baseline selects fallback independently of manual XY.");
            Check(shifted_.Left == occupiedZero_.Left + x_ && shifted_.Top == occupiedZero_.Top - 10, "Fallback applies the full signed XY offset once.");
        }
        AnchorResult topZero_ = Horizontal(0,0,1,topBar_,new Rect(700,0,48,48),empty_,"Above");
        AnchorResult topShifted_ = Horizontal(-70,3,1,topBar_,new Rect(700,0,48,48),empty_,"Above");
        Check(topZero_.IsFallback && topZero_.Top == 54, "Top taskbar fallback opens below its bottom edge.");
        Check(topShifted_.Left == topZero_.Left - 70 && topShifted_.Top == topZero_.Top + 3, "Top fallback accepts linear negative X and positive Y.");
        AnchorResult bottomZero_ = Horizontal(0,0,1,bottomBar_,start_,empty_,"Above");
        Check(bottomZero_.Top == bottomBar_.Top - 42, "Bottom taskbar fallback keeps its original outward baseline.");
        AnchorResult leftBar_ = Horizontal(0,0,1,new NativeRect { Left=0,Top=0,Right=48,Bottom=1080 },new Rect(0,300,48,48),empty_,"StartLeft");
        AnchorResult rightBar_ = Horizontal(0,0,1,new NativeRect { Left=1872,Top=0,Right=1920,Bottom=1080 },new Rect(1872,300,48,48),empty_,"StartLeft");
        Check(leftBar_.IsFallback && leftBar_.Left == 54 && leftBar_.Top == 306, "Left taskbar fallback selects the visible right-hand side.");
        Check(rightBar_.IsFallback && rightBar_.Left == 1788 && rightBar_.Top == 306, "Right taskbar fallback selects the visible left-hand side.");
        AnchorResult scaledZero_ = Horizontal(0,0,1.5,bottomBar_,start_,empty_,"StartLeft");
        AnchorResult scaledShifted_ = Horizontal(-70,-3,1.5,bottomBar_,start_,empty_,"StartLeft");
        Check(scaledShifted_.Left == scaledZero_.Left - 105 && scaledShifted_.Top == scaledZero_.Top - 4.5, "Both offsets use physical scaling at 150 percent DPI.");
        AnchorResult farLeft_ = Horizontal(-3000,0,1,bottomBar_,start_,empty_,"StartLeft");
        AnchorResult farRight_ = Horizontal(3000,0,1,bottomBar_,start_,empty_,"StartLeft");
        Check(farLeft_.Left == -16 && farRight_.Left == 1858, "X bounds clamp visible ring edges rather than transparent window padding.");
        Check(farLeft_.IsXClamped && farLeft_.RequestedLeft == zero_.Left - 3000, "Left clamp preserves the requested X for diagnostics.");
        Check(farRight_.IsXClamped && farRight_.RequestedLeft == zero_.Left + 3000, "Right clamp preserves the requested X for diagnostics.");
        Check(!farLeft_.IsFallback && !farRight_.IsFallback, "Screen clamps do not replace the selected baseline.");
        AnchorResult scaledEdge_ = Horizontal(-3000,0,1.5,bottomBar_,start_,empty_,"StartLeft");
        Check(scaledEdge_.Left == -24 && scaledEdge_.IsXClamped, "Transparent padding and screen clamps scale together.");
        AnchorResult offsetMonitor_ = TaskbarAnchor.ComputeGeometry(new Rectangle(-1920,-1080,1920,1080),new NativeRect { Left=-1920,Top=-1080,Right=0,Bottom=-1032 },new Rect(-1220,-1080,48,48),empty_,78,36,20,1,"Above",-70,0,6,2);
        Check(offsetMonitor_.Left == -1290 && offsetMonitor_.Top == -1026, "Top-edge fallback works with negative virtual-screen coordinates.");
    }
}
