using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Windows.Forms;
using System.IO;
using System.Threading;
using MotionCtrl;
using System.Drawing;
using System.Data;

using Cognex.VisionPro;
using Cognex.VisionPro.Exceptions;
using Cognex.VisionPro.ImageProcessing;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.CalibFix;
using Cognex.VisionPro.PMAlign;
using Cognex.VisionPro.Display;
using Cognex.VisionPro.ImageFile;
using System.Reflection;


using System.ComponentModel;
using System.Drawing.Imaging;
using DevReport;
using UI.Class;

namespace UI
{
    /// <summary>
    /// 委托相机硬件触发
    /// </summary>
    /// <param name="delay">脉冲宽度(ms)</param>
    /// <returns></returns>
    public delegate EM_RES CAM_HW_TRIGER(int delay = 10);
    /// <summary>
    /// 硬件定位委托，使能时定位并返回当前坐标。
    /// </summary>
    /// <param name="bEnX">使能X定位</param>
    /// <param name="bEnY">使能Y定位</param>
    /// <param name="bEnZ">使能Z定位></param>
    /// <param name="bEnA">使能U定位</param>
    /// <param name="StPos">定位位置/返回坐标</param>
    /// <param name="Delay">延迟(ms)后返回坐标</param>
    /// <returns></returns>
    public delegate EM_RES MOVE_HANDLE(ref bool bquit, bool bEnX, bool bEnY, bool bEnZ, bool bEnA, bool bIsU1, ref ST_XYZA StPos, string CamName = "", int Delay = 300);
    public class Cam : IDisposable
    {
        #region 硬件/参数
        //public double mExposure;
        //public double mLiveExposure;
        //public double mBrightness;
        //public double mContrast;
        public CogAcqTriggerModelConstants mTriggerMode;
        public string mSerialNumber;
        public string mName;
        public string disc;
        public string englishdisc;
        public int mID = -1;
        public bool bInit = false;
        public event EventHandler CompeletedEventHandler;
        public MOVE_HANDLE MoveHandle = null;
        public CAM_HW_TRIGER HwTrigerHandle = null;
        public bool bUnitCam = false;
        public bool bReverseX = false;
        public bool bReverseY = false;
        public GPIO TriIO = null;
        public bool bSaveImage = false;
        bool block = false;
        public bool FlushOk = false;
        public bool FlushUpdate = false;
        public const ushort CamNpointCail = 0;
        public const ushort TaskNpointCail = 1;
        #endregion
        #region 状态定义
        public enum CAM_STA
        {
            [Description("未知")]
            UNKOWN,
            [Description("初始化中")]
            INIT,
            [Description("就绪")]
            READY,
            [Description("断开")]
            DISCONNECT,
            [Description("运行")]
            RUN,
            [Description("OK")]
            OK,
            [Description("NG")]
            NG,
            [Description("实况")]
            LIVE,
            [Description("错误")]
            ERROR
        }
        public CAM_STA status;
        #endregion
        #region 构造
        public Cam(string name, string disc, bool bUnitCam = false, bool bReverseX = false, bool bReverseY = false, CAM_HW_TRIGER triger = null, MOVE_HANDLE move_handle = null, GPIO Tri_io = null)
        {
            mName = name;
            this.disc = disc;
            this.bUnitCam = bUnitCam;
            HwTrigerHandle = triger;
            MoveHandle = move_handle;
            this.bReverseX = bReverseX;
            this.bReverseY = bReverseY;
            this.TriIO = Tri_io;
            this.englishdisc = name;
        }

