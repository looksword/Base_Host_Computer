using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Reflection;

namespace Base.Service
{
    public class TcpServer
    {
        private TcpListener listener = null;
        private AsyncCallback acceptAcb = null;
        public delegate void StrMsgHandler(string msg, string destIP);
        public event StrMsgHandler OnMessage = null;
        public event StrMsgHandler OnError = null;

        public delegate void DataHandler(TcpConnection sender, byte[] data);
        public event DataHandler OnData = null;
        private int actualPort = 0;

        private List<TcpConnection> connList = new List<TcpConnection>();

        public TcpServer(string ip, int port)
        {
            if (port <= 0)
            {
                port = 0;
                for (port = 45535; port > 0; port--)
                {
                    listener = new TcpListener(System.Net.IPAddress.Parse(ip), port);
                    try
                    {
                        listener.Start();
                        actualPort = port;
                        break;
                    }
                    catch { }
                }
            }
            else
            {
                listener = new TcpListener(System.Net.IPAddress.Parse(ip), port);
                listener.Start();
                actualPort = port;
            }
            acceptAcb = new AsyncCallback(AcceptCallback);
            listener.BeginAcceptTcpClient(acceptAcb, this);
        }

        public int ActualPort { get { return actualPort; } }

        /// <summary>
        /// 异步接受调用
        /// </summary>
        /// <param name="ar"></param>
        private void AcceptCallback(IAsyncResult ar)
        {
            if (listener != null)
            {
                TcpConnection tcpConn = new TcpConnection(this);
                tcpConn.tcpClient = listener.EndAcceptTcpClient(ar);
                try
                {
                    tcpConn.BeginReceive();
                    connList.Add(tcpConn);
                }
                catch { }
                listener.BeginAcceptTcpClient(acceptAcb, this);
            }
        }

        /// <summary>
        /// 异步发送
        /// </summary>
        /// <param name="str"></param>
        public void AsyncSendStrToAllClient(string str)
        {
            int count = 0;
            lock (this)
            {
                count = connList.Count;
            }
            for (int i = count - 1; i >= 0; i--)
            {
                TcpConnection conn = null;
                lock (this) { conn = connList[i]; }
                if (conn.Connected)
                {
                    conn.AsyncSendStr(str);
                }
                else
                {
                    lock (this)
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
                    OnError(msg, destIP);
            }
            catch { }
        }

        /// <summary>
        /// 数据句柄
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="data"></param>
        internal void HandleData(TcpConnection conn, byte[] data)
        {
            if (OnData != null)
            {
                OnData(conn, data);
            }
        }

        public static DateTime StringToDate(string dateStr)
        {
            DateTime dt = new DateTime();
            try
            {
                if (!string.IsNullOrEmpty(dateStr))
                {
                    dateStr = dateStr.Substring(0, 19);
                    dateStr = dateStr.Replace("T", " ");
                    dateStr = dateStr.Replace("-", "/");
                    dt = DateTime.Parse(dateStr);
                }
            }
            catch (Exception) { }
            return dt;
        }
    }

    public class TcpConnection : IDisposable
    {
        private TcpServer server = null;
        internal TcpClient tcpClient;
        private byte[] recvBuf = new byte[2048];
        private AsyncCallback recvAcb;
        private AsyncCallback sendAcb;
        private byte[] accuBuf = null;
        private bool connected = true;

        internal TcpConnection(TcpServer server)
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

        /// <summary>
        /// 开始接收
        /// </summary>
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

        /// <summary>
        /// 接收调用
        /// </summary>
        /// <param name="ar"></param>
        private void recvCallback(IAsyncResult ar)
        {
            NetworkStream ns = (NetworkStream)ar.AsyncState;
            int len = 0;
            try
            {
                len = ns.EndRead(ar);
            }
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

        /// <summary>
        /// 发送
        /// </summary>
        /// <param name="data"></param>
        public void Send(byte[] data)
        {
            if ((tcpClient == null) || (data == null))
                return;
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

        /// <summary>
        /// 异步发送
        /// </summary>
        /// <param name="data"></param>
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

        /// <summary>
        /// 异步发送字符串
        /// </summary>
        /// <param name="str"></param>
        public void AsyncSendStr(string str)
        {
            AsyncSend(Encoding.Default.GetBytes(str + "\x03"));
        }

        /// <summary>
        /// 发送调用
        /// </summary>
        /// <param name="ar"></param>
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

        /// <summary>
        /// 处理接收包
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataLen"></param>
        private void ProcessReceivedPackages(byte[] data, int dataLen)
        {
            //if (server != null)
            //{
            //    server.ShowMessage("Received data: " + Encoding.UTF8.GetString(data, 0, dataLen), "");
            //}

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

        /// <summary>
        /// 处理接收数据
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataLen"></param>
        /// <returns></returns>
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
                        if (server != null)
                        {
                            byte[] buf = new byte[etxIndex - processed];
                            Array.Copy(data, processed, buf, 0, etxIndex - processed);
                            server.HandleData(this, buf);
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
}
