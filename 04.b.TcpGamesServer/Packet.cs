using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TcpGames
{
    public class Packet
    {
        [JsonProperty("command")]
        public string Command { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        public Packet(string command = "", string message = "")
        {
            this.Command = command;
            this.Message = message;
        }

        public override string ToString()
        {
            return string.Format("[Packet:\n" +
                "  Command=`{0}`\n" +
                "  Message=`{1}`]",
                Command, Message);
        }

        // 格式化为Json字符串
        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        // 反序列化
        public static Packet FromJson(string jsonData)
        {
            return JsonConvert.DeserializeObject<Packet>(jsonData);
        }
    }
}
