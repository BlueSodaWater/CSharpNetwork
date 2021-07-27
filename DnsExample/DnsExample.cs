using System;
using System.Net;

namespace DnsExample
{
    class DnsExample
    {
        public static string domain = "16bpp.net";

        public static void Main(string[] args)
        {
            // 打印和我们相关的一些信息
            Console.WriteLine("Local Hostname: {0}", Dns.GetHostName());
            Console.WriteLine();

            // 同步获取DNS信息
            IPHostEntry hostInfo = Dns.GetHostEntry(domain);

            // 打印别名
            if (hostInfo.Aliases.Length > 0)
            {
                Console.WriteLine("Aliases for {0}:", hostInfo.HostName);
                foreach (string alias in hostInfo.Aliases)
                    Console.WriteLine("  {0}", alias);
                Console.WriteLine();
            }

            // 打印IP地址
            if (hostInfo.AddressList.Length > 0)
            {
                Console.WriteLine("IP Addressed for {0}:", hostInfo.HostName);
                foreach (IPAddress addr in hostInfo.AddressList)
                    Console.WriteLine("  {0}", addr);
                Console.WriteLine();
            }
        }
    }
}
