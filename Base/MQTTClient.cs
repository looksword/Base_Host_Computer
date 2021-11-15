using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Receiving;

namespace Base
{
    /// <summary>
    /// level 0：最多一次的传输
    /// level 1：至少一次的传输
    /// level 2： 只有一次的传输
    /// </summary>
    public class MQTTClient
    {
        private MqttClient mqttClient = null;
        public delegate void StrMsgHandler(string msg, string destIP);
        public event StrMsgHandler OnMessage = null;
        public event StrMsgHandler OnError = null;
        public delegate void CommandHandler(MQTTClient sender, DateTime time, string jsonStrCmd);
        public event CommandHandler OnCommand = null;
        public bool TransDataFlag = false;
        private string Server = "127.0.0.1";
        private int Port = 1883;
        public bool IsConnected
        {
            get
            {
                return mqttClient.IsConnected;
            }
        }

        public MQTTClient(string server, int port)
        {
            connect(server, port);
            Server = server;
            Port = port;
        }

        /// <summary>
        /// 连接
        /// </summary>
        /// <param name="server"></param>
        /// <param name="port"></param>
        private void connect(string server, int port)
        {
            if (mqttClient == null)
            {
                mqttClient = new MqttFactory().CreateMqttClient() as MqttClient;
            }
            //定义遗嘱消息
            var will = new MqttApplicationMessageBuilder()
                .WithTopic("Code/status")
                .WithPayload(System.Text.Encoding.UTF8.GetBytes("offline"))
                .WithAtLeastOnceQoS()//至少一次
                .WithRetainFlag()
                .Build();
            //var will2 = new MqttApplicationMessage(){ Topic = "PLC/status", Payload = System.Text.Encoding.UTF8.GetBytes("offline") };//定义遗嘱消息
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(server, port) // Port is optional
                .WithClientId("Code")
                .WithWillMessage(will)
                .Build();
            mqttClient.ConnectAsync(options, CancellationToken.None); // Since 3.0.5 with CancellationToken

            mqttClient.UseConnectedHandler(async e =>
            {
                // Subscribe to a topic
                var topicFilterBulder = new MqttTopicFilterBuilder().WithTopic("Code/write").WithAtMostOnceQoS().Build();
                await mqttClient.SubscribeAsync(topicFilterBulder);

                // 连接MQTT时发送online
                byte[] payload = ASCIIEncoding.UTF8.GetBytes("online");
                var sendmessage = new MqttApplicationMessageBuilder()
                    .WithTopic("Code/status")
                    .WithPayload(payload)
                    .WithAtLeastOnceQoS()//至少一次
                    .WithRetainFlag()
                    .Build();
                await mqttClient.PublishAsync(sendmessage, CancellationToken.None);
            });

            mqttClient.UseDisconnectedHandler(async e =>
            {// Reconnect
                await Task.Delay(TimeSpan.FromSeconds(10));

                try
                {
                    connect(server, port);
                }
                catch (Exception ex)
                {
                    ShowError(ex.Message, "");
                }
            });

            Func<MqttApplicationMessageReceivedEventArgs, Task> func = MqttApplicationMessageReceived;
            mqttClient.UseApplicationMessageReceivedHandler(func);

            //mqttClient.ConnectedHandler = new MqttClientConnectedHandlerDelegate(new Func<MqttClientConnectedEventArgs, Task>(Connected));
            //mqttClient.DisconnectedHandler = new MqttClientDisconnectedHandlerDelegate(new Func<MqttClientDisconnectedEventArgs, Task>(Disconnected));
            //mqttClient.ApplicationMessageReceivedHandler = new MqttApplicationMessageReceivedHandlerDelegate(new Func<MqttApplicationMessageReceivedEventArgs>(MqttApplicationMessageReceived));
        }

        //private async Task Connected(MqttClientConnectedEventArgs e)
        //{
        //    try
        //    {
        //        //List<TopicFilter> listTopic = new List<TopicFilter>();
        //        //if (listTopic.Count() <= 0)
        //        //{
        //        //    var topicFilterBulder = new TopicFilterBuilder().WithTopic("PLC/write").WithAtMostOnceQoS().Build();
        //        //    listTopic.Add(topicFilterBulder);
        //        //    //Console.WriteLine("Connected >>Subscribe " + Topic);
        //        //} 
        //        //await mqttClient.SubscribeAsync(listTopic.ToArray());
        //        //Console.WriteLine("Connected >>Subscribe Success");
        //        var topicFilterBulder = new TopicFilterBuilder().WithTopic("PLC/write").WithAtMostOnceQoS().Build();
        //        await mqttClient.SubscribeAsync(topicFilterBulder);
        //    }
        //    catch (Exception ex)
        //    {
        //        ShowError(ex.Message, "");
        //    }
        //}

        //private async Task Disconnected(MqttClientDisconnectedEventArgs e)
        //{
        //    try
        //    {
        //        Console.WriteLine("Disconnected >>Disconnected Server");
        //        await Task.Delay(TimeSpan.FromSeconds(10));
        //        try
        //        {
        //            connect(Server,Port);
        //        }
        //        catch (Exception exp)
        //        {
        //            Console.WriteLine("Disconnected >>Exception " + exp.Message);
        //        }
        //    }
        //    catch (Exception exp)
        //    {
        //        Console.WriteLine(exp.Message);
        //    }
        //}

        private Task MqttApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            try
            {
                byte[] data = arg.ApplicationMessage.Payload;
                DateTime time = DateTime.Now;
                string jsonStr = ASCIIEncoding.ASCII.GetString(data, 0, data.Length);
                HandleCommand(this, time, jsonStr);//处理数据
            }
            catch (Exception ex)
            {
                ShowError(ex.Message, "");
            }
            //throw new NotImplementedException();
            return Task.FromResult(0);
        }

        /// <summary>
        /// 发布
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="payload"></param>
        public void publish(string topic, string jsonStr)
        {
            byte[] payload = ASCIIEncoding.UTF8.GetBytes(jsonStr);
            var sendmessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithAtMostOnceQoS()
                .WithRetainFlag()
                .Build();
            mqttClient.PublishAsync(sendmessage, CancellationToken.None); // Since 3.0.5 with CancellationToken
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

        internal void HandleCommand(MQTTClient sender, DateTime time, string jsonStrCmd)
        {
            if (OnCommand != null)
            {
                OnCommand(sender, time, jsonStrCmd);
            }
        }
    }
}
