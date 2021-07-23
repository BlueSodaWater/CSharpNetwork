using System;
using System.Net.NetworkInformation;
using System.Threading;

namespace PingExample
{
    // 这是一个教你如何使用Ping同步或者异步（使用回调函数而不是Task）的例子

    class PingExample
    {
        private static string _hostname;
        private static int _timeout = 2000; // 2000毫秒
        private static object _consoleLock = new object();

        // 锁住控制台输出然后打印关于PingReply的信息（有颜色）
        public static void PrintPingReply(PingReply reply, ConsoleColor textColor)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = textColor;

                Console.WriteLine("Got ping response from {0}", _hostname);
                Console.WriteLine("  Remote address: {0}", reply.Address);
                Console.WriteLine("  Roundtrip Time: {0}", reply.RoundtripTime);
                Console.WriteLine("  Size: {0} bytes", reply.Buffer.Length);
                Console.WriteLine("  TTL: {0}", reply.Options.Ttl);

                Console.ResetColor();
            }
        }

        // 异步ping的回调
        public static void PingCompletedHandler(object sender, PingCompletedEventArgs e)
        {
            // 取消，错误，或者正常
            if (e.Cancelled)
                Console.WriteLine("Ping was canceled.");
            else if (e.Error != null)
                Console.WriteLine("There was an erroe with Ping, reason={0}", e.Error.Message);
            else
                PrintPingReply(e.Reply, ConsoleColor.Cyan);

            // 通知回调线程
            AutoResetEvent waiter = (AutoResetEvent)e.UserState;
            waiter.Set();
        }

        // 同步Ping
        public static void SendSynchronousPing(Ping pinger, ConsoleColor textColor)
        {
            PingReply reply = pinger.Send(_hostname, _timeout); // 至少阻塞两秒
            if (reply.Status == IPStatus.Success)
                PrintPingReply(reply, ConsoleColor.Magenta);
            else
            {
                Console.WriteLine("Synchronous Ping to {0} failed:", _hostname);
                Console.WriteLine("  Status: {0}", reply.Status);
            }
        }

        public static void Main(string[] args)
        {
            // 设置ping
            Ping pinger = new Ping();
            pinger.PingCompleted += PingCompletedHandler;

            // 设置我们要ping的对象
            Console.WriteLine("Send a Ping to whom: ");
            _hostname = Console.ReadLine();

            // 异步发送
            AutoResetEvent waiter = new AutoResetEvent(false); // 设置无信号
            pinger.SendAsync(_hostname, waiter);

            // 立刻检测异步ping
            if (waiter.WaitOne(_timeout) == false)
            {
                pinger.SendAsyncCancel();
                Console.WriteLine("Async Ping to {0} timed out.", _hostname);
            }

            // 同步发送
            SendSynchronousPing(pinger, ConsoleColor.Magenta);
        }
    }
}
