using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Timers;

namespace Lib
{
    //QueryPerformanceCounter is not precise on variable frequency computer, so we use timeGetTime instead.
    //timeSetEvent can only create 16 active timer at the same time, so we do not use timeSetEvent to create timer.
    //WinmmTimer is obsoleted, but we keep the code for reference.
    public class WinmmTimer : IDisposable
    {
        //private static readonly UInt32 TIME_ONESHOT = 0;
        private static readonly UInt32 TIME_PERIODIC = 1;

        [System.Runtime.InteropServices.DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern UInt32 timeGetTime();

        [System.Runtime.InteropServices.DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern UInt32 timeBeginPeriod(UInt32 uPeriod);

        [System.Runtime.InteropServices.DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern UInt32 timeEndPeriod(UInt32 uPeriod);

        private delegate void OnTimeEventHandler(UInt32 wTimerID, UInt32 msg, IntPtr dwUser, UInt32 dwl, UInt32 dw2);
        [System.Runtime.InteropServices.DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern UInt32 timeSetEvent(UInt32 uDelay, UInt32 uResolution, OnTimeEventHandler lpTimeProc, IntPtr dwUser, UInt32 fuEvent);

        [System.Runtime.InteropServices.DllImport("winmm.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern UInt32 timeKillEvent(UInt32 timerHandle);

        public WinmmTimer()
        {
            timeBeginPeriod(1);
            oteh = new OnTimeEventHandler(HandleOnTimeEvent);
        }

        ~WinmmTimer()
        {
            //timeEndPeriod(1);
        }

        public void Dispose()
        {

        }

        /// <summary>
        /// 返回操作系统启动到现在所经过的毫秒数
        /// </summary>
        /// <returns></returns>
        public static UInt32 GetTickCount()
        {
            return timeGetTime();
        }

        private UInt32 timerID = 0;
        private OnTimeEventHandler oteh = null;
        public void Start()
        {
            //timeSetEvent((UInt32)Interval, 1, oteh, Marshal.GetIUnknownForObject(this), TIME_PERIODIC);
            timerID = timeSetEvent((UInt32)Interval, 1, oteh, IntPtr.Zero, TIME_PERIODIC);
        }

        public void Stop()
        {
            if (timerID != 0)
            {
                timeKillEvent(timerID);
                timerID = 0;
            }
        }

        public Int32 Interval = 10;//ms
        public event EventHandler Tick = null;

        private void HandleOnTimeEvent(UInt32 wTimerID, UInt32 msg, IntPtr dwUser, UInt32 dwl, UInt32 dw2)
        {
            //WinmmTimer timer = (WinmmTimer)Marshal.GetObjectForIUnknown(dwUser);
            timeKillEvent(wTimerID);
            if (Tick != null)
            {
                Tick(this, null);
            }
        }
    }

    /// <summary>
    /// 异步执行器
    /// </summary>
    public class AsyncPerformer : IDisposable
    {
        public static String AppDirectory = "";

        private System.Timers.Timer delayer = new System.Timers.Timer();
        //private WinmmTimer delayer = new WinmmTimer();
        private bool stopSignal = true;
        private bool asyncWorking = false;
        public uint LoopTimes = 0;//=0: endless; >0: loop specified times.
        private uint loopCount = 0;
        private object userState = null;
        private bool reqNewThread = false;
        private ThreadStart asyncStart = null;
        private Thread thread = null;
        ////private Semaphore sp = new Semaphore(0, 1);
        public uint Timeout = 0;//=0: infinity; >0: milliseconds
        private bool timeoutCtrlFlag = false;

        public AsyncPerformer()
        {
            asyncStart = new ThreadStart(DoWork);
            delayer.Interval = 8;
            delayer.Elapsed += delayerElapsed;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }

        void delayerElapsed(object sender, ElapsedEventArgs e)
        {
            delayer.Stop();

            if (stopSignal)
            {
                DoAfterStop(1);
                return;
            }

            lock (this)
            {
                asyncWorking = true;
            }

            try
            {
                if (timeoutCtrlFlag)//If do work timeout
                {//超时
                    timeoutCtrlFlag = false;
                    if (thread != null)
                    {
                        try { thread.Abort(); }
                        catch { }
                        thread = null;
                    }
                }

                if (reqNewThread || (thread == null))//If the worker thread has to be renew
                {//线程更新
                    if (thread != null)
                    {
                        try { thread.Abort(); }
                        catch { }
                        thread = null;
                    }
                    MsgOut(this, "Note", this.GetType().Name + ": About to create new thread.", 2);
                    try
                    {
                        thread = new Thread(asyncStart);
                        thread.Start();
                    }
                    catch
                    {
                        delayer.Interval = 66;
                        delayer.Start();//Retry to create thread
                        return;
                    }
                }

                //if (Timeout > 0)
                //{
                //    timeoutCtrlFlag = true;
                //    delayer.Interval = (int)Timeout;
                //    delayer.Start();
                //}
                ////sp.Release();//Release next work
            }
            catch (Exception ex)
            {
                MsgOut(this, "Error01", this.GetType().Name + ": " + ex.Message, 65535);
            }
        }

        public delegate void WorkHandler(ref bool reqNewThread, ref object userState);
        /// <summary>
        /// 异步操作、非继承
        /// </summary>
        public event WorkHandler OnAsyncWork = null;
        /// <summary>
        /// 修订间隔
        /// </summary>
        private int revisedInterval = 0;
        /// <summary>
        /// 异步执行
        /// </summary>
        private void DoWork()
        {
            try
            {
                reqNewThread = false;

                while (!stopSignal)
                {
                    ////sp.WaitOne();//Wait for next work
                    UInt32 tickCnt1 = WinmmTimer.GetTickCount();

                    if (Timeout > 0)
                    {
                        timeoutCtrlFlag = true;
                        delayer.Interval = (int)Timeout;
                        delayer.Start();//Start timeout counting
                    }

                    try
                    {
                        if (OnAsyncWork != null)
                        {
                            OnAsyncWork(ref reqNewThread, ref userState);
                        }
                        else
                        {
                            AsyncWork(ref reqNewThread, ref userState);
                        }
                    }
                    catch (Exception ex)
                    {
                        //string errStr = ex.Message + " ";
                        //if (ex.GetBaseException() != null)
                        //{
                        //    errStr += ex.GetBaseException().Message;
                        //}
                        //MsgOut(this, "Error02", this.GetType().Name + ": " + errStr, 65535);
                        MsgOut(this, "TCP communication error", ex.Message, 65535);
                    }

                    if (Timeout > 0)
                    {
                        timeoutCtrlFlag = false;
                        delayer.Stop();//Stop timeout counting
                    }

                    UInt32 tickCnt2 = WinmmTimer.GetTickCount();
                    if (tickCnt1 > tickCnt2)
                    {//若操作系统连续运行2^32-1 = 4,294,967,295毫秒，这个数字会回到0
                        revisedInterval = interval - (int)(4294967295 - tickCnt1 + 1 + tickCnt2);
                    }
                    else
                    {
                        revisedInterval = interval - (int)(tickCnt2 - tickCnt1);
                    }
                    //MsgOut(this, "Info", "Revised interval is " + revisedInterval.ToString() + ".", 2);

                    if (LoopTimes > 0)
                    {//默认无限循环
                        loopCount++;
                        if (loopCount >= LoopTimes)
                        {
                            stopSignal = true;
                        }
                    }

                    if (stopSignal)
                    {
                        asyncWorking = false;
                        return;
                    }

                    if (revisedInterval > 0)
                    {
                        Thread.Sleep(revisedInterval);
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                if (stopSignal)
                {
                    DoAfterStop(2);
                    return;
                }
                reqNewThread = true;
            }
            catch
            {
                reqNewThread = true;
                return;
            }

            lock (asyncStart)
            {
                if (asyncWorking)
                {
                    asyncWorking = false;
                    DoAfterStop(3);
                }
            }
        }

        private int interval = 2;
        public int Interval
        {
            get { return interval; }
            set
            {
                if (value < 2)
                    value = 2;
                if (value > 18000000)
                    value = 18000000;
                interval = value;
                revisedInterval = interval;
            }
        }

        public delegate void MsgHandler(object sender, string topic, string msg, int msgType);
        public event MsgHandler OnMsg = null;

        public void MsgOut(object sender, string topic, string msg, int msgType)
        {
            MsgOut(sender, topic, msg, msgType, false);
        }

        public void MsgOut(object sender, string topic, string msg, int msgType, bool withTime)
        {
            if (OnMsg == null)
            {
                return;
            }

            try
            {
                if (withTime)
                {
                    msg = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg;
                }
                OnMsg(sender, topic, msg, msgType);
            }
            catch { }
        }

        /// <summary>
        /// 异步操作、继承
        /// </summary>
        /// <param name="reqNewThread"></param>
        /// <param name="userState"></param>
        protected virtual void AsyncWork(ref bool reqNewThread, ref object userState) { }

        public bool StopSignal
        {
            get { return stopSignal; }
        }

        public delegate void StateHandler(AsyncPerformer performer, object userState);
        public event StateHandler OnStart = null;
        protected virtual void AfterStart(object userState) { }
        public event StateHandler OnStop = null;
        /// <summary>
        /// 停止异步执行后
        /// </summary>
        /// <param name="userState"></param>
        protected virtual void AfterStop(object userState) { }
        /// <summary>
        /// 停止异步执行后，带标签
        /// </summary>
        /// <param name="userState"></param>
        private void DoAfterStop(object userState)
        {
            MsgOut(this, "Call AfterStop", userState.ToString() + "," + this.GetHashCode().ToString(), 1);
            try { AfterStop(userState); }
            catch (Exception ex)
            {
                MsgOut(this, "Error", ex.Message, 1);
            }
        }

        /// <summary>
        /// 启动
        /// </summary>
        public void Start()
        {
            lock (this)
            {
                if (asyncWorking)
                {
                    return;
                }
                loopCount = 0;
                stopSignal = false;
                timeoutCtrlFlag = false;
                reqNewThread = true;
                delayer.Interval = interval;
                delayer.Start();
            }

            if (OnStart != null)
            {
                OnStart(this, userState);
            }

            AfterStart(userState);
        }

        /// <summary>
        /// 关闭
        /// </summary>
        public void Stop()
        {
            if (OnStop != null)
            {
                OnStop(this, userState);
            }

            delayer.Stop();
            stopSignal = true;
            if (Stopped)
            {
                DoAfterStop(4);
                return;
            }

            lock (asyncStart)
            {
                if (asyncWorking)
                {
                    asyncWorking = false;
                    if (thread != null)
                    {
                        try { thread.Abort(); }
                        catch { }
                        thread = null;
                    }
                    DoAfterStop(5);
                }
            }
        }

        public bool Stopped
        {
            get
            {
                if (stopSignal && (!asyncWorking)) return true;
                return false;
            }
        }
    }
}
