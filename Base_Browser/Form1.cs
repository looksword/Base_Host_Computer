using System;
using System.IO;
using System.Collections.Generic;
//using System.ComponentModel;
//using System.Data;
using System.Drawing;
//using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Windows.Forms;
using System.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Lib;

namespace Base.Browser
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            System.Windows.Forms.Timer dcLoader = new System.Windows.Forms.Timer();
            dcLoader.Interval = 500;
            dcLoader.Tick += dcLoaderTick;
            dcLoader.Start();
        }

        #region 初始化
        private TcpTransport dataTrans = null;
        public int _LastError = 0;
        private JsonSerializer js = new JsonSerializer();
        private Dictionary<string, PLCData> DataDict = new Dictionary<string, PLCData>();
        public class CmdObject
        {
            public string Func = "";
            public string ExJsonStr = "";
        }
        public class PLCData
        {
            public string Value = "";
            public bool ValueChanged = false;
        }
        #endregion

        #region 后台通讯
        private void dcLoaderTick(object sender, EventArgs e)
        {
            System.Windows.Forms.Timer dcLoader = (System.Windows.Forms.Timer)sender;
            dcLoader.Stop();
            dcLoader.Tick -= dcLoaderTick;

            dataTrans = new TcpTransport();
            dataTrans.IP = "127.0.0.1";
            dataTrans.Port = 8216;
            dataTrans.OnConnected += dataTransOnConnected;
            dataTrans.OnMsg += dataTransOnMsg;
            dataTrans.OnReceivedData += dataTransOnReceivedData;
            dataTrans.Start();
        }
        private void dataTransOnConnected(TcpTransport sender)
        {
            CmdObject cmd = new CmdObject();
            cmd.Func = "00900000";//join as msg listener.
            dataTrans.AsyncSendStr(JsonConvert.SerializeObject(cmd) + "\x03");
        }
        private void dataTransOnMsg(object sender, string topic, string msg, int msgType)
        {
            //ShowMsg(StringMsgType.Info, topic + ".  " + msg + "\r\n");
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    text_error.Text = DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff ") + topic + ".  " + msg;
                }));
            }
            else
            {
                text_error.Text = DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff ") + topic + ".  " + msg;
            }
            //text_error.Text = topic + ".  " + msg;
        }
        private void dataTransOnReceivedData(byte[] data, int dataLen)
        {
            string jsonStr = Encoding.UTF8.GetString(data, 0, dataLen);
            CmdObject cmd = (CmdObject)js.Deserialize(new StringReader(jsonStr), typeof(CmdObject));
            this.Invoke(new MethodInvoker(delegate
            {
                switch (cmd.Func)
                {
                    case "00900000":
                        MakeMsg(cmd.ExJsonStr);
                        break;
                    default:
                        break;
                }
            }));
        }
        private void MakeMsg(string dataStr)
        {
            if (string.IsNullOrEmpty(dataStr))
            {
                return;
            }
            string[] ss = dataStr.Split('\x1F');
            if (ss.Length < 4)
            {
                ShowMsg(StringMsgType.Info, dataStr + "\r\n");
                return;
            }
            int msgType = 2;
            try { msgType = int.Parse(ss[2]); }
            catch { }

            if (msgType == 50000)
            {//是否自动
                return;
            }

            ShowMsg(StringMsgType.Info, ss[1] + ".  " + ss[3] + "\r\n");
        }
        private void ShowMsg(StringMsgType msgType, string msg)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new MethodInvoker(delegate
                {
                    DoShowMsg(msgType, msg);
                }));
            }
            else
            {
                DoShowMsg(msgType, msg);
            }
        }
        private void DoShowMsg(StringMsgType msgType, string msg)
        {
            if (text_Msg.Lines.Length > 30000)
            {
                text_Msg.Clear();
            }
            text_Msg.AppendText(DateTime.Now.ToString("yyyyMMdd HH:mm:ss.fff") + ": " + msg);
        }
        #endregion

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            dataTrans.Stop();
            dataTrans.OnConnected -= dataTransOnConnected;
            dataTrans.OnMsg -= dataTransOnMsg;
            dataTrans.OnReceivedData -= dataTransOnReceivedData;
        }
    }
}
