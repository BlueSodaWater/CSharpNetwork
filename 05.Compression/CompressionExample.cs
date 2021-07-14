using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace CompressionExample
{
    class CompressionExample
    {
        // 帮助获取一个数字代表字节（兆）
        public static float ComputeSizeInMB(long size)
        {
            return (float)size / 1024f / 1024f;
        }

        public static void Main(string[] args)
        {
            // 我们的测试文件
            string fileToCompress = "film.mp4";
            byte[] uncompressedBytes = File.ReadAllBytes(fileToCompress);

            // 标记
            Stopwatch timer = new Stopwatch();

            // 展示一些信息
            long uncompressedFileSize = uncompressedBytes.LongLength;
            Console.WriteLine("{0} uncompressed is {1:0.0000} MB large.",
                fileToCompress,
                ComputeSizeInMB(uncompressedFileSize));

            // 使用 Deflate 进行压缩（Optimal）
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // 初始化
                DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionLevel.Optimal, true);

                // 运行压缩
                timer.Start();
                deflateStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                deflateStream.Close();
                timer.Stop();

                // 打印一些信息
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using DeflateStream (Optimal): {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // 关闭
                timer.Reset();
            }

            // 使用 Deflate 进行压缩（fast）
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // 初始化
                DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionLevel.Fastest, true);

                // 运行压缩
                timer.Start();
                deflateStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                deflateStream.Close();
                timer.Stop();

                // 打印一些信息
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using DeflateStream (Optimal): {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // 关闭
                timer.Reset();
            }

            // 使用Gzip压缩（并保存）
            string savedArchive = fileToCompress + ".gz";
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // 初始化
                GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true);

                // 运行压缩
                timer.Start();
                gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                gzipStream.Close();
                timer.Stop();

                // 打印一些信息
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using GZipStream: {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // 保存他
                using (FileStream saveStream = new FileStream(savedArchive, FileMode.Create))
                {
                    compressedStream.Position = 0;
                    compressedStream.CopyTo(saveStream);
                }

                // 清除
                timer.Reset();
            }
        }
    }
}
