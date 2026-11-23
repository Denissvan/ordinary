// Compile with the production inspection adapter, replacing hardware only.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MotionCtrl;
using UI;

internal static class FeedDistancePoseRegression
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static WS Reset()
    {
        VAR.gsys_set.bquit = false; WS.bquit = false; WS.bpause = false; UpDownLoad.bquit = false;
        COM.UDLoad1 = new UpDownLoad(); COM.UDLoad2 = new UpDownLoad();
        SerialCommander.LaserOn = SerialCommander.LaserOff = SerialCommander.Disposed = 0;
        SerialCommander.Sample = 100; SerialCommander.ReadError = false; SerialCommander.OffError = false;
        var ws = new WS();
        ws.list_gpio_zk.Add(new GPIO { Status = GPIO.IO_STA.OUT_ON });
        ws.list_gpio_zk.Add(new GPIO { Status = GPIO.IO_STA.OUT_OFF });
        Turntable.GetWSOnFeedPos = ws;
        return ws;
    }
    private static EM_RES Measure(WS ws)
    {
        object[] args = { ws, 1L, null };
        return (EM_RES)typeof(FeedDistanceInspection).GetMethod("Measure", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
    private static void VacuumRestored(WS ws)
    {
        Check(ws.list_gpio_zk[0].Status == GPIO.IO_STA.OUT_ON && ws.list_gpio_zk[1].Status == GPIO.IO_STA.OUT_OFF,
            "Original individual vacuum outputs must be restored");
    }
    public static int Main()
    {
        var ws = Reset();
        Check(Measure(ws) == EM_RES.OK, "Closed vertical fixture must flip then measure");
        Check(ws.Flips == 1 && ws.isUInFeedPos && ws.isFrDown && ws.isBkDown, "Keep clamps held during and after inspection");
        Check(SerialCommander.LaserOn == 1 && SerialCommander.LaserOff == 1 && SerialCommander.Disposed == 1, "Laser/port cleanup");
        VacuumRestored(ws);

        ws = Reset(); ws.ax_u.fenc_pos = ws.ax_u.pos1;
        Check(Measure(ws) == EM_RES.OK && ws.Flips == 0, "Horizontal retry must not flip again");
        ws = Reset(); ws.isFrDown = false; ws.isFrUp = true;
        Check(Measure(ws) != EM_RES.OK && ws.Flips == 0 && SerialCommander.LaserOn == 0, "Open clamp must block motion and laser");
        ws = Reset(); ws.ax_u.isStop = false;
        Check(Measure(ws) != EM_RES.OK && ws.Flips == 0 && SerialCommander.LaserOn == 0, "Moving U axis must block inspection");
        ws = Reset(); ws.ax_u.isALM = true;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.LaserOn == 0, "U alarm must block inspection");
        ws = Reset(); ws.FlipResult = EM_RES.ERR;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.LaserOn == 0, "Failed flip must not trigger laser"); VacuumRestored(ws);
        ws = Reset(); ws.StoppedAfterFlip = false;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.LaserOn == 0, "At coordinate but still moving is not ready"); VacuumRestored(ws);
        ws = Reset(); ws.CancelDuringFlip = true;
        Check(Measure(ws) == EM_RES.QUIT && SerialCommander.LaserOn == 0, "Cancelled flip must not measure"); VacuumRestored(ws);
        ws = Reset(); SerialCommander.ReadError = true;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.LaserOff == 1 && SerialCommander.Disposed == 1, "Read failure cleanup"); VacuumRestored(ws);
        ws = Reset(); SerialCommander.Sample = 200;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.LaserOff == 1, "Out of tolerance cannot pass"); VacuumRestored(ws);
        ws = Reset(); SerialCommander.OffError = true;
        Check(Measure(ws) != EM_RES.OK && SerialCommander.Disposed == 1, "Laser cleanup failure cannot pass"); VacuumRestored(ws);
        Check(WS.ComPortGuard.Guard.Wait(0), "COM guard must be released on failure"); WS.ComPortGuard.Guard.Release();
        Console.WriteLine("PASS: production inspection adapter horizontal/held-clamp sequence and failure cleanup (simulated hardware).");
        return 0;
    }
}

