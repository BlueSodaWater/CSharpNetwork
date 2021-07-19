using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PongGame
{
    // h会被弹来弹去的球
    public class Ball
    {
        // 静态变量
        public static Vector2 InitialSpeed = new Vector2(60F, 60F);

        // 私有数据成员
        private Texture2D _sprite;
        private Random _random = new Random(); // 随机数生成器

        // 公共数据成员
        public Vector2 Position = new Vector2();
        public Vector2 Speed;
        public int LeftmostX { get; private set; }
        public int RightmostX { get; private set; }
        public int TopmostY { get; private set; }
        public int BottommostY { get; private set; }

        // 打击区域
        public Rectangle CollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), GameGeometry.BallSize); }
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("ball.png");
        }

        // 将球的位置重置到屏幕中心
        public void Initialize()
        {
            // 将球置于中心
            Rectangle playAreaRect = new Rectangle(new Point(0, 0), GameGeometry.PlayArea);
            Position = playAreaRect.Center.ToVector2();
            Position = Vector2.Subtract(Position, GameGeometry.BallSize.ToVector2() / 2f);

            // 设置速度
            Speed = InitialSpeed;

            // 随机方向
            if (_random.Next() % 2 == 1)
                Speed.X *= -1;
            if (_random.Next() % 2 == 1)
                Speed.Y *= -1;

            // 设置弹跳
            LeftmostX = 0;
            RightmostX = playAreaRect.Width - GameGeometry.BallSize.X;
            TopmostY = 0;
            BottommostY = playAreaRect.Height - GameGeometry.BallSize.Y;
        }

        // 移动小球，应该由服务器调用
        public void ServerSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // 增加距离
            Position = Vector2.Add(Position, timeDelta * Speed);
        }

        // 在屏幕上画球，只有客户端调用
        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);
        }
    }
}
