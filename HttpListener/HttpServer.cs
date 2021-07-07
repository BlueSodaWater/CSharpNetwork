using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HttpListenerExample
{
    class HttpServer
    {
        public static string urlToDownload = "https://16bpp.net/";
        public static string fileName = "index.html";

        public static async Task DownloadWebPage()
        {
            Console.WriteLine("Starting download...");

            using (HttpClient httpClient = new HttpClient())
            {
                var resp = await httpClient.GetAsync(urlToDownload);

                if (resp.IsSuccessStatusCode)
                {
                    Console.WriteLine("Got it...");

                    var data = await resp.Content.ReadAsByteArrayAsync();

                    var fStream = File.Create(fileName);
                    await fStream.WriteAsync(data, 0, data.Length);
                    fStream.Close();

                    Console.WriteLine("Done!");
                }
            }
        }

        public static void Main(string[] args)
        {
            var dlTask = DownloadWebPage();

            Console.WriteLine("Holding for at least 5 seconds...");
            Thread.Sleep(TimeSpan.FromSeconds(5));

            dlTask.GetAwaiter().GetResult();
        }
    }
}