namespace MotionCtrl
{
    public enum EM_RES { OK, ERR, QUIT, PARA_ERR }
    public enum EM_ALM_STA { WAR_RED_FLASH, NOR_GREEN }
    public static class ERR_ALM { public enum EmErrItem { CaptureAbnomal } }
    public sealed class CARD { public bool isReady = true; public EM_RES UpdateIOBuff(bool force) { return EM_RES.OK; } }
    public sealed class GPIO
    {
        public enum IO_STA { OUT_ON, OUT_OFF }
        public IO_STA Status = IO_STA.OUT_OFF;
        public bool isON { get { return Status == IO_STA.OUT_ON; } }
        public bool isOFF { get { return !isON; } }
        public EM_RES SetOn() { Status = IO_STA.OUT_ON; return EM_RES.OK; }
        public EM_RES SetOff() { Status = IO_STA.OUT_OFF; return EM_RES.OK; }
    }
    public sealed class AXIS
    {
        public enum HOME_STA { OK }
        public CARD card = new CARD(); public HOME_STA home_status;
        public bool isALM, isStop = true, isORG = true;
        public double fenc_pos, pos1;
    }
    public sealed class Msg
    {
        public enum EM_MSGTYPE { NOR, DBG, WAR, ERR }
        public void AddMsg(EM_MSGTYPE kind, string text) { }
    }
    public sealed class SystemState { public bool bquit, bclose; }
    public sealed class SystemInfo { public void Set(EM_ALM_STA status, string text, int count, bool force) { } }
    public static class VAR
    {
        public static bool ClearMt;
        public static SystemState gsys_set = new SystemState();
        public static Msg msg = new Msg(); public static SystemInfo sys_inf = new SystemInfo();
    }
    public static class PT_SET
    {
        public static bool FeedDistanceCheckEnabled = true, AFC_distance_check_open, DCC_distance_check_open, OTP_distance_check_open,
            AFC_luxcct_check_open, DCC_luxcct_check_open, OTP_luxcct_check_open;
        public static int CheckWs = 1;
        public static string COM_1 = "SIM";
        public static double FeedDistanceStandard = 100, FeedDistanceTolerance = 0.1, FeedDistanceCompensation, distance_coefficient = 1;
    }
}
namespace UI
{
    public sealed class warning { }
    public static class MT
    {
        public static CARD CARD_ECI2400_0 = new CARD();
        public static GPIO GPIO_IN_TT_INP = new GPIO { Status = GPIO.IO_STA.OUT_ON }, GPIO_IN_TT_MOVE = new GPIO(),
            GPIO_IN_TT_ALM = new GPIO(), GPIO_OUT_TT_FWD = new GPIO(), GPIO_OUT_TT_REV = new GPIO(), GPIO_IN_LIGHT = new GPIO();
        public sealed class ST_WARN { public string title, msg, lb_msg, ok_txt, abort_txt; }
        public static System.Windows.Forms.DialogResult Display_frwarn(warning dialog, ST_WARN info, ERR_ALM.EmErrItem item)
        { return System.Windows.Forms.DialogResult.Cancel; }
    }
    public static class Turntable { public static bool NeedRecovery; public static WS GetWSOnFeedPos; }
    public sealed class WS
    {
        public static bool Demo, bquit, bpause;
        public int num; public string disc = "SIM";
        public AXIS ax_u = new AXIS { fenc_pos = 90, pos1 = 0 };
        public bool isFrDown = true, isBkDown = true, isFrUp, isBkUp;
        public List<GPIO> list_gpio_zk = new List<GPIO>();
        public int Flips; public bool StoppedAfterFlip = true, CancelDuringFlip;
        public EM_RES FlipResult = EM_RES.OK;
        public bool isUInFeedPos { get { return Math.Abs(ax_u.fenc_pos - ax_u.pos1) < 0.5; } }
        public EM_RES TurnToFeedSafe(ref bool quit)
        {
            Flips++; foreach (var gpio in list_gpio_zk) gpio.SetOff();
            if (FlipResult == EM_RES.OK) ax_u.fenc_pos = ax_u.pos1;
            ax_u.isStop = StoppedAfterFlip;
            if (CancelDuringFlip) quit = true;
            return FlipResult;
        }
        public static class ComPortGuard
        {
            public static readonly SemaphoreSlim Guard = new SemaphoreSlim(1, 1);
            public static SemaphoreSlim Get(string port) { return Guard; }
        }
    }
    public sealed class Position { public double y = 100; }
    public sealed class XT { public Position st_cap_pos = new Position(); }
    public sealed class UpDownLoad
    {
        public static bool bquit;
        public bool Demo, brun_ud; public Task UpDownTask;
        public AXIS ax_x = new AXIS(), ax_y = new AXIS(), ax_z = new AXIS(), ax_u1 = new AXIS(), ax_u2 = new AXIS();
        public List<XT> list_xt = new List<XT> { new XT(), new XT() };
    }
    public static class COM { public static UpDownLoad UDLoad1 = new UpDownLoad(), UDLoad2 = new UpDownLoad(); }
}
namespace UI.Class
{
    public static class AutoInspectionParameter
    {
        public static bool ifcheckontime, ifcheck, ifcheckontime_enabled;
        public static long _elapsedMilliseconds;
        public static void SaveToTxt(params object[] values) { }
    }
}
public sealed class SerialCommander : IDisposable
{
    public static int LaserOn, LaserOff, Disposed;
    public static double Sample = 100;
    public static bool ReadError, OffError;
    public SerialCommander(string port, int baud, int timeout) { }
    public void SetLaserStatus(bool on)
    {
        if (on)
        {
            if (!Turntable.GetWSOnFeedPos.isUInFeedPos || !Turntable.GetWSOnFeedPos.ax_u.isStop ||
                !Turntable.GetWSOnFeedPos.isFrDown || !Turntable.GetWSOnFeedPos.isBkDown)
                throw new Exception("Laser called before horizontal/stop/clamp confirmation");
            LaserOn++;
        }
        else { LaserOff++; if (OffError) throw new Exception("Injected laser off failure"); }
    }
    public double ReadInspectionDistance(int timeout, Func<bool> cancelled)
    { if (ReadError) throw new TimeoutException(); return Sample; }
    public void Dispose() { Disposed++; }
}
