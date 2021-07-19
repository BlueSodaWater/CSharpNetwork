using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PongGame
{
    public enum PaddleSide : uint
    {
        None,
        Left,
        Right
    };

    // 板的碰撞类型
    public enum PaddleCollision
    {
        None,
        WithTop,
        WithFront,
        WithBottom
    };

    // 这是服务器的板类
    public class Paddle
    {
        // 私有数据成员
        private Texture2D _sprite;
        private DateTime _lastCollisiontime = DateTime.MinValue;
        private TimeSpan _minCollisionTimeGap = TimeSpan.FromSeconds(0.2);

        // 公共数据成员
        public readonly PaddleSide Side;
        public int Score = 0;
        public Vector2 Position = new Vector2();
        public int TopmostY { get; private set; }
        public int BottommostY { get; private set; }

        public Rectangle TopCollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), new Point(GameGeometry.PaddleSize.X, 4)); }
        }

        public Rectangle BottomCollisionArea
        {
            get
            {
                return new Rectangle(
                    (int)Position.X, FrontCollisionArea.Bottom, GameGeometry.PaddleSize.X, 4
                    );
            }
        }

        public Rectangle FrontCollisionArea
        {
            get
            {
                Point pos = Position.ToPoint();
                pos.Y += 4;
                Point size = new Point(GameGeometry.PaddleSize.X, GameGeometry.PaddleSize.Y - 8);

                return new Rectangle(pos, size);
            }
        }

        // 设置哪一边的板
        public Paddle(PaddleSide side)
        {
            Side = side;
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("paddle.png");
        }

        // 将板放在他可以移动的地方
        public void Initialize()
        {
            // 确实板放的位置
            int x;
            if (Side == PaddleSide.Left)
                x = GameGeometry.GoalSize;
            else if (Side == PaddleSide.Right)
                x = GameGeometry.PlayArea.X - GameGeometry.PaddleSize.X - GameGeometry.GoalSize;
            else
                throw new Exception("Side is not `Left` or `Right`");

            Position = new Vector2(x, (GameGeometry.PlayArea.Y / 2) - (GameGeometry.PaddleSize.Y / 2));
            Score = 0;

            // 设置弹跳
            TopmostY = 0;
            BottommostY = GameGeometry.PlayArea.Y - GameGeometry.PaddleSize.Y;
        }

        // 根据用户的输入移动板（由客户端调用）
        public void ClientSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float dist = timeDelta * GameGeometry.PaddleSpeed;

            // 检测上下键
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Up))
                Position.Y -= dist;
            else if (kbs.IsKeyDown(Keys.Down))
                Position.Y += dist;

            // 弹跳检测
            if (Position.Y < TopmostY)
                Position.Y = TopmostY;
            else if (Position.Y > BottommostY)
                Position.Y = BottommostY;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);
        }

        // 检测板的哪一部分和球碰撞了（如果有的话）
        public bool Collides(Ball ball, out PaddleCollision typeOfCollision)
        {
            typeOfCollision = PaddleCollision.None;

            // 确保有足够的时间进行新的碰撞
            // (避免球速太快会引起的错误)
            if (DateTime.Now < (_lastCollisiontime.Add(_minCollisionTimeGap)))
                return false;

            // 顶部和底部的碰撞优先
            if (ball.CollisionArea.Intersects(TopCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithTop;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            if (ball.CollisionArea.Intersects(BottomCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithBottom;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            // 检测挡板
            if (ball.CollisionArea.Intersects(FrontCollisionArea))
            {
                typeOfCollision = PaddleCollision.WithBottom;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            // 未碰撞
            return false;
        }
    }
}
