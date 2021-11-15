using System;
using System.IO;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Base
{
    public partial class Base : IDisposable
    {
        #region initial
        private TcpTransport transHB = null;
        private System.Timers.Timer hbDetecter = new System.Timers.Timer();
        private System.Timers.Timer statusSender = new System.Timers.Timer();

        private CmdServer cmdServer = null;
        private List<CmdConnection> msgListeners = new List<CmdConnection>();
        private MQTTClient mqttClient = null;
        private JsonSerializer js = new JsonSerializer();
        private Dictionary<string, PLCData> DataDict = new Dictionary<string, PLCData>();
        public event HandleStringMsg OnMsg;

        private FileQueue fileQueue = null;//文件系统队列

        private RollingFileWriterManager rfwManager = null;
        private bool enableWriteFile = false;
        private int maxFileKiloByte = 10000;
        private int maxFileNum = 10;
        #endregion

        public Base() { }

        #region base
        private DateTime lastHeartbeatTime = DateTime.Now;// 上次心跳时间
        private void transHBOnReceivedData(byte[] data, int dataLen)
        {
            lastHeartbeatTime = DateTime.Now;// 收到心跳信息 更新上次心跳时间
        }
        private void hbDetecterElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            TimeSpan ts = DateTime.Now - lastHeartbeatTime;
            if (ts.TotalSeconds > 12)
            {
                hbDetecter.Stop();
                hbDetecter.Elapsed -= hbDetecterElapsed;
                if (transHB != null)
                {
                    transHB.Stop();
                    transHB.OnReceivedData -= transHBOnReceivedData;
                }
                Stop();
                Environment.Exit(-1);
            }
            if (transHB != null)
            {
                transHB.AsyncSendData(new byte[] { 0x0A, 0x03 });
            }
        }
        public void Init(ushort heartbeatPort)
        {
            if (heartbeatPort > 0)
            {
                transHB = new TcpTransport();
                transHB.IP = "127.0.0.1";
                transHB.Port = heartbeatPort;
                transHB.OnReceivedData += transHBOnReceivedData;
                transHB.Start();

                lastHeartbeatTime = DateTime.Now;
                hbDetecter.Elapsed += hbDetecterElapsed;
                hbDetecter.Interval = 1000;
                hbDetecter.Start();
            }

            string currDir = Assembly.GetExecutingAssembly().Location.ToString();
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
            AsyncPerformer.AppDirectory = currDir;

            fileQueue = new FileQueue();

            try
            {
                enableWriteFile = bool.Parse(ConfigurationManager.AppSettings["EnableWriteFile"]);
            }
            catch (Exception) { }
            if (enableWriteFile)
            {
                try
                {
                    maxFileKiloByte = int.Parse(ConfigurationManager.AppSettings["MaxFileKiloByte"]);
                }
                catch (Exception) { }

                try
                {
                    maxFileNum = int.Parse(ConfigurationManager.AppSettings["MaxFileNum"]);
                }
                catch (Exception) { }

                rfwManager = new RollingFileWriterManager(maxFileKiloByte, maxFileNum);

                try
                {
                    rfwManager.WriteInterval = int.Parse(ConfigurationManager.AppSettings["WriteFileInterval"]);
                }
                catch (Exception) { }

                try
                {
                    rfwManager.FileNameSuffix = ConfigurationManager.AppSettings["FileNameSuffix"];
                }
                catch (Exception) { }

                try
                {
                    rfwManager.OutputPath = ConfigurationManager.AppSettings["OutputPath"];
                }
                catch (Exception) { }

                rfwManager.OnErrMsg += rfwManagerOnErrMsg;
                rfwManager.StartWrite();
            }

            int commandServerPort = 8116;//不直接8116？
            try
            {
                commandServerPort = int.Parse(ConfigurationManager.AppSettings["CommandServerPort"]);
            }
            catch (Exception) { }
            try
            {
                cmdServer = new CmdServer(commandServerPort);
                cmdServer.OnError += cmdServerOnError;
                cmdServer.OnCommand += cmdServerOnCommand;
            }
            catch (Exception ex)
            {
                ShowMsg(StringMsgType.Error, "CmdServer", "Initializing failed. Detail: " + ex.Message);
            }

            string mqttServerIP = "127.0.0.1";
            int mqttServerPort = 1883;
            try
            {
                mqttServerIP = ConfigurationManager.AppSettings["mqttServerIP"];
                mqttServerPort = int.Parse(ConfigurationManager.AppSettings["mqttServerPort"]);
            }
            catch (Exception) { }
            try
            {
                mqttClient = new MQTTClient(mqttServerIP, mqttServerPort);
                mqttClient.OnError += mqttClientOnError;
                mqttClient.OnCommand += mqttClientOnCommand;
            }
            catch (Exception ex)
            {
                ShowMsg(StringMsgType.Error, "MQTTClient", "Initializing failed. Detail: " + ex.Message);
            }

            statusSender = new System.Timers.Timer();
            statusSender.Interval = 1000;
            statusSender.Elapsed += statusSenderElapsed;
            statusSender.Start();

            ShowMsg(StringMsgType.Info, "ALL", "Started.");
        }
        private void statusSenderElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            statusSender.Stop();

            CmdObject cmdObj = new CmdObject();
            cmdObj.Func = "00900000";
            cmdObj.ExJsonStr = "ALL\x1F" + "Status" + "\x1F" + "50000\x1Fstart";
            cmdServer.SendToAllClient(cmdObj);

            statusSender.Start();
        }
        public void Start()
        {
            try
            {
                statusSenderElapsed(statusSender, null);
            }
            catch (Exception ex)
            {
                ShowMsg(StringMsgType.Error, "ALL", "Failed to do auto start. " + ex.Message);
            }
        }
        public void Stop()
        {
            ShowMsg(StringMsgType.Info, "Base", "即将关闭后台进程");
            statusSender.Stop();
            statusSender.Elapsed -= statusSenderElapsed;
            if (rfwManager != null)
            {
                rfwManager.StopWrite();
                Thread.Sleep(rfwManager.WriteInterval + 1000);
            }
        }
        private void rfwManagerOnErrMsg(string errMsg)
        {
            ShowMsg(StringMsgType.Error, "FileWriter", errMsg);
        }
        public void Dispose()
        {
            try
            {
                Stop();
                cmdServer.OnError -= cmdServerOnError;
                cmdServer.OnCommand -= cmdServerOnCommand;
                mqttClient.OnError -= mqttClientOnError;
                mqttClient.OnCommand -= mqttClientOnCommand;
                if (rfwManager != null)
                {
                    rfwManager.StopWrite();
                    rfwManager.OnErrMsg -= rfwManagerOnErrMsg;
                }
            }
            catch { }
        }
        public void ShowMsg(StringMsgType msgType, string machineID, string msg)
        {
            if (msgType == StringMsgType.Info || msgType == StringMsgType.Warning || msgType == StringMsgType.Error)
            {
                try
                {
                    if (OnMsg != null)
                    {
                        try { OnMsg(msgType, machineID + ": " + msg + "\r\n"); }
                        catch { }
                    }

                    if (msgListeners.Count > 0)
                    {
                        CmdObject cmdObj = new CmdObject();
                        cmdObj.Func = "00900000";
                        cmdObj.ExJsonStr = machineID + "\x1F" + msgType.ToString() + "\x1F" + ((int)msgType).ToString() + "\x1F" + msg;
                        for (int i = 0; i < msgListeners.Count; )
                        {
                            CmdConnection cmdConn = msgListeners[i];
                            if (!cmdConn.Connected)
                            {
                                msgListeners.RemoveAt(i);
                                try { using (cmdConn) { } }
                                catch { }
                                continue;
                            }
                            i++;

                            cmdConn.AsyncSendCmdObj(cmdObj);
                        }
                    }
                }
                catch
                {

                }
            }
            if (rfwManager != null)
            {
                if (msgType == StringMsgType.Data)
                {
                    return;
                }
                rfwManager.AddData(machineID, msgType.ToString(), DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff") + ": " + msg + "\r\n");
            }
        }
        #endregion

        #region cmdServer
        private void cmdServerOnCommand(CmdConnection sender, CmdObject obj)
        {
            if (obj == null)
            {
                return;
            }
            ShowMsg(StringMsgType.Info, "Command Received", "Func = " + obj.Func);
            string func = obj.Func.Trim();
            switch (func)
            {
                case "00900000"://Join.
                    {
                        if (!msgListeners.Contains(sender))
                        {
                            msgListeners.Add(sender);
                            //第一次连接，交换信息
                        }
                    }
                    return;
                default:
                    {
                        throw new Exception("Func error");
                    }
            }
        }

        private void cmdServerOnError(string msg, string destIP)
        {
            ShowMsg(StringMsgType.Error, "CmdServer", msg);
        }
        #endregion

        #region mqtt
        private void mqttClientOnError(string msg, string destIP)
        {
            ShowMsg(StringMsgType.Error, "MQTTClient", string.Format("{0}. ({1})", msg, destIP));
        }

        private void mqttClientOnCommand(MQTTClient sender, DateTime time, string jsonStrCmd)
        {
            try
            {
                lock (js)
                {
                    JObject jo = (JObject)js.Deserialize(new StringReader(jsonStrCmd), typeof(JObject));
                    {
                        foreach (JProperty jp in jo.Children())
                        {
                            //if (DataDict.ContainsKey(jp.Name))
                            //{
                            //    if (DataDict[jp.Name].Value != jp.Value.ToString())
                            //    {
                            //        DataDict[jp.Name].Value = jp.Value.ToString();
                            //        DataDict[jp.Name].ValueChanged = true;//值变化且未处理
                            //    }
                            //    else
                            //    {
                            //        //DataDict[jp.Name].ValueChanged = false;
                            //    }
                            //}
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //text_error.Text = ex.Message;
                throw ex;
            }
        }
        #endregion
    }
}
