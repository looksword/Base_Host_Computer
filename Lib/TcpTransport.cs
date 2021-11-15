using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Timers;

namespace Lib
{
    /// <summary>
    /// TCP异步传输
    /// </summary>
    public class TcpTransport : AsyncPerformer
    {
        private string ip = "127.0.0.1";
        private ushort port = 62888;
        private TcpClient tcpClient = null;
        private byte[] recvBuf = new byte[4096];
        private List<byte[]> dataList = new List<byte[]>();
        private AsyncCallback readCallback = null;
        private int sendHeartbeatInterval = 5000;//in millisecond
        /// <summary>
        /// 心跳、内置、默认不用
        /// </summary>
        private Timer heartbeatTimer = null;
        private bool timeToSendHeartbeat = false;
        private int recvHeartbeatTimeout = 15000;//in millisecond
        private DateTime lastRecvHeartbeatTime = DateTime.MinValue;
        public bool EnableHeartbeatTest = false;

        public TcpTransport()
        {
            readCallback = new AsyncCallback(ReadCallback);
            heartbeatTimer = new Timer();
            heartbeatTimer.Interval = sendHeartbeatInterval;
            heartbeatTimer.Elapsed += heartbeatTimerElapsed;
        }

        private void heartbeatTimerElapsed(object sender, ElapsedEventArgs e)
        {
            timeToSendHeartbeat = true;
        }

        public string IP
        {
            get { return ip; }
            set
            {
                if (string.IsNullOrEmpty(value))
                    return;
                ip = value;
            }
        }

        public ushort Port
        {
            get { return port; }
            set { port = value; }
        }

        public string MachineID { get; set; }

        public static string GetMacViaNetBios(string ip)
        {
            string insStr = "00000010000100000000000020434B4141414141414141414141414141414141414141414141414141414141410000210001";
            int len = insStr.Length / 2;
            byte[] ins = new byte[len];
            for (int i = 0; i < len; i++)
            {
                ins[i] = Convert.ToByte(insStr.Substring(i * 2, 2), 16);
            }
            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Parse(ip), 137);
            string mac = "";
            try
            {
                using (UdpClient udpClient = new UdpClient(62168))
                {
                    udpClient.Connect(RemoteIpEndPoint);
                    udpClient.Client.ReceiveTimeout = 1000;//in millisecond.
                    udpClient.Send(ins, ins.Length);
                    ins = null;
                    ins = udpClient.Receive(ref RemoteIpEndPoint);
                    int idx = 56 + ins[56] * 18 + 1;
                    for (int i = 0; i < 6; i++)
                    {
                        mac += Convert.ToString(ins[idx + i], 16);
                    }
                }
            }
            catch { }
            return mac.ToUpper();
        }

        /// <summary>
        /// 字符串发数据、放入列表
        /// </summary>
        /// <param name="data"></param>
        public void AsyncSendData(byte[] data)
        {
            if (data == null)
            {
                return;
            }
            lock (this)
            {
                dataList.Add(data);
                //if (dataList.Count > 10000)
                //{
                //    dataList.RemoveAt(0);
                //}
            }
        }

