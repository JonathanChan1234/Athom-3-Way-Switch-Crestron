using Crestron.SimplSharp;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AthomSwitch
{
    public class AthomSwitchClient
    {
        private IManagedMqttClient _client;
        private MqttClientOptions _clientOptions;
        private ManagedMqttClientOptions _connectionOptions;

        private string Host;
        private int Port;
        private string Username;
        private string Password;
        private string ClientId;
        private string Topic;

        public stateChanged PowerStateChanged { get; set; }
        public stateChanged OnlineStateChanged { get; set; }
        public stateChanged MqttConnectionStateChanged { get; set; }

        public delegate void stateChanged(ushort status);

        public AthomSwitchClient() { }

        public void InitAthomSwitch(
           string host,
           int port,
           string username,
           string password,
           string clientId,
           string topic)
        {
            var factory = new MqttFactory();
            _client = factory.CreateManagedMqttClient();

            Host = host;
            Port = port;
            Username = username;
            Password = password;
            ClientId = clientId;
            Topic = topic;
        }

        public void Connect()
        {
            Task.Run(async () =>
            {
                _clientOptions = new MqttClientOptionsBuilder()
                .WithClientId(ClientId.ToString())
                .WithTcpServer(Host, Port)
                .WithCredentials(Username, Password)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithCleanSession()
                .Build();
                _connectionOptions = new ManagedMqttClientOptionsBuilder()
                    .WithAutoReconnectDelay(TimeSpan.FromSeconds(60))
                    .WithClientOptions(_clientOptions)
                    .Build();
                _client.ConnectedAsync += MqttClient_ConnectedAsync;
                _client.DisconnectedAsync += MqttClient_DisconnectedAsync;
                _client.ConnectingFailedAsync += MqttClient_ConnectingFailedAsync;
                _client.ApplicationMessageReceivedAsync += MqttClient_ApplicationMessageReceivedAsync;
                await _client.StartAsync(_connectionOptions);
                var topicFilters = new List<MqttTopicFilter> { new MqttTopicFilter { Topic = $"{Topic}/#" } };
                await _client.SubscribeAsync(topicFilters);
            }).Wait();
        }

        public void Disconnect()
        {
            Task.Run(async () =>
            {
                await _client.StopAsync();
            }).Wait();
        }

        public void PowerOn()
        {
            Publish($"{Topic}/cmnd/power1", "on");
        }

        public void PowerOff()
        {
            Publish($"{Topic}/cmnd/power1", "off");
        }

        public void Toggle()
        {
            Publish($"{Topic}/cmnd/power1", "");
        }

        private Task MqttClient_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            var topic = arg?.ApplicationMessage?.Topic;
            var payloadText = Encoding.UTF8.GetString(
                arg?.ApplicationMessage?.PayloadSegment.Array ?? Array.Empty<byte>());

            if (topic == $"{Topic}/power1")
            {
                PowerStateChanged?.Invoke((ushort)(payloadText == "on" ? 1 : 0));
            }
            if (topic == $"{Topic}/availability")
            {
                OnlineStateChanged?.Invoke((ushort)(payloadText == "online" ? 1 : 0));
            }
            return Task.CompletedTask;
        }

        private Task MqttClient_ConnectingFailedAsync(ConnectingFailedEventArgs arg)
        {
            MqttConnectionStateChanged?.Invoke(3);
            CrestronConsole.PrintLine($"Fail to connect to MQTT broker. Reason: {arg.Exception.Message}");
            return Task.CompletedTask;
        }

        private Task MqttClient_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            MqttConnectionStateChanged?.Invoke(2);
            CrestronConsole.PrintLine($"Disconnect from MQTT broker. Reason: {arg.Exception.Message}");
            return Task.CompletedTask;
        }

        private Task MqttClient_ConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            MqttConnectionStateChanged?.Invoke(1);
            CrestronConsole.PrintLine("Connected to MQTT broker");
            return Task.CompletedTask;
        }

        private void Publish(string topic, string payload)
        {
            if (!_client.IsStarted || !_client.IsConnected)
            {
                CrestronConsole.PrintLine($"Mqtt client not connected, fail to publish {payload} (topic: {topic})");
                return;
            }
            CrestronConsole.PrintLine($"Sending topic: {topic}, payload: {payload}");
            var message = new MqttApplicationMessageBuilder()
              .WithTopic(topic)
              .WithPayload(payload)
              .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
              .WithRetainFlag()
              .Build();
            Task.Run(async () =>
                await _client.InternalClient.PublishAsync(message)
            ).Wait();
        }
    }
}
