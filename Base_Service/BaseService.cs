using System;
using System.IO;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
using System.Diagnostics;
using System.Reflection;
//using System.Linq;
//using System.ServiceProcess;
using System.Text;
//using System.Threading.Tasks;
using System.ServiceProcess;
using System.Threading;
using System.Timers;
using Base;

namespace Base.Service
{
    public partial class BaseService : ServiceBase
    {
        private TcpServer dataServer = null;
        private bool stopSignal = false;
        private Process mainSDCProc = null;
        private System.Timers.Timer watchDCTimer = new System.Timers.Timer();
        private string currDir = "";
        private DateTime lastHeartbeatTime = DateTime.Now;
        private int heartbeatTimeout = 30;//seconds
        string filePath = Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "Base_serviceLog.txt");
        private string currFilePath = "";
        private DateTime insTime = DateTime.MinValue;
        private FileStream currFileFS = null;

        public BaseService()
        {
            InitializeComponent();

            watchDCTimer.Elapsed += watchDCTimerElapsed;

            currDir = Assembly.GetExecutingAssembly().Location.ToString();
            string[] dirs = currDir.Split('\\', '/');
            currDir = "";
            for (int i = 0; i < dirs.Length - 1; i++)
            {
                if (i > 0)
                {
                    currDir += "\\";
                }
                currDir += dirs[i];
            }
            dataServer = new TcpServer("127.0.0.1", 0);//固定8132
            dataServer.OnData += dataServerOnData;
        }

        private void Log(string str)
        {
            try
            {
                FileStream fs = new FileStream(currDir + "\\log.txt", FileMode.Append);
                str = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + str + "\r\n";
                byte[] bs = Encoding.Default.GetBytes(str);
                fs.Write(bs, 0, bs.Length);
                fs.Flush();
                fs.Close();
                using (fs) { }
            }
            catch { }
        }

        private Process CreateDCProcess(string port)
        {
            Process procObj = new Process();
            ProcessStartInfo psi = procObj.StartInfo;
            psi.FileName = "Zebra_DM.exe";
            psi.WorkingDirectory = currDir;
            psi.Arguments = port;
            psi.UseShellExecute = false;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = false;
            procObj.Start();
            return procObj;
        }

        private void StartDC()
        {
            stopSignal = false;
            lastHeartbeatTime = DateTime.Now;
            watchDCTimer.Interval = 2000;
            watchDCTimer.Start();
        }

        private void StopDC()
        {
            watchDCTimer.Stop();
            stopSignal = true;
            Thread.Sleep(8000);//Give the SDCs some time to quit.

            if ((mainSDCProc != null))// && (!mainSDCProc.HasExited))//If SDC can not quit by itself
            {
                Log("About to kill main SDC process.");
                try { mainSDCProc.Kill(); }
                catch { }
            }
            mainSDCProc = null;
        }

        protected override void OnStart(string[] args)
        {
            StartDC();
            Log("服务启动");
        }

        protected override void OnStop()
        {
            StopDC();
            Log("服务停止");
        }

        private void watchDCTimerElapsed(object sender, ElapsedEventArgs e)//this type of timer is running by main thread
        {
            watchDCTimer.Stop();
            try
            {
                TimeSpan ts = DateTime.Now - lastHeartbeatTime;
                if (ts.TotalSeconds > heartbeatTimeout)//Heartbeat timeout, kill process.
                {
                    lastHeartbeatTime = DateTime.Now;
                    try { mainSDCProc.Kill(); }
                    catch { }
                }

                if ((mainSDCProc == null) || mainSDCProc.HasExited)
                {
                    Log("Renew MDEP.exe");
                    mainSDCProc = CreateDCProcess(dataServer.ActualPort.ToString());
                    Log("Renew MDEP.exe successfully.");
                }
            }
            catch { }
            watchDCTimer.Start();
        }

        private void dataServerOnData(TcpConnection sender, byte[] data)
        {
            lastHeartbeatTime = DateTime.Now;
            if (!stopSignal)
            {
                sender.AsyncSendStr("HB");
            }
        }
    }
}