        /// <summary>
        /// 异步发字符串、放入列表
        /// </summary>
        /// <param name="str"></param>
        public void AsyncSendStr(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return;
            }
            AsyncSendData(Encoding.Default.GetBytes(str));
        }

        /// <summary>
        /// 停止异步执行后
        /// </summary>
        /// <param name="userState"></param>
        protected override void AfterStop(object userState)
        {
            Disconnect();
        }

        private bool connected = false;
        private void Disconnect()
        {
            connected = false;
            heartbeatTimer.Stop();

            if (tcpClient != null)
            {
                tcpClient.Close();
                tcpClient = null;
            }
        }

        /// <summary>
        /// 读取调用
        /// </summary>
        /// <param name="ar"></param>
        private void ReadCallback(IAsyncResult ar)
        {
            NetworkStream ns = (NetworkStream)ar.AsyncState;
            int count = 0;
            try
            {
                count = ns.EndRead(ar);
            }
            catch
            {
                connected = false;
                return;
            }

            try
            {
                lastRecvHeartbeatTime = DateTime.Now;
                ProcessRecvPackages(recvBuf, count);
            }
            catch (Exception ex) { MsgOut(this, "Error", ex.Message, 1); }

            try
            {//一直异步读取
                ns.BeginRead(recvBuf, 0, recvBuf.Length, readCallback, ns);
            }
            catch
            {
                connected = false;
                return;
            }
        }

        public delegate void ConnHandler(TcpTransport sender);
        public event ConnHandler OnConnected = null;
        public event ConnHandler OnDisconnected = null;
        /// <summary>
        /// 异步操作、继承
        /// </summary>
        /// <param name="reqNewThread"></param>
        /// <param name="userState"></param>
        protected override void AsyncWork(ref bool reqNewThread, ref object userState)
        {
            if (!connected)
            {
                Disconnect();
                if (OnDisconnected != null)
                {
                    try { OnDisconnected(this); }
                    catch { }
                }

                tcpClient = new TcpClient();
                //tcpClient.Client.Blocking = false;
                tcpClient.Connect(ip, port);
                NetworkStream nStream = tcpClient.GetStream();
                nStream.WriteTimeout = 10000;//10 second
                nStream.BeginRead(recvBuf, 0, recvBuf.Length, readCallback, nStream);
                connected = true;
                lastRecvHeartbeatTime = DateTime.Now;
                heartbeatTimer.Start();
                timeToSendHeartbeat = true;

                if (OnConnected != null)
                {
                    try { OnConnected(this); }
                    catch { }
                }
            }
            NetworkStream ns = tcpClient.GetStream();
            bool remainData = false;
            while (true)
            {
                if (ns.CanWrite)
                {
                    if (timeToSendHeartbeat)
                    {
                        timeToSendHeartbeat = false;
                        byte[] hb = GetHeartbeat();
                        if (hb != null)
                        {

                            try
                            {
                                ns.Write(hb, 0, hb.Length);
                            }
                            catch
                            {
                                connected = false;
                                break;
                            }
                        }
                    }

                    byte[] bs = GetBytesToBeSend(ref remainData);
                    if (bs != null)
                    {
                        try
                        {
                            ns.Write(bs, 0, bs.Length);
                        }
                        catch
                        {
                            connected = false;
                            break;
                        }
                    }
                }

                if (StopSignal || (!remainData))
                {
                    break;
                }
            }

            if ((EnableHeartbeatTest) && ((DateTime.Now - lastRecvHeartbeatTime).Ticks > recvHeartbeatTimeout))
            {
                connected = false;
            }
        }

        /// <summary>
        /// 心跳信息
        /// </summary>
        /// <returns></returns>
        private byte[] GetHeartbeat()
        {
            if (EnableHeartbeatTest)
            {
                string hb = "##" + DateTime.Now.Ticks.ToString() + "#" + MachineID + "#EQP";
                hb = "#" + hb.Length.ToString() + hb;
                return Encoding.Default.GetBytes(hb);
            }
            return null;
        }

        /// <summary>
        /// 从列表获取要发送的数据
        /// </summary>
        /// <param name="remainData"></param>
        /// <returns></returns>
        private byte[] GetBytesToBeSend(ref bool remainData)
        {
            remainData = false;
            lock (this)
            {
                if (dataList.Count == 0)
                {
                    return null;
                }
                byte[] bs = dataList[0];
                dataList.RemoveAt(0);
                if (dataList.Count > 0)
                {
                    remainData = true;
                }
                return bs;
            }
        }

        private byte[] accuBuf = null;
        /// <summary>
        /// 处理接收包
        /// </summary>
        /// <param name="data"></param>
        /// <param name="dataLen"></param>
        protected virtual void ProcessRecvPackages(byte[] data, int dataLen)
        {
            //if (OnReceivedData != null)
            //{
            //    OnReceivedData(data, dataLen);
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

        public delegate void DataHandler(byte[] data, int dataLen);
        public event DataHandler OnReceivedData = null;
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
                        if (OnReceivedData != null)
                        {
                            byte[] buf = new byte[etxIndex - processed];
                            Array.Copy(data, processed, buf, 0, etxIndex - processed);
                            OnReceivedData(buf, buf.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        MsgOut(this, "Error", ex.Message, 1);
                    }
                    processed = etxIndex + 1;
                    etxIndex = -1;
                }
            }
            return processed;
        }
    }
}
