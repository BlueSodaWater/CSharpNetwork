using Microsoft.Xna.Framework;

namespace PongGame
{
    // 这是一个包含游戏中对象几何相关信息的类（区域，板/球尺寸等等）
    public static class GameGeometry
    {
        public static readonly Point PlayArea = new Point(320, 240); // 客户端区域
        public static readonly Vector2 ScreenCenter = new Vector2(PlayArea.X / 2f, PlayArea.Y / 2f); // 屏幕中心点
        public static readonly Point BallSize = new Point(8, 8); // 球大小
        public static readonly Point PaddleSize = new Point(8, 44); // 板大小
        public static readonly int GoalSize = 12; // 板的宽度
        public static readonly float PaddleSpeed = 100f; // 板的速度，（像素/秒）
    }
}
