using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

namespace UdpFileTransfer
{
    public class UdpFileReceiver
    {
        public static readonly int MD5ChecksumByteSize = 16;

        enum ReceiverState
        {
            NotRunning,
            RequestingFile,
            WaitingForRequestFileACK,
            WaitingForInfo,
            PreparingForTransfer,
            Transfering,
            TransferSuccessful,
        }

        // 连接数据
        private UdpClient _client;
        public readonly int Port;
        public readonly string Hostname;
        private bool _shutdownRequested = false;
        private bool _running = false;

        // 获取数据
        private Dictionary<UInt32, Block> _blocksReceived = new Dictionary<UInt32, Block>();
        private Queue<UInt32> _blockRequestQueue = new Queue<UInt32>();
        private Queue<NetworkMessage> _packetQueue = new Queue<NetworkMessage>();

        // 其他数据
        private MD5 _hasher;

        // 构造器，在端口上设置hostname
        public UdpFileReceiver(string hostname, int port)
        {
            Port = port;
            Hostname = hostname;

            // 设置一个默认的客户端来接受/发送包
            _client = new UdpClient(Hostname, Port);
            _hasher = MD5.Create(); // 将会为我们解析DNS
        }

        // 尝试优雅关闭
        public void Shutdown()
        {
            _shutdownRequested = true;
        }

        // 尝试获取文件然后将其下载到本地
        public void GetFile(string filename)
        {
            // 初始化获取文件状态
            Console.WriteLine("Requesting file: {0}", filename);
            ReceiverState state = ReceiverState.RequestingFile;
            byte[] checksum = null;
            UInt32 fileSize = 0;
            UInt32 numBlocks = 0;
            UInt32 totalRequestedBlocks = 0;
            Stopwatch transferTimer = new Stopwatch();

            // 重置发送状态的帮助函数
            Action ResetTransferState = new Action(() =>
            {
                state = ReceiverState.RequestingFile;
                checksum = null;
                fileSize = 0;
                numBlocks = 0;
                totalRequestedBlocks = 0;
                _blockRequestQueue.Clear();
                _blocksReceived.Clear();
                transferTimer.Restart();
            });

            // 主循环
            _running = true;
            bool senderQuit = false;
            bool wasRunning = _running;
            while (_running)
            {
                // 检测是否有新的数据报（如果有）
                CheckForNetwokMessage();
                NetworkMessage nm = (_packetQueue.Count > 0) ? _packetQueue.Dequeue() : null;

                // 防止发送方则已经关闭，退出
                bool isBye = (nm == null) ? false : nm.Packet.IsBye;
                if (isBye)
                    senderQuit = true;

                // 状态
                switch (state)
                {
                    case ReceiverState.RequestingFile:
                        // 创建REQF
                        RequestFilePacket REQF = new RequestFilePacket();
                        REQF.Filename = filename;

                        // 发送他
                        byte[] buffer = REQF.GetBytes();
                        _client.Send(buffer, buffer.Length);

                        // 将状态改为等待ACK
                        state = ReceiverState.WaitingForRequestFileACK;
                        break;

                    case ReceiverState.WaitingForRequestFileACK:
                        // 如果获取了ACK并且负载时文件名，说明我们运行正常
                        bool isACK = (nm == null) ? false : (nm.Packet.IsAck);
                        if (isACK)
                        {
                            AckPacket ACK = new AckPacket(nm.Packet);

                            // 确保他们响应了用户名
                            if (ACK.Message == filename)
                            {
                                // 得到了之后，转换状态
                                state = ReceiverState.WaitingForInfo;
                                Console.WriteLine("They have the file, waiting for INFO...");
                            }
                            else
                                ResetTransferState(); // 不合我们的要求，则重置
                        }
                        break;

                    case ReceiverState.WaitingForInfo:
                        // 确认文件信息
                        bool isInfo = (nm == null) ? false : (nm.Packet.IsInfo);
                        if (isInfo)
                        {
                            // 拉取数据
                            InfoPacket INFO = new InfoPacket(nm.Packet);
                            fileSize = INFO.FileSize;
                            checksum = INFO.Checksum;
                            numBlocks = INFO.BlockCount;

                            // 分配客户端的资源
                            Console.WriteLine("Receive an INFO packet:");
                            Console.WriteLine("  Max block size: {0}", INFO.MaxBlockSize);
                            Console.WriteLine("  Num blocks: {0}", INFO.BlockCount);

                            // 发送INFO的ACK
                            AckPacket ACK = new AckPacket();
                            ACK.Message = "INFO";
                            buffer = ACK.GetBytes();
                            _client.Send(buffer, buffer.Length);

                            // 将状态转为为准备好
                            state = ReceiverState.PreparingForTransfer;
                        }
                        break;

                    case ReceiverState.PreparingForTransfer:
                        // 准备请求队列
                        for (UInt32 id = 1; id <= numBlocks; id++)
                            _blockRequestQueue.Enqueue(id);
                        totalRequestedBlocks += numBlocks;

                        // 转换状态
                        Console.WriteLine("Starting Transfer...");
                        transferTimer.Start();
                        state = ReceiverState.Transfering;
                        break;

                    case ReceiverState.Transfering:
                        // 发送块请求
                        if (_blockRequestQueue.Count > 0)
                        {
                            // 设置一个块的请求
                            UInt32 id = _blockRequestQueue.Dequeue();
                            RequestBlockPacket REQB = new RequestBlockPacket();
                            REQB.Number = id;

                            // 发送数据包
                            buffer = REQB.GetBytes();
                            _client.Send(buffer, buffer.Length);

                            // 一些信息
                            Console.WriteLine("Sent request for Block #{0}", id);
                        }

                        // 检测我们的队列中是否有块
                        bool isSend = (nm == null) ? false : (nm.Packet.IsSend);
                        if (isSend)
                        {
                            // 获取数据（保存他）
                            SendPacket SEND = new SendPacket(nm.Packet);
                            Block block = SEND.Block;
                            _blocksReceived.Add(block.Number, block);

                            // 打印一些信息
                            Console.WriteLine("Received Block #{0} [{1} bytes]", block.Number, block.Data.Length);
                        }

                        // 重新将我们还没获得响应的请求放入队列
                        if ((_blockRequestQueue.Count == 0) && (_blocksReceived.Count != numBlocks))
                        {
                            for (UInt32 id = 1; id <= numBlocks; id++)
                            {
                                if (!_blocksReceived.ContainsKey(id) && !_blockRequestQueue.Contains(id))
                                {
                                    _blockRequestQueue.Enqueue(id);
                                    totalRequestedBlocks++;
                                }
                            }
                        }

                        // 我们是否获取了我们所需要的所有块？如果是的话状态改为传输成功
                        break;

                    case ReceiverState.TransferSuccessful:
                        transferTimer.Stop();

                        // 一切运行正常，发送BYE消息
                        Packet BYE = new Packet(Packet.Bye);
                        buffer = BYE.GetBytes();
                        _client.Send(buffer, buffer.Length);

                        Console.WriteLine("Transfer successful; it took {0:0.000}s with a success ratio of {1:0.000}.",
                            transferTimer.Elapsed.TotalSeconds, (double)numBlocks / (double)totalRequestedBlocks);
                        Console.WriteLine("Decompressing the Blocks...");

                        // 重新将数据结构化
                        if (SaveBlocksToFile(filename, checksum, fileSize))
                            Console.WriteLine("Saved file as {0}.", filename);
                        else
                            Console.WriteLine("There was some trouble in saving the Blocks to {0}.", filename);

                        // 然后就结束了
                        _running = false;
                        break;
                }

                // Sleep
                Thread.Sleep(1);

                // 检测是否关闭
                _running &= !_shutdownRequested;
                _running &= !senderQuit;
            }

            // 如果用户想要关闭，发送一个BYE消息
            if (_shutdownRequested && wasRunning)
            {
                Console.WriteLine("User canceled transfer.");

                Packet BYE = new Packet(Packet.Bye);
                byte[] buffer = BYE.GetBytes();
                _client.Send(buffer, buffer.Length);
            }

            // 如果服务器让我们关闭
            if (senderQuit && wasRunning)
                Console.WriteLine("The sender quit on us, canceling the transfer.");

            ResetTransferState(); // 同时也清除了所有集合
            _shutdownRequested = false; // 防止我们关闭一个下载，但想开始一个新的

        }

