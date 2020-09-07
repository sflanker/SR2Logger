using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ModApi.Craft.Program;
using UnityEngine;

namespace Assets.Scripts.Craft
{
    /// <summary>
    /// A server for logging values to UDP.
    /// </summary>
    /// <seealso cref="System.IDisposable" />
    public class UdpServer : IDisposable
    {
        private static readonly Encoding StringEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly SemaphoreSlim packetSemaphore = new SemaphoreSlim(1);
        private readonly ConcurrentQueue<String> pendingLogMessages = new ConcurrentQueue<String>();

        /// <summary>
        /// The UDP client.
        /// </summary>
        private readonly UdpClient client;

        /// <summary>
        /// The memory stream for the packet being crafted.
        /// </summary>
        private readonly MemoryStream _packetStream;

        /// <summary>
        /// The binary writer for the packet being crafted.
        /// </summary>
        private readonly BinaryWriter _packetWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="UdpServer"/> class.
        /// </summary>
        /// <param name="host">The host to send packets to.</param>
        /// <param name="port">The port to send packets to.</param>
        public UdpServer(String host, Int32 port)
        {
            Hostname = host;
            Port = port;
            client = new UdpClient();
            _packetStream = new MemoryStream();
            _packetWriter = new BinaryWriter(_packetStream);
        }

        /// <summary>
        /// Gets or sets the hostname to send packets to.
        /// </summary>
        /// <value>
        /// The hostname.
        /// </value>
        public String Hostname { get; }

        /// <summary>
        /// Gets or sets the port.
        /// </summary>
        /// <value>
        /// The port.
        /// </value>
        public Int32 Port { get; }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// Also stops server.
        /// </summary>
        public void Dispose()
        {
            this.client.Close();
            this.client.Dispose();
        }

        /// <summary>
        /// Sends the raw data as a UDP packet.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="length">The length of bytes to send.</param>
        private void SendData(Byte[] bytes, Int32 length)
        {
            this.client.Send(bytes, length, Hostname, Port);
        }

        /// <summary>
        /// Resets the state.
        /// </summary>
        private void ResetState()
        {
            Array.Clear(this._packetStream.GetBuffer(), 0, (Int32)this._packetStream.Length);
            this._packetStream.Position = 0;
            this._packetStream.SetLength(0);
        }

        public void BeginVariableSample(TimeSpan timestamp)
        {
            this.packetSemaphore.Wait();
            this.ResetState();
            this._packetWriter.Write((Byte)MessageType.VariableSample);
            this._packetWriter.Write((UInt64)timestamp.TotalMilliseconds);
        }

        public void SendVariable(String name, ExpressionResult value)
        {
            this._packetWriter.Write(StringEncoding.GetByteCount(name));
            this._packetWriter.Write(StringEncoding.GetBytes(name));

            switch (value.ExpressionType)
            {
                case ExpressionType.Boolean:
                    this._packetWriter.Write((Byte)TypeCodes.Boolean);
                    this._packetWriter.Write(value.BoolValue);
                    break;
                case ExpressionType.Number:
                    this._packetWriter.Write((Byte)TypeCodes.Float64);
                    this._packetWriter.Write(value.NumberValue);
                    break;
                case ExpressionType.Vector:
                    this._packetWriter.Write((Byte)TypeCodes.Vector3d);
                    this._packetWriter.Write(value.VectorValue.x);
                    this._packetWriter.Write(value.VectorValue.y);
                    this._packetWriter.Write(value.VectorValue.z);
                    break;
                case ExpressionType.Text:
                    this._packetWriter.Write((Byte)TypeCodes.Text);
                    this._packetWriter.Write(StringEncoding.GetByteCount(value.TextValue));
                    this._packetWriter.Write(StringEncoding.GetBytes(value.TextValue));
                    break;
                case ExpressionType.List:
                    this._packetWriter.Write((Byte)TypeCodes.List);
                    this._packetWriter.Write(value.ListValue.Count);
                    foreach (var item in value.ListValue)
                    {
                        this._packetWriter.Write(item != null ? StringEncoding.GetByteCount(item) : -1);
                        if (!String.IsNullOrEmpty(item))
                        {
                            this._packetWriter.Write(StringEncoding.GetBytes(item));
                        }
                    }

                    break;
                default:
                    Debug.LogWarning("Cannot log type: " + value.ExpressionType);
                    this._packetWriter.Write((Byte)TypeCodes.None);
                    break;
            }
        }

        /// <summary>
        /// Sends the packet that was crafted.
        /// </summary>
        public void FinishVariableSample()
        {
            try
            {
                this.SendData(this._packetStream.GetBuffer(), (Int32)this._packetStream.Length);
                this.ResetState();

                if (this.pendingLogMessages.Count > 0)
                {
                    // Flush the message queue
                    while (this.pendingLogMessages.TryDequeue(out var message))
                    {
                        this._packetWriter.Write((Byte)MessageType.LogMessage);
                        this._packetWriter.Write(StringEncoding.GetByteCount(message));
                        this._packetWriter.Write(StringEncoding.GetBytes(message));
                        this.SendData(this._packetStream.GetBuffer(), (Int32)this._packetStream.Length);
                        this.ResetState();
                    }
                }
            } finally
            {
                this.packetSemaphore.Release();
            }
        }

        public void SendLogMessage(String message)
        {
            if (this.packetSemaphore.Wait(TimeSpan.Zero))
            {
                try
                {
                    this.ResetState();
                    this._packetWriter.Write((Byte)MessageType.LogMessage);
                    this._packetWriter.Write(StringEncoding.GetByteCount(message));
                    this._packetWriter.Write(StringEncoding.GetBytes(message));
                    this.SendData(this._packetStream.GetBuffer(), (Int32)this._packetStream.Length);
                    this.ResetState();
                } finally
                {
                    this.packetSemaphore.Release();
                }
            } else
            {
                // Defer the log message until the current variable sampling request completes.
                this.pendingLogMessages.Enqueue(message);
            }
        }
    }

    internal enum MessageType : Byte
    {
        None = 0,
        VariableSample,
        LogMessage
    }

    internal enum TypeCodes : Byte
    {
        None = 0,
        Float64,
        Boolean,
        Vector3d,
        Text,
        List
    }
}
