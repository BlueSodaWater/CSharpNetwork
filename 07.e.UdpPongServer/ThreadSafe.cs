using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PongGame
{
    // 是一个变量线程安全
    public class ThreadSafe<T>
    {
        // 数据和锁
        private T _value;
        private object _lock = new object();

        // 如何获取和设置数据
        public T Value
        {
            get
            {
                lock (_lock)
                    return _value;
            }

            set
            {
                lock (_lock)
                    _value = value;
            }
        }

        // 初始化值
        public ThreadSafe(T value = default(T))
        {
            Value = value;
        }
    }
}
