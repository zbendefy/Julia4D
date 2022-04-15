using Engine.AssetManagement;
using Engine.BackEnd;
using Engine.Utils;
using OpenTK;
using OpenTK.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engine;
using System.Threading;
using Engine.FrontEnd;
using OpenTK.Windowing.Common;
using Julia4D;

namespace Game
{
    public class GameInstance : IGameHandler
    {
        JuliaGameThread gameThread;

        public void OnClosing()
        {
            gameThread.OnClosing();
        }

        
        public static void CreateGameThread<T>(Processor processor, ref T target, Dictionary<string, object> app_config)
        {
            target = (T)Activator.CreateInstance(typeof(T), processor, app_config);
        }

        public void OnInit(Processor processor, Dictionary<string, object> app_config)
        {
            CreateGameThread(processor, ref gameThread, app_config);
        }

        public void OnKeyDown(KeyboardKeyEventArgs e)
        {
            gameThread.OnKeyDown(e);
        }

        public void OnKeyUp(KeyboardKeyEventArgs e)
        {
            gameThread.OnKeyUp(e);
        }

        public void OnMouseDown(MouseButtonEventArgs e)
        {
            gameThread.OnMouseDown(e);
        }

        public void OnMouseMove(MouseMoveEventArgs e)
        {
            gameThread.OnMouseMove(e);
        }

        public void OnMouseUp(MouseButtonEventArgs e)
        {
            gameThread.OnMouseUp(e);
        }

        public void OnMouseWheel(MouseWheelEventArgs e)
        {
            gameThread.OnMouseWheel(e);
        }

        public void OnResize(int newWidth, int newHeight)
        {
            gameThread.OnResize(newWidth, newHeight);
        }

        public void OnUpdate(double dt, double t)
        {
            gameThread.Tick(t, dt);
        }
    }
}
