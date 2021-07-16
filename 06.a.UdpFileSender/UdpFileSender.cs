using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UdpFileTransfer
{
    public class UdpFileSender
    {
        public static readonly UInt32 MaxBlockSize = 8 * 2014; // 8KB

        enum SenderState
        {
            NotRunning,
            WaitingForFileRequest,
            PreparingFileForTransfer,
            SendingFileInfo,
            WaitingForInfoACK,
            Transfering
        }

        // 连接数据
        private UdpClient _client;
        public readonly int Port;
        public bool Running { get; private set; } = false;

        // 传输数据
        public readonly string FilesDirectory;
        private HashSet<string> _transferableFiles;
        private Dictionary<UInt32, Block> _blocks = new Dictionary<UInt32, Block>();
        private Queue<NetworkMessage> _packetQueue = new Queue<NetworkMessage>();

        // 其他的数据
        private MD5 _hasher;

        // 构造器，在端口是创造一个UdpClient
        public UdpFileSender(string filesDirectory, int port)
        {
            FilesDirectory = filesDirectory;
            Port = port;
            _client = new UdpClient(Port, AddressFamily.InterNetwork);  // 绑定IPv4
            _hasher = MD5.Create();
        }

        // 准备发送方进行文件传输
        public void Init()
        {
            // 扫描文件（只扫描顶部目录）
            List<string> files = new List<string>(Directory.EnumerateFiles(FilesDirectory));
            _transferableFiles = new HashSet<string>(files.Select(s => s.Substring(FilesDirectory.Length + 1)));

            // 确保我们至少有一个来发送
            if (_transferableFiles.Count != 0)
            {
                // 修改状态
                Running = true;

                // 打印信息
                Console.WriteLine("I'll transfer these files:");
                foreach (string s in _transferableFiles)
                    Console.WriteLine("  {0}", s);
            }
            else
            {
                Console.WriteLine("I don't have any files to transfer.");
            }
        }

        // （优雅）关闭的信息
        public void ShutDown()
        {
            Running = false;
        }

        // 发送者主循环
        public void Run()
        {
            // 设置一些状态
            SenderState state = SenderState.WaitingForFileRequest;
            string requestedFile = "";
            IPEndPoint receiver = null;

            // 这是一个重置状态的帮助函数
            Action ResetTransferState = new Action(() =>
            {
                state = SenderState.WaitingForFileRequest;
                requestedFile = "";
                receiver = null;
                _blocks.Clear();
            });

            while (Running)
            {
                // 检测新消息
                CheckForNetworkMessages();
                NetworkMessage nm = (_packetQueue.Count > 0) ? _packetQueue.Dequeue() : null;

                // 检测我们有无得到BYE
                bool isBye = (nm == null) ? false : nm.Packet.IsBye;
                if (isBye)
                {
                    // 重置为原来的状态
                    ResetTransferState();
                    Console.WriteLine("Received a BYE message, waiting for next client.");
                }

                // 根据目前的状态进行下一步操作
                switch (state)
                {
                    case SenderState.WaitingForFileRequest:
                        // 检测我们是否有文件请求
                        // 如果有一个数据包，他请求文件，发送ACK然后切换状态
                        bool isRequestFile = (nm == null) ? false : nm.Packet.IsRequestFile;
                        if (isRequestFile)
                        {
                            // 准备ACK
                            RequestFilePacket REQF = new RequestFilePacket(nm.Packet);
                            AckPacket ACK = new AckPacket();
                            requestedFile = REQF.Filename;

                            // 打印信息
                            Console.WriteLine("{0} has requested file file \"{1}\".", nm.Sender, requestedFile);

                            // 检测我们是否有此文件
                            if (_transferableFiles.Contains(requestedFile))
                            {
                                // 标记文件已经存在，并把发送消息方作为我们的接收方
                                receiver = nm.Sender;
                                ACK.Message = requestedFile;
                                state = SenderState.PreparingFileForTransfer;

                                Console.WriteLine("  We have it.");
                            }
                            else
                                ResetTransferState();

                            // 发送消息
                            byte[] buffer = ACK.GetBytes();
                            _client.Send(buffer, buffer.Length, nm.Sender);
                        }
                        break;

                    case SenderState.PreparingFileForTransfer:
                        // 使用需求的文件，将其存放到内存中
                        byte[] checksum;
                        UInt32 fileSize;
                        if (PrepareFile(requestedFile, out checksum, out fileSize))
                        {
                            // 运行正常，发送info包
                            InfoPacket INFO = new InfoPacket();
                            INFO.Checksum = checksum;
                            INFO.FileSize = fileSize;
                            INFO.MaxBlockSize = MaxBlockSize;
                            INFO.BlockCount = Convert.ToUInt32(_blocks.Count);

                            // 发送它
                            byte[] buffer = INFO.GetBytes();

                            // 切换状态
                            Console.WriteLine("Sending INFO, waiting for ACK...");
                            state = SenderState.WaitingForInfoACK;
                        }
                        else
                            ResetTransferState(); // 文件未准备好，重置状态
                        break;

                    case SenderState.WaitingForInfoACK:
                        // 如果获取了ACK并且负载是文件名，运作正常
                        bool isAck = (nm == null) ? false : (nm.Packet.IsAck);
                        if (isAck)
                        {
                            AckPacket ACK = new AckPacket(nm.Packet);
                            if (ACK.Message == "INFO")
                            {
                                Console.Write("Starting Transfer...");
                                state = SenderState.Transfering;
                            }
                        }
                        break;

                    case SenderState.Transfering:
                        // 如果是块请求，就发送他
                        bool isRequestBlock = (nm == null) ? false : nm.Packet.IsRequestBlock;
                        if (isRequestBlock)
                        {
                            // 拉取数据
                            RequestBlockPacket REQB = new RequestBlockPacket(nm.Packet);
                            Console.WriteLine("Got request for Block #{0}", REQB.Number);

                            // 创造响应包
                            Block block = _blocks[REQB.Number];
                            SendPacket SEND = new SendPacket();
                            SEND.Block = block;

                            // 发送他
                            byte[] buffer = SEND.GetBytes();
                            _client.Send(buffer, buffer.Length, nm.Sender);
                            Console.WriteLine("Sent Block #{0} [{1} bytes]", block.Number, block.Data.Length);
                        }
                        break;
                }

                Thread.Sleep(1);
            }

            // 如果有接收方，意味着我们要提醒他关闭
            if (receiver != null)
            {
                Packet BYE = new Packet(Packet.Bye);
                byte[] buffer = BYE.GetBytes();
                _client.Send(buffer, buffer.Length, receiver);
            }

            state = SenderState.NotRunning;
        }

        // 关闭底层UDP客户端
        public void Close()
        {
            _client.Close();
        }

        // 尝试填充数据包队列
        private void CheckForNetworkMessages()
        {
            if (!Running)
                return;

            // 检查是否有可用数据（类型至少有四个字节）
            int bytesAvailable = _client.Available;
            if (bytesAvailable >= 4)
            {
                // 将会读取一个数据报（即使已经获得多个）
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = _client.Receive(ref ep);

                // 创建消息结构然后将其排队处理
                NetworkMessage nm = new NetworkMessage();
                nm.Sender = ep;
                nm.Packet = new Packet(buffer);
                _packetQueue.Enqueue(nm);
            }
        }

        // 将文件加载到块中，如果需要的文件已经准备好了就返回true
        private bool PrepareFile(string requestedFile, out byte[] checksum, out UInt32 fileSize)
        {
            Console.WriteLine("Preparing the file to send...");
            bool good = false;
            fileSize = 0;

            try
            {
                // 读入并计算原始文件的校验和
                byte[] fileBytes = File.ReadAllBytes(Path.Combine(FilesDirectory, requestedFile));
                checksum = _hasher.ComputeHash(fileBytes);
                fileSize = Convert.ToUInt32(fileBytes.Length);
                Console.WriteLine("{0} is {1} bytes large.", requestedFile, fileSize);

                // 将其压缩
                Stopwatch timer = new Stopwatch();
                using (MemoryStream compressedStream = new MemoryStream())
                {
                    // 执行实际的压缩
                    DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Compress, true);
                    timer.Start();
                    deflateStream.Write(fileBytes, 0, fileBytes.Length);
                    deflateStream.Close();
                    timer.Stop();

                    // 将其放入块中
                    compressedStream.Position = 0;
                    long compressedSize = compressedStream.Length;
                    UInt32 id = 1;
                    while (compressedStream.Position < compressedSize)
                    {
                        // 抓取一块
                        long numBytesLeft = compressedSize - compressedStream.Position;
                        long allocationSize = (numBytesLeft > MaxBlockSize) ? MaxBlockSize : numBytesLeft;
                        byte[] data = new byte[allocationSize];
                        compressedStream.Read(data, 0, data.Length);

                        // 创建一个新块
                        Block b = new Block(id++);
                        b.Data = data;
                        _blocks.Add(b.Number, b);
                    }

                    // 打印信息并且表面我们运作正常
                    Console.WriteLine("{0} compressed is {1} bytes large in {2:0.000}s.", requestedFile, compressedSize, timer.Elapsed.TotalSeconds);
                    Console.WriteLine("Sending the file in {0} blocks, using a max block size of {1} bytes.", _blocks.Count, MaxBlockSize);
                    good = true;
                }
            }
            catch (Exception e)
            {
                // 信息
                Console.WriteLine("Could not prepare the file for transfer, reason:");
                Console.WriteLine(e.Message);

                // 重置一些参数
                _blocks.Clear();
                checksum = null;
            }

            return good;
        }


        public static UdpFileSender fileSender;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            fileSender?.ShutDown();
        }

        public static void Main(string[] args)
        {
            // 设置发送端
            string filesDirectpry = "Files";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            fileSender = new UdpFileSender(filesDirectpry, port);

            // 增加Ctrl-C控制器
            Console.CancelKeyPress += InterruptHandler;

            // 运行
            fileSender.Init();
            fileSender.Run();
            fileSender.Close();
        }
    }
}
