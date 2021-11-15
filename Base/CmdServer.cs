using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Base
{
    public class CmdServer
    {
        private TcpListener listener = null;
        private AsyncCallback acceptAcb = null;
        public delegate void StrMsgHandler(string msg, string destIP);
        public event StrMsgHandler OnMessage = null;
        public event StrMsgHandler OnError = null;

        public delegate void CommandHandler(CmdConnection sender, CmdObject obj);
        public event CommandHandler OnCommand = null;
        //private int actualPort = 0;
        private List<CmdConnection> connList = new List<CmdConnection>();

        public CmdServer(int port)
        {
            if (port <= 0)
            {
                port = 0;
                for (port = 65535; port > 0; port--)
                {
                    listener = new TcpListener(System.Net.IPAddress.Parse("0.0.0.0"), port);
                    try
                    {
                        listener.Start();
                        break;
                    }
                    catch { }
                }
            }
            else
            {
                listener = new TcpListener(System.Net.IPAddress.Parse("0.0.0.0"), port);
                listener.Start();
            }
            acceptAcb = new AsyncCallback(AcceptCallback);
            listener.BeginAcceptTcpClient(acceptAcb, this);
        }

        public int ClientCount { get { lock (connList) { return connList.Count; } } }

        private void AcceptCallback(IAsyncResult ar)
        {
            if (listener != null)
            {
                CmdConnection cmdConn = new CmdConnection(this);
                cmdConn.tcpClient = listener.EndAcceptTcpClient(ar);
                lock (connList)
                {
                    connList.Insert(0, cmdConn);
                }
                cmdConn.BeginReceive();
                listener.BeginAcceptTcpClient(acceptAcb, this);
            }
        }

        public void SendToAllClient(CmdObject cmdObj)
        {
            int count = 0;
            lock (connList)
            {
                count = connList.Count;
            }
            for (int i = count - 1; i >= 0; i--)
            {
                CmdConnection conn = null;
                try
                {
                    lock (connList) { conn = connList[i]; }
                }
                catch
                {
                    continue;
                }
                if (conn.Connected)
                {
                    conn.AsyncSendCmdObj(cmdObj);
                }
                else
                {
                    lock (connList)
                    {
                        connList.Remove(conn);
                        using (conn) { }
                    }
                }
            }
        }

        internal void ShowMessage(string msg, string destIP)
        {
            try
            {
                if (OnMessage != null)
                    OnMessage(msg, destIP);
            }
            catch { }
        }

        internal void ShowError(string msg, string destIP)
        {
            try
            {
                if (OnError != null)
                {
                    OnError(msg, destIP);
                }
            }
            catch { }
        }

        internal void HandleCommand(CmdConnection sender, CmdObject obj)
        {
            if (OnCommand != null)
            {
                OnCommand(sender, obj);
            }
        }
    }

    public class CmdConnection : IDisposable
    {
        private CmdServer server = null;
        internal TcpClient tcpClient;
        private byte[] recvBuf = new byte[2048];
        private AsyncCallback recvAcb;
        private AsyncCallback sendAcb;
        private byte[] accuBuf = null;
        private bool connected = true;

        internal CmdConnection(CmdServer server)
        {
            this.server = server;
            recvAcb = new AsyncCallback(recvCallback);
            sendAcb = new AsyncCallback(sendCallback);
        }

        public void Dispose()
        {
            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
        }

        public bool Connected { get { return connected; } }

        internal void BeginReceive()
        {
            if (tcpClient == null)
                return;
            NetworkStream ns = tcpClient.GetStream();
            try
            {
                ns.BeginRead(recvBuf, 0, recvBuf.Length, recvAcb, ns);
            }
            catch (Exception)
            {
                connected = false;
            }
        }

        private void recvCallback(IAsyncResult ar)
        {
            NetworkStream ns = (NetworkStream)ar.AsyncState;
            int len = 0;
            try { len = ns.EndRead(ar); }
            catch (Exception)
            {
                connected = false;
                return;
            }
            if (len > 0)
            {
                ProcessReceivedPackages(recvBuf, len);
                try
                {
                    ns.BeginRead(recvBuf, 0, recvBuf.Length, recvAcb, ns);
                }
                catch (Exception)
                {
                    connected = false;
                }
                return;
            }
            if (len == 0)
            {
                ns.Close();
            }
        }

        public void Send(byte[] data)
        {
            if ((tcpClient == null) || (data == null))
            {
                return;
            }
            NetworkStream ns = null;
            try
            {
                ns = tcpClient.GetStream();
                ns.Write(data, 0, data.Length);
            }
            catch (Exception)
            {
                connected = false;
            }
        }

        public void AsyncSend(byte[] data)
        {
            if ((tcpClient == null) || (data == null))
            {
                return;
            }
            NetworkStream ns = null;
            try
            {
                ns = tcpClient.GetStream();
                ns.BeginWrite(data, 0, data.Length, sendAcb, ns);
            }
            catch (Exception)
            {
                connected = false;
            }
        }

        public void AsyncSendMsg(string msg)
        {
            AsyncSend(Encoding.UTF8.GetBytes(msg + "\x03"));
        }

        public void AsyncSendCmdObj(CmdObject cmdObj)
        {
            ////The marked code below is low performance, do not use in high performance code.
            //JsonSerializerSettings jsetting = new JsonSerializerSettings();
            //jsetting.ContractResolver = new LimitPropsContractResolver(new string[] { "WearVarName", "WearVarValue" });
            //string cmdStr = JsonConvert.SerializeObject(cmdObj, Formatting.None), jsetting);

            string cmdStr = JsonConvert.SerializeObject(cmdObj);
            AsyncSend(Encoding.UTF8.GetBytes(cmdStr + "\x03"));
        }

        private void sendCallback(IAsyncResult ar)
        {
            NetworkStream ns = (NetworkStream)ar.AsyncState;
            try
            {
                ns.EndWrite(ar);
            }
            catch (Exception)
            {
                connected = false;
            }
        }

        private void ProcessReceivedPackages(byte[] data, int dataLen)
        {
            if (server != null)
            {
                server.ShowMessage("Received data: " + Encoding.UTF8.GetString(data, 0, dataLen), "");
            }

            int processed = 0;
            byte[] tempBuf = null;
            int len = 0;
            if ((accuBuf != null) && (accuBuf.Length >= 20971520))//20MB
            {
                accuBuf = null;
            }
            if (accuBuf == null)
            {
                tempBuf = data;
                len = dataLen;
            }
            else
            {
                len = accuBuf.Length + dataLen;
                tempBuf = new byte[len];
                Array.Copy(accuBuf, tempBuf, accuBuf.Length);
                Array.Copy(data, 0, tempBuf, accuBuf.Length, dataLen);
            }

            processed = ProcessReceivedData(tempBuf, len);
            if (processed >= len)
            {
                accuBuf = null;
            }
            else
            {
                if (processed > 0)
                {
                    accuBuf = new byte[len - processed];
                    Array.Copy(tempBuf, processed, accuBuf, 0, accuBuf.Length);
                }
                else
                {
                    if (tempBuf == data)
                    {
                        accuBuf = new byte[dataLen];
                        Array.Copy(data, accuBuf, dataLen);
                    }
                    else
                        accuBuf = tempBuf;
                }
            }
        }

        private CmdObject currCmdObj = null;
        private JsonSerializer js = new JsonSerializer();
        private int ProcessReceivedData(byte[] data, int dataLen)//return processed byte count.
        {
            int processed = 0;//processed byte count
            int etxIndex = -1;
            for (int i = 0; i < dataLen; i++)
            {
                if (data[i] == 0x03)
                {
                    etxIndex = i;
                }
                if (etxIndex > processed)
                {
                    try
                    {
                        string jsonStr = Encoding.UTF8.GetString(data, processed, etxIndex - processed);
                        CmdObject obj = (CmdObject)js.Deserialize(new StringReader(jsonStr), typeof(CmdObject));
                        currCmdObj = obj;
                        if (server != null)
                        {
                            server.HandleCommand(this, obj);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (server != null)
                        {
                            server.ShowError(ex.Message, "");
                        }
                    }
                    processed = etxIndex + 1;
                    etxIndex = -1;
                }
            }
            return processed;
        }
    }

    public class CmdObject
    {
        public string Func = "";
        public string ExJsonStr = "";
    }
}