        ~Cam()
        {
            Dispose();
        }
        #endregion
        #region 初始化
        public bool isInit
        {
            get
            {
                return bInit;
            }
        }
        public EM_RES Init(string filename)
        {
            if (bInit) return EM_RES.OK;
            if (LoadCfg("") == EM_RES.ERR) return EM_RES.ERR;

            CogFrameGrabbers fgs = new CogFrameGrabbers();
            string camName = new DirectoryInfo(filename).Name;
            try
            {
                foreach (ICogFrameGrabber fg in fgs)
                {
                    try
                    {
                        if (fg.SerialNumber == mSerialNumber)
                        {
                            try
                            {
                                if (mFrameGrabber != null)
                                {
                                    if (mFrameGrabber.GetStatus(true) == CogFrameGrabberStatusConstants.Active) continue;
                                    mFrameGrabber.Disconnect(true);
                                }
                                mFrameGrabber = fg;

                                try
                                {
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("LineSelector", "Line1");
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("LineMode", "Input");
                                    // mFrameGrabber.OwnedGigEAccess.SetFeature("LineInverter", "1");

                                    mFrameGrabber.OwnedGigEAccess.SetFeature("LineSelector", "Out1");
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("LineMode", "Output");

                                    mFrameGrabber.OwnedGigEAccess.SetFeature("TriggerSelector", "FrameStart");
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("TriggerMode", "On");
                                    // mFrameGrabber.OwnedGigEAccess.SetFeature("TriggerActivation", "FallingEdge");
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("TriggerActivation", "RisingEdge");

                                    mFrameGrabber.OwnedGigEAccess.SetFeature("LineSource", "ExposureActive");
                                    mFrameGrabber.OwnedGigEAccess.SetFeature("TriggerSource", "Line1");

                                    if (bReverseX) mFrameGrabber.OwnedGigEAccess.SetFeature("ReverseX", "true");
                                    //if (bReverseY) mFrameGrabber.OwnedGigEAccess.SetFeature("ReverseY", "true");

                                    var str = mFrameGrabber.OwnedGigEAccess.GetAvailableFeatures("TransportLayer");
                                    if (str.IndexOf("LineDebouncerTimeAbs") > 0) mFrameGrabber.OwnedGigEAccess.SetFeature("LineDebouncerTimeAbs", "10");
                                }
                                catch (Exception ex)
                                {
                                    bInit = false;
                                    mFrameGrabber.Disconnect(true);
                                    mAcqFifo = null;
                                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 配置相机出错,{1}", disc, ex.Message) : string.Format("{0}Error configuring camera    ({1} 配置相机出错,{2})", englishdisc, disc, ex.Message), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                                    return EM_RES.ERR;
                                }

                                //create FIFO
                                mAcqFifo = mFrameGrabber.CreateAcqFifo(fg.AvailableVideoFormats[0], CogAcqFifoPixelFormatConstants.Format8Grey, 0, true);
                                //int X, Y, Width, Height;
                                //mAcqFifo.OwnedROIParams.GetROIXYWidthHeight(out X, out Y, out Width, out Height);
                                //mAcqFifo.OwnedROIParams.SetROIXYWidthHeight(X, Y, Width, Height);
                                //mAcqFifo.OwnedROIParams.SetROIXYWidthHeight(432, 428, 1600, 1200);
                                mAcqFifo.OwnedGigEVisionTransportParams.TransportTimeout = 500;
                                //mAcqFifo.OwnedGigEVisionTransportParams.PacketSize = 1024;

                                mAcqFifo.Complete -= new CogCompleteEventHandler(AcqFifoComplete);
                                mAcqFifo.Complete += new CogCompleteEventHandler(AcqFifoComplete);
                                mAcqFifo.Overrun -= new CogOverrunEventHandler(AcqFifoOverRun);
                                mAcqFifo.Overrun += new CogOverrunEventHandler(AcqFifoOverRun);
                                CapCfg(liveCapCfg);

                                mAcqFifo.Prepare();
                                bInit = true;
                                status = CAM_STA.READY;
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}(SN={1})初始化成功!", disc, mSerialNumber) : string.Format("{0} (SN={2}) initialize Initialization successful!   ({1}(SN={2})初始化成功!)", englishdisc, disc, mSerialNumber));
                                return EM_RES.OK;
                            }
                            catch (Exception ex)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 连接出错,{1}", disc, ex.Message) : string.Format("{0} link err,{2}   ({1} 连接出错,{2})", englishdisc, disc, ex.Message), DReport.EmErrCode.ConnectFailed, (int)DReport.EmHareware.Cam);
                                bInit = false;
                                mFrameGrabber.Disconnect(true);
                                mAcqFifo = null;
                                return EM_RES.ERR;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0},{1}!", VAR.IsChinese ? disc : englishdisc, ex.Message), DReport.EmErrCode.ConnectFailed, (int)DReport.EmHareware.Cam);

                    }
                }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0},{1}!", VAR.IsChinese ? disc : englishdisc, ex.Message));
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}(SN={1}) 找不到对应相机", disc, mSerialNumber) : string.Format("{0} (SN={2}) Can't find camera!   ({1}(SN={2}) 找不到对应相机)", englishdisc, disc, mSerialNumber), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
            return EM_RES.ERR;
        }
        #endregion
        #region 拍照/光源参数
        public enum EM_LIGHT_TYPE
        {
            IO,
            PWM,
        }
        public struct ST_LIGHT
        {
            public GPIO IO;
            public EM_LIGHT_TYPE Type;
            public short Value;
        }
        public struct ST_CAP_CFG
        {
            public List<ST_LIGHT> LightCfg;
            public double Exposure;
            public double Brightness;
            public double Contrast;
        }
        public ST_CAP_CFG curCapCfg = new ST_CAP_CFG();
        /// <summary>
        /// 拍照配置
        /// </summary>
        /// <param name="st_cfg"></param>
        /// <param name="boff"></param>
        /// <returns></returns>
        public EM_RES CapCfg(ST_CAP_CFG st_cfg, bool boff = false)
        {
            if (mAcqFifo == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 取图未初始化!", mName) : string.Format("{0} mAcqFifo is Uninitialized!   ({0} 取图未初始化!)", mName), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
            }

            if (st_cfg.LightCfg != null && st_cfg.LightCfg.Count > 0)
            {
                foreach (ST_LIGHT cfg in st_cfg.LightCfg)
                {
                    if (cfg.IO != null)
                    {
                        if (cfg.Type == EM_LIGHT_TYPE.IO)
                        {
                            cfg.IO.Status = boff ? GPIO.IO_STA.OUT_OFF : GPIO.IO_STA.OUT_ON;
                        }
                        //else if(cfg.Type == EM_LIGHT_TYPE.PWM);
                    }
                }
            }

            try
            {
                int t = Environment.TickCount;
                bool bchanged = false;
                ICogAcqContrast contrastParams = mAcqFifo.OwnedContrastParams;
                if (contrastParams != null && contrastParams.Contrast != st_cfg.Contrast) bchanged = true;
                ICogAcqBrightness brightnessParams = mAcqFifo.OwnedBrightnessParams;
                if (brightnessParams != null && brightnessParams.Brightness != st_cfg.Brightness) bchanged = true;
                if (st_cfg.Exposure != mAcqFifo.OwnedExposureParams.Exposure) bchanged = true;
                if (mAcqFifo.OwnedTriggerParams.TriggerModel != mTriggerMode) bchanged = true;
                if (bchanged)
                {
                    mAcqFifo.OwnedTriggerParams.TriggerEnabled = false;
                    mAcqFifo.OwnedExposureParams.Exposure = st_cfg.Exposure;
                    if (contrastParams != null) contrastParams.Contrast = st_cfg.Contrast;
                    if (brightnessParams != null) brightnessParams.Brightness = st_cfg.Brightness;
                    mAcqFifo.OwnedTriggerParams.TriggerModel = mTriggerMode;
                    mAcqFifo.OwnedTriggerParams.TriggerEnabled = true;
                    //mAcqFifo.Flush();
                }
                //mAcqFifo.Flush();
               // VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("{0}完成CapCfg配置,T={1}ms", disc, Environment.TickCount - t) : string.Format("{0} CapCfg finished,T={1}ms   ({1}完成CapCfg配置,T={2}ms)", englishdisc, disc, Environment.TickCount - t));
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 设置参数（Exp={1},Brt={2},Con={3}）异常!{4}", mName, st_cfg.Exposure, st_cfg.Brightness, st_cfg.Contrast, ex.Message) : string.Format("{0} Set parameters （Exp={1},Brt={2},Con={3}）err!{4}   ({0} 设置参数（Exp={1},Brt={2},Con={3}）异常!{4})", mName, st_cfg.Exposure, st_cfg.Brightness, st_cfg.Contrast, ex.Message), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
            }
            return EM_RES.OK;
        }
        #endregion
        #region 参数存取
        /// <summary>
        /// 加载配置
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public EM_RES LoadCfg(string filename = "")
        {
            IniFile inf = new IniFile(Path.GetFullPath("..") + "\\syscfg\\syscfg.ini");
            mTriggerMode = (CogAcqTriggerModelConstants)inf.ReadInteger(mName, "TRG_MODE", (int)CogAcqTriggerModelConstants.Manual);
            mSerialNumber = inf.ReadString(mName, "SN", "");
            string str = inf.ReadString(mName, "DISC", "");
            if (str.Length > 3) disc = str.Trim('\0');

            if (filename.Length < 3) filename = string.Format("{0}\\product\\{1}\\Camera\\{2}\\config.ini", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mName);
            inf = new IniFile(filename);
            curCapCfg.Contrast = inf.ReadDouble(mName, "CONTRAST", curCapCfg.Contrast);
            curCapCfg.Exposure = inf.ReadDouble(mName, "EXPOSURE", curCapCfg.Exposure);
            curCapCfg.Brightness = inf.ReadDouble(mName, "BRIGHTNESS", curCapCfg.Brightness);
            liveCapCfg = curCapCfg;
            liveCapCfg.Exposure = inf.ReadDouble(mName, "LIVE_EXPOSURE", liveCapCfg.Exposure);
            bSaveImage = inf.ReadBool(mName, "EN_SAVEIMAGE", false);

            if (mSerialNumber == "")
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}加载参数异常！", disc) : string.Format("{0} ERROR:Load parameters!   ({1}加载参数异常！)", englishdisc, disc), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
            return EM_RES.OK;
        }
        /// <summary>
        /// 保存配置
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public EM_RES SaveCfg(string filename = "")
        {
            EM_RES res = EM_RES.OK;
            if (filename.Length < 3) filename = Path.GetFullPath("..") + "\\syscfg\\syscfg.ini";
            string[] backup = File.ReadAllLines(filename);
            bool ischange = false;
            IniFile inf = new IniFile(filename);


            inf.WriteInteger(mName, "TRG_MODE", (int)mTriggerMode, ref ischange, true, filename);
            inf.WriteString(mName, "SN", mSerialNumber, ref ischange, true, filename);
            inf.WriteString(mName, "DISC", disc, ref ischange, true, filename);
            if (ischange)
            {
                //创建backup
                //第一层
                string backup_filename = string.Format("{0}\\syscfg\\backup", Path.GetFullPath(".."));
                res = SYS_PUD.CopyFile2(backup_filename);
                if (res != EM_RES.OK) return res;
                //文件
                string[] str = filename.Split('\\');
                res = SYS_PUD.FileWriteLine(string.Format(str[str.Length - 1]), backup, backup_filename);
                if (res != EM_RES.OK) return res;
            }

            filename = string.Format("{0}\\product\\{1}\\Camera\\{2}\\config.ini", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mName);
            inf = new IniFile(filename);
            inf.WriteDouble(mName, "CONTRAST", curCapCfg.Contrast, ref ischange, false);
            inf.WriteDouble(mName, "EXPOSURE", curCapCfg.Exposure, ref ischange, false);
            inf.WriteDouble(mName, "BRIGHTNESS", curCapCfg.Brightness, ref ischange, false);
            inf.WriteDouble(mName, "LIVE_EXPOSURE", liveCapCfg.Exposure, ref ischange, false);

            return EM_RES.OK;
        }

        /// <summary>
        /// 加载与保存图片标志
        /// </summary>
        /// <param name="issave">true:保存 false:加载</param>
        /// <param name="filename"></param>
        /// <returns></returns>
        public EM_RES LoadOrSaveImgCfg(bool issave = true, string filename = "")
        {
            if (filename.Length < 3) filename = string.Format("{0}\\product\\{1}\\Camera\\{2}\\config.ini", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mName);
            IniFile inf = new IniFile(filename);
            bool ischange = false;
            if (issave) inf.WriteBool(mName, "EN_SAVEIMAGE", bSaveImage, ref ischange, false);
            else bSaveImage = inf.ReadBool(mName, "EN_SAVEIMAGE", false);
            return EM_RES.OK;
        }
        #endregion
        #region 校正关系
        public class CaliTool
        {
            public enum EM_TYPE { CogCalibNPointToNPointTool, CogCalibCheckerboardTool };
            public EM_TYPE type;
            public string name = "";
            public string disc = "";
            public object mTool = null;

            /// <summary>
            /// 加载校正工具
            /// </summary>
            /// <param name="filename">指定文件路径</param>
            /// <returns></returns>
            public EM_RES Load(string filename)
            {
                if (filename.Length < 3) return EM_RES.PARA_ERR;
                if (filename.Contains("Checkerboard")) type = EM_TYPE.CogCalibCheckerboardTool;
                else if (filename.Contains("NPoint")) type = EM_TYPE.CogCalibNPointToNPointTool;
                else
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}/{1} CaliTool类型异常！", disc, Path.GetFileNameWithoutExtension(filename)) : string.Format("{0}/{1} CaliTool type exception     ({0}/{1} CaliTool类型异常！)", disc, Path.GetFileNameWithoutExtension(filename)), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.PARA_ERR;
                }
                if (File.Exists(filename))
                {

                    name = Path.GetFileNameWithoutExtension(filename);
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("加载 {0}/{1}...", disc, name) : string.Format("Loading {0}/{1}...   (加载 {0}/{1}...)", disc, name));
                    mTool = CogSerializer.LoadObjectFromFile(filename, typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter), CogSerializationOptionsConstants.Minimum);
                    return EM_RES.OK;
                }
                else
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("加载 {0}出错，文件不存在，", disc, filename) : string.Format("ERROR:Load {0},file doesn't exist   (加载 {0}出错，文件不存在，)", disc, filename), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
            }
            /// <summary>
            /// 根据已加载工具执行图片校正
            /// </summary>
            /// <param name="Image">待校正图片，校正后返回</param>
            /// <returns></returns>
            public EM_RES Execute(ref ICogImage Image)
            {
                //check
                if (mTool == null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 未初始化CaliTool! ", disc) : string.Format("{0} CaliTool is Uninitialized!   ({0} 未初始化CaliTool!)", disc), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                    return EM_RES.PARA_ERR;
                }
                try
                {
                    switch (type)
                    {
                        case EM_TYPE.CogCalibCheckerboardTool:
                            ((CogCalibCheckerboardTool)mTool).InputImage = Image;
                            ((CogCalibCheckerboardTool)mTool).Run();
                            Image = ((CogCalibCheckerboardTool)mTool).OutputImage;
                            break;
                        case EM_TYPE.CogCalibNPointToNPointTool:
                            ((CogCalibNPointToNPointTool)mTool).InputImage = Image;
                            ((CogCalibNPointToNPointTool)mTool).Run();
                            Image = ((CogCalibNPointToNPointTool)mTool).OutputImage;
                            break;
                        default:
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}未定义CaliTool类型!", disc) : string.Format("{0} Undefined CaliTool type!    ({0}未定义CaliTool类型!)", disc), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} CaliTool出错，{1}", disc, ex.Message) : string.Format("{0} ERROR:CaliTool,{1}    ({0} CaliTool出错，{1})", disc, ex.Message), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
                return EM_RES.OK;
            }
            /// <summary>
            /// 根据转换关系映射点，无转换关系则返回输入点
            /// </summary>
            /// <param name="Pt">待映射点</param>
            /// <returns>返回映射后的点，A为映射角度</returns>
            public ST_XYA MapPoint(ST_XY Pt)
            {
                //check  
                if (mTool == null || type != EM_TYPE.CogCalibNPointToNPointTool) return Pt.ToXYA();
                //get transform
                CogTransform2DLinear TransformMap = (CogTransform2DLinear)((CogCalibNPointToNPointTool)mTool).Calibration.GetComputedUncalibratedFromCalibratedTransform();
                //stransform
                ST_XYA MatPt = new ST_XYA();
                TransformMap.MapPoint(Pt.x, Pt.y, out MatPt.x, out MatPt.y);
                MatPt.a = TransformMap.Rotation;
                return MatPt;
            }
            /// <summary>
            /// 根据转换关系映射点数组，无转换关系则返回输入点数组
            /// </summary>
            /// <param name="Pt">待映射点数组</param>
            /// <returns>返回映射后的点数组，A为映射角度</returns>
            public ST_XYA[] MapPoints(ST_XY[] Pt)
            {
                if (Pt == null || Pt.Length == 0) return null;

                //stransform
                ST_XYA[] MapPt = new ST_XYA[Pt.Length];
                for (int i = 0; i < Pt.Length; i++)
                {
                    MapPt[i] = MapPoint(Pt[i]);
                }
                return MapPt;
            }
        }
        public List<CaliTool> ListCaliTool = new List<CaliTool>();
        /// <summary>
        /// 提取校正链比例
        /// </summary>
        /// <returns></returns>
        public EM_RES GetAllCaliToolXYAScale(ref ST_XYA st_transform)
        {
            st_transform = new ST_XYA(1, 1, 0);
            if (ListCaliTool.Count <= 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 不存在或未加载校正关系!", mName) : string.Format("Calibration relationship does not exist or is not loaded    ({0} 不存在或未加载校正关系!)", mName), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_TASK_LOAD_ERR;
            }
            CogTransform2DLinear[] stransform = new CogTransform2DLinear[ListCaliTool.Count];

            foreach (CaliTool tool in ListCaliTool)
            {

                if (tool.type == CaliTool.EM_TYPE.CogCalibCheckerboardTool)
                    stransform[ListCaliTool.IndexOf(tool)] = (CogTransform2DLinear)((CogCalibCheckerboardTool)tool.mTool).Calibration.OwnedWarpParams.GetOutputImageRootFromCalibratedTransform();
                else if (tool.type == CaliTool.EM_TYPE.CogCalibNPointToNPointTool)
                    stransform[ListCaliTool.IndexOf(tool)] = (CogTransform2DLinear)((CogCalibNPointToNPointTool)tool.mTool).Calibration.GetComputedUncalibratedFromCalibratedTransform();
                else break;

                if (Math.Abs(stransform[ListCaliTool.IndexOf(tool)].ScalingX) < 0.001 || Math.Abs(stransform[ListCaliTool.IndexOf(tool)].ScalingX) < 0.001)
                {
                    MessageBox.Show(VAR.IsChinese ? "相机像素比例异常，请先校准后再扫描!" : "The camera pixel ratio is abnormal, please calibrate before scanning!\r\n相机像素比例异常，请先校准后再扫描!");
                    return EM_RES.ERR;
                }
            }

            if (stransform.Length < 1) return EM_RES.ERR;
            if (bUnitCam)
            {
                st_transform.x = st_transform.x / stransform[0].ScalingX;
                st_transform.y = st_transform.y / stransform[0].ScalingY;
                st_transform.a = CogMisc.DegToRad(stransform[0].Rotation);
            }
            else
            {
                if (stransform.Length < 2) return EM_RES.ERR;
                st_transform.x = (st_transform.x / stransform[1].ScalingX) / stransform[0].ScalingX;
                st_transform.y = (st_transform.y / stransform[1].ScalingY) / stransform[0].ScalingY;
                st_transform.a = CogMisc.DegToRad(stransform[0].Rotation);
            }
            return EM_RES.OK;
        }
        /// <summary>
        /// 加载校正关系
        /// </summary>
        /// <param name="path">存储路径</param>
        /// <returns></returns>
        public EM_RES LoadCaliTool(string path = "")
        {
            if (path.Length < 3) path = string.Format("{0}\\syscfg\\Calibration\\{1}\\", Path.GetFullPath(".."), mName);
            ListCaliTool.Clear();

            //get file
            if (!Directory.Exists(path))
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 路径不存在，未加载任何校正文件!", mName) : string.Format("{0} Path does not exist, no calibration file is loaded    ({0} 路径不存在，未加载任何校正文件!)", mName), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_TASK_LOAD_ERR;
            }

            string[] vppfile = Directory.GetFiles(path, "*.vpp");
            if (vppfile.Length == 0) return EM_RES.CAM_TASK_LOAD_ERR;

            //load all ToolBlock             
            foreach (string filneam in vppfile)
            {
                if (!File.Exists(filneam)) continue;

                CaliTool tool = new CaliTool();
                if (EM_RES.OK == tool.Load(filneam))
                {
                    ListCaliTool.Add(tool);
                }
            }

            if (ListCaliTool.Count == 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 未加载任何校正文件!", mName) : string.Format("{0} No calibration files loaded!    ({0} 未加载任何校正文件!)", mName), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_TASK_LOAD_ERR;
            }
            ListCaliTool.Sort((a, b) => { return a.name.CompareTo(b.name); });
            return EM_RES.OK;
        }
        #endregion
        #region 取图
        private ICogFrameGrabber mFrameGrabber = null;
        public ICogAcqFifo mAcqFifo = null;
        public bool bcontinue;
        public bool bRunImage = true;
        public bool bNewImage = false;
        private static object obj = new object();
        private static object obj1 = new object();
        // public ICogImage Image;
        public CogImage8Grey Image;
        public int numAcqs = 0;
        public bool bIsLive = false;
        #region 事件
        private void AcqFifoOverRun(object sender, CogOverrunEventArgs e)
        {
            if (curTask != null)
            {
                curTask.ResData.Clear(true);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 图像丢失,{1}", disc, curTask.TaskName) : string.Format("{0} Image loss,{2}     ({1} 图像丢失,{2})", englishdisc, disc, curTask.TaskName), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
            }
            else VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 图像丢失！", disc) : string.Format("{0} Image loss    ({1} 图像丢失！)", englishdisc, disc), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);

            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                "CaptureTrace 图像丢失(Overrun) Cam={0},Session={1}，本批作废", mName, captureSessionId));
            InvalidateCaptureSession("相机图像丢失(Overrun)");
        }
        private void AcqFifoComplete(object sender, CogCompleteEventArgs e)
        {
            //Task t = new Task(() =>
            //{
            int numReady, numPending;
            bool busy;
            CogAcqInfo info = new CogAcqInfo();
            VisionTask vstask = curTask;
            CaptureTicket<VisionTask> ticket = null;
            var taskid = -1;
            CogImage8Grey curImage = null;
            bool taskScheduled = false;
            try
            {
                //get FIFO status and run image
                mAcqFifo.GetFifoState(out numPending, out numReady, out busy);
                //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0},Pb{1},Rb{2},{3},{4}", disc, numPending, numReady, busy, curTask!=null? curTask.TaskName:"null"));
                if (numReady > 0)
                {
                    lock (obj)
                    {
                        //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0},P{1},R{2},{3},{4}", disc, numPending, numReady, busy, curTask != null ? curTask.TaskName : "null"));
                        //get img
                        // 锁内重新确认有已完成图像：恢复线程可能已清空FIFO(清空后会出现虚假Complete事件)
                        mAcqFifo.GetFifoState(out numPending, out numReady, out busy);
                        if (numReady <= 0) return;
                        // 从这里到认领Ticket结束都在 lock(obj) 内；相机恢复的结束步骤也持有 lock(obj)，
                        // 因此已取进内存但尚未认领的图像，不会跨过恢复结束点去认领新批次Ticket
                        Image = mAcqFifo.CompleteAcquireEx(info) as CogImage8Grey;
                        Interlocked.Increment(ref acquiredFrameCount);
                        Interlocked.Exchange(ref lastFrameTicksUtc, DateTime.UtcNow.Ticks);
                        lastFrameTriggerNumber = info.TriggerNumber;
                        CogTransform2DLinear TransForm = new CogTransform2DLinear();
                        TransForm.TranslationX = 0;
                        TransForm.TranslationY = 0;
                        TransForm.Scaling = 1;
                        TransForm.Rotation = 0;
                        Image.PixelFromRootTransform = TransForm;
                        inputImageCnt++;//输入图像次数计数，防止误触发！
                        bNewImage = true;
                        if (!bRunImage)
                        {
                            captureTickets.NoteFrameTrigger(info.TriggerNumber);
                            return;
                        }
                        string frameNote;
                        CaptureFrameKind frameKind = captureTickets.TakeNextFrame(info.TriggerNumber, out ticket, out frameNote);
                        if (frameKind == CaptureFrameKind.DuringRecovery)
                        {
                            // 丢帧恢复期间不存在任何有效Ticket，收到的图像身份不明确，只能丢弃
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                "CaptureTrace 恢复期间图像已丢弃 Cam={0},Session={1},{2},AcqTs={3}",
                                mName, captureSessionId, frameNote, info.TimeStamp));
                            return;
                        }
                        if (ticket != null)
                        {
                            vstask = ticket.Task;
                            taskid = ticket.Sequence;
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                                "CaptureTrace 回图 Cam={0},Session={1},Ticket={2},Hole={3},Task={4},TriNum={5},CfgToFrame={6}ms,AcqTs={7}{8}",
                                mName, ticket.SessionId, ticket.TicketId, ticket.PositionIndex,
                                vstask == null ? "NULL" : vstask.TaskName, info.TriggerNumber,
                                ticket.ConfigToFrameMs, info.TimeStamp,
                                string.IsNullOrEmpty(frameNote) ? string.Empty : "," + frameNote));
                            if (frameKind == CaptureFrameKind.TicketedTriggerMismatch)
                            {
                                // 触发号与本批首帧推算值不一致：可能丢帧/多帧导致FIFO错位
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                    "CaptureTrace 触发号不连续 Cam={0},Session={1},Ticket={2},Hole={3},{4},拦截={5}",
                                    mName, ticket.SessionId, ticket.TicketId, ticket.PositionIndex, frameNote, CaptureGuardBlocking));
                                if (CaptureGuardBlocking)
                                {
                                    InvalidateCaptureSession("相机触发号不连续");
                                    captureTickets.Complete(ticket);
                                    return;
                                }
                            }
                            if (captureTickets.GetState(ticket) == CaptureTicketState.Expired)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                    "CaptureTrace 迟到图像已丢弃 Cam={0},Session={1},Ticket={2},Hole={3},TriNum={4},CfgToFrame={5}ms",
                                    mName, ticket.SessionId, ticket.TicketId, ticket.PositionIndex, info.TriggerNumber,
                                    ticket.ConfigToFrameMs));
                                captureTickets.Complete(ticket);
                                return;
                            }
                        }
                        else if (captureTickets.HasOutstanding)
                        {
                            // 本批Ticket都已认领，却又来了一张图：说明帧数多于触发登记数，本批身份不可信
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                "CaptureTrace 多余图像 Cam={0},Session={1},TriNum={2},AcqTs={3}，本批作废",
                                mName, captureSessionId, info.TriggerNumber, info.TimeStamp));
                            InvalidateCaptureSession("收到多余图像");
                            return;
                        }
                        else if (TryConsumeManualCredit())
                        {
                            // 手动/标定/连续采集：Triger()时没有登记Ticket，按原curTask方式处理
                            vstask = curTask;
                            taskid = curTaskIdx;
                            NextTask();
                        }
                        else
                        {
                            // 没有任何登记、也不是手动触发的图像：可能是多余触发、误触发或上一批残留。
                            // 身份不明确，不写入任何任务，并作废当前批次
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                "CaptureTrace 未登记Ticket的图像已丢弃 Cam={0},Session={1},TriNum={2},AcqTs={3}，本批作废",
                                mName, captureSessionId, info.TriggerNumber, info.TimeStamp));
                            InvalidateCaptureSession("收到未登记Ticket的图像");
                            return;
                        }
                        if (vstask == null)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}图像未找到对应任务,TriNum:{1}", disc, info.TriggerNumber));
                            captureTickets.Complete(ticket);
                            return;
                        }
                        curImage=  new CogImage8Grey(Image);
                        curImage.SetPixel(0, 0, (byte)taskid);
                        curImage.SetPixel(1, 0, (byte)(info.TriggerNumber&0xFF));
                        if(vstask.ResData.bOK&& vstask.ResData.bUpdate)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("当前相机任务已经更新且有结果可能是误触发{0},Pe:{1},Re:{2},{3},{4},TimeSamp:{5},TriNum:{6}", VAR.IsChinese ? disc : englishdisc, numPending, numReady, busy, vstask != null ? vstask.TaskName : "null", info.TimeStamp, info.TriggerNumber));
                            if (ticket != null)
                            {
                                // 已登记的任务在认领前就有结果：结果来源不明确，本批作废
                                InvalidateCaptureSession("任务认领前已有结果");
                            }
                            captureTickets.Complete(ticket);
                            return;
                        }
                        vstask.Image = curImage;
                        vstask.ResData.TimeStamp = info.TimeStamp;
                        vstask.ResData.TriNum = info.TriggerNumber;
                       // VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("相机接收图片{0},Pe:{1},Re:{2},{3},{4},TimeSamp:{5},TriNum:{6}", VAR.IsChinese ? disc : englishdisc, numPending, numReady, busy, vstask != null ? vstask.TaskName : "null", info.TimeStamp, info.TriggerNumber));
                    }
                    
                    TaskRunImage(ticket, vstask, curImage);
                    taskScheduled = true;


                    //Thread.Sleep(100);
                    //Task t = new Task(() =>
                    //{
                    //    lock (obj1)
                    //    {
                    //        //CogImage8Grey Img = new CogImage8Grey(Image);
                    //        ////current task
                    //        //vstask = curTask;
                    //        ////next task
                    //        //NextTask();
                    //        //run task                            
                    //        if (vstask != null)
                    //        {
                    //            string str = string.Empty;//= string.Format("{0}/{1}:{2} Run...", disc, vstask.TaskName, vstask.Image.GetHashCode());
                    //            // VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, str);
                    //            vstask.RunImage(vstask.Image);
                    //            if (CompeletedEventHandler != null)
                    //                CompeletedEventHandler(vstask, new EventArgs());
                    //            //display
                    //            RecordDisplay(vstask.ResData.OutputImg, vstask.ResData.bOK, vstask.DrawResultToGraphic(), null, vstask.Block.CreateLastRunRecord(), bSaveImage);
                    //            str = string.Format("{0}/{1}:{2}", VAR.IsChinese ? disc : englishdisc, vstask.TaskName, vstask.ResData.ToString());
                    //            // Utility.WriteStrToCSV(VAR.gsys_set.GetCurProductPath + "camdata.csv", string.Format("{0},{1},{2},{3},{4:F3},{5:F3},{6:F3}", DateTime.Now.ToString("hh:mm:ss:fff"), disc, vstask.TaskName, vstask.ResData.BarCode!=null?vstask.ResData.BarCode:"", vstask.ResData.PosMM.x, vstask.ResData.PosMM.y, vstask.ResData.PosMM.a));
                    //            VAR.msg.AddMsg(vstask.ResData.bOK ? Msg.EM_MSGTYPE.DBG : Msg.EM_MSGTYPE.ERR, str);
                    //        }
                    //    }
                    //});
                    //t.Start();

                }
                numAcqs++;
                if (numAcqs > 16)
                {
                    GC.Collect();
                    numAcqs = 0;
                }
                //auto Acquire
                if (bcontinue)
                {
                    Triger(bcontinue);
                }

            }
            catch (Exception ex)
            {
                if (!taskScheduled) captureTickets.Complete(ticket);
                captureTickets.MarkTriggerContinuityUnknown();
                if (ticket == null && captureTickets.HasOutstanding)
                {
                    // 取图异常时FIFO中的一次采集可能已被消耗而Ticket未出队，后续图像会错位，本批作废
                    InvalidateCaptureSession("取图异常:" + ex.Message);
                }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 图像处理出错,{1}", disc, ex.Message) : string.Format("{0} Image processing error,{2}     ({1} 图像处理出错,{2})", englishdisc, disc, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                if(Image!=null&& vstask!=null)
                RecordDisplay(Image, vstask.ResData.bOK);
            }
            //});

            //t.Start();
        }

        public void ResultTaskTempListAdd(VisionOutPutData vsres)
        {
            if (ListResultTemp == null)
            {
                ListResultTemp = new List<VisionOutPutData>();
                if (vsres == null)
                {
                    VAR.msg.AddMsg( Msg.EM_MSGTYPE.ERR,disc+"当前ResultTaskTempListAdd传入数据为空");
                    return;
                }

                ListResultTemp.Add(vsres.Clone());
            }
            else
            {
                if (ListResultTemp.Count > 50)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + "当前相机缓存结果大于5，自动清零");

                    ListResultTemp.Clear();
                }

                ListResultTemp.Add(vsres.Clone());
            }
        }
        void TaskRunImage(CaptureTicket<VisionTask> ticket, VisionTask task, CogImage8Grey myImage)
        {                    

            Task t = new Task(() =>
            {
                lock (obj1)
                {
                    try
                    {
                        if (myImage == null || task == null)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("相机{0}图像或任务为空", disc));
                            return;
                        }

                        if (ticket != null && captureTickets.GetState(ticket) == CaptureTicketState.Expired)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                "{0}图像处理前Ticket已失效,Session:{1},Ticket:{2}", disc, ticket.SessionId, ticket.TicketId));
                            return;
                        }

                        if (ticket != null && task.CaptureTicketId != ticket.TicketId)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                                "CaptureTrace 任务已绑定新Ticket,图像丢弃 Cam={0},Task={1},图像Ticket={2},任务当前Ticket={3}",
                                mName, task.TaskName, ticket.TicketId, task.CaptureTicketId));
                            return;
                        }

                        if (ticket != null)
                        {
                            ticket.ProcessingStartedAtUtc = DateTime.UtcNow;
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                                "CaptureTrace VPP开始 Cam={0},Session={1},Ticket={2},Hole={3},Task={4},FrameToVpp={5}ms",
                                mName, ticket.SessionId, ticket.TicketId, ticket.PositionIndex, task.TaskName,
                                ticket.FrameToProcessingMs));
                        }

                        if (task.ResData.bUpdate)
                        {
                            foreach (var tsk in List_vs_task_cur)
                            {
                                tsk.ResData.bUpdate = true;
                                tsk.ResData.bOK = false;
                            }
                            task.ResData.bUpdate = true;
                            task.ResData.bOK = false;
                            return;
                        }

                        task.Image = myImage;
                        // 记录结果来源(RunImage内的ResData.Clear不会清除这三个字段)
                        if (ticket != null)
                        {
                            task.ResData.CaptureSessionId = ticket.SessionId;
                            task.ResData.CaptureTicketId = ticket.TicketId;
                            task.ResData.CaptureHole = ticket.PositionIndex;
                        }
                        else
                        {
                            task.ResData.CaptureSessionId = 0;
                            task.ResData.CaptureTicketId = 0;
                            task.ResData.CaptureHole = -1;
                        }
                        task.RunImage(myImage);
                        ResultTaskTempListAdd(task.ResData.Clone());
                        if (CompeletedEventHandler != null)
                            CompeletedEventHandler(task, new EventArgs());
                        if (ticket != null)
                        {
                            VAR.msg.AddMsg(task.ResData.bOK ? Msg.EM_MSGTYPE.DBG : Msg.EM_MSGTYPE.ERR, string.Format(
                                "CaptureTrace VPP完成 Cam={0},Session={1},Ticket={2},Hole={3},Task={4},Result={5},Barcode={6},Vpp={7}ms",
                                mName, ticket.SessionId, ticket.TicketId, ticket.PositionIndex, task.TaskName,
                                task.ResData.bOK, task.ResData.BarCode ?? "EMPTY",
                                (long)(DateTime.UtcNow - ticket.ProcessingStartedAtUtc.Value).TotalMilliseconds));
                        }
                        string taskSaveBarcode = !string.IsNullOrWhiteSpace(task.SaveBarcode) ? task.SaveBarcode : task.ResData.BarCode;
                        // 先取出显示/存图数据，再释放Ticket：存图耗时不再阻塞下一次拍照
                        var displayImage = task.ResData.OutputImg;
                        bool displayOk = task.ResData.bOK;
                        var displayGraphics = task.DrawResultToGraphic();
                        var displayRecord = task.Block.CreateLastRunRecord();
                        captureTickets.Complete(ticket);
                        RecordDisplay(displayImage, displayOk, displayGraphics, null, displayRecord, bSaveImage, taskSaveBarcode);
                    }
                    finally
                    {
                        captureTickets.Complete(ticket);
                    }
                }
            });
            t.Start();
        }



        #endregion
        #region 触发        
        public EM_RES Triger(bool bcontinue = false, bool bWaitImg = false)
        {
            EM_RES res = EM_RES.OK;
            try
            {
                // graphics_other.Clear();
                if (mAcqFifo != null)
                {
                    try
                    {
                        bNewImage = false;
                        // 没有登记Ticket的触发(手动/标定/连续采集)，允许一张图走原curTask处理
                        if (!captureTickets.HasPending) AddManualCredit();
                        // TrigTrace：分段计时(仅日志)
                        System.Diagnostics.Stopwatch trSw = System.Diagnostics.Stopwatch.StartNew();
                        long trSinceFrameMs = ElapsedMsSince(Interlocked.Read(ref lastFrameTicksUtc));
                        bool trFlushOkBefore = FlushOk;
                        long trStopLiveMs, trCapCfgMs, trWaitMs, trFlushMs = -1, trHwMs = 0;
                        int trPendBefore = -1, trReadyBefore = -1, trPendAfter = -1, trReadyAfter = -1;
                        bool trBusyBefore = false, trBusyAfter = false, trFlushed = false;
                        string trPath = "HW";
                        EM_RES trHwRes = EM_RES.OK;

                        //stop live
                        res = StopLive();
                        if (res != EM_RES.OK) return res;
                        trStopLiveMs = trSw.ElapsedMilliseconds;

                        //set cap cfg
                        res = CapCfg(curCapCfg);
                        if (res != EM_RES.OK) return res;
                        trCapCfgMs = trSw.ElapsedMilliseconds - trStopLiveMs;
                        long trWaitStart = trSw.ElapsedMilliseconds;
                        int n = 0;
                        while (FlushUpdate)
                        {
                            Thread.Sleep(10);
                            if (n++ > 150)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}等待FlushUpdate为false超时1.5S", mName) : string.Format("{0} Wait for FlushUpdate to be false timeout(1.5s)   ({0}等待FlushUpdate为false超时1.5S)", mName), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                                return EM_RES.TIMEOUT;
                            }
                        }
                        trWaitMs = trSw.ElapsedMilliseconds - trWaitStart;
                        //clear result
                        if (!FlushOk && !FlushUpdate)
                        {
                            ReadFifoStateForTrace(out trPendBefore, out trReadyBefore, out trBusyBefore);
                            long trFlushStart = trSw.ElapsedMilliseconds;
                            FlushUpdate = true;
                            mAcqFifo.Flush();
                            FlushOk = true;
                            FlushUpdate = false;
                            trFlushMs = trSw.ElapsedMilliseconds - trFlushStart;
                            trFlushed = true;
                            ReadFifoStateForTrace(out trPendAfter, out trReadyAfter, out trBusyAfter);
                        }
                        if (curTask != null) curTask.ResData.Clear();


                        //优先硬件触发，失败后软件触发
                        long trHwStart = trSw.ElapsedMilliseconds;
                        Interlocked.Exchange(ref frameCountAtLastPulse, Interlocked.Read(ref acquiredFrameCount));
                        Interlocked.Exchange(ref lastPulseTicksUtc, DateTime.UtcNow.Ticks);
                        if (HwTrigerHandle == null)
                        {
                            trPath = "SW(无HW)";
                        }
                        else
                        {
                            trHwRes = HwTrigerHandle();
                        }
                        trHwMs = trSw.ElapsedMilliseconds - trHwStart;
                        if (HwTrigerHandle == null || EM_RES.OK != trHwRes)
                        {
                            if (HwTrigerHandle != null) trPath = "SW(HW失败)";
                            mAcqFifo.OwnedTriggerParams.TriggerEnabled = false;
                            mAcqFifo.OwnedTriggerParams.TriggerModel = CogAcqTriggerModelConstants.Manual;
                            mAcqFifo.OwnedTriggerParams.TriggerEnabled = true;
                            this.bcontinue = bcontinue;
                            mAcqFifo.StartAcquire();
                        }

                        lastTriggerTrace = string.Format(
                            "距上一帧={0}ms,上一帧TriNum={1},FlushOk前={2},StopLive={3}ms,CapCfg={4}ms,等FlushUpdate={5}ms,Flush={6}/{7}ms,Flush前P/R/B={8}/{9}/{10},Flush后P/R/B={11}/{12}/{13},触发方式={14},HW返回={15},HW耗时={16}ms,总耗时={17}ms",
                            trSinceFrameMs, lastFrameTriggerNumber, trFlushOkBefore, trStopLiveMs, trCapCfgMs, trWaitMs,
                            trFlushed ? "是" : "否", trFlushMs, trPendBefore, trReadyBefore, trBusyBefore,
                            trPendAfter, trReadyAfter, trBusyAfter, trPath, trHwRes, trHwMs, trSw.ElapsedMilliseconds);
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("TrigTrace Cam={0},{1}", mName, lastTriggerTrace));

                        //wait img update
                        int t = 0;
                        while (bWaitImg)
                        {
                            if (bNewImage) break;
                            Thread.Sleep(2);
                            Application.DoEvents();
                            t++;
                            if (t > 500)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}触发等待图像更新超时(1000ms)", mName) : string.Format("{0} Trigger waiting for image update timeout(1000ms)    ({0}触发等待图像更新超时(1000ms))", mName), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                                return EM_RES.TIMEOUT;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}触发异常，{1}", mName, ex.Message) : string.Format("{0} ERROR:Trigger.{1}   ({0}触发异常，{1})", mName, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam, ERR_ALM.EmErrItem.CaptureAbnomal);
                        return EM_RES.CAM_ERR;
                    }
                }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}触发异常，{1}", mName, ex.Message) : string.Format("{0} ERROR:Trigger.{1}   ({0}触发异常，{1})", mName, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam, ERR_ALM.EmErrItem.CaptureAbnomal);
                return EM_RES.CAM_ERR;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("{0}触发", mName) : string.Format("{0} trigger  ({0}触发)", mName));
            return EM_RES.OK;
        }
        public EM_RES TrigerByLoadImg(string filename)
        {
            if (curTask == null || filename.Length < 3 || !File.Exists(filename)) return EM_RES.PARA_ERR;

            Bitmap bmp = new Bitmap(filename);
            Image = new CogImage8Grey(bmp);
            curTask.RunImage(Image);
            if (CompeletedEventHandler != null)
                CompeletedEventHandler(curTask, new EventArgs());
            //display
            RecordDisplay(curTask.ResData.OutputImg, curTask.ResData.bOK, curTask.DrawResultToGraphic(), null, curTask.Block.CreateLastRunRecord());
            string str = string.Format("{0}/{1}:{2}", VAR.IsChinese ? disc : englishdisc, curTask.TaskName, curTask.ResData.ToString());
            VAR.msg.AddMsg(curTask.ResData.bOK ? Msg.EM_MSGTYPE.DBG : Msg.EM_MSGTYPE.ERR, str);
            return EM_RES.OK;
        }

        //public EM_RES TrigerByImg(ICogImage img)
        //{
        //    if (img == null) return EM_RES.ERR;
        //     curTask.RunImage(img);
        //    if (CompeletedEventHandler != null)
        //        CompeletedEventHandler(curTask, new EventArgs());
        //    //display
        //    RecordDisplay(curTask.ResData.OutputImg, curTask.DrawResultToGraphic(), null, curTask.Block.CreateLastRunRecord());
        //    string str = string.Format("{0}/{1}:{2}", disc, curTask.TaskName, curTask.ResData.ToString());
        //    VAR.msg.AddMsg(curTask.ResData.bOK ? Msg.EM_MSGTYPE.DBG : Msg.EM_MSGTYPE.ERR, str);
        //    return EM_RES.OK;
        //}
        #endregion
        #region 实况
        public bool isLive
        {
            get
            {
                try
                {
                    if (mFrameGrabber == null || mFrameGrabber.GetStatus(true) != CogFrameGrabberStatusConstants.Active || mAcqFifo == null) return false;
                    if (mCogRecDisplay != null) return mCogRecDisplay.LiveDisplayRunning;
                    return false;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("{0} 检查实况出错,{1}", mName, ex.Message) : string.Format("{0}Check live error,{1}    ({0} 检查实况出错,{1})", mName, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                }
                return false;
            }
        }
        public ST_CAP_CFG liveCapCfg;
        /// <summary>
        /// 在指定Display控件上实况显示
        /// </summary>
        /// <param name="CogRecDisplay">指定Display控件</param>
        /// <param name="blive">True开始实况，False为关闭实况</param>
        /// <param name="bclear">True则清除显示</param>
        /// <returns></returns>
        public EM_RES Live(CogRecordDisplay CogRecDisplay, bool blive = true, bool bclear = true)
        {
            if (!blive && !bIsLive && VAR.gsys_set.status == EM_SYS_STA.RUN) return EM_RES.OK;
            if (CogRecDisplay != null) mCogRecDisplay = CogRecDisplay;
            if (mCogRecDisplay == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 实况窗口未初始化!", mName) : string.Format("{0}Live window is not initialized!    ({0} 实况窗口未初始化!)", mName), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_PARA_ERR;
            }

            if (mAcqFifo == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 取图未初始化!", mName) : string.Format("The image is not initialized!   ({0} 取图未初始化!)", mName), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_INIT_ERR;
            }

            EM_RES res = EM_RES.OK;
            if (blive && !isLive)
            {
                bIsLive = true;
                TriIO.SetOn();
                bRunImage = false;
                if (bclear)
                {
                    mCogRecDisplay.StaticGraphics.Clear();
                    mCogRecDisplay.InteractiveGraphics.Clear();
                }
                mAcqFifo.OwnedTriggerParams.TriggerEnabled = false;
                res = CapCfg(liveCapCfg);
                mAcqFifo.OwnedTriggerParams.TriggerModel = CogAcqTriggerModelConstants.Manual;
                mAcqFifo.OwnedTriggerParams.TriggerEnabled = true;
                if (res != EM_RES.OK) return res;
                mCogRecDisplay.StartLiveDisplay(mAcqFifo);
                mCogRecDisplay.AutoFit = true;
                // mCogRecDisplay.Fit();
                ShowCrossMark();
            }
            else if (mCogRecDisplay.LiveDisplayRunning)
            {
                bIsLive = false;
                TriIO.SetOff();
                bRunImage = true;
                mCogRecDisplay.StopLiveDisplay();
                int n;
                for (n = 0; n < 10; n++)
                {
                    Thread.Sleep(10);
                    //Application.DoEvents();
                }

                try
                {
                    mCogRecDisplay.StaticGraphics.Remove("CrossMark");
                }
                catch
                { }
                mCogRecDisplay.InteractiveGraphics.Clear();

                mAcqFifo.OwnedTriggerParams.TriggerEnabled = false;
                res = CapCfg(curCapCfg);
                mAcqFifo.OwnedTriggerParams.TriggerModel = mTriggerMode;
                mAcqFifo.OwnedTriggerParams.TriggerEnabled = true;
                if (res != EM_RES.OK) return res;
                mAcqFifo.Prepare();
            }
            return res;
        }
        /// <summary>
        /// 停止实况
        /// </summary>
        /// <returns></returns>
        public EM_RES StopLive()
        {
            return Live(mCogRecDisplay, false);
        }
        /// <summary>
        /// 开始实况
        /// </summary>
        /// <returns></returns>
        public EM_RES StartLive()
        {
            return Live(mCogRecDisplay, true);
        }
        #endregion
        #region  保存显示画面
        public void SaveDisplay(CogRecordDisplay display, string path, string name = "")
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                string filename = "";
                if (name == "") filename = DateTime.Now.ToString("yyMMdd_hhmmss_fff") + ".jpeg";
                else filename = name;

                Bitmap bmp = display.CreateContentBitmap(CogDisplayContentBitmapConstants.Display) as Bitmap;
                if (Path.GetExtension(filename) == "bmp")
                    bmp.Save(path + "\\" + filename, System.Drawing.Imaging.ImageFormat.Bmp);
                else
                    bmp.Save(path + "\\" + filename, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("保存处理后图像出错，{0}", ex.Message) : string.Format("ERROR:Save image,{0}    (保存处理后图像出错，{0})", ex.Message), DReport.EmErrCode.SaveFailed, (int)DReport.EmHareware.Cam);
            }
        }
        #endregion
        #region 保存原始图像
        private static string SanitizeImageFileToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "NoCode";
            foreach (char c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            return value;
        }

        public void SaveOriginImage(ICogImage image, string path, string name = "")
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                string filename = "";
                if (name == "") filename = DateTime.Now.ToString("yyMMdd_hhmmss_fff") + ".jpeg";
                else filename = name;
                Bitmap bmp = image.ToBitmap();
                if (Path.GetExtension(filename) == "bmp")
                    bmp.Save(path + "\\" + filename, System.Drawing.Imaging.ImageFormat.Bmp);
                else
                    bmp.Save(path + "\\" + filename, System.Drawing.Imaging.ImageFormat.Jpeg);

            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("保存原始图像出错，{0}", ex.Message) : string.Format("ERROR:Save original image,{0}   (保存原始图像出错，{0})", ex.Message), DReport.EmErrCode.SaveFailed, (int)DReport.EmHareware.Cam);
            }
        }
        #endregion
        #endregion
        #region 显示
        /// <summary>
        /// 在图像中心显示十字架
        /// </summary>
        /// <param name="CogRecDisplay"></param>
        public void ShowCrossMark(CogRecordDisplay CogRecDisplay = null)
        {
            if (CogRecDisplay == null) CogRecDisplay = mCogRecDisplay;
            if (CogRecDisplay == null) return;

            CogPointMarker marker = new CogPointMarker();
            marker.Color = CogColorConstants.Orange;
            marker.LineStyle = CogGraphicLineStyleConstants.Solid;
            if (CogRecDisplay.LiveDisplayRunning)
            {
                marker.X = CogRecDisplay.Width / 2;
                marker.Y = CogRecDisplay.Height / 2;
                marker.SizeInScreenPixels = 10000;
                marker.SelectedSpaceName = "*";
            }
            else
            {
                if (CogRecDisplay.Image != null)
                {
                    marker.X = CogRecDisplay.Image.Width / 2;
                    marker.Y = CogRecDisplay.Image.Height / 2;
                    marker.SizeInScreenPixels = CogRecDisplay.Image.Width * 10;
                    marker.SelectedSpaceName = "#";
                }
            }
            //try
            //{
            //    CogRecDisplay.StaticGraphics.Remove("CrossMark");
            //}
            //catch
            //{ }
            CogRecDisplay.StaticGraphics.Add(marker, "CrossMark");
        }
        public CogRecordDisplay mCogRecDisplay = new CogRecordDisplay();

        delegate void RD(ICogImage Image = null, bool bOk = false, CogGraphicCollection graphics = null,
            CogRecordDisplay CogRecDisplay = null, ICogRecord Record = null, bool bsave = false, string saveBarcode = null);
        public CogGraphicCollection graphics_other = new CogGraphicCollection();
        // public CogGraphicInteractiveCollection Interactive_ofs=new CogGraphicInteractiveCollection();
        public void RecordDisplay(ICogImage Image = null, bool bOk = false, CogGraphicCollection graphics = null, CogRecordDisplay CogRecDisplay = null, ICogRecord Record = null, bool bsave = false, string saveBarcode = null)
        {
            int i = 0;
            try
            {

                if (CogRecDisplay == null) CogRecDisplay = mCogRecDisplay;
                if (CogRecDisplay == null) return;
                if (Image == null) return;
                CogGraphicCollection graphicscopy = null;
                if (graphics != null && graphics.Count > 0) graphicscopy = new CogGraphicCollection(graphics);
                //CogRecDisplay.StaticGraphics.Clear();

                // InvokeRequired required compares the thread ID of the 
                // calling thread to the thread ID of the creating thread. 
                // If these threads are different, it returns true. 
                if (!CogRecDisplay.IsHandleCreated)
                {
                    i = 1;
                    //解决窗体关闭时出现“访问已释放句柄“的异常
                    if (CogRecDisplay.Disposing || CogRecDisplay.IsDisposed) return;
                }

                if (CogRecDisplay.InvokeRequired)//如果调用控件的线程和创建创建控件的线程不是同一个则为True
                {
                    i = 2;
                    //VS_DisplayIMG_Callback d = new VS_DisplayIMG_Callback(VS_DisplayIMG);
                    CogRecDisplay.BeginInvoke(new RD(RecordDisplay), new object[] { Image, bOk, graphics, CogRecDisplay, Record, bsave, saveBarcode });
                }
                else
                {
                    //try
                    //{
                    //  CogRecDisplay.StaticGraphics.Remove("Result"); block = false;

                    //}
                    //catch
                    //{ }

                    i = 3;
                    CogRecDisplay.Image = Image;
                    i = 4;
                    if (Record != null) CogRecDisplay.Record = Record;
                    i = 5;
                    if (VAR.gsys_set.status == EM_SYS_STA.RUN) CogRecDisplay.StaticGraphics.Clear();
                    if (graphicscopy != null && graphicscopy.Count > 0) CogRecDisplay.StaticGraphics.AddList(graphicscopy, "Result"); //else { }
                    // if (Interactive_ofs != null) CogRecDisplay.InteractiveGraphics.AddList(Interactive_ofs, "ofs", false);
                    i = 6;
                    if (graphics_other != null && graphics_other.Count > 0) CogRecDisplay.StaticGraphics.AddList(graphics_other, "Other");
                    i = 7;
                    ShowCrossMark(CogRecDisplay);
                    i = 8;
                    CogRecDisplay.AutoFit = true;
                    // CogRecDisplay.Fit();

                    //save
                    //check disk space
                    //todo

                    //try
                    //{
                    if (bsave && CogRecDisplay.Record != null)
                    {
                        //ImageSaveQueue.gOnly.NewPart();
                        Image imageToSave = CogRecDisplay.CreateContentBitmap(CogDisplayContentBitmapConstants.Display);
                        string fileToken = SanitizeImageFileToken(saveBarcode);
                        if (bOk)
                        {
                            ImageSaveQueue.gOnly.AddToQueue(
                                string.Format("{0}\\image\\{1}\\{2}\\Ok\\{3}_{4}.jpg", VAR.gsys_set.GetCurProductPath, mName,
                                    DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss_fff"), fileToken),
                                imageToSave);
                        }
                        else
                        {
                            ImageSaveQueue.gOnly.AddToQueue(
                                string.Format("{0}\\image\\{1}\\{2}\\Ng\\{3}_{4}.jpg", VAR.gsys_set.GetCurProductPath, mName,
                                    DateTime.Now.ToString("yyyyMMdd"), DateTime.Now.ToString("HHmmss_fff"), fileToken),
                                imageToSave);
                        }
                    }

                    //}
                    //catch(Exception ex)
                    //{
                    //    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("显示出错，", ex.Message));
                    //}
                }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}显示出错:{1}", i, ex.Message) : string.Format("{0} ERROR:Display  {1}   ({0}显示出错:{1})", i, ex.Message), DReport.EmErrCode.DisplayError, (int)DReport.EmHareware.Cam);
            }
        }
        #endregion
        #region 视觉任务
        #region 结果
        public class VisionOutPutData
        {
            public string TaskName;
            public string CamName;
            public int CamId;
            public int TriNum;          //触发顺序
            public long TimeStamp;       //时间戳
            public ST_XYA PosPix;       //像素坐标
            public ST_XYA PosMM;        //mm坐标
            public ST_XYA Offset;       //偏差
            public ST_XYA CentOfs;      //与画面中心偏差            
            public string BarCode;      //二维码
            public string Message;      //结果信息/多点数据输出
            public double Score;        //得分
            public bool bOK;            //结果
            public bool bUpdate;        //更新标志
            public ICogImage OutputImg; //输出图像
            public CogGraphicCollection GCOutput;   //输出界面绘图
            public CogCompositeShape ShapeOutput;  //输出形状
            public int CTms;    //用时ms
            public long CaptureSessionId;   //产生该结果的拍照批次(0=无Ticket)
            public long CaptureTicketId;    //产生该结果的拍照Ticket(0=无Ticket)
            public int CaptureHole = -1;    //该Ticket登记的孔位(-1=定拍/未知)
            /// <summary>
            /// 清除结果数据
            /// </summary>
            /// <param name="bupdate">数据更新标志</param>
            public void Clear(bool bupdate = false)
            {
                PosPix = new ST_XYA();
                PosMM = new ST_XYA();
                Offset = new ST_XYA();
                CentOfs = new ST_XYA();
                BarCode = "";
                Message = "";
                bOK = false;
                bUpdate = bupdate;
                OutputImg = null;
                if (GCOutput != null) GCOutput.Clear();
                CTms = 0;
                TriNum = 0;
                TimeStamp = 0;
            }
            /// <summary>
            /// 返回包含mm坐标/分数/时间/二维码(信息)的字符串
            /// </summary>
            /// <returns></returns>
            public override string ToString()
            {
                return string.Format("{0} X:{1:F3},Y{2:F3},A{3:F3},S{4:F3},T{5},{6},{7}", bOK ? "OK" : "NG", PosMM.x, PosMM.y, PosMM.a, Score, CTms, BarCode != null && BarCode.Length > 0 ? BarCode : Message, TriNum);
            }
            public VisionOutPutData Clone()
            {
                return new VisionOutPutData()
                {


                    TaskName = TaskName,
                    CamName = CamName,
                    CamId = CamId,
                    TriNum = TriNum,
                    PosPix = PosPix,
                    PosMM = PosMM,
                    Offset = Offset,
                    CentOfs = CentOfs,
                    BarCode = BarCode,
                    Message = Message,
                    Score = Score,
                    bOK = bOK,
                    bUpdate = bUpdate,
                    CaptureSessionId = CaptureSessionId,
                    CaptureTicketId = CaptureTicketId,
                    CaptureHole = CaptureHole
                };
            }
        }
        #endregion
        #region 任务
        public class VisionTask
        {
            private static readonly object obj = new object();
            /// <summary>
            /// 取图参数，光源控制，曝光时间，对比度，亮度等
            /// </summary>
            public ST_CAP_CFG CapCfg = new ST_CAP_CFG();
            /// <summary>
            /// 运行参数，传递给视觉模块，用于控制流程，逻辑等
            /// </summary>
            public int RunCfg = 0;
            /// <summary>
            /// 关联的相机
            /// </summary>
            public Cam Camera;

            public int Xtid=-1;
            public long CaptureSessionId;
            public long CaptureTicketId;
            /// <summary>
            /// 保存OK/NG图片时附加的模组二维码(由业务层配置任务时注入)
            /// </summary>
            public string SaveBarcode;
            /// <summary>
            /// 视觉模块
            /// </summary>
            public CogToolBlock Block = null;
            /// <summary>
            /// 任务名称，即文件名
            /// </summary>
            public string TaskName = "";
            /// <summary>
            /// 任务的文件路径
            /// </summary>
            public string path;
            /// <summary>
            /// 校正关系
            /// </summary>
            public List<CaliTool> ListCaliTool = new List<CaliTool>();
            /// <summary>
            /// 任务结果数据
            /// </summary>
            public VisionOutPutData ResData = new VisionOutPutData();
            /// <summary>
            /// 触发点或拍照点
            /// </summary>
            public ST_XYN TriPos = new ST_XYN();


            public CogImage8Grey Image;
            public VisionTask Clone()
            {
                VisionTask tsk = new VisionTask();
                tsk.CapCfg = CapCfg;
                tsk.RunCfg = RunCfg;
                tsk.Camera = Camera;
                tsk.Block = (CogToolBlock)CogSerializer.DeepCopyObject(Block);
                tsk.TaskName = TaskName;
                tsk.path = path;
                tsk.ListCaliTool = ListCaliTool;
                ResData = new VisionOutPutData();
                Image = new CogImage8Grey();
                return tsk;
            }
            public VisionTask()
            {
            }
            /// <summary>
            /// 新建并加载任务
            /// </summary>
            /// <param name="filepath">任务路径，默认为空，不加载</param>
            public VisionTask(string filepath, Cam camera = null)
            {
                CapCfg = new ST_CAP_CFG();
                RunCfg = 0;
                this.Camera = camera;
                ResData = new VisionOutPutData();
                Load(filepath);
            }
            /// <summary>
            /// 保存任务
            /// </summary>
            /// <param name="filepath">保存路径，默认为加载时的路径</param>
            /// <returns></returns>
            public bool Save(string filepath = "")
            {
                if (Block == null) return false;
                if (filepath == "") filepath = path;
                try
                {
                    CogSerializer.SaveObjectToFile(Block, filepath, typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter), CogSerializationOptionsConstants.Minimum);
                    VisionTask vstsk = Camera.List_vs_copytask.Find(s => s.TaskName.Contains(TaskName));
                    if (vstsk != null)
                    {
                        Camera.List_vs_copytask.Remove(vstsk);
                    }
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}保存成功！{1}", TaskName, filepath) : string.Format("{0} Save successfully! {1}   ({0}保存成功！{1})", TaskName, filepath));
                    MessageBox.Show(VAR.IsChinese ? string.Format("{0}保存成功！{1}", TaskName, filepath) : string.Format("{0} Save successfully! {1}   ({0}保存成功！{1})", TaskName, filepath), VAR.IsChinese ? "信息" : "information", MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                    return true;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}保存失败！{1}", TaskName, ex.Message) : string.Format("{0} Save failed! {1}   ({0}保存失败！{1})", TaskName, ex.Message));
                    MessageBox.Show(VAR.IsChinese ? string.Format("{0}保存失败！{1}", TaskName, ex.Message) : string.Format("{0} Save failed! {1}   ({0}保存失败！{1})", TaskName, ex.Message), VAR.IsChinese ? "错误" : "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                    return false;
                }

            }
        static    bool bShow = true;
            /// <summary>
            /// 加载任务
            /// </summary>
            /// <param name="filepath">任务文件路径</param>
            /// <returns></returns>
            public bool Load(string filepath = "")
            {
                if (filepath == "") filepath = path;
                if (filepath.Length > 0)
                {
                    TaskName = Path.GetFileNameWithoutExtension(filepath);
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("正在加载{0}...", TaskName) : string.Format("Loding{0}...", TaskName));
                    //Application.DoEvents();
                    try
                    {
                        Block = CogSerializer.LoadObjectFromFile(filepath, typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter), CogSerializationOptionsConstants.Minimum) as CogToolBlock;
                        Block.GarbageCollectionEnabled = true;
                        path = filepath;
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}加载完成", TaskName) : string.Format("{0} Load successfully   ({0}加载完成)", TaskName));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, VAR.IsChinese ? string.Format("{0}加载失败！{1}", TaskName, ex.Message) : string.Format("{0} Load failed! {1}    ({0}加载失败！{1})", TaskName, ex.Message), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                      if(bShow)
                        MessageBox.Show(VAR.IsChinese ? string.Format("{0}加载失败!\r\n文件不存在，或配置文件遭到破坏！", Path.GetFileName(filepath)) : string.Format("{0}Failed to load!\r\n The file does not exist or the configuration file is corrupted!\r\n{0}加载失败!\r\n文件不存在，或配置文件遭到破坏！", Path.GetFileName(filepath)), "错误", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                        bShow = false;
                        Block = null;
                        path = "";
                        TaskName = "";
                    }
                }
                else
                {
                    Block = null;
                    TaskName = "";
                    path = "";
                }
                return false;
            }
            /// <summary>
            /// 触发并等待返回结果
            /// </summary>
            /// <param name="bquit">退出控制</param>
            /// <param name="TimeOut">超时</param>
            /// <param name="TryCnt">尝试次数</param>
            /// <returns></returns>
            public EM_RES TriAndWaitResult(ref bool bquit, int TimeOut = 1000, int TryCnt = 1, bool Demo = false)
            {
                //check
                if (Camera == null) return EM_RES.PARA_ERR;
                try
                {
                    for (int n = 0; n < TryCnt; n++)
                    {
                        // 先登记本次拍照Ticket(必要时等待上一批释放)，再清结果，避免上一批迟到结果写入本次。
                        // Demo不等待真实图像，不登记Ticket(真实图像到达时按手动触发处理，与原逻辑一致)
                        if (!Demo)
                        {
                            EM_RES prepareRes = Camera.PrepareDirectCapture(this);
                            if (prepareRes != EM_RES.OK) return prepareRes;
                        }
                        //clear result
                        ResData.Clear();
                        //triger
                        EM_RES res = Camera.Triger();
                        if (res != EM_RES.OK)
                        {
                            // 无法确认触发是否已发出：本次Ticket作废，下次拍照前按丢帧流程恢复
                            if (!Demo) Camera.InvalidateCaptureSession(string.Format("{0}触发失败", TaskName));
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}/{1}触发 失败，返回{2}!", Camera.disc, TaskName, res), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                            return res;
                        }
                        //wati result
                        if (Demo)
                        {
                            ResData.bOK = true;
                            ResData.PosMM = new ST_XYA(0, 0, 0);
                            ResData.bUpdate = true;
                            return EM_RES.OK;
                        }
                        int num = 0;
                        while (ResData.bUpdate == false)
                        {
                            if (bquit)
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}/{1}等待结果取消!", Camera.disc, TaskName) : string.Format("{0}/{2} Wait for results,cancel!    ({1}/{2}等待结果取消!)", Camera.englishdisc, Camera.disc, TaskName), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                                // 中途停止：本次图像可能稍后到达，Ticket作废
                                Camera.InvalidateCaptureSession(string.Format("{0}等待结果取消", TaskName));
                                return EM_RES.QUIT;
                            }
                            // Application.DoEvents();
                            Thread.Sleep(10);
                            if (++num > TimeOut / 10)
                            {
                                ResData.bUpdate = true;
                                ResData.bOK = false;
                                Camera.LogCaptureTimeoutDiagnostics(TaskName);
                                Camera.InvalidateCaptureSession(string.Format("{0}等待结果超时", TaskName));
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}/{1}等待结果超时!", Camera.disc, TaskName) : string.Format("{0}/{2} Wait fot results timeout!   ({1}/{2}等待结果超时!)", Camera.englishdisc, Camera.disc, TaskName), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                                return EM_RES.TIMEOUT;
                            }
                        }
                        if (ResData.bOK) return EM_RES.OK;
                    }

                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format( "{0}/{1} 拍照已出图但视觉判定NG, Message={2}, Score={3}",Camera.disc, TaskName, ResData.Message, ResData.Score));
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR,  string.Format("{0}拍照异常",ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam, ERR_ALM.EmErrItem.CaptureAbnomal);
                    return EM_RES.CAM_ERR;
                }

                return EM_RES.CAM_ERR;
            }

            /// <summary>
            /// 提取视觉任务的输出变量，视觉任务为null，或变量不存在则返回默认值
            /// </summary>
            /// <param name="name">变量名称</param>
            /// <param name="def">默认值</param>
            /// <returns></returns>
            object GetBlockOutput(string name, object def)
            {
                if (Block == null) return def;
                else return Block.Outputs.Contains(name) && Block.Outputs[name].Value != null ? Block.Outputs[name].Value : def;
            }
            /// <summary>
            /// 设置视觉任务的输入变量
            /// </summary>
            /// <param name="name"></param>
            /// <param name="def"></param>
            /// <returns></returns>
            bool SetBlockInput(string name, object val)
            {
                if (Block == null) return false;
                if (Block.Inputs.Contains(name))
                {
                    try
                    {
                        Block.Inputs[name].Value = val;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}{1}任务{2}设置参数{3}出错，", Camera == null ? "" : Camera.disc, TaskName, name, ex.Message) : string.Format("{0}{2} Task{3} Set parameter {4} err!   ({1}{2}任务{3}设置参数{4}出错!)", Camera == null ? "" : Camera.englishdisc, Camera == null ? "" : Camera.disc, TaskName, name, ex.Message), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
                        return false;
                    }
                }
                return false;
            }
            /// <summary>
            /// 视觉任务处理图像，并更新结果数据到ResData
            /// </summary>
            /// <param name="image"></param>
            public void RunImage(ICogImage Image)
            {
                try
                {
                    ResData.Clear();
                    if (Block == null || Image == null) return;
                    ResData.OutputImg = Image;


                    //优先使用相机校正关系
                    if (Camera != null && Camera.ListCaliTool != null && TaskName != "AffTransTool")
                    {
                        //Camera.ListCaliTool.RemoveAt(2);
                        foreach (CaliTool tool in Camera.ListCaliTool)
                        {
                            tool.Execute(ref Image);
                        }
                    }

                    //使用任务校正关系
                    if (ListCaliTool.Count != 0)
                    {
                        foreach (CaliTool tool in ListCaliTool)
                        {
                            tool.Execute(ref Image);
                        }
                    }

                    ResData.OutputImg = Image;

                    //init 
                    if (!SetBlockInput("InputImage", Image))
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}不包含InputImage参数，无法传递图像!", TaskName) : string.Format("{0} Cannot pass image without InputImage parameter!    ({0}不包含InputImage参数，无法传递图像!)", TaskName), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                        return;
                    }

                    if(TaskName.Contains("ModDw"))
                    {
                        if((PT_SET.BarcodeMode == 1 && !PT_SET.bDwAddCapQrcode) || PT_SET.bdownqr) RunCfg = RunCfg | 0x01;
                        else RunCfg = RunCfg & (~0x01);
                    }
                    else if(TaskName.Contains("ModUp"))
                    {
                        if (PT_SET.BarcodeMode == 0 && !PT_SET.Isaloneset)
                        {

                            bool bGetOrcodOnWs = NewSysInf.UserParams.bGetOrcodOnWs;
                            if (!bGetOrcodOnWs)
                            RunCfg = RunCfg & (~0x01);
                            else
                            RunCfg = RunCfg | 0x01;

                        }
                        else RunCfg = RunCfg | 0x01;
                    }
                    else if (TaskName.Contains("WsMod"))
                    {
                        if (PT_SET.BarcodeMode == 0) RunCfg = RunCfg & (~0x01);
                        else RunCfg = RunCfg | 0x01;
                        if(PT_SET.bConnectorCheck==true) RunCfg = RunCfg & (~0x08);
                        else RunCfg = RunCfg | 0x08;
                    }

                    if (PT_SET.bGTMCheck) RunCfg = RunCfg | 0x02;
                    else RunCfg = RunCfg & (~0x02);

                    if (PT_SET.bWsVsAddCheckEn) RunCfg = RunCfg | 0x04;
                    else RunCfg = RunCfg & (~0x04);

                    int temp = (int)(PT_SET.GTMOfs * 100);
                    temp = temp << 8;
                    RunCfg = (RunCfg & 0x00ff) | temp;

                    SetBlockInput("RunCfg", RunCfg);

                    //run tool
                    ResData.bUpdate = false;
                    ResData.bOK = false;
                   // string str = string.Format("相机开始处理图片{0}/{1}:{2} Run...", VAR.IsChinese ? Camera.disc : Camera.englishdisc, TaskName, Image.GetHashCode());
                   // VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, str);
                    Block.Run();

                    //get data
                    if (Block.RunStatus.Result == CogToolResultConstants.Accept || (Block.RunStatus.Message.Contains("Tool \"CogIDTool1\" failed.") && TaskName.Contains("WsMod_Shp")))
                    {
                        GetResData();
                    }
                    else
                    {
                        ResData.Message = (string)GetBlockOutput("Message", "");
                        ResData.bOK = false;
                        ResData.bUpdate = true;
                    }

                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "RunImage异常," : "Err RunImage," + ex.Message, DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                }
                finally
                {
                    Camera.FlushOk = false;
                }
            }
            /// <summary>
            /// 提取任务结果数据
            /// </summary>
            /// <returns></returns>
            public bool GetResData()
            {
                if (Block == null) return false;

                try
                {
                    //结果提取                
                    ResData.bOK = (bool)GetBlockOutput("Result", false);
                    ResData.GCOutput = (CogGraphicCollection)GetBlockOutput("GCOutput", null);
                    ResData.Message = (string)GetBlockOutput("Message", "");
                    ResData.Score = (double)GetBlockOutput("Score", new double());
                    ResData.OutputImg = (ICogImage)GetBlockOutput("OutputImg", ResData.OutputImg);
                    ResData.ShapeOutput = (CogCompositeShape)GetBlockOutput("ShapeOutput", ResData.ShapeOutput);
                    ResData.CTms = (int)Block.RunStatus.TotalTime;
                    //PointPix
                    PointF pt = (PointF)GetBlockOutput("PointPix", new PointF());
                    double angle = (double)GetBlockOutput("AnglePix", new double());
                    ResData.PosPix.x = pt.X;
                    ResData.PosPix.y = pt.Y;
                    ResData.PosPix.a = angle;
                    //CentOfs
                    pt = (PointF)GetBlockOutput("CentOfs", new PointF());
                    ResData.CentOfs.x = pt.X;
                    ResData.CentOfs.y = pt.Y;
                    ResData.CentOfs.a = ResData.PosPix.a;
                    //PointMM
                    pt = (PointF)GetBlockOutput("PointMM", new PointF());
                    angle = (double)GetBlockOutput("AngleMM", new double());
                    ResData.PosMM.x = pt.X;
                    ResData.PosMM.y = pt.Y;
                    ResData.PosMM.a = angle;
                    //PointOfs
                    pt = (PointF)GetBlockOutput("PointMM", new PointF());
                    angle = (double)GetBlockOutput("AngleOfs", new double());
                    ResData.Offset.x = pt.X;
                    ResData.Offset.y = pt.Y;
                    ResData.Offset.a = angle;
                    //BarCode
                    ResData.BarCode = (string)GetBlockOutput("BarCode", "");

                    //finally update flag
                    ResData.bUpdate = true;
                    return true;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}获取结果异常，{1}", TaskName, ex.Message) : string.Format("{0} Get result exception,{1}    ({0}获取结果异常，{1})", TaskName, ex.Message), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    return false;
                }
            }
            /// <summary>
            /// 把字符串绘制到图形集合中
            /// </summary>
            /// <param name="gc">图形集合</param>
            /// <param name="x">字符串位置X</param>
            /// <param name="y">字符串位置Y</param>
            /// <param name="cl">绘制颜色</param>
            /// <param name="str">字符串</param>
            public void DrawTextToGC(CogGraphicCollection gc, int x, int y, CogColorConstants cl, string str)
            {
                CogGraphicLabel lable = new CogGraphicLabel();
                lable.Alignment = CogGraphicLabelAlignmentConstants.BaselineLeft;
                lable.SelectedSpaceName = "*";
                lable.Font = new Font("Arial", 12);
                lable.LineWidthInScreenPixels = 3;
                lable.Color = cl;
                lable.SetXYText(x, y, str);
                gc.Add(lable);
            }
            /// <summary>
            /// 绘制结果到图形集合
            /// </summary>
            /// <param name="bAdd">True时，直接增加到ResData.GCOutput的集合中</param>
            /// <returns></returns>
            public CogGraphicCollection DrawResultToGraphic(bool bAdd = true)
            {
                int lineHeight = 24;
                int n = 0;
                CogColorConstants cl = ResData.bOK ? CogColorConstants.Green : CogColorConstants.Red;

                CogGraphicCollection gc = new CogGraphicCollection();
                if (bAdd && ResData.GCOutput != null) gc = ResData.GCOutput;
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, ResData.bOK ? "OK" : "NG");
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, string.Format("X:{0:000.000}", ResData.PosMM.x));
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, string.Format("Y:{0:000.000}", ResData.PosMM.y));
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, string.Format("A:{0:000.000}", ResData.PosMM.a));
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, string.Format("S: {0:0.000}", ResData.Score));
                DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, string.Format("T: {0:000} ms", ResData.CTms));
                if (ResData.BarCode.Length > 0) DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, ResData.BarCode);
                if (!ResData.bOK && ResData.Message.Length > 0) DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl, ResData.Message);
                if(TaskName== "WsMod_Shp" && ResData.bOK && ResData.Message.Length > 0)
                {
                    CogColorConstants cl1 =  CogColorConstants.Green;
                    CogColorConstants cl2 = CogColorConstants.Green;
                    CogColorConstants cl3 = CogColorConstants.Green;
                    CogColorConstants cl4 = CogColorConstants.Green;
                    string[] date = new string[3];
                    double left = double.MaxValue;
                    double right = double.MaxValue;
                    double up = double.MaxValue; ;
                    double down = double.MaxValue;
                    try
                    {
                        date = ResData.Message.Split(',');
                        left = Convert.ToDouble(date[0]);
                        if ((Camera.mName.Contains("1") && Math.Abs(left - PT_SET.LeftArea) > PT_SET.Area)
                            || (Camera.mName.Contains("2") && Math.Abs(left - PT_SET.LeftArea2) > PT_SET.Area2))
                        {
                            cl1 = CogColorConstants.Red;
                        }
                        DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl1, "左：" + left);
                        right = Convert.ToDouble(date[1]);
                        if ((Camera.mName.Contains("1") && Math.Abs(right - PT_SET.RightArea) > PT_SET.Area)
                            || (Camera.mName.Contains("2") && Math.Abs(right - PT_SET.RightArea2) > PT_SET.Area2))
                        {
                            cl2 = CogColorConstants.Red;
                        }
                        DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl2, "右：" + right);
                        if (PT_SET.bConnectorCheck)
                        {
                            up = Convert.ToDouble(date[2]);
                            if ((Camera.mName.Contains("1") && Math.Abs(up - PT_SET.UpArea) > PT_SET.Area)
                            || (Camera.mName.Contains("2") && Math.Abs(up - PT_SET.UpArea2) > PT_SET.Area2))
                            {
                                cl3 = CogColorConstants.Red;
                            }
                            DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl3, "上：" + up);
                            down = Convert.ToDouble(date[3]);
                            if ((Camera.mName.Contains("1") && Math.Abs(down - PT_SET.DownArea) > PT_SET.Area)
                            || (Camera.mName.Contains("2") && Math.Abs(down - PT_SET.DownArea2) > PT_SET.Area2))
                            {
                                cl4 = CogColorConstants.Red;
                            }
                            DrawTextToGC(gc, 10, 30 + lineHeight * n++, cl4, "下：" + down);
 
                        }
                        
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
                return gc;
            }
        }
        /// <summary>
        /// 所有加载的任务清单
        /// </summary>
        public List<VisionTask> List_vs_task = new List<VisionTask>();
        /// <summary>
        /// 当前使用的任务清单
        /// </summary>
        public List<VisionTask> List_vs_task_cur = new List<VisionTask>();

        private readonly CaptureTicketQueue<VisionTask> captureTickets = new CaptureTicketQueue<VisionTask>();
        private long captureSessionId;
        private volatile bool captureSessionReady = true;
        /// <summary>丢失帧Ticket失效后，等待迟到图像的隔离时间</summary>
        private const int CaptureQuarantineMs = 2000;
        /// <summary>开始新批次时，等待上一批Ticket释放及相机恢复的最长时间</summary>
        private const int CaptureDrainWaitMs = 5000;
        /// <summary>相机恢复：关触发并清FIFO后，要求连续多长时间没有任何图像到达</summary>
        private const int CaptureQuietMs = 500;
        /// <summary>从FIFO实际取出的图像总数(恢复静默窗口判断用)</summary>
        private long acquiredFrameCount;
        /// <summary>进入丢帧恢复前的触发使能状态(恢复成功后还原)</summary>
        private bool? triggerEnabledBeforeRecovery;

        // ---- 触发追踪(TrigTrace)：只用于日志诊断，不参与任何判断 ----
        /// <summary>最近一帧从FIFO取出的时间(UTC Ticks)</summary>
        private long lastFrameTicksUtc;
        /// <summary>最近一帧的TriggerNumber</summary>
        private int lastFrameTriggerNumber = -1;
        /// <summary>最近一次发出触发时的已取出图像总数</summary>
        private long frameCountAtLastPulse = -1;
        /// <summary>最近一次发出触发的时间(UTC Ticks)</summary>
        private long lastPulseTicksUtc;
        /// <summary>最近一次触发的分段耗时记录，超时诊断时一并输出</summary>
        private volatile string lastTriggerTrace = string.Empty;

        private void ReadFifoStateForTrace(out int pending, out int ready, out bool busy)
        {
            pending = -1;
            ready = -1;
            busy = false;
            try
            {
                if (mAcqFifo != null) mAcqFifo.GetFifoState(out pending, out ready, out busy);
            }
            catch
            {
            }
        }

        private static long ElapsedMsSince(long ticksUtc)
        {
            return ticksUtc <= 0 ? -1 : (DateTime.UtcNow.Ticks - ticksUtc) / TimeSpan.TicksPerMillisecond;
        }

        /// <summary>
        /// 拍照超时诊断日志：区分"触发后相机完全没出图"和"出图了但未处理完"，并带出本次触发的分段耗时。
        /// </summary>
        public void LogCaptureTimeoutDiagnostics(string taskName)
        {
            try
            {
                int fifoPending, fifoReady;
                bool fifoBusy;
                ReadFifoStateForTrace(out fifoPending, out fifoReady, out fifoBusy);
                long countAtPulse = Interlocked.Read(ref frameCountAtLastPulse);
                long countNow = Interlocked.Read(ref acquiredFrameCount);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                    "TrigTrace 超时诊断 Cam={0},Task={1},距最近触发={2}ms,触发后取出图像数={3},FIFO当前Pending/Ready/Busy={4}/{5}/{6},距上一帧={7}ms,上一帧TriNum={8},Ticket待认领={9},最近触发记录:[{10}]",
                    mName, taskName, ElapsedMsSince(Interlocked.Read(ref lastPulseTicksUtc)),
                    countAtPulse < 0 ? -1 : countNow - countAtPulse,
                    fifoPending, fifoReady, fifoBusy,
                    ElapsedMsSince(Interlocked.Read(ref lastFrameTicksUtc)), lastFrameTriggerNumber,
                    captureTickets.HasPending, lastTriggerTrace));
            }
            catch
            {
            }
        }
        /// <summary>未登记Ticket的触发(手动/标定/连续采集)允许按curTask处理的图像数</summary>
        private int manualFrameCredits;
        private const int ManualFrameCreditMax = 4;

        public bool CaptureSessionReady
        {
            get { return captureSessionReady; }
        }

        public long CurrentCaptureSessionId
        {
            get { return Interlocked.Read(ref captureSessionId); }
        }

        /// <summary>
        /// 触发号校验是否拦截：false=仅记录日志(默认，诊断观察)，true=触发号不衔接时让本批失效。
        /// 只影响依赖TriggerNumber语义的判断；Overrun、多余图像、未登记图像、取图异常始终作废本批。
        /// 触发号校验只能发现部分丢帧/多帧，不能证明图像与触发一一对应；现场确认相机TriggerNumber语义后再开启。
        /// </summary>
        private static bool CaptureGuardBlocking
        {
            get
            {
                try
                {
                    return NewSysInf.UserParams != null && NewSysInf.UserParams.bCaptureGuardBlock;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void AddManualCredit()
        {
            while (true)
            {
                int current = manualFrameCredits;
                if (current >= ManualFrameCreditMax) return;
                if (Interlocked.CompareExchange(ref manualFrameCredits, current + 1, current) == current) return;
            }
        }

        private bool TryConsumeManualCredit()
        {
            while (true)
            {
                int current = manualFrameCredits;
                if (current <= 0) return false;
                if (Interlocked.CompareExchange(ref manualFrameCredits, current - 1, current) == current) return true;
            }
        }

        public List<VisionOutPutData> ListResultTemp = new List<VisionOutPutData>();
        /// <summary>
        /// //相机接收图像次数计数，防止误触发！
        /// </summary>
        public int inputImageCnt = 0;
        /// <summary>
        /// 误触发计数记录，大于4次弹窗提示
        /// </summary>
        public int inputImageErrCnt = 0;
        /// <summary>
        /// 复制任务
        /// </summary>
        public List<VisionTask> List_vs_copytask = new List<VisionTask>();
        /// <summary>
        /// 用于指示使用List_vs_task_cur中的哪个任务
        /// </summary>
        int curTaskIdx = 0;
        /// <summary>
        /// 设置/获取当前任务
        /// </summary>
        public VisionTask curTask
        {
            get
            {
                //lock (obj)
                {
                    if (List_vs_task_cur != null && curTaskIdx >= 0 && curTaskIdx < List_vs_task_cur.Count)
                    {
                        return List_vs_task_cur.ElementAt(curTaskIdx);
                    }
                    return null;
                }
            }
            set
            {
                if (List_vs_task_cur != null && curTaskIdx >= 0 && curTaskIdx < List_vs_task_cur.Count)
                {
                    List_vs_task_cur[curTaskIdx] = value;
                }
            }
        }
        /// <summary>
        /// 指向下一个任务，到最后一个后折返回第一个。
        /// </summary>
        public void NextTask()
        {
            //lock (obj)
            {
                //next task
                curTaskIdx++;
                if (curTaskIdx >= List_vs_task_cur.Count) curTaskIdx = 0;
            }
        }
        /// <summary>
        /// 加载所有任务
        /// </summary>
        /// <param name="path">任务文件路径，默认加载 当前产品/Carera/相机名/ 文件夹下所有视觉文件</param>
        /// <returns></returns>
        public EM_RES LoadTask(string path = "")
        {
            if (path.Length < 3) path = string.Format("{0}\\product\\{1}\\Camera\\{2}\\", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mName);
            List_vs_task.Clear();
            List_vs_copytask.Clear();

            //chk direct
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                return EM_RES.CAM_TASK_LOAD_ERR;
            }

            //get file
            string[] vppfile = Directory.GetFiles(path, "*.vpp");
            if (vppfile.Length == 0) return EM_RES.CAM_TASK_LOAD_ERR;

            //load all ToolBlock             
            foreach (string filepath in vppfile)
            {
                bool bHad = false;
                foreach (VisionTask task in List_vs_task)
                {
                    if (task.TaskName == Path.GetFileNameWithoutExtension(filepath))
                    {
                        bHad = true;
                        break;
                    }
                }
                if (bHad) continue;

                List_vs_task.Add(new VisionTask(filepath, this));

                //Load ToolBlock ListCaliTool
                LoadTaskCaliTool(List_vs_task[List_vs_task.Count - 1]);
            }

            return EM_RES.OK;
        }

        public EM_RES LoadTaskCaliTool(VisionTask tsk, string path = "")
        {
            if (tsk == null) return EM_RES.PARA_ERR;
            if (path.Length < 3) path = string.Format("{0}\\syscfg\\Calibration\\{1}\\TaskCalibration\\", Path.GetFullPath(".."), mName);
            //清空任务中的校正关系
            tsk.ListCaliTool.Clear();
            //get file
            if (!Directory.Exists(path)) return EM_RES.OK;

            string[] vppfile = Directory.GetFiles(path, "*.vpp");
            if (vppfile.Length == 0) return EM_RES.OK;

            //load all ToolBlock             
            foreach (string filneam in vppfile)
            {
                if (!File.Exists(filneam)) continue;

                CaliTool tool = new CaliTool();
                if (Path.GetFileNameWithoutExtension(filneam).Contains(tsk.TaskName))
                {
                    if (EM_RES.OK == tool.Load(filneam))
                    {
                        tsk.ListCaliTool.Add(tool);
                    }

                    break;
                }

            }
            return EM_RES.OK;
        }

        /// <summary>
        /// 找任务拍照
        /// </summary>
        /// <param name="name"任务名称</param>
        /// <returns></returns>
        public EM_RES FindTaskTriAndWait(string name, bool Demo = false)
        {
            EM_RES res;
            try
            {
                List_vs_task_cur.Clear();
                inputImageCnt = 0;
                curTaskIdx = 0;
                VisionTask task = List_vs_task.Find(s => s.TaskName.Equals(name));
                if (task == null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 无法找到流程!名字:{1}", mName, name) : string.Format("{0} Can't find process!name:{1}   ({0} 无法找到流程!名字:{1})", mName, name), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
                List_vs_task_cur.Add(task);

                //long tsBefore = task.ResData == null ? 0 : task.ResData.TimeStamp;   
                //int triBefore = task.ResData == null ? -1 : task.ResData.TriNum;      

                res = task.TriAndWaitResult(ref VAR.gsys_set.bquit, 2000, 1, Demo);
                if (res != EM_RES.OK) return res;

                if (!Demo && inputImageCnt == 0)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                        "{0} 拍照无新帧却返回OK，按超时处理 flow={1}", mName, name));
                    return EM_RES.TIMEOUT;
                }
                //if (task.ResData == null || (task.ResData.TimeStamp == tsBefore && task.ResData.TriNum == triBefore))
                //{
                //    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                //        "{0} 拍照命中残留旧结果，按超时处理 flow={1} TriNum={2}",
                //        mName, name, triBefore));
                //    return EM_RES.TIMEOUT;
                //}
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 拍照异常!{1}", mName, ex.Message) : string.Format("{0}ERROR:photograph!    ({0} 拍照异常!{1})", mName, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
            return EM_RES.OK;
        }

        ///// <summary>
        ///// 飞拍配置任务
        ///// </summary>
        ///// <param name="TaskName">列表名称</param>
        ///// <returns></returns>
        //public EM_RES ListTaskCfg(List<string> ListTaskName, bool IfClear = true)
        //{
        //    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0}配置飞拍任务!", disc));
        //    if (ListTaskName.Count == 0)
        //    {
        //        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}配置飞拍任务的名称列表为空!", mName));
        //        return EM_RES.CAM_PARA_ERR;
        //    }
        //    if(IfClear) List_vs_task_cur.Clear();
        //    curTaskIdx = 0;
        //    //foreach (string TaskName in ListTaskName)
        //    for (int i = 0; i < ListTaskName.Count;i++ )
        //    {
        //        VisionTask task = List_vs_task.Find(s => s.TaskName.Equals(ListTaskName[i]));
        //        if (task == null)
        //        {
        //            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 无法找到流程!名字:{1}", mName, ListTaskName[i]));
        //            return EM_RES.CAM_PARA_ERR;
        //        }
        //        if (List_vs_task_cur.Contains(task))
        //        {
        //            VisionTask vstask = List_vs_copytask.Find(s => s.TaskName.Equals(ListTaskName[i]+i.ToString()));
        //            if (vstask == null)
        //            {
        //                vstask = task.Clone();
        //                vstask.TaskName = ListTaskName[i] + i.ToString();
        //                List_vs_copytask.Add(vstask);
        //            }

        //            vstask.ResData.Clear();
        //            List_vs_task_cur.Add(vstask);
        //        }
        //        else
        //        {
        //            task.ResData.Clear();
        //            List_vs_task_cur.Add(task);
        //        }


        //    }
        //    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}STOP...!", disc));
        //    EM_RES res;//= StopLive();
        //    //if (res != EM_RES.OK) return res;
        //    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}CFG...!", disc));
        //    res = CapCfg(curCapCfg);
        //    if (res != EM_RES.OK) return res;
        //    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}清空...!", disc));
        //    if (!FlushOk)
        //    {
        //        mAcqFifo.Flush();
        //        FlushOk = true;
        //    }
        //    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0}配置飞拍任务完成,{1}", disc, FlushOk));
        //    return EM_RES.OK;
        //}

        /// <summary>
        /// 开始新拍照批次：作废上一批未完成Ticket，等待已到图的处理完成、丢失帧过隔离期后回收，再建立新批次。
        /// 最多等待 CaptureDrainWaitMs，超时则禁止拍照(本次返回失败，下次调用会再次尝试)。
        /// </summary>
        private bool BeginCaptureSession(string source)
        {
            int abandoned = captureTickets.AbandonOutstanding();
            if (abandoned > 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                    "CaptureTrace {0}前作废上一批未完成Ticket Cam={1},Session={2},Count={3}，其迟到图像将被丢弃",
                    source, mName, captureSessionId, abandoned));
            }

            DateTime startUtc = DateTime.UtcNow;
            string detail = string.Empty;
            while (true)
            {
                int reclaimed;
                if (captureTickets.TryReclaimLostFrames(TimeSpan.FromMilliseconds(CaptureQuarantineMs), out reclaimed, out detail))
                {
                    if (reclaimed > 0)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                            "CaptureTrace {0}前回收未到图Ticket Cam={1},{2}", source, mName, detail));
                    }

                    if (captureTickets.IsRecoveryRequired)
                    {
                        string recoveryDetail;
                        if (!RunCaptureRecovery(source, startUtc.AddMilliseconds(CaptureDrainWaitMs), out recoveryDetail))
                        {
                            detail = recoveryDetail;
                            if ((DateTime.UtcNow - startUtc).TotalMilliseconds >= CaptureDrainWaitMs) goto Failed;
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    long sessionId;
                    string error;
                    if (captureTickets.TryBeginSession(out sessionId, out error))
                    {
                        Interlocked.Exchange(ref captureSessionId, sessionId);
                        Interlocked.Exchange(ref manualFrameCredits, 0);
                        captureSessionReady = true;
                        long waitedMs = (long)(DateTime.UtcNow - startUtc).TotalMilliseconds;
                        if (waitedMs > 50)
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                                "CaptureTrace {0}等待上一批释放 Cam={1},Wait={2}ms", source, mName, waitedMs));
                        }
                        return true;
                    }
                    detail = error;
                }

                if ((DateTime.UtcNow - startUtc).TotalMilliseconds >= CaptureDrainWaitMs) goto Failed;
                Thread.Sleep(10);
            }

        Failed:
            captureSessionReady = false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                "{0}{1}失败，上一批拍照在{2}ms内未能释放或相机恢复未完成({3})。为避免身份不明的图像进入新批次，本次禁止拍照。",
                mName, source, CaptureDrainWaitMs, detail), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
            return false;
        }

        /// <summary>
        /// 丢帧后的相机恢复：关闭触发 -> 清空FIFO(必须成功) -> 连续 CaptureQuietMs 无任何图像到达 -> 恢复触发。
        /// 任一步失败都不结束恢复期，调用方不得建立新批次。
        /// 依据：VisionPro/CVL 文档说明 Flush 会取消并丢弃未完成和已完成的采集；静默窗口用于现场确认没有在途图像。
        /// </summary>
        private bool RunCaptureRecovery(string source, DateTime deadlineUtc, out string detail)
        {
            detail = string.Empty;
            if (mAcqFifo == null)
            {
                detail = "相机未连接，无法恢复";
                return false;
            }

            bool triggerDisabled = false;
            try
            {
                int n = 0;
                while (FlushUpdate && n++ < 150) Thread.Sleep(10);
                FlushUpdate = true;

                if (mAcqFifo.OwnedTriggerParams != null)
                {
                    // 只在第一次进入恢复时记录原触发状态；上次恢复失败时触发已被关闭，不能把关闭状态当成原状态
                    if (!triggerEnabledBeforeRecovery.HasValue)
                    {
                        triggerEnabledBeforeRecovery = mAcqFifo.OwnedTriggerParams.TriggerEnabled;
                    }
                    mAcqFifo.OwnedTriggerParams.TriggerEnabled = false;
                    triggerDisabled = true;
                }

                mAcqFifo.Flush();
                FlushOk = true;

                long seen = Interlocked.Read(ref acquiredFrameCount);
                DateTime quietStart = DateTime.UtcNow;
                while ((DateTime.UtcNow - quietStart).TotalMilliseconds < CaptureQuietMs)
                {
                    if (DateTime.UtcNow >= deadlineUtc)
                    {
                        detail = "清空FIFO后仍持续收到图像，静默确认未通过";
                        return false;
                    }
                    Thread.Sleep(10);
                    long now = Interlocked.Read(ref acquiredFrameCount);
                    if (now != seen)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                            "CaptureTrace 恢复静默期内收到图像 Cam={0},Count={1}，重新计时", mName, now - seen));
                        seen = now;
                        quietStart = DateTime.UtcNow;
                    }
                }

                // 结束恢复必须与回调的"取图->认领"临界区互斥(同一把 lock(obj))：
                // 1) 已从FIFO取进内存、尚未认领的图像，会先在恢复期内被判为 DuringRecovery 丢弃，之后这里才能拿到锁；
                // 2) 拿到锁时再确认FIFO没有已完成图像、静默期计数没有变化，然后结束恢复期。
                // 触发保持关闭到恢复期结束之后才打开，因此结束前不会产生新采集。
                int lockWaitMs = Math.Max(0, (int)(deadlineUtc - DateTime.UtcNow).TotalMilliseconds);
                if (!Monitor.TryEnter(obj, lockWaitMs))
                {
                    detail = "等待相机回调退出超时";
                    return false;
                }
                try
                {
                    int fifoPending, fifoReady;
                    bool fifoBusy;
                    mAcqFifo.GetFifoState(out fifoPending, out fifoReady, out fifoBusy);
                    if (fifoReady > 0)
                    {
                        detail = string.Format("结束恢复时FIFO仍有已完成图像(Ready={0},Pending={1})", fifoReady, fifoPending);
                        return false;
                    }
                    if (Interlocked.Read(ref acquiredFrameCount) != seen)
                    {
                        detail = "结束恢复时又收到图像";
                        return false;
                    }
                    if (!captureTickets.CompleteRecovery())
                    {
                        detail = "恢复期间出现新的未完成Ticket";
                        return false;
                    }
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                        "CaptureTrace 结束恢复 Cam={0},FIFO Ready={1},Pending={2}", mName, fifoReady, fifoPending));
                }
                finally
                {
                    Monitor.Exit(obj);
                }

                // 在锁外恢复触发：避免设置触发参数时与相机事件线程(正在等待 lock(obj))互相等待
                if (triggerDisabled && triggerEnabledBeforeRecovery.HasValue && triggerEnabledBeforeRecovery.Value)
                {
                    mAcqFifo.OwnedTriggerParams.TriggerEnabled = true;
                }
                triggerDisabled = false;
                triggerEnabledBeforeRecovery = null;

                VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, string.Format(
                    "CaptureTrace {0}前相机恢复完成 Cam={1},关触发+清FIFO+静默{2}ms", source, mName, CaptureQuietMs));
                return true;
            }
            catch (Exception ex)
            {
                // 清FIFO或触发设置失败：保持恢复期，禁止建立新批次
                detail = "相机恢复异常:" + ex.Message;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("CaptureTrace 相机恢复失败 Cam={0},{1}", mName, ex.Message));
                return false;
            }
            finally
            {
                FlushUpdate = false;
                if (triggerDisabled)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                        "CaptureTrace 相机恢复未完成，触发保持关闭 Cam={0}", mName));
                }
            }
        }

        /// <summary>
        /// 为当前批次登记Ticket。useTaskHole=true 时孔位取任务的 TriPos.n(飞拍)；false 时记 -1(定拍，孔位由调用方日志给出)。
        /// </summary>
        private bool RegisterCaptureTickets(IList<VisionTask> tasks, string source, bool useTaskHole)
        {
            try
            {
                long sessionId = Interlocked.Read(ref captureSessionId);
                foreach (VisionTask task in tasks)
                {
                    int hole = useTaskHole ? task.TriPos.n : -1;
                    CaptureTicket<VisionTask> ticket = captureTickets.Register(sessionId, task, hole);
                    task.CaptureSessionId = ticket.SessionId;
                    task.CaptureTicketId = ticket.TicketId;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                        "CaptureTrace 配置 Cam={0},Source={1},Session={2},Ticket={3},Hole={4},Task={5}",
                        mName, source, ticket.SessionId, ticket.TicketId, ticket.PositionIndex, task.TaskName));
                }
                return true;
            }
            catch (Exception ex)
            {
                captureSessionReady = false;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                    "{0}{1}登记拍照Ticket失败:{2}", mName, source, ex.Message), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                return false;
            }
        }

        private bool CanAppendCaptureTickets(string source)
        {
            if (!captureSessionReady || captureTickets.ActiveSessionId != Interlocked.Read(ref captureSessionId) || !captureTickets.HasOutstanding)
            {
                captureSessionReady = false;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                    "{0}{1}失败，当前拍照批次不可追加。", mName, source), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                return false;
            }
            return true;
        }

        public EM_RES PrepareDirectCapture(VisionTask task)
        {
            if (task == null) return EM_RES.PARA_ERR;
            if (!BeginCaptureSession("定拍")) return EM_RES.TIMEOUT;
            return RegisterCaptureTickets(new List<VisionTask> { task }, "定拍", false) ? EM_RES.OK : EM_RES.CAM_ERR;
        }

        public void InvalidateCaptureSession(string reason)
        {
            captureTickets.InvalidateSession(Interlocked.Read(ref captureSessionId));
            captureSessionReady = false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                "{0}拍照批次Session:{1}失效，{2}。迟到图像将被丢弃。", mName, captureSessionId, reason), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
        }

        public EM_RES ListTaskCfg(List<string> ListTaskName, List<ST_XYN> tripos, bool IfClear = true, List<string> saveBarcodes = null)
        {
            //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("{0}配置飞拍任务!", disc) : string.Format("{0} Configure flyshot task!       ({1}配置飞拍任务!)", englishdisc, disc));
            if (ListTaskName.Count == 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}配置飞拍任务的名称列表为空!", mName) : string.Format("{0} Configure the name list of the aerial task to be empty!   ({0}配置飞拍任务的名称列表为空!)", mName), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_PARA_ERR;
            }
            // 先占用拍照批次(作废并等待上一批释放)，再改写任务对象，避免上一批迟到结果写入本批任务
            if (IfClear)
            {
                if (!BeginCaptureSession("飞拍配置")) return EM_RES.CAM_ERR;
            }
            else if (!CanAppendCaptureTickets("飞拍追加配置"))
            {
                return EM_RES.CAM_ERR;
            }
            if (IfClear)
            {
                List_vs_task_cur.Clear();
                ListResultTemp.Clear();
                inputImageCnt = 0;
            }
            curTaskIdx = 0;
            //foreach (string TaskName in ListTaskName)
            for (int i = 0; i < ListTaskName.Count; i++)
            {
                VisionTask task = List_vs_task.Find(s => s.TaskName.Equals(ListTaskName[i]));
                if (task == null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 无法找到流程!名字:{1}", mName, ListTaskName[i]) : string.Format("{0}Can't find process,name:{1}    ({0} 无法找到流程!名字:{1})", mName, ListTaskName[i]), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.CAM_PARA_ERR;
                }
                if (List_vs_task_cur.Contains(task))
                {
                    VisionTask vstask = List_vs_copytask.Find(s => s.TaskName.Equals(ListTaskName[i] + i.ToString()));
                    if (vstask == null)
                    {
                        vstask = task.Clone();
                        vstask.TaskName = ListTaskName[i] + i.ToString();
                        List_vs_copytask.Add(vstask);
                    }

                    vstask.ResData.Clear();
                    if (i < tripos.Count) vstask.TriPos = tripos[i].clone();
                    vstask.SaveBarcode = (saveBarcodes != null && i < saveBarcodes.Count) ? saveBarcodes[i] : null;
                    List_vs_task_cur.Add(vstask);
                }
                else
                {
                    task.ResData.Clear();
                    if (i < tripos.Count) task.TriPos = tripos[i].clone();
                    task.SaveBarcode = (saveBarcodes != null && i < saveBarcodes.Count) ? saveBarcodes[i] : null;
                    List_vs_task_cur.Add(task);
                }

            }
            if (IfClear)
            {
                if (!RegisterCaptureTickets(List_vs_task_cur, "飞拍配置", true)) return EM_RES.CAM_ERR;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                    "{0}飞拍配置建立拍照批次Session:{1},TicketCnt:{2}", mName, CurrentCaptureSessionId, List_vs_task_cur.Count));
            }
            else
            {
                var appendedTasks = new List<VisionTask>();
                for (int i = List_vs_task_cur.Count - ListTaskName.Count; i < List_vs_task_cur.Count; i++)
                {
                    if (i >= 0) appendedTasks.Add(List_vs_task_cur[i]);
                }
                if (!RegisterCaptureTickets(appendedTasks, "飞拍追加配置", true)) return EM_RES.CAM_ERR;
            }

            //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}STOP...!", disc));
            EM_RES res;//= StopLive();
            //if (res != EM_RES.OK) return res;
            //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}CFG...!", disc));
            res = CapCfg(curCapCfg);
            if (res != EM_RES.OK) return res;
            //VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}清空...!", disc));
            int n = 0;
            while (FlushUpdate)
            {
                Thread.Sleep(10);
                if (n++ > 150)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}飞拍配置等待FlushUpdate为false超时1.5S", mName) : string.Format("{0} Flyshot configuration waits for FlushUpdate to false timeout 1.5S   ({0}飞拍配置等待FlushUpdate为false超时1.5S)", mName), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
            }
            //clear result
            if (!FlushOk && !FlushUpdate)
            {
                FlushUpdate = true;
                mAcqFifo.Flush();
                FlushOk = true;
                FlushUpdate = false;
            }
           // VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese ? string.Format("{0}配置飞拍任务完成,{1}", disc, FlushOk) : string.Format("{0} Configure flyshot successfully,{2}   ({1}配置飞拍任务完成,{2})", englishdisc, disc, FlushOk));
            return EM_RES.OK;
        }
        #endregion
        #endregion
        #region 轮廓存取/移动
        public class ComShape
        {
            public CogCompositeShape mShape = new CogCompositeShape();
            public event EventHandler ShapeChanged;
            public CogTransform2DLinear ShapePos = new CogTransform2DLinear();
            public CogRecordDisplay mCogRecDisplay = new CogRecordDisplay();
            /// <summary>
            /// 加载轮廓
            /// </summary>
            /// <param name="filename">轮廓路径,默认为产品目录/Shape(id).shp</param>
            /// <param name="bEnableDrag">True为允许拖拽</param>
            /// <param name="id">轮廓编号</param>
            public bool Load(int id = 0, string filename = "", bool bEnableDrag = true)
            {
                if (filename == "") filename = string.Format("{0}\\product\\{1}\\Shape\\Shape{2}.shp", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, id);
                if (!File.Exists(filename)) return false;

                try
                {
                    mShape = (CogCompositeShape)CogSerializer.LoadObjectFromFile(filename);
                    if (mShape == null) return false;
                    mShape.ID = id;
                    mShape.SelectedSpaceName = "mm";
                    mShape.SelectedColor = CogColorConstants.Blue;
                    mShape.SelectedLineStyle = CogGraphicLineStyleConstants.Solid;
                    mShape.SelectedLineWidthInScreenPixels = 3;
                    mShape.SelectionGraphicColor = CogColorConstants.Blue;
                    mShape.SelectionGraphicLineStyle = CogGraphicLineStyleConstants.Solid;
                    mShape.SelectionGraphicLineWidthInScreenPixels = 2;
                    ShapePos.TranslationX = 0;
                    ShapePos.TranslationY = 0;
                    ShapePos.Rotation = 0;
                    //upate event
                    if (ShapeChanged != null)
                    {
                        mShape.Changed -= new CogChangedEventHandler(ShapeChanged);
                        mShape.Changed += new CogChangedEventHandler(ShapeChanged);
                    }
                    //enable drag
                    if (bEnableDrag) mShape.GraphicDOFEnable = CogCompositeShapeDOFConstants.Position | CogCompositeShapeDOFConstants.Rotation;
                    else mShape.GraphicDOFEnable -= (CogCompositeShapeDOFConstants.Position | CogCompositeShapeDOFConstants.Rotation);
                    return true;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("加载轮廓出错，{0},{1}", ex.Message, filename) : string.Format("ERROR:Load mShape,{0},{1}     (加载轮廓出错，{0},{1})", ex.Message, filename), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return false;
                }
            }
            /// <summary>
            /// 保存轮廓
            /// </summary>
            /// <param name="filename">轮廓路径,默认为产品目录/Shape(id).shp</param>
            public void Save(string filename = "")
            {

                if (mShape != null)
                {
                    // if (DialogResult.No == MessageBox.Show("是否保存图像轮廓?", "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question)) return;
                    if (filename == "") filename = string.Format("{0}\\product\\{1}\\Shape\\Shape{2}.shp", Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mShape.ID);
                    CogSerializer.SaveObjectToFile(mShape, filename);
                }
            }
            /// <summary>
            /// 轮廓定位
            /// </summary>
            /// <param name="x">X坐标</param>
            /// <param name="y">Y坐标</param>
            /// <param name="r">R坐标</param>
            /// <param name="bAbs">True 绝对定位，False 相对定位</param>
            /// <param name="Recdisplay">显示控件</param>
            public void Move(double x, double y, double r, bool bAbs = true, CogRecordDisplay Recdisplay = null)
            {
                if (Recdisplay == null) Recdisplay = mCogRecDisplay;
                if (mShape != null && Recdisplay != null && Recdisplay.Image != null)
                {
                    Recdisplay.InteractiveGraphics.Clear();
                    //set position
                    if (bAbs)
                    {
                        ShapePos.TranslationX = x;
                        ShapePos.TranslationY = y;
                        ShapePos.Rotation = CogMisc.DegToRad(r);
                    }
                    else
                    {
                        ShapePos.TranslationX += x;
                        ShapePos.TranslationY += y;
                        ShapePos.Rotation += CogMisc.DegToRad(r);
                    }
                    mShape.ParentFromChildTransform = ShapePos;
                    //update display
                    Recdisplay.InteractiveGraphics.Add(mShape, mShape.ID.ToString(), true);
                }
            }
            /// <summary>
            /// 返回当前坐标
            /// </summary>
            /// <returns></returns>
            public ST_XYA GetPos()
            {
                ST_XYA st_ofs = new ST_XYA();
                st_ofs.x = ShapePos.TranslationX;
                st_ofs.y = ShapePos.TranslationY;
                st_ofs.a = CogMisc.RadToDeg(ShapePos.Rotation);
                return st_ofs;
            }
        }

        #endregion
        #region 基本功能        
        #region 仿射变换
        public class AffineTransform
        {
            public CogCalibNPointToNPointTool NPointToNPointTool = new CogCalibNPointToNPointTool();
            public CogTransform2DLinear TransformMap = new CogTransform2DLinear();
            public string path;
            public string name = "unnamed";
            public ST_XY Scale = new ST_XY();
            public Double Angle = 0;
            public Double RMSError = 0;
            public bool bCalibrated
            {
                get
                {
                    return NPointToNPointTool.Calibration.Calibrated;
                }
            }
            /// <summary>
            /// 建立仿射关系
            /// </summary>
            /// <param name="InputCalibrated">已校正数据</param>
            /// <param name="InputUnCalibration">待校正数据</param>
            /// <param name="MaxRMSError">误差值限制</param>
            /// <param name="MaxAngle">角度限制(Degree)</param>
            /// <param name="MaxScale">缩放限制(‰)</param>
            /// <returns></returns>
            public EM_RES BuildTransform(ST_XY[] InputCalibrated, ST_XY[] InputUnCalibration, double MaxRMSError = 0.7, double MaxAngle = 360, double MaxScale = double.MaxValue)
            {
                EM_RES res = EM_RES.OK;
                //check
                if (InputCalibrated == null || InputUnCalibration == null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "仿射输入数据为null！" : "Affine input data is null!", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.PARA_ERR;
                }
                if (InputCalibrated.Length < 3 || InputUnCalibration.Length < 3)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "仿射输入数据数量<3！" : "Number of affine input data <3", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.PARA_ERR;
                }
                if (InputCalibrated.Length != InputUnCalibration.Length)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "仿射输入数据数量不等！" : "Affine input data varies", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return EM_RES.PARA_ERR;
                }

                //init
                NPointToNPointTool.Calibration.Uncalibrate();
                while (NPointToNPointTool.Calibration.NumPoints > 0)
                {
                    for (int i = 0; i < NPointToNPointTool.Calibration.NumPoints; i++)
                    {
                        NPointToNPointTool.Calibration.DeletePointPair(i);
                    }
                }
                //calibration
                for (int i = 0; i < InputCalibrated.Length; i++)
                {
                    NPointToNPointTool.Calibration.AddPointPair(InputCalibrated[i].x, InputCalibrated[i].y, InputUnCalibration[i].x, InputUnCalibration[i].y);
                }
                NPointToNPointTool.Calibration.Calibrate();

                //get result
                if (NPointToNPointTool.Calibration.Calibrated == false)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "仿射失败，请检查仿射变化的输入数据！" : "Affine failed, please check the input data for affine changes!", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    res = EM_RES.PARA_ERR;
                    return res;
                }
                RMSError = NPointToNPointTool.Calibration.ComputedRMSError;

                //get transform information
                TransformMap = (CogTransform2DLinear)NPointToNPointTool.Calibration.GetComputedUncalibratedFromCalibratedTransform();
                Angle = CogMisc.RadToDeg(TransformMap.Rotation);
                double scalingX, scalingY;
                double rotationX, rotationY;
                double translationX, translationY;
                TransformMap.GetScalingsRotationsTranslation(out scalingX, out scalingY, out rotationX, out rotationY, out translationX, out translationY);
                Scale.x = scalingX;
                Scale.y = scalingY;
                scalingX = (scalingX - 1) * 1000;
                scalingY = (scalingY - 1) * 1000;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, VAR.IsChinese ? string.Format("仿射sX={0:F3}‰,sY={1:F3}‰,R={2:F3},RMSE={3:F3}", scalingX, scalingY, Angle, RMSError) : string.Format("scalingX={0:F3}‰,scalingY={1:F3}‰,Angle={2:F3},RMSError={3:F3} (仿射sX={0:F3}‰,sY={1:F3}‰,R={2:F3},RMSE={3:F3})", scalingX, scalingY, Angle, RMSError));

                //check result
                if (RMSError > MaxRMSError)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("当前仿射误差{0:0.000}大于限定值{1:0.000}！", RMSError, MaxRMSError) : string.Format("Current RMSError({0:0.000})>MaxRMSError({1:0.000})!    (当前仿射误差{0:0.000}大于限定值{1:0.000}！)", RMSError, MaxRMSError), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    res = EM_RES.ERR;
                    return res;
                }
                if (Math.Abs(scalingX) > MaxScale)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("X方向张缩率{0:F3}‰超过限定值±{1:F3}‰", scalingX, MaxScale) : string.Format("scalingX({0:F3}‰) over ±MaxScale({1:F3}‰)    (X方向张缩率{0:F3}‰超过限定值±{1:F3}‰)", scalingX, MaxScale), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    res = EM_RES.PARA_OUTOFRANG;
                }
                if (Math.Abs(scalingY) > MaxScale)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("Y方向张缩率{0:F3}‰超过限定值±{1:F3}‰", scalingY, MaxScale) : string.Format("scalingY({0:F3}‰) over ±MaxScale({1:F3}‰)   (Y方向张缩率{0:F3}‰超过限定值±{1:F3}‰)", scalingY, MaxScale), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    res = EM_RES.PARA_OUTOFRANG;
                }

                if (Math.Abs(Angle) > MaxAngle)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("仿射角度{0:F3}超范围±{1:F3}", Angle, MaxAngle) : string.Format("Angle({0:F3}‰) over ±MaxAngle({1:F3}‰)    (仿射角度{0:F3}超范围±{1:F3})", Angle, MaxAngle), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    res = EM_RES.PARA_OUTOFRANG;
                }
                return res;
            }
            /// <summary>
            /// 根据BuildTransform建立的映射关系映射点数组
            /// </summary>
            /// <param name="InputUnCalibration">待映射的数组</param>
            /// <returns>返回映射后的点数组，A为映射角度</returns>
            public ST_XYA[] Transform(ST_XY[] InputUnCalibration)
            {
                //check
                if (InputUnCalibration.Length == 0) return new ST_XYA[0];
                ST_XYA[] Calibrateddata = new ST_XYA[InputUnCalibration.Length];

                //stransform
                for (int i = 0; i < InputUnCalibration.Length; i++)
                {
                    TransformMap.MapPoint(InputUnCalibration[i].x, InputUnCalibration[i].y, out Calibrateddata[i].x, out Calibrateddata[i].y);
                    Calibrateddata[i].a = TransformMap.Rotation;
                }
                return Calibrateddata;
            }
            /// <summary>
            /// 保存映射关系
            /// </summary>
            /// <param name="filename">保存路径，默认用path保存的路径</param>
            /// <returns></returns>
            public bool Save(string spacename, string filename = "")
            {
                if (filename.Length < 3) filename = path;
                if (filename.Length < 3)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}仿射保存路径异常{1}，", name, filename) : string.Format("{0}Affine save path exception{1}    ({0}仿射保存路径异常{1}，)", name, filename), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return false;
                }
                try
                {
                    NPointToNPointTool.RunParams.CalibratedSpaceName = spacename;
                    CogSerializer.SaveObjectToFile(NPointToNPointTool, filename);
                    return true;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}仿射保存出错，{1}，{2}", name, ex.Message, filename) : string.Format("{0}ERROR:Affine save,{1},{2}    ({0}仿射保存出错，{1}，{2})", name, ex.Message, filename), DReport.EmErrCode.SaveFailed, (int)DReport.EmHareware.Cam);
                    return false;
                }
            }
            /// <summary>
            /// 加载映射关系
            /// </summary>
            /// <param name="filename">保存路径，默认用path保存的路径</param>
            /// <returns></returns>
            public bool Load(string filename = "")
            {
                if (filename.Length < 3) filename = path;
                if (filename.Length < 3)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}仿射加载路径异常{1}，", name, filename) : string.Format("{0}Affine loading path abnormal,{1}   ({0}仿射加载路径异常{1}，)", name, filename), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return false;
                }
                try
                {
                    NPointToNPointTool = CogSerializer.LoadObjectFromFile(filename, typeof(System.Runtime.Serialization.Formatters.Binary.BinaryFormatter), CogSerializationOptionsConstants.Minimum) as CogCalibNPointToNPointTool;
                    return true;
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}仿射加载出错，{1}，{2}", name, ex.Message, filename) : string.Format("{0}Affine loading error,{1},{2}   ({0}仿射加载出错，{1}，{2})", name, ex.Message, filename), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                    return false;
                }
            }
        }
        #endregion
        #region 圆形拟合
        /// <summary>
        /// 根据提供的点数据拟合圆
        /// </summary>
        /// <param name="pt">点数据</param>
        /// <param name="Circle">返回圆心(XY),半径(Z),误差(A)</param>
        /// <param name="MaxRMSError">允许的最大误差</param>
        /// <param name="IgnoreNum">允许忽略点的个数</param>
        /// <param name="SpaceName">使用坐标空间</param>
        /// <param name="CogRecDisplay">显示控件</param>
        /// <returns></returns>
        public EM_RES FitCircle(ST_XY[] pt, ref ST_XYZA Circle, double MaxRMSError = 0.5, int IgnoreNum = 5, string SpaceName = "mm", CogRecordDisplay CogRecDisplay = null)
        {
            //check
            if (pt == null && pt.Length < 3)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "圆拟合数据为空或数量小于3！" : "The circle fitting data is empty or the number is less than 3!       ( 圆拟合数据为空或数量小于3)", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }
            //init
            CogFitCircle fitcircle = new CogFitCircle();
            for (int n = 0; n < fitcircle.NumPoints; n++) fitcircle.DeletePoint(n);
            for (int i = 0; i < pt.Length; i++) fitcircle.AddPoint(pt[i].x, pt[i].y);
            fitcircle.NumToIgnore = IgnoreNum;
            //cacl
            CogFitCircleResult CircleRes;
            CircleRes = fitcircle.Execute(SpaceName);
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, VAR.IsChinese ? string.Format("旋转中心 X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3}", CircleRes.GetCircle().CenterX, CircleRes.GetCircle().CenterY, CircleRes.GetCircle().Radius, CircleRes.RMSError) : string.Format("Rotation center X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3}     (旋转中心 X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3})", CircleRes.GetCircle().CenterX, CircleRes.GetCircle().CenterY, CircleRes.GetCircle().Radius, CircleRes.RMSError));
            //check result
            if (CircleRes.RMSError > MaxRMSError)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("拟合圆失败,RMSE={0:F3}，超范围({1:F3})", CircleRes.RMSError, MaxRMSError) : string.Format("Failed to fit circle,RMSE={0:F3}，over range({1:F3})     (拟合圆失败,RMSE={0:F3}，超范围({1:F3}))", CircleRes.RMSError, MaxRMSError), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
            //get data
            Circle.x = CircleRes.GetCircle().CenterX;
            Circle.y = CircleRes.GetCircle().CenterY;
            Circle.z = CircleRes.GetCircle().Radius;
            Circle.a = CircleRes.RMSError;

            //show
            //Thread.Sleep(10);
            //Application.DoEvents();
            // graphics_other.Clear();
            graphics_other = CircleRes.CreateResultGraphics(CogFitCircleResultGraphicConstants.All);
            if (CogRecDisplay != null)// && CogRecDisplay.Image != null)
            {
                CogRecDisplay.StaticGraphics.AddList(graphics_other, "Other");
            }
            return EM_RES.OK;
        }
        #endregion
        #region 直线拟合
        /// <summary>
        /// 根据提供的点数据拟合直线
        /// </summary>
        /// <param name="pt">点数据</param>
        /// <param name="Line">返回直线参考点(XY),角度(Z),误差(A)</param>
        /// <param name="MaxRMSError">允许的最大误差</param>
        /// <param name="IgnoreNum">允许忽略点的个数</param>
        /// <param name="SpaceName">使用坐标空间</param>
        /// <param name="CogRecDisplay">显示控件</param>
        /// <returns></returns>
        public EM_RES FitLine(ST_XY[] pt, ref ST_XYZA Line, double MaxRMSError = 0.5, int IgnoreNum = 5, string SpaceName = "mm", CogRecordDisplay CogRecDisplay = null)
        {
            //check
            if (pt == null && pt.Length < 2)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "直线拟合数据为空或数量小于2！" : "Line fitting data is empty or the number is less than 2!        (直线拟合数据为空或数量小于2！)", DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }
            //init
            CogFitLine fiteline = new CogFitLine();
            for (int n = 0; n < fiteline.NumPoints; n++) fiteline.DeletePoint(n);
            for (int i = 0; i < pt.Length; i++) fiteline.AddPoint(pt[i].x, pt[i].y);
            fiteline.NumToIgnore = IgnoreNum;
            //cacl
            CogFitLineResult LineRes;
            LineRes = fiteline.Execute(SpaceName);
            if (LineRes.GetLine() == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "直线拟合失败" : "Straight line fitting failed       (直线拟合失败)", DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, VAR.IsChinese ? string.Format("旋转中心 X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3}", LineRes.GetLine().X, LineRes.GetLine().Y, LineRes.GetLine().Rotation, LineRes.RMSError) : string.Format("Rotation center X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3}    (旋转中心 X={0:F3} Y={1:F3}，R={2:F3},RMSE={3:F3})", LineRes.GetLine().X, LineRes.GetLine().Y, LineRes.GetLine().Rotation, LineRes.RMSError));
            //check result
            if (LineRes.RMSError > MaxRMSError)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("直线拟合失败,RMSE={0:F3}，超范围({1:F3})", LineRes.RMSError, MaxRMSError) : string.Format("Straight line fitting failed,RMSE={0:F3}，over range({1:F3})    (直线拟合失败,RMSE={0:F3}，超范围({1:F3}))", LineRes.RMSError, MaxRMSError), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
            //get data
            Line.x = LineRes.GetLine().X;
            Line.y = LineRes.GetLine().Y;
            Line.z = LineRes.GetLine().Rotation;
            Line.a = LineRes.RMSError;

            //show
            graphics_other = LineRes.CreateResultGraphics(CogFitLineResultGraphicConstants.All);
            if (CogRecDisplay != null && CogRecDisplay.Image != null)
            {
                CogRecDisplay.StaticGraphics.AddList(graphics_other, "Other");
            }
            return EM_RES.OK;
        }
        #endregion
        #region 画面置中
        /// <summary>
        /// 把识别点移动到画面中心
        /// </summary>
        /// <param name="bquit">退出控制</param>
        /// <param name="VsTask">视觉任务</param>
        /// <param name="Tol">容差，与画面中心距离小于这个值时成功返回</param>
        /// <param name="TryCnt">尝试对中次数</param>
        /// <param name="VsTryCnt">视觉尝试次数</param>
        /// <param name="VsTimeOut">视觉超时</param>
        /// <returns></returns>
        public EM_RES MoveToImgCenter(ref bool bquit, ref ST_XYZA StMovPos, VisionTask VsTask, List<CaliTool> ListCaliTool = null, double Tol = 0.02, int TryCnt = 10, int VsTryCnt = 5, int VsTimeOut = 1000)
        {
            //check
            if (VsTask == null || MoveHandle == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("对中参数异常，视觉任务为{0}，{1}", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "移动函数为 null" : "") : string.Format("The alignment parameters are abnormal, and the vision task is {0},{1}     (对中参数异常，视觉任务为{0}，{1})", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "Move function is null    (移动函数为 null)" : ""), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }

            EM_RES res = EM_RES.OK;
            int n;
            ST_XYZA StPos = new ST_XYZA();
            StPos = StMovPos;
            try
            {
                //update posInf
                res = MoveHandle(ref bquit, true, true, false, false, false, ref StPos, this.mName);
                if (res != EM_RES.OK) return res;

                //rotate and cap	
                for (n = 0; n < TryCnt; n++)
                {

                    //triger and wait result
                    res = VsTask.TriAndWaitResult(ref bquit, VsTimeOut, VsTryCnt);
                    if (res != EM_RES.OK) return res;

                    //check result
                    if (VsTask.ResData.bOK == false)
                    {
                        return EM_RES.ERR;
                    }
                    Thread.Sleep(10);
                    Application.DoEvents();
                    //if (ListCaliTool != null && ListCaliTool.Count > 0)
                    //{
                    //    //cali point posInf
                    //    ST_XYA st_pt = VsTask.ResData.PosMM;
                    //    foreach (CaliTool tool in ListCaliTool)
                    //    {
                    //        st_pt = tool.MapPoint(st_pt.ToXY());
                    //    }
                    //    //cali center posInf
                    //    ST_XYA st_pt_cent = VsTask.ResData.CentOfs;
                    //    foreach (CaliTool tool in ListCaliTool)
                    //    {
                    //        st_pt_cent = tool.MapPoint(st_pt_cent.ToXY());
                    //    }
                    //    //cal offset
                    //    VsTask.ResData.CentOfs = st_pt_cent - st_pt;
                    //}

                    //check posInf
                    if (Math.Abs(VsTask.ResData.CentOfs.x) <= Tol && Math.Abs(VsTask.ResData.CentOfs.y) <= Tol)
                        break;
                    //calc new posInf 
                    StPos.x -= VsTask.ResData.CentOfs.x;
                    StPos.y -= VsTask.ResData.CentOfs.y;
                    //move
                    res = MoveHandle(ref bquit, true, true, false, false, false, ref StPos, this.mName);
                    if (res != EM_RES.OK) return res;


                }

                if (n >= TryCnt)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "对中失败" : "Falied to move to image center    (对中失败)", DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
                StMovPos = StPos;
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("对中出错，{0}", ex.Message) : string.Format("Error:Move to image center,{0}   (对中出错，{0})", ex.Message), DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
            }
            return EM_RES.OK;
        }
        #endregion
        #region 旋转中心
        /// <summary>
        /// 委托硬件旋转，根据指定视觉任务的返回值，计算旋转中心
        /// </summary>
        /// <param name="bquit">取消控制</param>
        /// <param name="RotCenter">返回旋转中心(XY),旋转半径(Z),拟合误差（A)</param>
        /// <param name="VsTask">指定视觉任务</param>
        /// <param name="CogRecDisplay">指定控件显示拟合结果</param>
        /// <param name="StartAngle">开始角度</param>
        /// <param name="EndAngle">结束角度</param>
        /// <param name="Cnt">平均取点数量</param>
        /// <param name="VsTryCnt">视觉失败尝试次数</param>
        /// <param name="VsTimeOut">视觉等待视觉</param>
        /// <returns></returns>
        public EM_RES CalRotCenter(ref bool bquit, ref ST_XYZA RotCenter, VisionTask VsTask, bool IsU1 = true, CogRecordDisplay CogRecDisplay = null, bool Growthrend = true, double MaxRMSError = 0.5, double StartAngle = 0, double EndAngle = 360, int Cnt = 20, int VsTryCnt = 3, int VsTimeOut = 1000)
        {
            //check
            if (VsTask == null || MoveHandle == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("CalRotCenter参数异常，视觉任务为{0}，{1}", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "移动函数为 null" : "") : string.Format("CalRotCenter parameter is abnormal, vision task is {0},{1}      (CalRotCenter参数异常，视觉任务为{0}，{1})", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "Move function is null   (移动函数为 null)" : ""), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }
            //get the first PAMlignTool
            CogPMAlignTool CogPAMTool = null;
            foreach (ICogTool tool in VsTask.Block.Tools)
            {
                if (tool.GetType() == typeof(CogPMAlignTool))
                {
                    CogPAMTool = (CogPMAlignTool)tool;
                }
            }
            if (CogPAMTool == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("视觉任务{0} 未包含PMA工具!", VsTask) : string.Format("The vision task {0} does not include PMA tools!      (视觉任务{0} 未包含PMA工具!)", VsTask), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }

            EM_RES res = EM_RES.OK;
            double StepAngle = (StartAngle - EndAngle) / (double)Cnt;
            List<ST_XY> ListVsPoint = new List<ST_XY>();
            List<double> ListAngle = new List<double>();
            bool ChkAngle = true;

            ST_XYZA StPos = new ST_XYZA(0, 0, 0, StartAngle);
            double ZoneAngleLow = CogPAMTool.RunParams.ZoneAngle.Low;
            double ZoneAngleLowHight = CogPAMTool.RunParams.ZoneAngle.High;
            graphics_other.Clear();

            try
            {
                //init
                ListVsPoint.Clear();

                //rotate and cap	
                for (int n = 0; n < Cnt; n++)
                {
                    //move u
                    StPos.a = StartAngle + StepAngle * n;
                    res = MoveHandle(ref bquit, false, false, false, true, IsU1, ref StPos, this.mName);
                    if (res != EM_RES.OK) return res;

                    //reset ZoneAngle 
                    CogPAMTool.RunParams.ZoneAngle.Low = CogMisc.DegToRad(CogMisc.RadToDeg(ZoneAngleLow) - StPos.a);
                    CogPAMTool.RunParams.ZoneAngle.High = CogMisc.DegToRad(CogMisc.RadToDeg(ZoneAngleLowHight) - StPos.a);

                    //triger and wait result
                    res = VsTask.TriAndWaitResult(ref bquit, VsTimeOut, VsTryCnt);
                    if (res != EM_RES.OK) return res;

                    //check result
                    if (VsTask.ResData.bOK == false)
                    {
                        return EM_RES.ERR;
                    }
                    //检测轴旋转方向
                    if (ChkAngle)
                    {
                        if (VsTask.ResData.PosMM.a > 0 && ListAngle.Count <= 3) ListAngle.Add(VsTask.ResData.PosMM.a);
                        else if (VsTask.ResData.PosMM.a <= 0) ListAngle.Clear();

                        if (ListAngle.Count >= 3)
                        {
                            if ((Growthrend && ListAngle[2] > ListAngle[1] && ListAngle[1] > ListAngle[0]) ||
                                (!Growthrend && ListAngle[2] < ListAngle[1] && ListAngle[1] < ListAngle[0]))
                            {
                                ChkAngle = false;
                            }
                            else
                            {
                                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "当前吸头旋转中心校正失败,请确认吸头的转旋方向是否正确?" : "The current correction of the rotation center of the tip failed. Please confirm whether the rotation direction of the tip is correct?    (当前吸头旋转中心校正失败,请确认吸头的转旋方向是否正确?)", DReport.EmErrCode.ResultNg, (int)DReport.EmHareware.Cam);
                                MessageBox.Show(VAR.IsChinese ? "当前校正吸头旋转中心校正失败,请确认吸头的转旋方向是否正确?" : "The current correction of the rotation center of the tip failed. Please confirm whether the rotation direction of the tip is correct?\r\n当前校正吸头旋转中心校正失败,请确认吸头的转旋方向是否正确?)", VAR.IsChinese ? "警告" : "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return EM_RES.ERR;
                            }
                        }
                    }


                    Thread.Sleep(10);
                    Application.DoEvents();
                    ListVsPoint.Add(VsTask.ResData.PosMM.ToXY());

                    CogPointMarker pmk = new CogPointMarker();
                    pmk.X = VsTask.ResData.PosMM.ToXY().x;
                    pmk.Y = VsTask.ResData.PosMM.ToXY().y;
                    pmk.Color = CogColorConstants.Green;
                    pmk.SizeInScreenPixels = 15;
                    pmk.LineWidthInScreenPixels = 2;
                    //pmk.SelectedSpaceName = "#";
                    mCogRecDisplay.StaticGraphics.Add(pmk, "Other");
                    graphics_other.Add(pmk);
                }

                //circle fit
                res = FitCircle(ListVsPoint.ToArray(), ref RotCenter, MaxRMSError, Cnt / 5, VsTask.ResData.OutputImg.SelectedSpaceName, CogRecDisplay);
                if (res != EM_RES.OK) return res;
            }
            finally
            {
                //restore setting
                CogPAMTool.RunParams.ZoneAngle.Low = ZoneAngleLow;
                CogPAMTool.RunParams.ZoneAngle.High = ZoneAngleLowHight;
            }
            return EM_RES.OK;
        }
        #endregion
        #region 生成回字形点阵列
        /// <summary>
        /// 生成回字形点阵列
        /// </summary>
        /// <param name="StartPos">回字中心，点阵起点</param>
        /// <param name="Step">步长</param>
        /// <param name="PointNum">点的数量</param>
        /// <returns></returns>
        public List<ST_XY> MakeRAPArray(ST_XY StartPos, double Step, int PointNum)
        {
            List<ST_XY> ListPoint = new List<ST_XY>();
            ListPoint.Clear();
            do
            {
                //0
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //1
                StartPos.y -= Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //2
                StartPos.x += Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //3
                StartPos.y += Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //4
                StartPos.y += Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //5
                StartPos.x -= Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;
                //6
                StartPos.x -= Step;
                ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
                if (ListPoint.Count >= PointNum) break;

                StartPos.y -= Step;

            } while (ListPoint.Count < PointNum);
            return ListPoint;
        }
        #endregion
        #region  九点阵列
        /// <summary>
        /// 生成回字形点阵列
        /// </summary>
        /// <param name="StartPos">回字中心，点阵起点</param>
        /// <param name="Step">步长</param>
        /// <returns></returns>
        public List<ST_XY> MakeNineArray(ST_XY StartPos, double Step)
        {
            List<ST_XY> ListPoint = new List<ST_XY>();
            ListPoint.Clear();
            //0
            ListPoint.Add(new ST_XY(StartPos.x, StartPos.y));
            //1
            ListPoint.Add(new ST_XY(StartPos.x + Step, StartPos.y));
            //2
            ListPoint.Add(new ST_XY(StartPos.x + Step, StartPos.y + Step));
            //3
            ListPoint.Add(new ST_XY(StartPos.x, StartPos.y + Step));
            //4
            ListPoint.Add(new ST_XY(StartPos.x - Step, StartPos.y + Step));
            //5
            ListPoint.Add(new ST_XY(StartPos.x - Step, StartPos.y));
            //6
            ListPoint.Add(new ST_XY(StartPos.x - Step, StartPos.y - Step));
            //7
            ListPoint.Add(new ST_XY(StartPos.x, StartPos.y - Step));
            //8
            ListPoint.Add(new ST_XY(StartPos.x + Step, StartPos.y - Step));
            return ListPoint;
        }
        #endregion
        #region 仿射校正
        /// <summary>
        /// 多点仿射校正
        /// </summary>
        /// <param name="bquit">取消控制</param>
        /// <param name="Transform">返回校正结果</param>
        /// <param name="VsTask">指定视觉任务</param>
        /// <param name="ListMcPoint">机械走动点位列表</param>
        /// <param name="RefPos">机械参考RefPos</param>
        /// <param name="MaxRMSError">最大允许误差</param>
        /// <returns></returns>
        public EM_RES NPointCali(ref bool bquit, ref AffineTransform Transform, VisionTask VsTask, List<ST_XY> ListMcPoint, ST_XY RefPos, ushort NpointMod = CamNpointCail, double MaxRMSError = 0.5)
        {
            //check
            if (VsTask == null || MoveHandle == null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("NinePointCali参数异常，视觉任务为{0}，{1}", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "移动函数为 null" : "") : string.Format("NinePointCali parameters are abnormal, the vision task is {0},{1}           (NinePointCali参数异常，视觉任务为{0}，{1})", VsTask == null ? "null" : VsTask.TaskName, MoveHandle == null ? "Move function is null     (移动函数为 null)" : ""), DReport.EmErrCode.GetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }
            if (ListMcPoint.Count < 3)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("NinePointCali;{0}点数小于3异常！", ListMcPoint.Count) : string.Format("NinePointCali {0} points less than 3 abnormal!     (NinePointCali;{0}点数小于3异常！)", ListMcPoint.Count), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
                return EM_RES.PARA_ERR;
            }

            try
            {
                EM_RES res = EM_RES.OK;
                ST_XYZA stpos;
                List<ST_XY> ListVsPoint = new List<ST_XY>();
                ListVsPoint.Clear();
                graphics_other.Clear();
                //rotate and cap	
                for (int n = 0; n < ListMcPoint.Count; n++)
                {
                    //move and refalsh posInf
                    stpos = ListMcPoint[n].ToXYZA();
                    res = MoveHandle(ref bquit, true, true, false, false, false, ref stpos, this.mName, 500);
                    if (res != EM_RES.OK) return res;
                    ListMcPoint[n] = stpos.ToXY();

                    //triger and wait result
                    res = VsTask.TriAndWaitResult(ref bquit, 1500, 3);
                    if (res != EM_RES.OK) return res;

                    Thread.Sleep(10);
                    Application.DoEvents();
                    //check result
                    if (VsTask.ResData.bOK == false)
                    {
                        return EM_RES.ERR;
                    }

                    if (NpointMod == CamNpointCail)
                        ListVsPoint.Add(VsTask.ResData.PosPix.ToXY());
                    else
                        ListVsPoint.Add(VsTask.ResData.PosMM.ToXY());
                    //show all point

                    try
                    {
                        mCogRecDisplay.StaticGraphics.Remove("Other");
                    }
                    catch
                    { }

                    foreach (ST_XY pt in ListVsPoint)
                    {
                        CogPointMarker pmk = new CogPointMarker();
                        pmk.X = pt.x;
                        pmk.Y = pt.y;
                        pmk.Color = CogColorConstants.Green;
                        pmk.SizeInScreenPixels = 15;
                        pmk.LineWidthInScreenPixels = 2;
                        if (NpointMod == CamNpointCail)
                            pmk.SelectedSpaceName = "#";
                        else
                            pmk.SelectedSpaceName = "mm";
                        mCogRecDisplay.StaticGraphics.Add(pmk, "Other");
                        graphics_other.Add(pmk);
                    }

                }
                //begin posInf
                stpos = RefPos.ToXYZA();
                res = MoveHandle(ref bquit, true, true, false, false, false, ref stpos, this.mName, 300);
                if (res != EM_RES.OK) return res;
                //refrence
                for (int n = 0; n < ListMcPoint.Count; n++)
                {
                    ListMcPoint[n] -= RefPos;
                }

                //transform
                res = Transform.BuildTransform(ListVsPoint.ToArray(), ListMcPoint.ToArray(), MaxRMSError);
                if (res != EM_RES.OK) return res;
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("出错:{0}", ex.Message) : string.Format("ERROR:{0}    (出错:{0})", ex.Message), DReport.EmErrCode.ToolError, (int)DReport.EmHareware.Cam);
            }
            return EM_RES.OK;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="bquit">取消控制</param>
        /// <param name="VsTask">指定视觉任务</param>
        /// <param name="StCenPos">视觉中心坐标</param>
        /// <param name="Step">步长</param>
        /// <param name="filename">保存路径，为空则不保存</param>
        /// <param name="MaxRMSError">最大误差</param>
        /// <returns></returns>
        public EM_RES NinePointCaliAction(ref bool bquit, ref AffineTransform AffineTransform, ref ST_XY StCenPos, ST_XYZA StMovPos, ref VisionTask VsTask, string spacename, double Step = 5, ushort NpointMod = CamNpointCail, string filename = "", double MaxRMSError = 0.5)
        {
            ST_XYZA CurPos = new ST_XYZA();
            CaliTool tool = new CaliTool();
            CurPos = StMovPos;
            bool TaskNpointUpdate = false;
            EM_RES res = EM_RES.OK;

            try
            {
                //Task Npoint校正数据另存清除
                if (NpointMod == TaskNpointCail && VsTask.ListCaliTool.Count > 0)
                {
                    tool = VsTask.ListCaliTool[0];
                    VsTask.ListCaliTool.Clear();
                }

                //move to center
                if (DialogResult.Yes == MessageBox.Show(VAR.IsChinese ? "是否画面对中?" : "Whether the screen is centered?\r\n是否画面对中", VAR.IsChinese ? "提示" : "Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                {
                    res = MoveToImgCenter(ref bquit, ref CurPos, VsTask);
                    if (res != EM_RES.OK) return res;
                }

                //update curpos
                StCenPos = CurPos.ToXY();
                res = MoveHandle(ref bquit, true, true, false, false, false, ref CurPos, this.mName, 0);
                if (res != EM_RES.OK) return res;

                //make MC point
                List<ST_XY> ListMcPoint = MakeRAPArray(CurPos.ToXY(), Step, 9);

                //cali
                AffineTransform Transform = new AffineTransform();

                res = NPointCali(ref bquit, ref Transform, VsTask, ListMcPoint, CurPos.ToXY(), NpointMod, MaxRMSError);
                if (res != EM_RES.OK) return res;


                //check result
                if (!Transform.bCalibrated)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "NinePointCail未完成校正!" : "NinePointCail has not completed calibration!     (NinePointCail未完成校正!)", DReport.EmErrCode.ToolError, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }

                //update
                if (AffineTransform != null)
                    AffineTransform = Transform;

                //save
                if (Transform.bCalibrated && filename == "")
                {
                    if (NpointMod == CamNpointCail)
                    {
                        Transform.Save(spacename, string.Format("{0}\\syscfg\\Calibration\\{1}\\1{2}_NPoint.vpp", Path.GetFullPath(".."), mName, mName));
                    }
                    else if (NpointMod == TaskNpointCail)
                    {
                        //保存
                        if (Transform.Save(spacename,
                            string.Format("{0}\\syscfg\\Calibration\\{1}\\TaskCalibration\\{2}_NPoint.vpp",
                                Path.GetFullPath(".."), mName, VsTask.TaskName)))
                        {
                            //加载
                            LoadTaskCaliTool(VsTask);
                            TaskNpointUpdate = true;
                        }
                    }
                }
                return res;
            }
            finally
            {
                if (!TaskNpointUpdate && tool != null && NpointMod == TaskNpointCail)
                {
                    VsTask.ListCaliTool.Add(tool);
                }
            }


        }
        #endregion
        #region  统一校正
        public EM_RES TrigerAndGetNPoint(ref bool bquit, ref ST_XYZA StMovPos, ref List<ST_XY> ListVsPoint)
        {
            //check 
            VisionTask VsTask = null;
            EM_RES res = EM_RES.OK;
            //move to center
            if (DialogResult.Yes == MessageBox.Show(VAR.IsChinese ? "是否画面对中?" : "Whether the screen is centered?\r\n是否画面对中", VAR.IsChinese ? "提示" : "Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
            {
                List_vs_task_cur.Clear();
                inputImageCnt = 0;
                curTaskIdx = 0;
                VsTask = List_vs_task.Find(s => s.TaskName.Equals("NpointTool"));
                List_vs_task_cur.Add(VsTask);
                res = MoveToImgCenter(ref bquit, ref StMovPos, VsTask, ListCaliTool, 0.02, 5, 3, 1000);
                if (res != EM_RES.OK) return res;
            }
            List_vs_task_cur.Clear();
            inputImageCnt = 0;
            curTaskIdx = 0;
            VsTask = List_vs_task.Find(s => s.TaskName.Equals("AffTransTool"));
            List_vs_task_cur.Add(VsTask);
            //trige and get result
            res = VsTask.TriAndWaitResult(ref bquit, 1500, 2);
            if (res != EM_RES.OK) return res;
            //get data
            string[] ListStr = VsTask.ResData.Message.Split('\n');
            foreach (string str in ListStr)
            {
                if (str.Length > 3)
                {
                    ST_XY pt = new ST_XY(double.MaxValue, double.MaxValue);
                    pt.FrString(str);
                    ListVsPoint.Add(pt);
                }
            }
            if (ListVsPoint.Count == 0) return EM_RES.ERR;

            return EM_RES.OK;
        }

        public EM_RES AffineCailAction(List<ST_XY> ListFrPoint, List<ST_XY> ListToPoint, ref AffineTransform AffineTransform, string spacename, string filename = "")
        {
            EM_RES res;
            AffineTransform Transform = new AffineTransform();
            //transform
            res = Transform.BuildTransform(ListFrPoint.ToArray(), ListToPoint.ToArray());
            if (res != EM_RES.OK) return res;

            if (!Transform.bCalibrated)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? "NinePointCail未完成校正!" : "NinePointCail has not completed calibration!     (NinePointCail未完成校正!)", DReport.EmErrCode.ToolError, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }

            //update
            if (AffineTransform != null)
                AffineTransform = Transform;

            //save
            if (filename == "" && Transform.bCalibrated)
            {
                Transform.Save(spacename, string.Format("{0}\\syscfg\\Calibration\\{1}\\0{2}_NPoint.vpp", Path.GetFullPath(".."), mName, mName));
            }
            return EM_RES.OK;
        }
        #endregion
        #region 扫描
        public EM_RES Scan(ref bool bquit, ref int percent, ref Image img, ST_XYZA st_ul, ST_XYZA st_br, int pic_w, int pic_h)
        {
            //计算行列
            int xn = 0, yn = 0;
            double dx = 0, dy = 0;
            int pic_dx = 0, pic_dy = 0;
            int img_w = 0;
            int img_h = 0;

            //get scaling
            ST_XYA st_stransform = new ST_XYA(1, 1, 0);
            EM_RES res = GetAllCaliToolXYAScale(ref st_stransform);
            if (res != EM_RES.OK) return res;
            //get size
            if (Image == null)
            {
                bRunImage = false;
                res = Triger(false, true);
                bRunImage = true;
                if (res != EM_RES.OK) return res;
                if (Image == null)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0}触发提取图片为空", mName) : string.Format("{0} Trigger extraction picture is empty!      ({0}触发提取图片为空)", mName), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                    return EM_RES.ERR;
                }
                img_w = Image.Width;
                img_h = Image.Height;
            }

            dx = img_w * st_stransform.x * -1;
            dy = img_h * st_stransform.y * -1;

            xn = (int)(Math.Abs(st_ul.x - st_br.x) / Math.Abs(dx) + 0.999) + 1;
            yn = (int)(Math.Abs(st_ul.y - st_br.y) / Math.Abs(dy) + 0.999) + 1;


            pic_dx = pic_w / xn;
            pic_dy = pic_h / yn;

            //保存比列
            if (((double)pic_dx / (double)pic_dy) > ((double)img_w / (double)img_h)) pic_dx = (int)(pic_dy * ((double)img_w / (double)img_h));
            else pic_dy = (int)((double)pic_dx * ((double)img_h / (double)img_w));

            img = new Bitmap(pic_w, pic_h);
            Graphics gg = Graphics.FromImage(img);
            gg.FillRectangle(Brushes.DarkGray, new Rectangle(new Point(), img.Size));

            percent = 0;

            double xs = dx / (double)pic_dx;
            double ys = dy / (double)pic_dy;
            //st_ul_cap.x = st_ul.x - dx / 2;
            //st_ul_cap.y = st_ul.y - dy / 2;
            //bsaveing = false;
            //bscaning = true;
            for (int n = 0; n < xn; n++)
            {
                for (int m = 0; m < yn; m++)
                {

                    //检查是否超范围
                    double x = st_ul.x + n * dx;
                    double y = st_ul.y + m * dy;
                    float kx = 0;
                    float ky = 0;

                    //move
                    if (MoveHandle != null)
                    {
                        ST_XYZA stpos = new ST_XYZA(x, y, 0, 0);
                        res = MoveHandle(ref bquit, true, true, false, false, false, ref stpos, this.mName);
                        if (res != EM_RES.OK) return res;
                    }

                    //triger
                    bRunImage = false;
                    Triger(false, true);
                    bRunImage = true;
                    if (res != EM_RES.OK) return res;

                    //get img
                    Image img1 = Image.ToBitmap();
                    //draw tu buf
                    gg.DrawImage(img1, n * pic_dx - kx * pic_dx, m * pic_dy - ky * pic_dy, pic_dx, pic_dy);

                    Thread.Sleep(1);
                    Application.DoEvents();

                    //进度
                    percent = (int)((n * yn + m + 1) * 100.0 / (xn * yn));
                }
            }
            if (gg != null)
            {
                gg.Dispose();
                gg = null;
            }
            percent = 100;
            Thread.Sleep(100);
            Application.DoEvents();
            //bscaning = false;

            //save posInf
            //string fileroad = Path.GetFullPath("..") + "\\product\\" + VAR.gst_sys_set.cur_product_name + "\\temp.inf";
            //IniFiles inf = new IniFiles(fileroad);

            //this.st_ul = st_ul;
            //this.st_br = st_br;
            //inf.WriteDouble("SCAN", "UL_X", st_ul.x);
            //inf.WriteDouble("SCAN", "UL_Y", st_ul.y);
            //inf.WriteDouble("SCAN", "UL_A", st_ul.a);
            //inf.WriteDouble("SCAN", "DR_X", st_br.x);
            //inf.WriteDouble("SCAN", "DR_Y", st_br.y);
            //inf.WriteDouble("SCAN", "DR_A", st_br.a);
            //inf.WriteDouble("SCAN", "SC_X", xs);
            //inf.WriteDouble("SCAN", "SC_Y", ys);
            //save img
            //bsaveing = true;
            //img.Save(Path.GetFullPath("..") + "\\product\\" + VAR.gst_sys_set.cur_product_name + "\\temp.bmp");            
            //bsaveing = false;
            //bscaning = false;
            return EM_RES.OK;

            ERR_END:
            if (gg != null)
            {
                gg.Dispose();
                gg = null;
            }
            //bscaning = false;
            return EM_RES.ERR;
        }
        #endregion
        #endregion
        #region 销毁
        public void Dispose()
        {
            try
            {
                if (mAcqFifo != null)
                {
                    bInit = false;
                    Live(mCogRecDisplay, false);
                    mAcqFifo.Complete -= new CogCompleteEventHandler(AcqFifoComplete);
                    mAcqFifo.OwnedTriggerParams.TriggerModel = CogAcqTriggerModelConstants.Manual;
                    mAcqFifo.Flush();
                    mAcqFifo.Complete -= new CogCompleteEventHandler(AcqFifoComplete);
                    mAcqFifo.Overrun -= new CogOverrunEventHandler(AcqFifoOverRun);
                    mFrameGrabber.Disconnect(true);
                }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, VAR.IsChinese ? string.Format("{0} 释放出错,{1}", disc, ex.Message) : string.Format("{0} Release error,{2}       ({1} 释放出错,{2})", englishdisc, disc, ex.Message), DReport.EmErrCode.ToolError, (int)DReport.EmHareware.Cam);
            }
        }
        #endregion
    }
}