        public void Close()
        {
            _client.Close();
        }

        // 尝试填充数据包队列
        private void CheckForNetwokMessage()
        {
            if (!_running)
                return;

            // 检测是否有空用数据（类型至少是4个字节）
            int bytesAvailable = _client.Available;
            if (bytesAvailable >= 4)
            {
                // 只会读取一个数据报（即使已经获得了多个）
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = _client.Receive(ref ep);
                Packet p = new Packet(buffer);

                // 创建消息结构体然后排队处理
                NetworkMessage nm = new NetworkMessage();
                nm.Sender = ep;
                nm.Packet = p;
                _packetQueue.Enqueue(nm);
            }
        }

        // 尝试解压数据块，然后将其保存为文件
        private bool SaveBlocksToFile(string filename, byte[] networkChecksum, UInt32 fileSize)
        {
            bool good = false;

            try
            {
                // 分配一些内存
                int compressedByteSize = 0;
                foreach (Block block in _blocksReceived.Values)
                    compressedByteSize += block.Data.Length;
                byte[] compressedBytes = new byte[compressedByteSize];

                // 将其整合到一个大块中
            }
            catch (Exception e)
            {
                // 信息
                Console.WriteLine("Could not save the blocks to \"{0}\", reason:", filename);
                Console.WriteLine(e.Message);
            }

            return good;
        }

        public static UdpFileReceiver fileReceiver;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            fileReceiver?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // 设置接收方信息
            string hostname = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            string filename = "short_message.txt";//args[2].Trim();
            fileReceiver = new UdpFileReceiver(hostname, port);

            // 增加Ctrl-C处理
            Console.CancelKeyPress += InterruptHandler;

            // 获取文件
            fileReceiver.GetFile(filename);
            fileReceiver.Close();
        }
    }
}
